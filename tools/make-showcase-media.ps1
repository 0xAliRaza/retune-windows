<#
.SYNOPSIS
    Makes pleasant synthetic videos for demos and README screenshots: slow pans over the fake provider's
    procedural landscapes (no copyrighted footage).

.DESCRIPTION
    Starts the fake provider, renders two landscapes at full size, and turns them into a live-style MPEG-TS
    and an on-demand MP4 with silent audio. Output: artifacts/showcase-media (git-ignored). Point the fake
    provider at it with --media to stream these instead of the test patterns.
#>
[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ffmpeg = & (Join-Path $PSScriptRoot 'find-ffmpeg.ps1')

$repoRoot = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repoRoot 'artifacts\showcase-media'
New-Item -ItemType Directory -Force -Path $out | Out-Null

$provider = Join-Path $repoRoot 'tools\FakeProvider\bin\Debug\net10.0\Retune.FakeProvider.exe'
if (-not (Test-Path $provider)) { throw 'Build the solution first (dotnet build Retune.slnx).' }

$port = 8097
$server = Start-Process -FilePath $provider -ArgumentList '--port', $port -WindowStyle Hidden -PassThru
try {
    Start-Sleep -Seconds 2
    $jobs = @(
        @{ Name = 'vod-1080p.mp4'; Scene = '12.png'; Size = '1920x1080'; Seconds = 60; Format = @('-movflags', '+faststart') },
        @{ Name = 'live-720p.ts';  Scene = 's3.png'; Size = '1280x720';  Seconds = 30; Format = @('-f', 'mpegts') }
    )
    foreach ($job in $jobs) {
        $target = Join-Path $out $job.Name
        if ((Test-Path $target) -and -not $Force) { Write-Host "skip  $($job.Name)"; continue }

        $still = Join-Path $out "$($job.Name).png"
        Invoke-WebRequest -Uri "http://127.0.0.1:$port/art/backdrop/$($job.Scene)?w=2560" -OutFile $still -UseBasicParsing
        $frames = $job.Seconds * 25
        # A slow push-in (Ken Burns) over the still, with a silent stereo track so audio paths still run.
        $pan = "scale=2560:-1,zoompan=z='1+0.12*on/${frames}':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d=${frames}:s=$($job.Size):fps=25"
        & $ffmpeg -hide_banner -loglevel error -y -loop 1 -i $still -f lavfi -i 'anullsrc=channel_layout=stereo:sample_rate=48000' `
            -vf $pan -t $job.Seconds -c:v libx264 -preset veryfast -pix_fmt yuv420p -g 25 -c:a aac -b:a 64k @($job.Format) $target
        if ($LASTEXITCODE -ne 0) { throw "FFmpeg failed for $($job.Name)" }
        Remove-Item $still
        Write-Host "made  $($job.Name)"
    }
}
finally {
    Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
}
