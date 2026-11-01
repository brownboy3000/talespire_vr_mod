# Rebuilds deploy\BepInEx\plugins\TaleSpireVR\weapons.bin (the first-person weapons) from seven Unity Asset Store
# packages: "Low Poly Weapons VOL.1" (guns), Blink's "FREE - Stylized Weapons" (swords, axes, daggers, hammers, shields,
# musket...), Donthrukat's "Simple low poly melee weapon pack" (modern axes, bats, knives...) and the PolynestLab
# "FREE Low Poly Sci-Fi Weapons Demo" (pistol, SMG, injector), Blink's "FREE - RPG Weapons" (bow, spear, staff, wand,
# mace...), Alvarolopz98's "Aged Medieval Weapons - Free" (halberd, machete, shield, hand axe...) and CASTLE BRAVO's
# "Low Poly ShotGun Weapon Pack 2". As with the hands, a Unity editor imports them in batch
# mode and WeaponExporter.cs writes meshes + PNG textures in a small binary format that the mod rebuilds at runtime
# (FirstPerson/FPWeapons.cs). Only models, materials, textures and prefabs are copied into the project (no package
# scripts). -List "id=candidate;..." picks what is exported (candidate keys: WeaponExporter.Candidates).
#   powershell -ExecutionPolicy Bypass -File VRMod\tools\weapons\export-weapons.ps1 [-Mode Export|Preview|Side|Analyze|Zoom] [-Keys a,b]
# Preview renders each candidate (or -Keys) from four sides (with x / y / z axes) to <Work>\weaponpreview; Side renders
# one side view per candidate with a grid in model units (to place grips, muzzles and blades); Analyze logs
# cross-sections; Zoom renders the shotgun's grip with a 1 cm grid.
# The temporary Unity project goes to %TEMP%\TaleSpireVR-weapons (a few hundred MB; delete it afterwards).
param(
    [string]$Mode = "Export",
    [string]$List = "shotgun=shotgun;sword=sword1;pistol=m1911;uzi=uzi;rifle=m4;m249=m249;m107=m107;rpg=rpg7;musket=musket;injector=ssg06;battleaxe=axeb2;fireaxe=axe5;warhammer=warhammer;bat=bat3;dagger=dagger1;shield=shield2;crowbar=crowbar1;bow=bowe;combatshotgun=sgj;wand=wande;firestaff=staffe;spear=spear1e;halberd=halberd;glaive=polearm;scythe=scythe;morningstar=mace1e;scimitar=sword3;handaxe=throwaxe;machete=machete;knife=knife2;sledgehammer=sledge1;kiteshield=ashield",
    [string]$Keys = "",
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor\Unity.exe",
    [string]$Store = "$env:APPDATA\Unity\Asset Store-5.x",
    [string]$Work = (Join-Path $env:TEMP "TaleSpireVR-weapons")
)
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$out = Join-Path $here "..\..\deploy\BepInEx\plugins\TaleSpireVR\weapons.bin"
$proj = Join-Path $Work "WeaponsProj"
$packages = @{
    "lowpoly"  = Join-Path $Store "Simon Mercuzot\3D ModelsPropsWeaponsGuns\Low Poly Weapons VOL1.unitypackage"
    "stylized" = Join-Path $Store "Blink\3D ModelsPropsWeapons\FREE - Stylized Weapons.unitypackage"
    "melee"    = Join-Path $Store "Donthrukat\3D ModelsPropsWeaponsGuns\Simple low poly melee weapon pack.unitypackage"
    "scifi"    = Join-Path $Store "The PolynestLab\3D ModelsPropsWeaponsGuns\FREE Low Poly Sci-Fi Weapons Demo Sector Delta Cosmic Retro.unitypackage"
    "rpg"      = Join-Path $Store "Blink\3D ModelsPropsWeapons\FREE - RPG Weapons.unitypackage"
    "aged"     = Join-Path $Store "Alvarolopz98\3D ModelsPropsWeapons\Aged Medieval Weapons-Free.unitypackage"
    "shotguns" = Join-Path $Store "CASTLE BRAVO\3D ModelsPropsWeaponsGuns\Low Poly ShotGun Weapon Pack 2.unitypackage"
}
$keep = '^Assets/(Low Poly Weapons VOL\.1/|Blink/|Donthrukat/|gun/|Aged Medieval Weapons/|Low Poly ShotGun Weapon Pack 2/)'

if (-not (Test-Path (Join-Path $proj "Assets"))) {
    & $Unity -batchmode -nographics -createProject $proj -quit -logFile (Join-Path $Work "unity-create.log") | Out-Null
}
foreach ($name in $packages.Keys) {
    # A .unitypackage is a tar.gz of <guid>\{asset, asset.meta, pathname} folders.
    $pkg = Join-Path $Work $name
    if (-not (Test-Path $pkg)) {
        New-Item -ItemType Directory -Force $pkg | Out-Null
        & tar -xzf $packages[$name] -C $pkg
    }
    foreach ($d in Get-ChildItem $pkg -Directory) {
        $pn = Join-Path $d.FullName "pathname"
        if (-not (Test-Path $pn)) { continue }
        $path = Get-Content $pn -TotalCount 1
        $asset = Join-Path $d.FullName "asset"
        $isDir = -not (Test-Path $asset)
        if (-not (($path + $(if ($isDir) { "/" } else { "" })) -match $keep)) { continue }
        if (-not $isDir -and -not ($path -match '\.(fbx|FBX|mat|png|tga|prefab)$')) { continue }
        $dest = Join-Path $proj $path
        if ($isDir) { New-Item -ItemType Directory -Force $dest | Out-Null }
        else { New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null; Copy-Item $asset $dest -Force }
        $meta = Join-Path $d.FullName "asset.meta"
        if (Test-Path $meta) { Copy-Item $meta "$dest.meta" -Force }
    }
}
New-Item -ItemType Directory -Force (Join-Path $proj "Assets\Editor") | Out-Null
Copy-Item (Join-Path $here "WeaponExporter.cs") (Join-Path $proj "Assets\Editor\WeaponExporter.cs") -Force
$env:WEAPONS_PREVIEW = Join-Path $Work "weaponpreview"
$env:WEAPONS_OUT = [IO.Path]::GetFullPath($out)
$env:WEAPONS_EXPORT = $List
$env:WEAPONS_KEYS = $Keys
$log = Join-Path $Work "unity-weapons-$Mode.log"
# (Not -nographics: Preview and Zoom render.)
& $Unity -batchmode -projectPath $proj -executeMethod "WeaponExporter.$Mode" -logFile $log | Out-Null
Select-String -Path $log -Pattern "WEAPON:|error CS" | ForEach-Object { $_.Line }
if ($Mode -eq "Export") { Get-Item $env:WEAPONS_OUT | Select-Object FullName, Length }
