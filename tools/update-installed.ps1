<#
.SYNOPSIS
    Updates this PC's installed Retune from artifacts/dist without losing your place: if something is playing, Retune
    reopens on the same channel; if Retune was open, it reopens.

.DESCRIPTION
    Closes Retune gracefully (so the final progress is saved), runs the per-user installer silently (no admin
    rights), then relaunches it - with --resume-last when the Windows media session showed it playing.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$setup = Get-ChildItem (Join-Path $repoRoot 'artifacts\dist') -Filter 'Retune-*-setup.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $setup) { throw 'No installer in artifacts\dist. Run tools\package.ps1 first.' }

function Test-RetunePlaying {
    try {
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        $asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() |
            Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } |
            Select-Object -First 1
        $managerType = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType = WindowsRuntime]
        $task = $asTask.MakeGenericMethod($managerType).Invoke($null, @($managerType::RequestAsync()))
        if (-not $task.Wait(5000)) { return $false }
        foreach ($session in $task.Result.GetSessions()) {
            if ($session.SourceAppUserModelId -match 'Retune' -and $session.GetPlaybackInfo().PlaybackStatus -eq 'Playing') { return $true }
        }
    }
    catch {
        Write-Warning "Couldn't read the media session: $($_.Exception.Message)"
    }
    return $false
}

$running = @(Get-Process Retune -ErrorAction SilentlyContinue)
$playing = $running.Count -gt 0 -and (Test-RetunePlaying)
foreach ($process in $running) {
    $process.CloseMainWindow() | Out-Null
    if (-not $process.WaitForExit(15000)) { throw 'Retune did not close; close it and run this again.' }
}

$previousData = $env:RETUNE_DATA_DIR
Remove-Item Env:\RETUNE_DATA_DIR -ErrorAction SilentlyContinue
try {
    $install = Start-Process $setup.FullName -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -PassThru
    if (-not $install.WaitForExit(180000)) { throw 'The installer timed out.' }
    if ($install.ExitCode -ne 0) { throw "The installer failed with exit code $($install.ExitCode)." }

    $exe = Join-Path $env:LOCALAPPDATA 'Programs\Retune\Retune.exe'
    if ($playing) {
        Start-Process $exe -ArgumentList '--resume-last'
    }
    elseif ($running.Count -gt 0) {
        Start-Process $exe
    }
}
finally {
    if ($previousData) { $env:RETUNE_DATA_DIR = $previousData }
}

Write-Host "Updated to $($setup.Name). Was open: $($running.Count -gt 0). Resumed playback: $playing."
