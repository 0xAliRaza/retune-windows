<#
.SYNOPSIS
    Regenerates the README screenshots in docs/screenshots.

.DESCRIPTION
    Starts the fake provider with the showcase media (tools/make-showcase-media.ps1), runs Retune's dev
    screenshot tour (--tour) with no audio device and without taking focus, then frames each capture with
    rounded corners and a soft shadow on a dark gradient. The tour renders the window itself, so it works even
    while the PC is locked or the window is covered. Everything shown is synthetic demo data.
#>
[CmdletBinding()]
param(
    # Where the (unfocused) window is placed, in physical pixels. Put it on a screen you aren't using.
    [int]$X = 40,
    [int]$Y = 40,
    [int]$Width = 1840,
    [int]$Height = 1130,
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$provider = Join-Path $repoRoot "tools\FakeProvider\bin\$Configuration\net10.0\Retune.FakeProvider.exe"
$app = Join-Path $repoRoot "src\Retune.App\bin\$Configuration\net10.0-windows10.0.19041.0\win-x64\Retune.exe"
$media = Join-Path $repoRoot 'artifacts\showcase-media'
$raw = Join-Path $repoRoot 'artifacts\screenshots-raw'
$data = Join-Path $repoRoot 'artifacts\screenshots-data'
$out = Join-Path $repoRoot 'docs\screenshots'
foreach ($required in $provider, $app) { if (-not (Test-Path $required)) { throw "Missing $required. Build the solution first." } }
if (-not (Test-Path (Join-Path $media 'vod-1080p.mp4'))) { & (Join-Path $PSScriptRoot 'make-showcase-media.ps1') }

Remove-Item $raw, $data -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $out | Out-Null

$port = 8098
$server = Start-Process -FilePath $provider -ArgumentList '--port', $port, '--media', "`"$media`"" -WindowStyle Hidden -PassThru
$previousData = $env:RETUNE_DATA_DIR
try {
    Start-Sleep -Seconds 2
    $env:RETUNE_DATA_DIR = $data
    $tour = Start-Process -FilePath $app -PassThru -ArgumentList @(
        '--demo', "http://127.0.0.1:$port", '--tour', "`"$raw`"", '--mute', '--no-activate', '--window', "$X,$Y,$Width,$Height")
    if (-not $tour.WaitForExit(240000)) { Stop-Process -Id $tour.Id -Force; throw 'The screenshot tour timed out.' }
}
finally {
    $env:RETUNE_DATA_DIR = $previousData
    Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
    Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue
}

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

$pad = 72; $radius = 14
foreach ($shot in Get-ChildItem $raw -Filter *.png) {
    $image = [System.Drawing.Image]::FromFile($shot.FullName)
    try {
        $canvas = New-Object System.Drawing.Bitmap ($image.Width + 2 * $pad), ($image.Height + 2 * $pad)
        $g = [System.Drawing.Graphics]::FromImage($canvas)
        $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
        $background = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point $canvas.Width, $canvas.Height), ([System.Drawing.Color]::FromArgb(255, 34, 22, 74)), ([System.Drawing.Color]::FromArgb(255, 10, 30, 48))
        $g.FillRectangle($background, 0, 0, $canvas.Width, $canvas.Height)

        # A soft shadow: stacked translucent rounded rectangles, offset downwards.
        for ($i = 24; $i -ge 1; $i--) {
            $shadow = New-RoundedPath ($pad - $i) ($pad - $i + 14) ($image.Width + 2 * $i) ($image.Height + 2 * $i) ($radius + $i)
            $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(5, 0, 0, 0))
            $g.FillPath($brush, $shadow); $brush.Dispose(); $shadow.Dispose()
        }

        $window = New-RoundedPath $pad $pad $image.Width $image.Height $radius
        $g.SetClip($window)
        $g.DrawImage($image, $pad, $pad, $image.Width, $image.Height)
        $g.ResetClip()
        $edge = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(60, 255, 255, 255)), 1
        $g.DrawPath($edge, $window)
        $edge.Dispose(); $window.Dispose(); $background.Dispose(); $g.Dispose()
        $canvas.Save((Join-Path $out $shot.Name), [System.Drawing.Imaging.ImageFormat]::Png)
        $canvas.Dispose()
        Write-Host "framed  $($shot.Name)"
    }
    finally {
        $image.Dispose()
    }
}
