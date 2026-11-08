<#
.SYNOPSIS
    Downloads the pinned libmpv build into third_party/libmpv and verifies its SHA-256.

.DESCRIPTION
    Retune plays video through libmpv. We pin one build from shinchiro/mpv-winbuild-cmake
    (GPL, matching Retune's license) and refuse anything whose hash doesn't match.
    To upgrade: change $Build and $Sha256 together, then run the full test suite.
#>
[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Build  = '20261021'
$Asset  = 'mpv-dev-x86_64-20261021-git-e471496c56.7z'
$Sha256 = 'efc719eff3dc5954b4a5efc800632808bcec01cc9034940dd14e5eb7fe9ea633'
# SHA-256 of libmpv-2.dll inside that archive: checked on every run and by tools/package.ps1 before shipping.
$DllSha256 = '04e4d27dc385dc18ddd93fbe69c75e635b864f3c6ec53a9122df54c2f7842f4a'
$Url    = "https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/$Build/$Asset"

$repoRoot  = Split-Path -Parent $PSScriptRoot
$targetDir = Join-Path $repoRoot 'third_party\libmpv'
$dll       = Join-Path $targetDir 'libmpv-2.dll'
$stamp     = Join-Path $targetDir 'BUILD'

function Test-LibMpv { (Test-Path $dll) -and (Get-FileHash -Algorithm SHA256 -Path $dll).Hash.ToLowerInvariant() -eq $DllSha256 }

if (-not $Force -and (Test-Path $stamp) -and ((Get-Content $stamp -Raw).Trim() -eq $Asset) -and (Test-LibMpv)) {
    Write-Host "libmpv $Build already present (hash verified)."
    return
}

$work = Join-Path ([IO.Path]::GetTempPath()) ("retune-libmpv-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $archive = Join-Path $work $Asset
    Write-Host "Downloading $Asset ..."
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $Url -OutFile $archive -UseBasicParsing

    $actual = (Get-FileHash -Algorithm SHA256 -Path $archive).Hash.ToLowerInvariant()
    if ($actual -ne $Sha256) {
        throw "SHA-256 mismatch for $Asset. Expected $Sha256, got $actual. Refusing to use it."
    }

    $extract = Join-Path $work 'x'
    New-Item -ItemType Directory -Path $extract | Out-Null
    tar -xf $archive -C $extract
    if ($LASTEXITCODE -ne 0) { throw "Extracting $Asset failed (tar exit $LASTEXITCODE)." }

    $found = Get-ChildItem -Path $extract -Recurse -Filter 'libmpv-2.dll' | Select-Object -First 1
    if (-not $found) { throw "libmpv-2.dll not found inside $Asset." }

    if (Test-Path $targetDir) { Remove-Item -Recurse -Force $targetDir }
    New-Item -ItemType Directory -Path $targetDir | Out-Null
    Copy-Item $found.FullName $dll
    if (-not (Test-LibMpv)) { throw "libmpv-2.dll from $Asset doesn't match the pinned hash. Refusing to use it." }
    $include = Get-ChildItem -Path $extract -Recurse -Directory -Filter 'include' | Select-Object -First 1
    if ($include) { Copy-Item $include.FullName (Join-Path $targetDir 'include') -Recurse }
    Set-Content -Path $stamp -Value $Asset -Encoding ascii

    Write-Host "libmpv $Build installed to $targetDir"
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
