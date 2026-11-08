<#
.SYNOPSIS
    Builds the release packages: a self-contained publish, a portable zip and (when Inno Setup 6 is installed)
    a per-user installer, plus SHA-256 checksums. Output: artifacts/dist.

.EXAMPLE
    ./tools/package.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$version = (Select-Xml -Path (Join-Path $repoRoot 'Directory.Build.props') -XPath '//Version' | Select-Object -First 1).Node.InnerText
if (-not $version) { throw 'No <Version> in Directory.Build.props.' }

$publish = Join-Path $repoRoot 'artifacts\publish\Retune'
$dist = Join-Path $repoRoot 'artifacts\dist'
Remove-Item $publish, $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $dist | Out-Null

if (-not $SkipTests) {
    dotnet test --solution (Join-Path $repoRoot 'Retune.slnx') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; not packaging.' }
}

# Self-contained, so users need no .NET install. ReadyToRun makes the first launch faster.
dotnet publish (Join-Path $repoRoot 'src\Retune.App\Retune.App.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:DebugType=none -p:PublishDocumentationFile=false -p:PublishReferencesDocumentationFiles=false -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
foreach ($required in 'Retune.exe', 'libmpv-2.dll') {
    if (-not (Test-Path (Join-Path $publish $required))) { throw "The publish output has no $required." }
}

# Ship only the libmpv that tools/fetch-libmpv.ps1 pinned and verified.
$pinned = [regex]::Match((Get-Content (Join-Path $PSScriptRoot 'fetch-libmpv.ps1') -Raw), "DllSha256 = '([0-9a-f]{64})'").Groups[1].Value
$shipped = (Get-FileHash -Algorithm SHA256 -Path (Join-Path $publish 'libmpv-2.dll')).Hash.ToLowerInvariant()
if (-not $pinned -or $shipped -ne $pinned) { throw "libmpv-2.dll doesn't match the pinned hash ($shipped). Refusing to package it." }
Copy-Item (Join-Path $repoRoot 'LICENSE') (Join-Path $publish 'LICENSE.txt')
Copy-Item (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') $publish

$zip = Join-Path $dist "Retune-$version-win-x64.zip"
# The Retune folder itself, so extracting creates its own folder: DLLs never land loose in Downloads.
# (Compress-Archive in Windows PowerShell writes backslashes in entry names, which the zip format doesn't allow.)
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
Write-Host "zip        $zip"

$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
    & $iscc /Q "/DAppVersion=$version" "/DPublishDir=$publish" "/DOutputDir=$dist" (Join-Path $repoRoot 'installer\Retune.iss')
    if ($LASTEXITCODE -ne 0) { throw 'The installer build failed.' }
    Write-Host "installer  $(Join-Path $dist "Retune-$version-setup.exe")"
}
else {
    Write-Warning 'Inno Setup 6 not found (winget install JRSoftware.InnoSetup --scope user); skipped the installer.'
}

$sums = Get-ChildItem $dist -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
$sums | Set-Content -Path (Join-Path $dist 'SHA256SUMS.txt') -Encoding ascii
Get-ChildItem $dist -File | ForEach-Object { '{0,-36} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
