<#
.SYNOPSIS
    Prints the path of ffmpeg.exe (PATH first, then a winget install whose PATH change this shell hasn't seen).
#>
$ErrorActionPreference = 'Stop'
$onPath = Get-Command ffmpeg -ErrorAction SilentlyContinue
if ($onPath) { return $onPath.Source }
$packages = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
$found = Get-ChildItem -Path $packages -Directory -Filter 'Gyan.FFmpeg*' -ErrorAction SilentlyContinue |
    ForEach-Object { Get-ChildItem -Path $_.FullName -Recurse -Filter 'ffmpeg.exe' } |
    Select-Object -First 1
if ($found) { return $found.FullName }
throw 'FFmpeg not found. Install it with: winget install Gyan.FFmpeg'