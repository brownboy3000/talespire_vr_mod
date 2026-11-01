# Builds TaleSpireVR and installs it into this TaleSpire copy.
#   powershell -ExecutionPolicy Bypass -File VRMod\build.ps1          (build + install)
#   powershell -ExecutionPolicy Bypass -File VRMod\build.ps1 -NoBuild (install only)
param([switch]$NoBuild)
$ErrorActionPreference = "Stop"
$vrmod = $PSScriptRoot
$game = Split-Path -Parent $vrmod
. "$vrmod\tools\env.ps1"

if (-not $NoBuild) {
    & dotnet build "$vrmod\src\TaleSpireVR\TaleSpireVR.csproj" -c Release -nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
}

$pluginDir = Join-Path $game "BepInEx\plugins\TaleSpireVR"
New-Item -ItemType Directory -Force $pluginDir | Out-Null
Copy-Item "$vrmod\build\TaleSpireVR.dll" $pluginDir -Force
if (Test-Path "$vrmod\build\TaleSpireVR.pdb") { Copy-Item "$vrmod\build\TaleSpireVR.pdb" $pluginDir -Force }

# controls.txt: the copy next to TaleSpire.exe is the one that gets edited; deploy\ holds it for the repository.
if (Test-Path "$game\controls.txt") { Copy-Item "$game\controls.txt" "$vrmod\deploy\controls.txt" -Force }

# Native SteamVR plugin, Unity subsystem manifest, SteamVR action manifest + bindings.
Copy-Item "$vrmod\deploy\*" $game -Recurse -Force
Write-Host "Installed TaleSpireVR into $game"
