THIRD-PARTY PARTS OF THE TALESPIRE VR DOWNLOAD
==============================================

TaleSpire VR's own code is under the GPL-3.0 licence (../LICENSE). Its source code is at
https://github.com/brownboy3000/talespire_vr_mod

These parts are other people's work. They are included unmodified and keep their own licences:

  BepInEx 5.4.23.5 ......... BepInEx\core\BepInEx*.dll
                             LGPL-2.1, BepInEx-LICENSE.txt
                             https://github.com/BepInEx/BepInEx
  UnityDoorstop 4.5.0 ...... winhttp.dll, doorstop_config.ini, .doorstop_version
                             LGPL-2.1, UnityDoorstop-LICENSE.txt
                             https://github.com/NeighTools/UnityDoorstop
  HarmonyX 2.9.0 ........... BepInEx\core\0Harmony.dll, HarmonyXInterop.dll
                             MIT, HarmonyX-LICENSE.txt
                             https://github.com/BepInEx/HarmonyX
  Harmony 2.0 .............. BepInEx\core\0Harmony20.dll
                             MIT, Harmony-LICENSE.txt
                             https://github.com/pardeike/Harmony
  MonoMod 22.1.29.1 ........ BepInEx\core\MonoMod.*.dll
                             MIT, MonoMod-LICENSE.txt
                             https://github.com/MonoMod/MonoMod
  Mono.Cecil 0.10.4 ........ BepInEx\core\Mono.Cecil*.dll
                             MIT, Mono.Cecil-LICENSE.txt
                             https://github.com/jbevain/cecil
  OpenVR 2.0.10 ............ TaleSpire_Data\Plugins\x86_64\openvr_api.dll
                             (and openvr_api.cs in the source code)
                             BSD-3-Clause, OpenVR-LICENSE.txt
                             https://github.com/ValveSoftware/openvr
  OpenVR Unity XR plugin ... TaleSpire_Data\Plugins\x86_64\XRSDKOpenVR.dll,
                             TaleSpire_Data\UnitySubsystems\XRSDKOpenVR
                             BSD-3-Clause, OpenVR-XR-plugin-LICENSE.txt
                             https://github.com/ValveSoftware/unity-xr-plugin

The OpenVR files came to this mod through Universal Unity VR (UUVR) by Raicuparta, which is under
GPL-3.0 (the same text as ../LICENSE): https://github.com/Raicuparta/uuvr

The BepInEx and UnityDoorstop files are loaded as separate libraries: you can replace them with your
own builds of those projects, whose source code is at the links above.

MODELS, TEXTURES AND EFFECTS
----------------------------
BepInEx\plugins\TaleSpireVR\weapons.bin, vrhands.bin and sigil_*.png hold content from Unity Asset
Store packages, listed under Credits in ../README.md. They belong to their publishers and are used
under the Asset Store's licence. They are not under the GPL. They are included only as part of this
mod: please don't extract, reuse or share them separately.
