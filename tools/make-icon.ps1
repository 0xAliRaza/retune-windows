<#
.SYNOPSIS
    Draws the Retune icon (a violet-to-blue rounded square with a play mark, as in the sidebar) and writes a
    multi-size .ico (16-256 px, PNG frames) to src/Retune.App/Assets/Retune.ico.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$target = Join-Path $repoRoot 'src\Retune.App\Assets\Retune.ico'
New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null

function New-Frame([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)

    # Small sizes fill the frame; large ones keep a little margin, like other Windows 11 app icons.
    $inset = if ($size -ge 48) { $size * 0.06 } else { 0 }
    $side = $size - 2 * $inset
    $radius = $side * 0.25
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($inset, $inset, $d, $d, 180, 90)
    $path.AddArc($inset + $side - $d, $inset, $d, $d, 270, 90)
    $path.AddArc($inset + $side - $d, $inset + $side - $d, $d, $d, 0, 90)
    $path.AddArc($inset, $inset + $side - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF $inset, $inset), (New-Object System.Drawing.PointF ($inset + $side), ($inset + $side)), ([System.Drawing.Color]::FromArgb(255, 0x8B, 0x5C, 0xF6)), ([System.Drawing.Color]::FromArgb(255, 0x3B, 0x82, 0xF6))
    $g.FillPath($fill, $path)

    # The play mark, optically centred (nudged right), with softly rounded corners.
    $c = $size / 2; $h = $side * 0.42; $w = $h * 0.9; $nudge = $side * 0.04
    $play = New-Object System.Drawing.Drawing2D.GraphicsPath
    $play.AddPolygon([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ($c - $w / 2 + $nudge), ($c - $h / 2)),
        (New-Object System.Drawing.PointF ($c + $w / 2 + $nudge), $c),
        (New-Object System.Drawing.PointF ($c - $w / 2 + $nudge), ($c + $h / 2))))
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $round = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1, $side * 0.06))
    $round.LineJoin = 'Round'
    $g.FillPath($white, $play); $g.DrawPath($round, $play)

    $round.Dispose(); $white.Dispose(); $play.Dispose(); $fill.Dispose(); $path.Dispose(); $g.Dispose()
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return ,$stream.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = foreach ($size in $sizes) { ,(New-Frame $size) }

# ICO container: header, one directory entry per frame, then the PNG frames.
$file = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write($frame) }
$writer.Flush()
[System.IO.File]::WriteAllBytes($target, $file.ToArray())
$writer.Dispose()
Write-Host "wrote $target"
