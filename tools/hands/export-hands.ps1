# Rebuilds deploy\BepInEx\plugins\TaleSpireVR\vrhands.bin (the first-person hands) from PolyOne's free
# "Free Pack - VR Hands" Unity package. TaleSpire's Unity (2021.3) can't load asset bundles from a newer editor,
# so a Unity editor imports the package in batch mode and HandsExporter.cs writes the model in a small binary
# format that the mod rebuilds at runtime (FirstPerson/FPHands.cs).
#   powershell -ExecutionPolicy Bypass -File VRMod\tools\hands\export-hands.ps1 [-Unity <Unity.exe>] [-Work <dir>]
# The temporary Unity project goes to %TEMP%\TaleSpireVR-hands (a few hundred MB; delete it afterwards).
param(
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor\Unity.exe",
    [string]$Package = "$env:APPDATA\Unity\Asset Store-5.x\PolyOne Studio\3D ModelsProps\Free Pack - VR Hands.unitypackage",
    [string]$Work = (Join-Path $env:TEMP "TaleSpireVR-hands")
)
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$out = Join-Path $here "..\..\deploy\BepInEx\plugins\TaleSpireVR\vrhands.bin"
$pkg = Join-Path $Work "package"
$proj = Join-Path $Work "HandsProj"

# A .unitypackage is a tar.gz of <guid>\{asset, asset.meta, pathname} folders.
if (-not (Test-Path $pkg)) {
    New-Item -ItemType Directory -Force $pkg | Out-Null
    & tar -xzf $Package -C $pkg
}
if (-not (Test-Path (Join-Path $proj "Assets"))) {
    & $Unity -batchmode -nographics -createProject $proj -quit -logFile (Join-Path $Work "unity-create.log") | Out-Null
}
foreach ($d in Get-ChildItem $pkg -Directory) {
    $path = Get-Content (Join-Path $d.FullName "pathname") -TotalCount 1
    $dest = Join-Path $proj $path
    $asset = Join-Path $d.FullName "asset"
    if (Test-Path $asset) { New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null; Copy-Item $asset $dest -Force }
    else { New-Item -ItemType Directory -Force $dest | Out-Null }
    $meta = Join-Path $d.FullName "asset.meta"
    if (Test-Path $meta) { Copy-Item $meta "$dest.meta" -Force }
}
New-Item -ItemType Directory -Force (Join-Path $proj "Assets\Editor") | Out-Null
Copy-Item (Join-Path $here "HandsExporter.cs") (Join-Path $proj "Assets\Editor\HandsExporter.cs") -Force
$env:HANDS_OUT = [IO.Path]::GetFullPath($out)
& $Unity -batchmode -nographics -projectPath $proj -executeMethod HandsExporter.Export -logFile (Join-Path $Work "unity-export.log") | Out-Null
Select-String -Path (Join-Path $Work "unity-export.log") -Pattern "HANDS:|error CS" | ForEach-Object { $_.Line }
Get-Item $env:HANDS_OUT | Select-Object FullName, Length
