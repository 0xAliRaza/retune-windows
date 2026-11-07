<#
.SYNOPSIS
    Generates small synthetic media files used by the player integration tests and the dev spike.

.DESCRIPTION
    Everything is generated with FFmpeg test sources, so there are no copyrighted samples and the
    files are reproducible. Output: tests/fixtures/media (git-ignored).
#>
[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ffmpeg = $null
$onPath = Get-Command ffmpeg -ErrorAction SilentlyContinue
if ($onPath) { $ffmpeg = $onPath.Source }
if (-not $ffmpeg) {
    # A winget install updates PATH only for shells started afterwards; find it directly.
    $packages = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    $found = Get-ChildItem -Path $packages -Directory -Filter 'Gyan.FFmpeg*' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem -Path $_.FullName -Recurse -Filter 'ffmpeg.exe' } |
        Select-Object -First 1
    if ($found) { $ffmpeg = $found.FullName }
}
if (-not $ffmpeg) { throw 'FFmpeg not found. Install it with: winget install Gyan.FFmpeg' }

$repoRoot = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repoRoot 'tests\fixtures\media'
New-Item -ItemType Directory -Force -Path $out | Out-Null

$video = 'testsrc2=size=1920x1080:rate=25'
$tone  = 'sine=frequency=440:sample_rate=48000'
$tone2 = 'sine=frequency=880:sample_rate=48000'

$jobs = [ordered]@{
    # 60 s 1080p H.264 + AAC: the general on-demand fixture
    'vod-1080p.mp4' = @('-f','lavfi','-i',"$video", '-f','lavfi','-i',"$tone", '-t','60',
                        '-c:v','libx264','-preset','veryfast','-pix_fmt','yuv420p','-g','50',
                        '-c:a','aac','-b:a','128k','-movflags','+faststart')
    # MPEG-TS like a live channel
    'live-720p.ts'  = @('-f','lavfi','-i','testsrc2=size=1280x720:rate=25', '-f','lavfi','-i',"$tone", '-t','30',
                        '-c:v','libx264','-preset','veryfast','-pix_fmt','yuv420p','-g','25',
                        '-c:a','aac','-b:a','96k','-f','mpegts')
    # Video without any audio track ("No audio in this stream")
    'no-audio.mp4'  = @('-f','lavfi','-i','testsrc2=size=1280x720:rate=25', '-t','10',
                        '-c:v','libx264','-preset','veryfast','-pix_fmt','yuv420p','-an')
}

foreach ($name in $jobs.Keys) {
    $target = Join-Path $out $name
    if ((Test-Path $target) -and -not $Force) { Write-Host "skip  $name"; continue }
    & $ffmpeg -hide_banner -loglevel error -y @($jobs[$name]) $target
    if ($LASTEXITCODE -ne 0) { throw "FFmpeg failed for $name" }
    Write-Host "made  $name"
}

# Two audio tracks (English, German) and one SRT subtitle track, for track-selection tests
$multi = Join-Path $out 'multi-track.mkv'
if (-not (Test-Path $multi) -or $Force) {
    $srt = Join-Path $out 'subs.srt'
    @"
1
00:00:00,500 --> 00:00:04,000
Hello from Retune

2
00:00:04,500 --> 00:00:09,000
Second subtitle line
"@ | Set-Content -Path $srt -Encoding utf8
    & $ffmpeg -hide_banner -loglevel error -y `
        -f lavfi -i 'testsrc2=size=1280x720:rate=25' -f lavfi -i $tone -f lavfi -i $tone2 -i $srt -t 10 `
        -map 0:v -map 1:a -map 2:a -map 3:s `
        -c:v libx264 -preset veryfast -pix_fmt yuv420p -c:a aac -b:a 96k -c:s srt `
        -metadata:s:a:0 language=eng -metadata:s:a:1 language=ger -metadata:s:s:0 language=eng `
        $multi
    if ($LASTEXITCODE -ne 0) { throw 'FFmpeg failed for multi-track.mkv' }
    Remove-Item $srt
    Write-Host 'made  multi-track.mkv'
}
