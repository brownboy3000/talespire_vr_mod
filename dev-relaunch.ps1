# Developer loop: rebuild (optional), restart TaleSpire, and click through to a board using the
# mod's debug command channel (VRMod/_debug/cmd.txt). Only presses menu buttons; never edits anything.
#   powershell -ExecutionPolicy Bypass -File VRMod\dev-relaunch.ps1 [-Build] [-ToBoard]
param([switch]$Build, [switch]$ToBoard)
$ErrorActionPreference = "Stop"
$vrmod = $PSScriptRoot
$game = Split-Path -Parent $vrmod
$dbg = Join-Path $vrmod "_debug"
New-Item -ItemType Directory -Force $dbg | Out-Null

# Close the game first: it locks the plugin DLL that the build installs.
Get-Process TaleSpire -ErrorAction SilentlyContinue | ForEach-Object { [void]$_.CloseMainWindow() }
Start-Sleep -Seconds 6
Get-Process TaleSpire -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
Start-Sleep -Seconds 2

if ($Build) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File "$vrmod\build.ps1"
    if ($LASTEXITCODE -ne 0) { throw "build failed" }
} else {
    & powershell -NoProfile -ExecutionPolicy Bypass -File "$vrmod\build.ps1" -NoBuild
}

$p = Start-Process -FilePath "$game\TaleSpire.exe" -WorkingDirectory $game -PassThru
Write-Host "Started TaleSpire PID $($p.Id)"
Start-Sleep -Seconds 30

function Cmd($text, $wait) { Set-Content (Join-Path $dbg "cmd.txt") $text; Start-Sleep -Seconds $wait }
if ($ToBoard) {
    # UI coordinates are in the 4K menu space, origin bottom-left, for MenuScale = 1.2 (they move if it changes).
    Cmd "click 1920 240" 6     # BEGIN
    Cmd "click 1020 1524" 5    # first campaign in the list (the most recently played one)
    Cmd "click 2580 318" 40    # PLAY
    Write-Host "Should be on the board now."
}
