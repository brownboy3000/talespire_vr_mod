# Builds the release zip: exactly what a player drags next to TaleSpire.exe, and nothing else. It holds the mod, its data,
# the native OpenVR plugin, BepInEx (taken from this game folder), controls.txt and the README. It holds no settings: the
# mod writes those on the first start, and an update then keeps them. Stops if anything from this machine is in it.
#   powershell -ExecutionPolicy Bypass -File VRMod\tools\release\make-release.ps1 [-AlsoScanFor "name1,name2"]
# Result: VRMod\release\TaleSpireVR-<version>.zip (<version> from Plugin.cs).
param([string]$AlsoScanFor = "")
$ErrorActionPreference = "Stop"
$vrmod = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$game = Split-Path -Parent $vrmod
. "$vrmod\tools\env.ps1"

$version = [regex]::Match((Get-Content "$vrmod\src\TaleSpireVR\Plugin.cs" -Raw), 'Version = "([^"]+)"').Groups[1].Value
$out = Join-Path $vrmod "release"
$stage = Join-Path $out "stage"
$compile = Join-Path $out "compile"
$zip = Join-Path $out "TaleSpireVR-$version.zip"
foreach ($old in @($stage, $compile, $zip)) { if (Test-Path $old) { Remove-Item $old -Recurse -Force -Confirm:$false } }
New-Item -ItemType Directory -Force $stage, $compile | Out-Null

# The mod, with no debug symbols and no build paths in it.
& dotnet build "$vrmod\src\TaleSpireVR\TaleSpireVR.csproj" -c Release -nologo -v:minimal --no-incremental `
    "-p:DebugType=none" "-p:DebugSymbols=false" "-p:PathMap=$vrmod=." "-p:OutputPath=$compile/" "-p:IntermediateOutputPath=$compile/obj/"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

# controls.txt: the copy next to TaleSpire.exe is the one that gets edited (build.ps1 copies it into deploy\ too).
if (Test-Path "$game\controls.txt") { Copy-Item "$game\controls.txt" "$vrmod\deploy\controls.txt" -Force }
Copy-Item "$vrmod\deploy\*" $stage -Recurse -Force
Copy-Item "$compile\TaleSpireVR.dll" "$stage\BepInEx\plugins\TaleSpireVR\" -Force

# BepInEx: its loader and core only (it writes its own config, cache and log on the first start).
foreach ($f in @("winhttp.dll", "doorstop_config.ini", ".doorstop_version")) { Copy-Item (Join-Path $game $f) $stage -Force }
New-Item -ItemType Directory -Force "$stage\BepInEx\core" | Out-Null
Copy-Item "$game\BepInEx\core\*" "$stage\BepInEx\core\" -Force

# The README and licences, in the folder the settings will be written to.
New-Item -ItemType Directory -Force "$stage\VRMod" | Out-Null
Copy-Item "$vrmod\README.md" "$stage\VRMod\" -Force
Copy-Item "$vrmod\LICENSE" "$stage\VRMod\" -Force
Copy-Item "$vrmod\licenses" "$stage\VRMod\" -Recurse -Force

# Everything that must be there.
$required = @(
    "winhttp.dll", "doorstop_config.ini", "BepInEx\core\BepInEx.Preloader.dll", "controls.txt",
    "BepInEx\plugins\TaleSpireVR\TaleSpireVR.dll", "BepInEx\plugins\TaleSpireVR\weapons.bin", "BepInEx\plugins\TaleSpireVR\vrhands.bin",
    "BepInEx\plugins\TaleSpireVR\sigil_disc.png", "BepInEx\plugins\TaleSpireVR\sigil_ring.png",
    "BepInEx\plugins\TaleSpireVR\SteamVR\actions.json", "BepInEx\plugins\TaleSpireVR\SteamVR\bindings_knuckles.json",
    "TaleSpire_Data\Plugins\x86_64\XRSDKOpenVR.dll", "TaleSpire_Data\Plugins\x86_64\openvr_api.dll",
    "TaleSpire_Data\UnitySubsystems\XRSDKOpenVR\UnitySubsystemsManifest.json",
    "VRMod\README.md", "VRMod\LICENSE", "VRMod\licenses\README.txt", "VRMod\licenses\BepInEx-LICENSE.txt", "VRMod\licenses\UnityDoorstop-LICENSE.txt")
foreach ($r in $required) { if (-not (Test-Path (Join-Path $stage $r))) { throw "Missing from the release: $r" } }

# Nothing that must not be there: settings, symbols, logs, test output, and anything naming this machine.
$files = Get-ChildItem $stage -Recurse -File -Force
$banned = $files | Where-Object { $_.Extension -in ".pdb", ".log", ".cfg" -or $_.Name -like "TaleSpireVR.*.ini" -or $_.Name -like "TaleSpireVR.*.txt" -or $_.FullName -match '\\_debug\\|\\_decompiled\\' }
if ($banned) { throw "Must not be in the release: $($banned.FullName -join ', ')" }
$needles = @($env:USERNAME, $game, "steamapps\common", "\Users\") + ($AlsoScanFor -split ",") | Where-Object { $_ }
$latin1 = [Text.Encoding]::GetEncoding(28591)
$found = foreach ($f in $files) {
    $bytes = [IO.File]::ReadAllBytes($f.FullName)
    $narrow = $latin1.GetString($bytes)
    $wide = [Text.Encoding]::Unicode.GetString($bytes)
    foreach ($n in $needles) { if ($narrow.Contains($n) -or $wide.Contains($n)) { "$($f.FullName.Substring($stage.Length + 1)): '$n'" } }
}
if ($found) { throw "This machine's details are in the release:`n$($found -join "`n")" }

# Zip entries with forward slashes (Windows PowerShell's own zip tools write backslashes, which other unzippers mishandle).
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$stream = [IO.File]::Open($zip, [IO.FileMode]::Create)
$archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
foreach ($f in $files) {
    $name = $f.FullName.Substring($stage.Length + 1).Replace('\', '/')
    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f.FullName, $name, [IO.Compression.CompressionLevel]::Optimal)
}
$archive.Dispose()
$stream.Dispose()

Remove-Item $compile -Recurse -Force -Confirm:$false
Write-Host ("{0}: {1:N1} MB, {2} files, scanned clean for: {3}" -f $zip, ((Get-Item $zip).Length / 1MB), $files.Count, ($needles -join ", "))
Write-Host "SHA-256: $((Get-FileHash $zip -Algorithm SHA256).Hash)"
