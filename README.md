# TaleSpire VR

A mod that makes [TaleSpire](https://store.steampowered.com/app/720620/TaleSpire/) a full 6DOF SteamVR game, for Game Masters and players. **This is a vibe-coded mod: all of its code was written by AI with [Claude Code](https://claude.ai/code), directed and tested by its author, not written by hand.** Expect bugs. It is an unofficial fan project, not made by or connected with Bouncyrock Entertainment.

[![Video of the mod's main features](https://img.youtube.com/vi/B212gN1KGME/hqdefault.jpg)](https://www.youtube.com/watch?v=B212gN1KGME)

**[Watch the main features on YouTube](https://www.youtube.com/watch?v=B212gN1KGME)** | **[Download the latest release](https://github.com/brownboy3000/talespire_vr_mod/releases/latest)** | [Source code](https://github.com/brownboy3000/talespire_vr_mod)

## Credits
- [**Universal Unity VR (UUVR)**](https://github.com/Raicuparta/uuvr) by Raicuparta (GPL-3.0): this mod started from UUVR's way of loading Unity's OpenVR plugin at runtime. `XRSDKOpenVR.dll`, `openvr_api.dll`, the subsystem manifest and `openvr_api.cs` were taken from it.
- [**BepInEx**](https://github.com/BepInEx/BepInEx) (LGPL-2.1): the mod loader, included in the download with the libraries it ships with ([UnityDoorstop](https://github.com/NeighTools/UnityDoorstop), [HarmonyX](https://github.com/BepInEx/HarmonyX), [Harmony](https://github.com/pardeike/Harmony), [MonoMod](https://github.com/MonoMod/MonoMod), [Mono.Cecil](https://github.com/jbevain/cecil)).
- **Valve:** the [OpenVR Unity XR plugin](https://github.com/ValveSoftware/unity-xr-plugin) and [OpenVR](https://github.com/ValveSoftware/openvr) (BSD-3-Clause), which the files above come from.
- **3D models, textures and effects from the Unity Asset Store:**
  - [Low Poly Weapons VOL.1](https://assetstore.unity.com/packages/3d/props/guns/low-poly-weapons-vol-1-151980) by Simon Mercuzot
  - [FREE - Stylized Weapons](https://assetstore.unity.com/packages/3d/props/weapons/free-stylized-weapons-216047) and [FREE - RPG Weapons](https://assetstore.unity.com/packages/3d/props/weapons/free-rpg-weapons-199738) by Blink
  - [Simple low poly melee weapon pack](https://assetstore.unity.com/packages/3d/props/guns/simple-low-poly-melee-weapon-pack-396222) by Donthrukat
  - [FREE Low Poly Sci-Fi Weapons Demo](https://assetstore.unity.com/packages/3d/props/guns/free-low-poly-sci-fi-weapons-demo-sector-delta-cosmic-retro-377194) by The PolynestLab
  - [Aged Medieval Weapons-Free](https://assetstore.unity.com/packages/3d/props/weapons/aged-medieval-weapons-free-207255) by Alvarolopz98
  - [Low Poly ShotGun Weapon Pack 2](https://assetstore.unity.com/packages/3d/props/guns/low-poly-shotgun-weapon-pack-2-299660) by CASTLE BRAVO
  - [Free Pack - VR Hands](https://assetstore.unity.com/packages/3d/props/free-pack-vr-hands-402280) by PolyOne Studio
  - [FX Magic Plane II free](https://assetstore.unity.com/packages/vfx/particles/spells/fx-magic-plane-ii-free-25218) by FX_Kandol_Pack
- **[TaleSpire](https://store.steampowered.com/app/720620/TaleSpire/)** by Bouncyrock Entertainment. You need your own copy; no TaleSpire files are included here.
- Written with **[Claude Code](https://claude.ai/code)** by Anthropic.

## What it does
- **The board on a table in front of you:** walk around it, zoom it, and use both controllers as laser pointers to move minis, build, measure, lasso and click TaleSpire's menus on floating panels.
- **Dice:** on your forearm when you turn your palm up, thrown with your real hand, results in the sky.
- **First-person mode:** become a creature and walk the board at its height, with tracked hands. Other players see it move.
- **Weapons and powers in first person:** 32 guns, bows, magic and melee weapons plus flame and ice, using TaleSpire's own attacks.
- **Stream camera (Game Master):** a second window to share on Discord or OBS, with a camera you can park and orbit over the board.
- **Playing together:** nobody else needs the mod, and the mod sends nothing over the network itself. Everything others see goes through TaleSpire's own actions, and its rules for players, turns and hidden creatures are respected.
- **A VR MODE button on the main menu** switches to normal TaleSpire and back at any time.

All buttons are explained in [controls.txt](https://github.com/brownboy3000/talespire_vr_mod/blob/main/deploy/controls.txt) (installed next to `TaleSpire.exe`).

## Install
You need Windows, TaleSpire on Steam, SteamVR and a PC VR headset.

1. Download `TaleSpireVR-<version>.zip` from the [latest release](https://github.com/brownboy3000/talespire_vr_mod/releases/latest).
2. In Steam, right-click TaleSpire > **Manage** > **Browse local files**. This opens the folder with `TaleSpire.exe` in it.
3. Open the zip and drag everything inside it into that folder. If Windows asks about merging folders, say yes. No TaleSpire file is replaced.
4. Start SteamVR, then start TaleSpire from Steam.

**Updating:** drag the new zip's contents in the same way. Your settings and your creatures' weapons are not in the zip, so they are kept.

**Removing the mod:** delete `BepInEx`, `VRMod`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and `controls.txt` from that folder, then `XRSDKOpenVR.dll` and `openvr_api.dll` from `TaleSpire_Data\Plugins\x86_64` and the `XRSDKOpenVR` folder from `TaleSpire_Data\UnitySubsystems`. If you use other BepInEx mods, keep what belongs to them.

## Settings
The first start creates these files directly in the `VRMod` folder of your TaleSpire install, with every option explained. Edit them while TaleSpire is closed: they are read when it starts.

| File | What it holds |
|---|---|
| `TaleSpireVR.Core.ini` | VR or normal mode at startup (the main menu button saves this too), render resolution, desktop window, logging |
| `TaleSpireVR.Controls.ini` | the table view: starting scale, moving, zooming, turning, the laser pointer |
| `TaleSpireVR.Menus.ini` | the floating menus, the radial menu, the forearm dice |
| `TaleSpireVR.Graphics.ini` | draw distance, shadows, bloom, anti-aliasing |
| `TaleSpireVR.StreamCamera.ini` | the stream window and held camera, or turning them off |
| `TaleSpireVR.FirstPerson.ini` | first-person mode, hands and weapons |

## Other controllers
Default bindings are included for Valve Index, Meta Touch, Pico, Vive Focus 3, HP Reverb G2, Vive Cosmos, Vive wands and Windows Mixed Reality. SteamVR picks the right one, and you can remap anything in SteamVR's own Controller Bindings screen, as in any SteamVR game.

## What has and hasn't been tested
It has been played on one PC with one headset and Valve Index controllers. The bindings for other controllers, and playing as a non-GM player in a real session with others, have had little or no real-world testing. A TaleSpire update can break the mod at any time; the VR MODE button or `VR = false` in `TaleSpireVR.Core.ini` puts you back in normal TaleSpire.

## Licence
The mod's own code is free software under GPL-3.0 ([LICENSE](LICENSE)); its source code is at https://github.com/brownboy3000/talespire_vr_mod. The third-party libraries keep their own licences (texts and details in [licenses](licenses/README.txt)). The models, textures and effects are not under the GPL: they belong to their publishers, are used under the Unity Asset Store's licence and are included only as part of this mod, so please don't extract, reuse or share them separately.

---

# For developers

The rest of this file is how the mod works, for anyone changing it. Playing it doesn't need any of this.

## Building from source
The repository is the `VRMod` folder of a TaleSpire install or copy, with BepInEx installed there (the references in `TaleSpireVR.csproj` point at the game's `TaleSpire_Data\Managed` and `BepInEx\core`). You need the .NET 8 SDK, on your PATH or unpacked in `tools\dotnet`.

`weapons.bin`, `vrhands.bin` and `sigil_*.png` hold Asset Store content and are not in the repository: copy them from a release zip into `deploy\BepInEx\plugins\TaleSpireVR\`, or rebuild them with `tools\weapons` and `tools\hands`. Also not in it: `_debug`, `_decompiled`, your own `.ini` files and the local SDK and decompiler under `tools` (see `.gitignore`).

`tools\release\make-release.ps1` builds the release zip. `controls.txt` next to `TaleSpire.exe` is the file to edit; `build.ps1` copies it into `deploy\` for the repository.

A development setup is marked by a `VRMod\_debug` folder: it switches on the debug channel (below) and `SteamRestartPatch`, which keeps a copy of the game running from its own folder instead of relaunching the Steam install. Neither runs without that folder.

## What gets installed (by `build.ps1`)
| File | Purpose |
|---|---|
| `BepInEx\plugins\TaleSpireVR\TaleSpireVR.dll` | the mod |
| `BepInEx\plugins\TaleSpireVR\SteamVR\actions.json`, `bindings_<controller>.json` | SteamVR Input actions + default bindings for `knuckles` (Index), `oculus_touch`, `pico_controller`, `vive_focus3_controller`, `hpmotioncontroller`, `vive_cosmos_controller`, `vive_controller`, `holographic_controller` (written by `tools\bindings\make-bindings.ps1`; see Controllers below) |
| `BepInEx\plugins\TaleSpireVR\vrhands.bin` | first-person hands (PolyOne "Free Pack - VR Hands", see First person below) |
| `BepInEx\plugins\TaleSpireVR\weapons.bin` | first-person weapon models (32), each with its catalogue picture: "Low Poly Weapons VOL.1" (shotgun, M1911 pistol, Uzi, M4, M249, M107, RPG-7, musket), Blink "Stylized" (Sword1_1, Sword3_1_3 scimitar, axe B2, war hammer, bat 3, dagger 1, shield 2, Polearm2_2_2 glaive, Scythe1_3_2), Donthrukat melee (fire axe 5, crowbar 1, knife 2, sledgehammer 1), PolynestLab "Sector Delta" sci-fi injector SSG06, Blink "RPG Weapons" (Bow_Epic, Wand_Epic, Staff_Epic, Spear1H_Epic, Mace1H_Epic), Alvarolopz98 "Aged Medieval Weapons" (halberd, machete, throwing axe, shield), CASTLE BRAVO "Low Poly ShotGun Weapon Pack 2" (ShotGun_J) (see Weapons below) |
| `BepInEx\plugins\TaleSpireVR\sigil_disc.png`, `sigil_ring.png` | the flame and ice powers' palm sigil (two circles from "FX Magic Plane II free"; recoloured icy for Ice) |
| `TaleSpire_Data\Plugins\x86_64\XRSDKOpenVR.dll`, `openvr_api.dll` | Unity's OpenVR XR plugin (native) + OpenVR runtime API (from UUVR) |
| `TaleSpire_Data\UnitySubsystems\XRSDKOpenVR\UnitySubsystemsManifest.json` | lets Unity discover the OpenVR display/input subsystems |

Nothing that TaleSpire ships with is modified or moved, and none of TaleSpire's own settings files is touched.

The settings (see Settings above; `ModFiles.cs`, `VRConfig.Bind`) are six BepInEx `ConfigFile`s with .ini names directly in
`VRMod\`, read once in `Plugin.Awake`: what `Bind` finds missing is written with its description on the first start, and
options that no longer exist are dropped (`DropOrphans`, BepInEx's `OrphanedEntries` via reflection). `VRConfig.StartInVR`
is the one the mod saves itself (setting `.Value` saves its file). Per-campaign weapon loadouts and hand colours are saved
choices, not settings: `TaleSpireVR.Weapons.txt` and `TaleSpireVR.HandColors.txt` in `BepInEx\config\TaleSpireVR\`.

`Plugin.Awake` also sets TaleSpire's own `AppStateManager.UsingCodeInjection`: `_m` shows after its build id and it leaves
exceptions and errors out of its automatic reports to Bouncyrock (`AppStateManager.OnLogMessageReceived`,
`TryDispatchReportError`), so the mod's errors never reach their backend.

## How it works
- **XR boot** (`XR/XRBoot.cs`): creates and starts the "OpenVR Display"/"OpenVR Input" subsystems directly (multi-pass stereo). No Unity XR Management package needed.
- **Controllers** (`XR/SteamVRInput.cs`): SteamVR Input action manifest read through Valve's C# bindings (`OpenVR/openvr_api.cs`).
- **Camera** (`VRRig.cs`): TaleSpire's own board camera becomes the headset camera inside a rig we control (head pose from `InputDevices`). On a board the rig is anchored to TaleSpire's camera root, so camera height (Page Up/Down), go-to-creature etc. keep working; zoom scales the rig (IPD scales with it).
- **Menus** (`UI/UICapture.cs`, `UI/UIPanels.cs`): overlay canvases are moved to a private orthographic camera whose world space equals screen-pixel space, rendered to a texture shown on floating quads. The radial menu is drawn at screen centre and shown on its own quad at the creature's head.
- **Input into the game** (`Input/VirtualInput.cs`, `Input/VRPointer.cs`): a virtual mouse + keyboard registered with the Input System (re-enabled each frame if Unity switched them off: added while the game window wasn't focused, before the background behaviour became IgnoreFocus). Laser hits on panels become mouse positions; on the board the laser replaces TaleSpire's camera ray (`Patches/WorldPatches.cs`).
- **Picking** (`Input/PickCamera.cs`): TaleSpire's GPU ID-picking pass is re-targeted to a tiny camera that looks down the laser.
- **VR or normal mode** (`ModeSwitch.cs`, `Patches/PatchManager.cs`): `Plugin.Awake` applies only the `[AlwaysOn]`
  patches (`SteamRestartPatch`, on a development setup) and adds the `TaleSpireVR_Mode` object. If `VRConfig.StartInVR`, `ModeSwitch.StartVR`
  applies every other patch (`PatchManager.ApplyVR`) and creates `VRManager` (XR boot); otherwise nothing else is
  patched or created. Its main-menu button (a child of `UI/IMG_BottomBar`, 250 x 44 with an orange `Outline`, the font
  of `UI/TopBar/Copyright Text`; shown while `UI/TopBar` is active and no board is open) sits on the bottom bar, centred
  each frame between the right end of `#PANEL_LoggedIn/TXT_Welcome` (its decorations included) and the left end of
  `Label_BuildID`, narrowed to fit if that gap is small - away from TaleSpire's settings / exit buttons at the top right,
  where it used to be (it falls back there, under the music volume, if the bottom bar is missing). It switches at any time: to normal = `VRManager.SwitchOff` + `PatchManager.RemoveVR`
  (`UnpatchSelf`, then the AlwaysOn patches again) + `UICapture.SendUiScaleChanged`; to VR = `ApplyVR` + `SwitchOn`
  (or a first `VRManager`). Either way `[Mode] VR` in `TaleSpireVR.Core.ini` is saved. If XR does not start (SteamVR not
  running), `Failed` removes the patches again and the button says "VR didn't start - is SteamVR running? Click to try
  again". `SwitchOff`: first person is aborted (creature put back), our arcs / catalogue close, the stream window
  closes, `UICapture.Suspend` puts every redirected canvas back to Screen Space Overlay on its layer and undoes our
  hidden canvases / CanvasGroups / scene objects / enlarged panels / paused raycasters, `VRGraphics.Suspend` restores
  bloom, DOF / SSR overrides, anti-aliasing and light shadows, `VRRig.Suspend` restores the camera's clip planes,
  shadow distance and pivot rings and takes the camera out of the rig, `InGameTextPatch.Restore` /
  `GameViewportPatch.Restore` give TaleSpire its labels and 3D-view image back, `MainMenuText.Restore` its warning,
  every component is disabled, our root objects (`TaleSpireVR_*`) are hidden, the virtual mouse / keyboard are removed
  (the real ones made current), runInBackground goes back to TaleSpire's and `XRBoot.Pause` stops the XR display +
  input (OpenVR stays up). `SwitchOn` = `XRBoot.Resume` and the reverse (components re-attach / re-redirect on their
  own). Debug channel commands are polled by `ModeSwitch` while VR is off (only when `VRMod\_debug` exists). Debug:
  `vrmode on|off` (what the button does), `vrstate` (camera, canvases, mouse, UI scale...), `flatsnap` (the game
  window's own picture as `flat.png`).
- **Game Master and players** (`Roles.cs`): VR works in Player mode too. Everything that acts on the board asks
  TaleSpire's own rules first: `Roles.IsGm` (`LocalClient.IsInGmMode`), `CanControl` (`LocalClient.CanControlCreature`),
  `TurnBased` / `Cutscene` (`PlayMode` state; a cutscene needs `CutsceneManager.CutsceneFocus`), `TurnAllows` (GM, not
  turn-based, no current turn, or `InitiativeManager.CurrentTurnElement` is this creature), `CanAct` = control + turn +
  no cutscene (players), `CanSee` = not `IsExplicitlyHidden` and `IsVisible` (fog / line of sight). Uses: first person
  enters only creatures you control; the radial's hand-colour / weapons buttons only on those; weapons, flame and ice
  only touch `CanSee` creatures; attacks and moving need `CanAct`; Predator Vision, the stream window / held camera
  (`StreamView.CheckRole`: closes on a board as a player, reopens off it or as GM) and the hotkey row's Build Mode /
  Build Grid (`HotkeyStrip.Entry.Enabled`, greyed) are GM only (`BuildModeSwitch` also checks `IsInGmMode`). The mod
  sends nothing over the network of its own: everything others see is TaleSpire's own actions (drops, the live-move
  stream, attacks, rolls). Debug: `roles player|off` (`DebugAsPlayer`: act as a player who controls every creature),
  `playmode local free|turnbased|cutscene` and `turnfake <n> <i> | next | off` (`DebugMultiplayer.cs`: TaleSpire's play
  mode / a lineup of the n nearest creatures changed on this computer only, never sent), `cutscene force on|off`,
  `clientmode gm|player`.
- **Turn banner** (`UI/TurnBanner.cs`): in turn-based mode, when `CurrentTurnElement` or the turn queue's hash
  changes, the region of `UI_InitativeManager._normalPanel`'s visible graphics (TaleSpire's own initiative bar, already
  in the captured UI texture) shows on a quad 36 degrees above the horizon, 1.8 m away, for 9 s (overlay layer, yaw easing
  after the head like the dice banner), with a `SkyText` title above it in TaleSpire's font (`TaleSpireFont`, taken from
  its UI): "Your turn" for a player's own creature, else "<name>'s turn". `TurnBanner.Note(text)` shows a 3 s message
  there ("Not X's turn", "A cutscene is playing").
- **Cutscene screen** (`UI/CutsceneScreen.cs`): a player (not GM) during a cutscene can't have the headset taken over,
  so the camera TaleSpire would have shown (`CameraModeBase.CameraSettings` position / rotation / FOV, recorded by
  `ApplyToCameraPatch` as `LastDesktop*`) renders at 1280 x 720, 30 fps, through the same `CullAndRender` hook as the
  stream's held camera (with its own PostProcessLayer and TaleSpire's light buffers), onto a floating screen the width
  of the menu panel in front of you, titled "The Game Master is showing a cutscene".
- **Main menu** (`UI/MainMenuText.cs`): TaleSpire's "Early Access" warning (`UI/TopBar/Panel_BetaWarning/Early Access
  Text`, whose last line overflowed onto the copyright at VR menu sizes) is replaced by a note about this mod at +2 pt;
  its panel grows to the laid-out height (measured on the next check, from `textInfo` lines) and `UI/TopBar/Copyright
  Text` (TaleSpire's own text, its `<size>` +2) moves just below it.
- **Controllers** (`XR/ControllerProfiles.cs`, `tools\bindings\make-bindings.ps1`): the default bindings ship with the
  mod (`actions.json`'s `default_bindings`, one `bindings_<controller_type>.json` each); SteamVR picks the one for the
  connected controller and players remap in SteamVR's own Controller Bindings screen - there is no mod menu for it.
  Run `make-bindings.ps1` after changing actions. Stick controllers (Touch, Pico, Focus 3, Reverb G2, Cosmos): X / Y =
  zoom out / in, A / B as on the Index, the grip's click (trigger mode) = the trackpad presses (elevate / rotate
  modifiers, ping taps, the two-handed hold; the Cosmos uses its bumpers instead), the grip's grab at 0.9 / 0.8 =
  measure / lasso. Vive wands: trackpad position = the sticks, dpad clicks (left: N zoom in, S zoom out, centre
  elevate; right: N right click, S middle click, centre rotate), menu buttons = Build Mode / menu, grip buttons =
  measure / lasso. WMR: sticks as Index, trackpad dpad as the wands. Extra actions for all: `TriggerValue` /
  `GripValue` (vector1, per hand) and `ThumbRest` (any touch sensor). `ControllerProfiles.Of(hand)` reads
  `Prop_ControllerType_String` of the hand's device (every 3 s) and gives per-type values: `FingerTracking` (Index
  only), `HasTrackpads` (Index, wands, WMR; without them Predator Vision needs both grips held 0.6 s), `WristBack` /
  `FingerDown` (where the hand sits relative to the tip pose when there is no skeleton) and `WeaponLean` (Index 15
  degrees; others auto: the controller's pointer direction against the grip, clamped -10..50, smoothed). Hands without
  full finger tracking (`GetSkeletalTrackingLevel` below Full, or a non-Index profile) use `FPHands.SynthCurls`: index
  from the trigger (0.12 -> 1), the other three from the grip (0.18 -> 1), the thumb resting (0.22) or on the thumb rest
  (0.55), closing over a held grip with the trigger (0.6 -> 0.9), half down on a trigger-only pull (0.45); the right hand
  opens flat while casting a power. Debug: `controller <type>|off` (pretend a controller type), `fingers <trigger>
  <grip> <thumbRest> | off` (made-up values for the synthetic fingers).

## Developer loop
```
powershell -ExecutionPolicy Bypass -File VRMod\build.ps1             # build + install
powershell -ExecutionPolicy Bypass -File VRMod\dev-relaunch.ps1 -ToBoard
```
Debug channel (`VRMod\_debug`): create `snap.request` to save `eye.png`, `ui.png`, `status.txt`; write a command into `cmd.txt`
(`click x y`, `rclick x y`, `probe x y`, `aimcreature [n]`, `pick`, `panel`, `esc`, `raise m`, `big`, `status`,
`drive stickX stickY zoom seconds`, `turn degrees`, `gfx info|ao|ssr|bloom|lights|pointshadows|sunshadows|shadows 0/1`,
`gfx sdist meters`, `gfx lightshadows count [meters]`, `gfx far meters`, `gfx cascades n`, `gfx rs scale`, `gfx uicam|pickcam 0/1`,
`grip measure|lasso left|right 1/0`, `trig left|right 1/0`, `stick left|right x y` (0 0 releases),
`btn rotate|elevate|zoomin|zoomout 1/0`, `tree <path> [depth]`, `displayitems`, `menuscale s`, `sidescale s`,
`hotkey n` (hotkey strip button), `kbtype text` (VR keyboard; ~ = Back, # = Enter, _ = Space), `aimkey label` (aim at a keyboard key, then `trig`), `fakehands 1/0` (draw both lasers from fake poses below the
head, for snapshots while the controllers sleep), `uicamclip near far`, `rulermode n`, `lassocircle radius`, `hold frames`, `sharp 0..1`, `renderers [range]`,
`uisnap` / `uidiff`). Commands are polled every 0.5 s and the file is overwritten, so leave >= 0.6 s between them.
Test clicks with `trig left|right 1/0` (goes through SteamVRInput like a real trigger); `pull`/`hold` inject the press
inside VRPointer and skip code that reads the real trigger. Unity exceptions land in Player.log
(`%USERPROFILE%\AppData\LocalLow\Bouncyrock Entertainment\TaleSpire`), not in LogOutput.log; side features in
VRPointer run inside `Safe(...)` so a fault there is logged once and never blocks clicking.

Side panels: TaleSpire slides the left / top-right / floor-slider panels off screen with `RectTransformTweener`s whose
end positions fit the normal panel width (LeftPanel 0 -> -400, RightPanel 0 -> +400); enlarged 1.5x, a third stayed on
screen when the toolbars collapse (seen after a board change). `SlideAsideOfEnlargedPanelsPatch` scales those slides by
SidePanelScale (and the player-portrait slide). In VR, TaleSpire's right-drag camera pan / Ctrl+right vertical move are
off (`NoRightDragPanPatch`: `MouseManager.groundDelta` and `DeltaWorldCalc.Delta` read zero during
`RootTargetCameraMode.CallLateUpdate` while the right button is down); with a radial open, an A press on a different
pickable thing gets a synthetic follow-up tap (`VRPointer.RadialRetap`). Trackpad taps ping (`Input/PingTap.cs`,
`GestureManager.PingAtLocation` / `PingCreatureAtLocation`); `BiggerPingPatch` draws `PingGesture` at 2x (its own scale
is 0.5).

UI notes: the menu camera only draws the UI layer, and nested canvases keep their own layer, so `UICapture` moves
nested canvases under redirected roots onto it (the right-hand floor slider was on Default). It also keeps a deep
near/far range because overlay canvases ignore depth and some parts sit far off the canvas plane (atmosphere dial
dots at z=16). `SidePanelScale` scales LeftPanel and the floor controls around their screen edge with shortened
vertical offsets (recomputed from the parent height each scan; only y offsets are ours, TaleSpire slides x), and
`SlideAsideOfEnlargedPanelsPatch` widens the client-list slide to match. When TaleSpire changes a panel's vertical
offsets, the base follows per edge (a whole-panel shift moves both; an edge TaleSpire set takes its value; an edge it
left keeps its base) - taking our own shortened edge as a new base would shrink the panel on every change, and the floor
slider's track and pins with it; each re-base is logged ("Enlarged '...': TaleSpire changed it"). No re-sizing while a
press that began on a menu is held (`VRPointer.UiPressHeld`): any size change of the floor slider makes TaleSpire put
every pin back from the current heights (`UI_FloorControllers.OnRectTransformDimensionsChange`), and the water height
only eases towards a dragged pin (8/s), so the pin being dragged jumped back. Debug: `floorpanel` (offsets, bases,
heights of the enlarged panels and the pins' track).

Radial isolation: `GUIManager/Ts/MapMenuManager` gets its own nested Canvas + GraphicRaycaster on layer 8 (with our
cursor canvas). While a radial menu is open the UI camera draws only layer 8 and every other GraphicRaycaster is
paused, so nothing else shows through the radial window or steals its clicks. Each radial option's name label
(`TXT_Title`) is its own nested canvas on the UI layer, so `FixRadialNestedLayers` moves the radial's nested canvases
to layer 8 when a radial opens (and every 0.25 s while open; options are pooled and created on demand); TaleSpire's
mouse-hover text canvas (`SingletonHub/_MouseManager`) is on layer 8 too.

VR keyboard (`UI/VRKeyboard.cs`): shown while `EventSystem.currentSelectedGameObject` is a TMP_InputField/InputField
and the panel is visible; keys edit the field's text/caret directly (TMP fields only read OS key events). Each hand
hovers/presses keys independently. Enter calls `SearchInputHandler.SendSearchParameters()` for search boxes (they only
search when TaleSpire's Accept action fires during onEndEdit); other fields get a virtual Enter tap and are deselected
the next frame. Right-stick menu scrolling feeds `VirtualInput.Scroll` while the active laser is on the main panel
(`VRPointer.StickScrollsMenu` also stops VRRig's up/down and snap turn). TaleSpire's UI drops the wheel's x, so
left/right is sent as the vertical wheel over horizontal ScrollRects (they map it to sideways) and ignored elsewhere. `Patches/UIInputPatches.cs` scales floor-pin drag deltas (FloorSliderSpeed).

Picking: `PixelPickingManager.SingleSelectMode` keeps `_lastCamera` / `_pickCommandBuffer` in statics. Our pick camera
survives board reloads, so `PickRenderPatch` detaches and re-attaches when the buffer instance changes (otherwise
picking silently stopped after "Exit to Main Menu" and re-entering a board). Menu size in VR =
supersample factor x `MenuScale`, applied through the `SettingsManager.UIScale` getter (`dev-relaunch.ps1`'s click
coordinates assume MenuScale 1.2). Create `crop.request` for a full-resolution crop of the eye around the menu panel (`eyecrop.png`).

Grip + trigger features (the grip actions are modifiers; the gesture starts on that hand's trigger press while the
laser is on the board, and VRPointer withholds that trigger's click): `Input/MeasureTool.cs` drives TaleSpire's `RulerBoardTool` with synthetic camera clicks (ruler type
from `RulerBoardTool.OnRulerModeChange`; indices: 0 circle, 1 line, 2 cone, 3 box). `Input/CreatureLasso.cs` selects
creatures inside the swept loop via `LocalClient.ToggleLassoMembership`; TaleSpire's `CreatureGroupMoveBoardTool` then
carries the group. `Patches/InGameTextPatches.cs` moves TaleSpire's screen-pinned labels (ruler distances, flying
heights) onto a world-space canvas. One gesture per squeeze: `HandButton.Armed` is set when the grip goes down and
cleared when it is let go or a gesture starts (`Disarm`), so a grip that stays "held" (firm hand, grab-mode release
threshold, 1.3 in the Index default bindings) no longer turns every trigger pull into another gesture. The ruler left
up after measuring keeps TaleSpire's `RulerBoardTool` active, which takes every board click; the next plain board
press calls `MeasureTool.TryPutAwayRuler` (back to the default tool) and is held back 7 frames, then goes through.
`status.txt` includes SteamVR GPU timing, a main-thread CPU split per camera and draw/shadow-caster counts.

Stream view (`Stream/`; only created when `[StreamView] Enabled` in `TaleSpireVR.StreamCamera.ini` says so; GM only on a board, see Roles
above): `StreamWindow` is a plain Win32 window on its own thread (Unity can only open extra windows
on extra monitors); finished BGRA frames are triple-buffered and drawn with GDI `StretchDIBits`, so the game never
waits on it (Discord/OBS window capture works). `StreamView` feeds it with `AsyncGPUReadback`:
- following: at end of frame the left eye (`XRDisplaySubsystem.GetRenderTextureForRenderPass(0)`, stored top-down)
  is blitted, cropped to 16:9, into an RGBA sRGB texture (`StreamFps`);
- held: a separate camera at the head pose captured on hold (roll removed, FOV = the eye's full width at 16:9).
  TaleSpire culls tiles into shared GPU buffers and queues their draws per camera (`BoardPresentation.CullAndRender`,
  called from `ActiveCameraManager.LateUpdate` with the headset camera). `HeldViewRenderPatch` runs a second
  `CullAndRender` for the held camera just before TaleSpire's own and renders it at once (`Camera.Render`), before
  the headset pass overwrites the buffers; `HeldViewNoPickingPatch` skips picking during it. TaleSpire's placeable
  lights are command buffers on the headset camera (`Ts*Lights`, AfterLighting) and are added to the held camera
  too; it has its own PostProcessLayer (SMAA), far clip (footprint-based when it looks down at the board) and sun
  shadow distance. Cost: ~5-6 ms main thread per held frame on a very busy board (draw calls; every object it sees is drawn
again, whether or not the headset also sees it), so around a fifth of the frame rate at `HeldViewFps` 30.
The held camera uses an explicit off-centre projection (`Matrix4x4.Frustum` from `_tan` = tangents of its left, right,
bottom, top edges; aspect set to 16:9 - left alone, Unity may take the aspect from the game window), by default the
exact stream crop of the left eye (`EyeCropTangents`). `HeldCameraMarker` draws the green wireframe on the overlay
layer, which the held camera excludes: corner lines from that projection - inset `HeldViewFrameInset` (0.1) of the
picture on every side (`StreamView.MarkerCorner`: viewport 0.1..0.9), so it frames less than the stream shows - end
where they meet TaleSpire's ground plane (y = 0, `CameraController._groundPlane`) or at the far-plane corner; the
outline is the ground cut by that inset frustum (`FloorSection`). `stream markersnap` also logs the corner lines' ends
in the held picture (they read 0.100 / 0.900). Release flies the stream camera back (`Mode.Returning`, `UpdateReturn`): eased lerp/slerp
from the held pose to the live left-eye pose (`GetStereoViewMatrix(Left)`, head tilt included) with a 0.15x arc,
tangents/near/far/shadow blended to the eye's, duration = clamp(max(distance / (0.5 x stick speed), angle / 120 deg/s),
0.75, 3) s; then the mirror takes over seamlessly. `stream markersnap` renders one held frame from behind with the
overlay included (to check marker vs picture). Readback is in the textures' own RGBA format and `StreamWindow` swaps to BGRA on its thread:
requesting BGRA made Unity log "B8G8R8A8_SRGB doesn't support ReadPixels" every frame, which SRDebugger's corner
button (`SRDebugger/Trigger`, top-left of the menu) flashed as a red "!".
Debug: `stream hold [pitch] | release | toggle | open | close | snap | far <units>`, `cambufs game|<camera name>`;
a snapshot also writes `stream.png` (the next stream frame, as the window shows it).

Two hands, one mouse (`VRPointer.ArbitrateHands`): `VRHands.ActiveHand` owns TaleSpire's board cursor and picking
(`WorldRay`) and changes only on a press - trigger = its hand, A / stick click = right hand - never on hover, never
while the other hand has a button held, and not for board presses while something is carried or being placed
(`PlacementLocked`: creature move/group move/spawner, prop/tile/slab placement, GM block placers). TaleSpire's
picking reads back 2-3 frames late, so a press that moves control onto the board is held back 7 frames (a release
during the wait still becomes a 2-frame tap). The other laser resting on a menu panel only drives the UI cursor
("cursor" hand, `VRHands.HighlightHand`), which turns TaleSpire's board cursor off (`WorldMode` false); a board press
in the first frames after that (`_worldFrames` < 7) is held back too - pressing in the very first frame always
missed, which looked like both triggers dying for as long as a laser rested on the menu. Before this, hovering made
that hand active, which stole the board from the other hand and made items being placed jump between lasers.
More debug: `handaim left|right ui x y | radial x y | creature n | board | off` (fixed laser per hand), `btn a|menu 1/0`,
`btntap a|menu|stick|build frames` (short presses: TaleSpire's radial needs a tap); status.txt shows each hand's target, the
hand in control, the press delay and the current board tool.

Build Mode (`Input/BuildModeSwitch.cs`: the left stick click, the hotkey row's Build Mode and Build Grid buttons) does
not send TaleSpire's B key, which silently does nothing (a) while a text box has focus (`ControllerManager.
KeyInputEnabled` = not `InputFieldManager.IsInputFieldActive()`, and a clicked library search stays focused) and
(b) unless the plain tool is on: `GUIManager.SetBuildMode` only switches from `DefaultBoardTool` / `DefaultBuildingTool`,
so with a slab / tile / prop on the pointer (`SlabBuilderBoardTool`, `TileBuilderBoardTool`, `PropPlaceBoardTool`),
the selection or build-plane tool, or `PickPlacableBoardTool` (which the slide modifier - the latched Shift - keeps
switching to) B was dead until A dropped it. It ends typing (`VRKeyboard.Done`), then on: `DefaultBoardTool` (if
another tool is on) then `DefaultBuildingTool`; off: the current tool's `Back()` (up to 4 times) until
`DefaultBuildingTool`, whose `Back()` leaves (`BoardBuilder.SetMode(false)` + `DefaultBoardTool`), with
`SwitchToTool<DefaultBoardTool>` as the fallback. Not with dice in hand (switching tools would throw them), loading,
or as a player. Logged with the tool it ended on. Debug: `btntap build 3`, `hotkey <index>` (8 = Build Grid), `tool
<BoardTool type name>` (switch TaleSpire's tool, e.g. `TilesSelectionBoardTool`).

Orbit (`StreamView.TryStickClick`, called by VRPointer on a right stick click unless `PlacementLocked`; a used click is
not passed on as a middle click): while held, circles the point the right laser hits (`StreamView.PivotPoint`:
`CameraController.Raycast.GetRayPoint`, QueryAll - tiles, props, creatures, as the laser's dot; the ground y = 0
where it hits nothing) at the camera's height, at MoveSpeed x zoom factor / 16 along the circle (<= 12.5 deg/s;
straight above the point it turns in place), slerping (tau 0.4 s) to face the point, so a raised pivot tilts the
camera to keep it centred; `HeldCameraMarker.SetPivot` rings the point. A second click stops it.

First person (`FirstPerson/`):
- `FirstPersonMode` (order 30035) owns the mode. Enter/exit run inside `FPBlink` (0.5 s closing, the switch while dark,
  0.5 s opening). Entering: scale = creature eye height (`CreatureBounds.max.y` x EyeHeight) / your HMD height at
  that moment (fixed for the visit, so real crouching works); yaw so you face `-Rotator.right`. The creature is hidden
  with `Renderer.forceRenderingOff` (TaleSpire toggles `enabled` itself; rescanned every 0.5 s), its BounceColliders are
  disabled (like `Pickup` does, without the RPC), and every frame it is moved locally to your feet facing your look
  (`JumpTo`, `JumpRotation`, `CorrectPos/Rotation`; idle creatures aren't position-corrected, only carried ones in
  `MovableHandle`). It is kept as `LocalClient.SelectedCreatureId` (lasso cleared), because `CreatureMenuBoardTool`
  offers Attacks only when another creature is selected and `AttackPlayer` sends the selected creature's
  `transform.position` with the attack op - so attacks start where you stand, for everyone. TaleSpire's toolbars are
  collapsed (`BoardToolManager.CollapseUI`, re-applied each frame; `ExtendUI` on exit if they were open).
  `VRRig.BeginFirstPerson` saves the table view (scale, yaw, offset, anchor), `SetFirstPersonPose` places the rig each
  frame (UpdateBoard is skipped: no zoom/stick moves/camera height; far clip and shadow distance from [FirstPerson]),
  `EndFirstPerson(shift)` restores it with TaleSpire's anchor moved by the creature's displacement. Exiting drops the
  creature with `CreaturePresenter.TeleportCreature(asset, feet, rotation)` (networked pickup + drop; the drop
  re-enables its colliders) when it moved or turned, otherwise (and for `home`) puts it back locally; the rotation is
  TaleSpire's own `JumpRotationTowards` maths (`RotationFacing`; `fp facetest` checks it without moving anything).
  `creature.Flying` is read every frame (switching it from the menu starts flying / falling).
- Others see you walk (`FirstPerson/FirstPersonSync.cs`): only creatures you control can be entered. While the creature
  may move (`Roles.CanAct`), it is put on TaleSpire's own `BoardToolManager.MovableHandle` (`Attach(creature, false)`,
  checked every 0.25 s; not while TaleSpire's own creature / group / key move tool uses the handle) - the same stream of
  positions TaleSpire sends while anyone carries a mini (PUN `OnPhotonSerializeView`; other clients lerp to it), so
  everyone sees it walk live at no extra cost and without any traffic of the mod's own. Once you have stood still for
  1 s the position is committed with a normal drop (`CreatureBoardAsset.Drop`; the creature is set back to idle and its
  colliders off again), and the next walk re-attaches. When it may not move (not its turn, a cutscene for players),
  the stick does nothing (walking, flying, jumping) and the sky shows "Not X's turn" with a buzz (`NoteLocked`), while
  you can still look and step around in place; exiting then puts it back where it was last placed. When its turn comes
  it attaches again ("moving allowed again" in the log); when its turn ends while moved, the move is committed first.
- Walking: the body (upright cylinder, radius 0.6 x `ScaledBaseRadius`, height = model height, 2/3 crouched) sweeps
  with `PhysicsEngine.CylinderCast` against the "Tile", "Board Mat", "Props" layers (never creatures). Grounded: a
  floor-level move (bumps up to 0.25 pass under the body; floor level first, so doorways and arches work); if a ledge
  stops it, `TryStartVault` probes the ledge top one body radius + 0.15 ahead (within StepHeight, capped by the
  ceiling; up/down casts are 0.8x thinner and ignore non-ceiling normals so a wall you touch isn't a ceiling), checks
  there is room on it, and climbs: 0.35 + 0.18 s per unit, rising over the first 80% and moving over the edge from 35%
  on, input locked, so the eyes are above the ledge before they cross its edge. The same runs in the air: a jump
  (JumpHeight 2.5) while walking into a ledge climbs as soon as the ledge top is within StepHeight of the feet, and the
  walk carries on over it (reach 2.5 + 2 = 4.5). A downward sphere cast (ignoring start overlaps) finds the floor; drops over 0.6 are falls. The eye height
  follows the body through `SmoothDamp` (0.05-0.14 s, clamped to 0.4 x eye height of lag in the air) so launches,
  landings and steps have no sudden starts or stops; jumps have a 0.067 s crouch first (Gravity 18). The ground plane
  y = 0 counts as floor when nothing is hit. The body follows the HMD's xz (real steps, no collision); the stick moves
  it with collision and sliding. Turning uses the table view's SnapTurnDegrees / SmoothTurn settings.
- `FPVignette`: a head-locked sphere (overlay layer, queue 4990), alpha by angle from straight ahead (clear to 14
  degrees, 0.93 by 46), faded in with the crouch.
- Status effects: TaleSpire plays them as persistent `ActionTimeline`s instantiated under the creature
  (`EmotingMovableBoardAsset.GetActionTimeline`; state 0/1 when added, 2 when removed). Hiding the creature skips every
  renderer under an `ActionTimeline`, so they keep playing around the invisible body. `CutOutEffects` (each frame after
  the creature follows you) turns off effect meshes whose bounds contain the eye, and shrinks particles within
  `CutoutRadius` (0.45 x eye height, at least 0.15) of the eye to size 0, restoring them as they move away (keyed by
  system + random seed) and on exit. The effects themselves are untouched.
- `FPHands` rebuilds the hands from `vrhands.bin` (exported once with Unity 6 batch mode by `HandsExporter.Export`,
  since TaleSpire's 2021.3 can't load newer bundles; `tools\hands\export-hands.ps1` redoes it): transforms, mesh, bone weights, bind poses, per-hand wrist frame
  (wrist -> middle knuckle, index -> pinky knuckle) and per-finger-joint bend axes from the idle pose. Each frame the
  forearm bone is placed so the wrist (at its rest pose on it) lands on SteamVR's skeleton wrist, aligned by the same
  knuckle frame (`GetSkeletalBoneData`, model space, all 31 joints, z negated; root pose from the skeleton action).
  Fingers (`FollowFingers`): each model finger segment (joints 1-2-3-4, the 4th found by name) is turned with
  `FromToRotation` to point along SteamVR's matching segment (index 7-10, middle 12-15, ring 17-20, pinky 22-25, thumb
  2-5 or 3-5 depending on where the model's thumb starts), from the rest pose every frame - real curl and spread, no
  overlapping fingers. A skeleton read that fails keeps the last one for 0.3 s (`Skeleton.Usable`). Without a
  skeleton, or without full finger tracking (other controllers than the Index): the tip pose placed by the profile's
  `WristBack` / `FingerDown`, with `SynthCurls` (see Controllers above) curling about the knuckle-line axis. The root
  scale matches SteamVR's hand size. Standard shader, colour HandColor, emission 0.3 (readable on dark boards).
  Skeletons are only read while first person is on (`SteamVRInput.WantSkeletons`).
- `RadialExtraButton` draws one button into the captured radial (layer 8 canvas) above the top option of the root
  `MapMenu` (`_menuStack` count 1; item 0 is always at the top) and clear of that option's name label (measured from
  its `_textMesh`: TaleSpire puts labels outward from the ring), with a TMP label copying an option label's font
  (OptimusPrinceps SDF) and fade; or alone for the exit-only fallback menu (`OwnMenuOpen`). VRPointer hit-tests it in
  UI pixels, parks TaleSpire's cursor at the menu centre while it is hovered (TaleSpire hovers options by direction)
  and keeps its click. In first person the lasers always point (they are drawn only with a grip held or a radial
  open: `PointerVisible`; white) and the trigger is not sent to the board. A (`VRPointer.FirstPersonA`) never reaches
  TaleSpire itself: its pointer is a squeezed grip's laser (right first), or with a radial open the right one
  (`AButtonPointer`; `ArbitrateHands` gives control to that hand, so picking follows it). No pointer: your creature's
  menu (`CreatureMenuBoardTool.SwitchToTool`). Pointer on the board: once picking follows that laser, a creature other
  than yours or a placeable with a state machine (doors, chests; `PointsAtSomethingWithMenu`) gets its menu by a
  synthetic 2-frame A tap (an open TaleSpire menu is closed first and RadialRetap's follow-up tap opens the new one;
  the creature whose menu is already open: nothing); anything else closes an open menu or opens yours. Pointer on the
  radial window: closes it. In first person the name of the menu's owner (`RadialTargetName`: creature name, a door's
  `PlaceableHandle.DbEntry.Name`, "Dice") is a TMP label (option label font, alpha forced to 1) in a 1.6 x 0.8 box of
  the ring's free inner radius (`_innerRadius`: nearest option edge to the root centre), auto-sized and ellipsised,
  hidden below 30 px x UI scale. Measure/lasso, the menu panel, Build Mode (left stick click at rest = ruler), middle
  clicks, stream hold/orbit are off. Jumps crouch 0.067 s first (was 0.2).
- Radial catch area (`RadialExtraButton.InCatchArea`, used by `VRPointer.Evaluate`): from the open `MapMenu` stack, every
  active option's rect padded by half its size, a circle per ROOT / SUBROOT ring (outermost option edge + half an option),
  our buttons, and while the colour arc is open its whole fan (between its end angles, out to past its swatches); the
  rest of the square window lets the laser through to the board. Unknown layout = whole window. `HoverAt` (from
  VRPointer) marks a laser as ours on our buttons or in the fan beyond TaleSpire's ring: TaleSpire's cursor is parked at
  the menu centre and the click is ours (`Press` on a button; nothing in the empty fan).
- Hand colours (`FirstPerson/HandColors.cs`, `RadialExtraButton`): a unique creature's (`CreatureBoardAsset.IsUnique`)
  root menu gets a blue hands button beside the eye (both 1.35 option radii from the centre line); it toggles an arc of
  13 swatches (Default + `HandColors.Palette`) from 168 to 12 degrees at the buttons' height + 2.4 option radii, 0.78
  option radius each, names placed outward. The arc belongs to that creature's open root menu (closes with it, on a
  sub-menu, or after a pick). `RequiredRadius` grows the radial window (`UIPanels.UpdateRadial`, same size per pixel).
  Saved in `BepInEx/config/TaleSpireVR/TaleSpireVR.HandColors.txt` as "campaign creature key" (`CampaignSessionManager.Id`,
  `CreatureId`); `Prune` (every 3 s while entries exist) drops entries of creatures on the board that are not unique.
  `FirstPersonMode` colours the hands on entry (`FPHands.SetColor`: colour + 0.3 emission) and on a pick for the creature
  you are (`HandColorChanged`). Debug: `uniques` (list), `uniques fake <n>|off` (treat the n-th nearest creature as
  unique this session only), `radialbtn main|hands|swatch <n>` (aim the right debug laser; then `trig right 1/0`).
- Weapons (`FirstPerson/WeaponLoadouts.cs`, `FPWeapons.cs`, `WeaponFx.cs`, `RadialExtraButton`): unique creatures' root
  menus get a third button (red, crossed sword + staff drawn as SDF capsules); the three buttons sit 2.7 option radii
  apart. It toggles an arc (Unarmed = hand icon, the creature's weapons, + ) centred over the top at the buttons' height
  + 3.4 option radii, 2.5 option radii apart along it (at most 17 degrees); items beyond what fits in the colour arc's
  168 -> 12 degree spread go on further arcs 2.5 radii further out, shared evenly, the first ones inside (a hovered
  button is drawn last so its name stays readable over the outer arc); with more than 24 items the buttons, radii and
  row gaps shrink to 0.8 so three rows fit the radial window; a pick equips (`WeaponLoadouts.Equip`) and closes
  the arc. Both buttons only show on creatures you control (`Roles.CanControl`). + opens
  the catalogue: TaleSpire's MapMenuManager gets a CanvasGroup at alpha 0 (the menu stays open), our canvas shows title,
  Return and one 9-sliced rounded tile per `WeaponLoadouts.Catalogue` entry (3 / 4 / 5 / 6 / 7 columns for up to 6 /
  12 / 20 / 30 / more, tiles 118 / 108 / 100 px from 5 columns on,
  the weapon's picture from `weapons.bin`, an SDF snowflake for Ice, names shrunk to fit); while it is open the catch area is only
  its rect and all of it is ours (TaleSpire's hidden options can't be hovered). Return closes it (the arc stays); the
  radial closing (A) closes it. Saved in `BepInEx/config/TaleSpireVR/TaleSpireVR.Weapons.txt` ("campaign creature held|- id,id"),
  pruned like hand colours. Models: `weapons.bin` ("TSVRWPN2") from a Unity 6 batch export (meshes baked to the prefab
  root, PNG textures at <= 1024, Standard material values - URP materials, the sci-fi pack's, read from their saved
  `_BaseMap` / `_BaseColor`), each followed by a 160 px catalogue picture (rendered over black and over white for its
  alpha); rebuilt as Standard materials (+ the albedo as a 0.12 emission). The file (~67 MB, 32 models) is indexed once
  (`FPWeapons.Index`: each model's offset, skipping meshes and textures with `SkipModel`) and a model is only built when
  first held (`EnsureModel` -> `LoadOne`); `FPWeapons.IconOf` reads only the pictures for the catalogue; the loader
  also still reads "TSVRWPN1" files (no pictures).
  In the hand (`FPWeapons.TryPose`): each model's grip is the point on its handle axis level with the middle finger,
  the hand's index->pinky and wrist->knuckle directions in model space, and the middle knuckle's offset from that
  point in real metres; the world pose comes from `FPHands.TryFrame(Right)` (the same skeleton / controller frame the
  hands are posed with, so it is identical in `VRHands.ComputePose` and after `FPHands.Update`). Shotgun: pistol grip
  slanting 25 degrees back; sword (scaled to 0.85, ~1 m): handle 20 degrees diagonal across the palm, hand a few cm
  below the guard. The other weapons use two helpers with values measured off the exporter's `Side` views: `Gun(scale,
  grip point, grip axis (down the grip), muzzle, barrel direction)` - barrel forward, grip in the fist - and
  `Melee(scale, grip point, handle axis (to the head), edge direction, knuckle height, tip, ray start, strike
  segments...)` - the handle 20 degrees across the palm like the sword, edge / hammer face forward. The shield is held
  by its back handle with its face normal along the fingers' direction (forward with the usual grip), so it covers you;
  its ray starts at the middle of its face. Newer grips: the bow is a `Gun` held upright by its riser (grip axis down
  the riser, "muzzle" at the arrow rest, the ray straight out of its front along the arrow); the combat shotgun a
  `Gun`; the wand a `Gun` at half scale lying along the hand, tip forward; the fire staff a `Melee` held upright whose
  `Caster` ray starts at the crystal and points where the hand points; spear, halberd, glaive and scythe (by the lower
  shaft), morning star, scimitar, hand axe, machete, knife, sledgehammer are `Melee`; the kite shield a custom `Grip`
  like the shield (strike segments across its face). On top of that every weapon leans forward, turned about the
  back of the hand's axis through the grip point (top / tip away from you, no left-right turn), to match the angle of
  the controller's handle in the hand (`FPWeapons.LeanFor`: the profile's `WeaponLean` - Index 15 degrees - else
  measured from that controller's pointer pose; `wpn tune <mesh> lean <deg>` changes it live, for all weapons).
  A gun or blade replaces the right hand: `FPWeapons.HidesHand` (a held weapon with a mesh) ->
  `FPHands.RightHidden`, and `FPHands.Update` collapses that hand and forearm (as for an untracked hand) while still
  using its frame for the hands' scale; the left hand, and the right one unarmed or with the flame, are posed as usual.
  (Fixed finger poses round the handles were tried and dropped: the PolyOne hand can't wrap them without clipping, and
  every new weapon would need its own.) The right ray (`VRHands.ComputePose`) is replaced by the weapon's
  (`TryTipRay`: from `Grip.RayFrom` along the barrel / blade - the muzzle for the shotgun, the crossguard for the
  sword, so near menus are easy to reach) plus how much of it is inside the model (`HandState.Hidden`: `RayFrom` to
  `Tip`); `DrawHand` draws the laser and its start dot only from there on (the overlay draws on top of everything, so
  a line over the blade would show through it), nothing of the line if the ray ends sooner (the hit dot still shows).
  The right trigger (`FPWeapons.Trigger`, from VRPointer) arms on a press that did not start on a
  UI target / our buttons. Effects reuse the Burning status's fire mesh (`TLA_Burning` / `vfx_fireBunch`,
  `Taleweaver/PlacableFireShader_anim`, `_Transition` via MaterialPropertyBlock: it dissolves in up to ~0.3, we use 0.5
  as full): shotgun = 6 puffs along the barrel (staggered, growing, dissolving in 0.4 s, one with its lights), sound
  `ts_sfx_magic_missile_hit` from 0.1 s at pitch 0.85 through the Sfx mixer group; with the right laser visible the
  nearest creature cylinder on the ray (in front of `WorldHitDistance`) gets `CreatureManager.AttackCreature`
  (`WeaponDef.Attack` - TLA_LaserGreen for the sci-fi injector and the bow, TLA_MagicMissileAttack for the wand and the
  fire staff - else ShotgunAttack, default TLA_LaserRed). Every ranged weapon (`FireGun`) fires one shot per press from
  its own `Grip.Tip`; `WeaponDef.Shot` (`ShotStyle`) picks the look: `Gun` = the puffs along the barrel; `Bow` = no
  flash, `WeaponSounds.Twang` (made at first use: a Karplus-Strong plucked string ~150 Hz with a low thump and an air
  "fwip", 0.6 s); `Magic` = a purple point light (`WeaponSounds.Flash`) and `WeaponSounds.Zap` (rising, detuned sines,
  0.5 s); `FireBolt` = four growing fire puffs out of the crystal (`Blast`) with the flame's whoosh. The attacks are
  only sent if the creature you are may act (`Roles.CanAct`; else "Not X's turn" in the sky), and only reach creatures
  you can see (`Creatures()` = `Roles.CanSee`, also for melee, flame and ice). Melee: each weapon's
  strike segments (`Grip.Strikes`, pairs of points; the sword's is guard -> tip, the axes and hammer add their heads,
  the crowbar its hook, the shield two crossing diameters of its face), 30 samples each, tested every frame while it
  is held; with the trigger held, >= 3 samples of one segment (a tenth of it) inside a creature's cylinder (base,
  `CreatureBounds` max y, 0.8 x its horizontal extent) = a hit (SwordAttack, default Hit - `AttackEmotes.Hit`, the
  radial menu's Attacks > Hit - `sfx-SwordBigHit`). A struck creature
  (`_struck`) can't be hit again until no sample is inside it (the blade has left it), trigger held or not, nor within
  0.4 s of its last strike (`_struckAt`: a blade grazing an edge). TaleSpire's Hit is the attacker's `_attackPlayable`
  (`TLA_MeleeAttack`, 2.05 s: rotate-to-target 0-0.32, `ANIM_Default_Attack` 0.37-1.20, an `AudioEventClip` swing
  0.78-2.01, the `TM_CharAttackEventClip` impact at 0.87 -> `AttackTargetCreature`: CircleDust, `_audioHitBoardHard`
  thud, victim `TiltPiece`), so it landed ~0.9 s after the strike (Green Laser ~0.6 s; the network round trip is
  ~15 ms). `Patches/AttackPatches.cs`: a postfix on `CreatureBoardAsset.Attack` (`FPWeapons.AttackPlayed`) jumps the
  director of our own Hits (`_hitsSent`, sent within 3 s) to 0.02 s before the impact clip - it lands ~40 ms after the
  strike, on this computer only - and for 2.5 s that director's `AudioEventClipBehaviour.ProcessFrame` is skipped
  (resolver = that director) and `AudioManager.Play` is skipped while its `AttackTargetCreature` runs, so only our
  sword sound plays. Guns' lasers: `CreatureBoardAsset.Attack` plays the timeline named after the attack
  (`TLA_LaserRed` / `TLA_LaserGreen`, 1.03 s), whose only track is a `ControlTransitionTrack` (bound to a
  `CtrTransitionVFX` that hires `VFXLaser`) with its clip at 0.43 s; the laser then flies 1/12 s to
  `AttackTargetCreature`. Our own shots jump to 0.02 s before that clip (TaleSpire's sounds are kept): it lands ~170 ms
  after the shot instead of ~600. Logged per hit ("[wpn] Hit plays ... / it lands ... ms after the strike"); debug `wpn strike
  <name>` (a strike on the nearest creature of that name), `wpn shoot <name>` (the gun in hand fires at it), `wpn skip
  0|1`, `wpn swordattack <kind>` (saved),
  `prefabdump attack` (the first-person creature's attack timeline: tracks, clips, times). Flame: the burning strip starts below the hand and runs in the
  palm's horizontal direction (fingers' if the palm faces down; smoothed over 0.08 s), 7 x power long (power 0 -> 1 over
  2 s) and 0.5 wide. Fire pieces never move: each frame, points every ~0.22 along the strip that have no piece within
  0.32 get a new one (random size, small jitter, spin; on the floor below it once, `FirstPersonMode.FloorAt`: tiles /
  props / board mat; at most 64); a piece grows while inside the strip (+0.1 margin) and dissolves where it is outside
  it, and is destroyed once out. 3 point lights follow the strip. Release = everything dissolves. Creatures in the
  strip get the creature's own `TLA_Burning` timeline (`GetActionTimeline` + `JumpToState(0)`, state 2 after 5 s unless
  the status is on the board). The sigil: FXKandolPack's "FX Magic Plane II free" circles (its shader can't be loaded,
  so its fiery disc `fx_etc_light_circle_c16` and corona ring `..._d16` ship as `sigil_disc.png` / `sigil_ring.png`),
  two quads in front of the palm facing out of it, turning 25 and -40 degrees/s, with `Legacy Shaders/Particles/
  Additive` (TaleSpire has it; else a fading Standard fallback with the black made transparent), tint 1.15 -> 1.6;
  under each a `Particles/Alpha Blended` copy at 75 -> 95 % opacity (additive alone fades out over a bright sky or
  floor). Size 7.5 -> 16 cm (the ring 1.3x) and 2 -> 6 cm off the palm with the power, eased out (1 - (1 - p)^2: most
  of the growth early), popping in over 0.12 s and shrinking to nothing in 0.15 s on release; an orange point light
  on it (0.4 -> 0.9, range 0.5 m) lights the hand. The sound (`FirstPerson/FlameSound.cs`): TaleSpire's only fire
  sounds are gentle ambient loops in its content packs, so both clips are made at first use (44.1 kHz mono,
  `AudioClip.Create`): a 1 s whoosh (white noise through a Chamberlin band-pass sweeping 220 Hz -> 1.7 kHz in 0.18 s
  -> 500 Hz, 0.05 s attack, 0.28 s decay, plus a low thump) on ignition, and a 6 s seamless loop (0.4 s crossfade) -
  a roar (white noise low-passed twice at 450 Hz, swelling at ~7 Hz), the rush (0.7-1.5 kHz band, ~5 Hz swells), a
  hiss (> 3 kHz) and ~35 crackles/s (1-3 ms bursts, cubed amplitudes), mixed 0.7 / 0.28 / 0.07 / 0.45 and soft-limited
  (tanh). Played at the right hand through `WeaponFx.SfxGroup` (TaleSpire's sound-effects volume); loop volume 0.45 ->
  0.95 and pitch 0.92 -> 1.08 with the power, up in 0.15 s, down in 0.35 s on release; stopped in `StopAll`.
  Ice is the same code with another `PowerStyle` (chosen on each new press; pieces keep the style they were made with,
  so a switch mid-fade is clean): the Frozen status (`TLA_Frozen`, 0.92 s, states NONE / PAUSE / NONE) gives creatures
  its look and its `BaseIce/mesh_iceBase` (`TS_Effects/FrozenEffect`, `_Transition` 0 -> 1 = grown in, pivot at the
  base, ~0.9 wide) is laid on the floor upright, scaled to the fire pieces' width x 0.8; lights (0.55, 0.8, 1) at
  intensity 1.4; the sigil textures recoloured at load (white = (g / max)^2, lerped from (0.32, 0.62, 1) to white) with
  tint (0.85, 1.15, 1.6) and a pale blue light. Its sound (`new FlameSound(true)`, own clips): a 1 s frosty rush (noise
  through the band-pass sweeping 6 -> 1.2 kHz in 0.6 s, five sine pings 3.3 - 7.4 kHz) and a 6 s loop - an airy hiss
  (2.5 - 6 kHz band, ~3 Hz swells), a lower 0.6 - 1.5 kHz rush, ~18 crystal tinks/s (decaying sines 2.5 - 8 kHz, up to 8
  at once) and ~6 ice cracks/s (5 - 20 ms low-passed noise bursts), mixed 0.45 / 0.22 / 0.5 / 0.4.
  Debug: `wpn equip <id>|none`, `wpn add|remove <id>`, `wpn tune <mesh> u|f|scale|px|py|pz|tilt <v>`, `wpn firetest`,
  `wpn icetest` (a row of the ice piece at transitions 0 - 1),
  `wpn slow <factor>`, `wpn aimat|turnto <creature name>`, `fp handpose <yaw> <roll> [distance]|off` (the right hand
  alone in front of the face), `fp entername <name> [home]`, `radialbtn weapons|arm n|return|tile n`, `findtext`,
  `audio`, `audiodata`, `playsound <clip> [volume]`, `prefabdump <status index>|<timeline name>|attack` (also its timeline's tracks and clips). The exporter is
  `tools\weapons\export-weapons.ps1` + `WeaponExporter.cs` (`-Mode Preview` renders, `Side` orthographic side views
  on a grid - right = the longest axis, up = the second, logging the centre and units per pixel to measure grips from
  - `Analyze` cross-sections, `Zoom` the shotgun grip on a 1 cm grid, `Export`; `-List id=asset;...` picks the models,
  `-Keys` limits a run to some of them). Each Asset Store package is extracted into its own folder under
  `%TEMP%\TaleSpireVR-weapons` (packages: lowpoly, stylized, melee, scifi, rpg, aged, shotguns; the asset keys are in
  `WeaponExporter.cs`). The packages have no crossbow, so there is none yet (the crossbow rules - firearm-like
  pointer, twang, green attack - would be a `Gun` grip with `Shot = Bow`).
- Predator Vision (`FirstPerson/PredatorVision.cs`, Game Master only, first person, both trackpads held - on controllers
  without trackpads both grips for 0.6 s - fades over 0.15 s; Alt / Ctrl are
  not sent in first person): no custom shaders can be loaded (TaleSpire is Unity 2021.3), so it is built from shaders in
  the game. CameraEvent.BeforeForwardAlpha: creature model renderers (not ActionTimeline effects, not your hidden
  creature; rescanned every 0.5 s) are drawn with UI/Default into a full-size mask (cleared to (1,1,1,0)) bound with the
  camera's depth, ZTest LessEqual - so walls hide them. CameraEvent.AfterImageEffects: the final picture is copied; PPv2's
  Uber shader (from the PostProcessLayer's resources, keyword COLOR_GRADING_LDR_2D, `_Lut2D` = a 1024 x 32 grey LUT
  (90 % desaturated, x 0.8), white `_AutoExposureTex`, `_UVTransform` (1, -1, 0, 1)) draws it grey into the eye with
  PPv2's full-screen triangle; the mask is halved six times by bilinear blits; red glow = the 1/8 level twice plus the
  1/64 level (brought up to 1/16) at 0.8; then the copy's alpha is set to max(mask, 1 - amount) (Internal-Colored
  additive / multiply blends, UI/Default with ColorWriteMask.Alpha) and the copy is blended back - creatures in colour,
  glow only around them. Debug: `predator on|off` (without the trackpads), `predator flip`.
  Outside first person A with the right laser on the radial window closes it (`AOnRadialCloses`; TaleSpire ignores
  right-clicks on its menus) and masks the press until A is released.
- First person, B (`UIPanels.ToggleEscMenu` / `EscOnly`): `GUIManager.ToggleMainMenu()` opens TaleSpire's ESC menu
  (`GUIManager._exitMenu`, a nested canvas at `GUIManager/Ts/ExitMenu`; `GUIManager.MainMenuIsOpen`). While it, the
  settings window (`GameSettings`, its own root canvas) or a question box (`SystemMessage`) is up, `UICapture` draws
  only those: every canvas under them (re-checked every 0.25 s, so dropdown lists count) goes onto the isolated layer
  (8, as for radial menus) with its layer remembered, the ESC menu gets its own GraphicRaycaster, and every other
  raycaster is paused; the main panel shows the texture, without the hotkey row or keyboard. B again closes the ESC
  menu and settings (`GameSettings.ToggleOpen`, which doesn't apply anything), A closes it (`VRPointer.FirstPersonA`), a
  radial menu opening closes it, and its own buttons closing everything end it after 0.6 s. Both lasers show meanwhile.
- One menu at a time (`UIPanels.LateUpdate`): a radial that opens this frame (`radialOpened`: open now, not last frame)
  clears `_mainWanted` - it does not return when the radial closes - and in first person closes the ESC menu
  (`UpdateEscOnly`); B with a radial open closes it and opens the panel / ESC menu (`ToggleMain` / `ToggleEscMenu`).
  Only a newly opened radial counts: TaleSpire's radial can still read as open for a frame or two after B closed it,
  which would otherwise shut the ESC menu B had just opened. Closing any of them opens nothing. Dice in hand hide the
  panel and the forearm dice only while in hand. Checked with `btntap menu|a 3` + `handaim right creature n|board`.
  Outside first person, closing the panel (B, or a radial opening while it is wanted) also closes TaleSpire's ESC menu
  and settings window opened in it (`UIPanels.CloseTaleSpireEscMenu`: `GameSettings` closed, then
  `GUIManager.ToggleMainMenu()` if its menu is up) - they were left open off-panel and showed through the dice results
  banner, which is also hidden while the ESC menu or a question box is up.
- Radial window flash: `UICapture` keeps capturing only the radial layer while the radial quad is still visible this
  frame (a menu closed after `UIPanels` placed it would otherwise show the middle of the whole UI - the dice tray -
  for a frame). B with a radial open closes the radial and opens the panel (`UIPanels.ToggleMain`).
- `StreamView.SetFirstPerson`: `Held` reads false (mirror path), orbit and marker paused, `_mode` kept; `FPBlink.Bands`
  draws the matching darkness rows into the held camera's picture (by row elevation from its pitch and tangents).
- `FPRuler`: line + end markers sized by creature height (hidden on every entry); distance = TaleSpire's line ruler
  maths (`DistanceUnits[0].NumberPerTile`, unit name). On the back of the left hand: a world-space canvas on the
  overlay layer (ZTest Always, shown only while the back of the hand faces you) with a dotted ring that turns -240
  degrees per unit the distance grows (clockwise as seen), and the number + unit inside.
- `Patches/CullingPatches.cs` (`StereoCullingPatch`): TaleSpire culls the board with `FrustumPlanes.BuildSoaPlanePackets`
  from the camera's mono projection; for the headset camera it now uses the union of that and both eyes' frustums
  (from a point behind the head). Where the mono frustum is already the wider this changes nothing; it guards headsets
  with wider or canted eyes. (It is not the cause of black flickers on very busy boards: those are frames the compositor
  fills in when the game can't keep up.)
Debug: `fp enter [n] [home] | exit [home] | jump | crouch | ruler | radial | menu | handview <curl>|-1|off [distance]
(-1 = tracked fingers on hands held in front of the face) | aimextra left|right | lift m | step h|off | fly 1|0|off |
blink c|off | trace n | facetest | jumph h|off`, `head <height> <pitch> [yaw]|off` (fixed HMD pose for a headset lying
on the desk), `fx list | play i | stop i | dump` (play a status effect on the first-person or nearest creature, on this
computer only). `home` = never move the creature on exit (testing on real boards).

A presses (`Patches/InputPatches.cs`): TaleSpire's mouse buttons have Tap / Hold / Press / MultiTap interactions; a
right Tap must be released within 0.2 s and after one click MultiTap swallows the next ~0.75 s as a double-click
(which no board tool uses) - that was "A works again only after a second" with the radial. In VR every A press is sent
as a 3-frame pulse however long it is held, and `*_OnMultiTap` is passed on as `*_OnTap`. Debug: `clicklog n` (log
TaleSpire's next n camera click events), `btntap2 a <gap frames>` (two quick A taps).

Dice (`UI/DiceUi.cs`, `UI/DiceForearm.cs`, `Input/DiceHand.cs`, `UIPanels.Dice` / `Banner`): `UIPanels.Dice` shows the UI
region around the `UIAddDiceButton`s plus room above for the tray, its Drag hint and GM button (`DiceUi.TrayRoom`), on the
inside of the left forearm. `DiceForearm` works in tracking space from the left tip pose (+x = out of a left palm, as in
FPHands' controller-only hand): the arm point is (0.04, -0.01, -0.25) from the tip; it shows once the palm has faced up
(the wrist turned >= 34 degrees counter-clockwise from the usual grip, `DiceForearm.ShowTurn`, compared as its sine with
`PalmDot`: the palm normal against world up projected perpendicular to the forearm - so the forearm's pitch doesn't
matter - when that projection is long enough, > 0.3) for 0.12 s, wherever you look, and hides when the turn falls below 17
degrees (`HideTurn`) for 0.15 s (not while a laser is on it); never while the left trigger is held or a
measure / lasso runs, in first person, with a radial open or dice in hand. (It used to need the palm towards the eyes
and the gaze on the arm, which was hard to hit; the gaze cosine is still in status.txt.) The quad lies on the arm (facing out of the palm, long side along the arm, top towards the top of your view, dice
row on the arm, slid two dice buttons' width towards the wrist), fades in over 0.12 s, DiceMenuWidth wide; its first
showing expands TaleSpire's toolbars (the dice row lives there). While it is out the left laser is invalid (no target, not drawn). B is only the menu panel again. TaleSpire takes dice out only on a press on the tray that leaves it
(`UIDiceTray.ButtonDown` / `SpawnDice`; a captured press stops at the window's edge, so it never left): `DiceHand` also
calls `UIDiceTray.Spawn(isGm)` on a trigger click on the tray / GM button (4 frames after the release, or the new
`DiceBoardTool` takes that release as its throw) and when a press leaves the window. While `DiceBoardTool` is on, the
lasers drive the board only (menus hidden, `PlacementLocked`), and each frame `DiceHand.Hold` works out the hold point:
`HoldDistance` (0.15 m x zoom) along the hand-in-control's ray (`WorldRay`), no further than 0.4 before the board it
points at (dice ray layers), lifted 0.4 above the surface if the hand is under it; it gathers the dice there with no
button down, and `DiceInHandPatch` (prefix on `GatherDice`) moves TaleSpire's own gathers (button held) to it too.
`VRHands.DrawHand` draws that hand's laser only to the dice, without the end dot. Own rolls are removed 10 s after they
land (`DiceLandedPatch`: `Dice.Roll.DoUpdate` returning UpdatedWithResult for a roll whose OwnerId is the local player;
`DiceCleanup` waits, skips a roll back in hand, cancels on a new throw, closes its radial if open, then `RemoveRoll`).
The throw speed is `MouseManager.GroundDeltaPerSecond`, which is screen-based and always 0 in VR: `LaserThrowSpeedPatch`
returns the hold point's own velocity (frame to frame, eased over 0.03 s; TaleSpire clamps it to 30) while dice are in
hand, else the laser's ground-plane speed (`VRPointer.LaserGroundVelocity`). Debug: `dicein [kind] [count]` (dice into
the tray and out into your hand; kind 1 = TaleSpire's first registered die). Results: `UIPanels.Banner` shows the region of
`GUIManager.DiceRollResult`'s visible graphics 24 degrees above the horizon, 1.8 m away, yaw easing after the head
(0.8 s), 0.9 m per 540 UI px (max 1.6 m); overlay layer, so the stream view
shows it too. `LongerDiceResultPatch` keeps the panel up 15 s. Debug: `forearm up|side|off` (a made-up left controller
pose: palm up / normal grip; status.txt shows the palm and gaze cosines), `forearm turn <deg> [pitch]` (the normal grip
turned palm-up by deg degrees, forearm pitched up by pitch, default 26), `diceui` (layout + click areas),
`handaim right dice x y`, `toss <yaw> <pitch> <frames>` (swing the right debug laser with the trigger held; only with dice
in hand), `dicelist [remove i]`; throws are logged with their speed.
Board safety while testing: a debug press on the board acts like a real one (it can pick up and move a mini). TaleSpire
writes a zlib-compressed, content-addressed snapshot of a board's creatures on each change to
`%USERPROFILE%\AppData\LocalLow\BouncyRock Entertainment\TaleSpire\primary\Persistence\<campaign>\<board>\Creatures`;
diffing the last two shows exactly what moved, and `creaturemove <from x y z> <to x y z>` puts a creature back with a
normal networked drop. A record is the creature GUID (16 bytes), its position (3 floats), 4 zero bytes and a uint32
whose low 16 bits are its facing in 1/65536 turns (`creaturerot <x y z> [axis degrees]` logs / sets the rotator's
local euler z, which maps onto it to within a few units - adjust and re-check). A carry cancelled with Escape drops
the mini where it was picked up, but its height can come back one float step off, and turn mode really turns it.
Escape with no tool to back out of opens TaleSpire's exit menu (a second Escape closes it).

Carrying modes (`Patches/CarryModePatches.cs`): TaleSpire's creature / group carry tools enter turn (Alt) and raise
(Ctrl) modes on those keys' down / up events and by polling `ControllerManager.Button.IsDown` while the trigger is held -
from any keyboard, and the events are dropped while an input field is focused. A stray or lost Alt left a carry in turn
mode (the mini no longer followed the laser and could not be raised, only turned). In VR `CarryModifierStatePatch` makes
`BtnRotationMod` / `BtnVerticalMod.IsDown` the controllers' state (right trackpad; left trackpad or zoom-button raising),
the tools' key-event handlers are skipped, and `CarryModes.Sync` (from `CreatureHold.Update`) moves the tool between its
Move / Rotate / Vertical states to match. Debug: `carrylog n` (n frames of carry state), `nudge <yaw> <pitch>` (turn the
right debug laser once).

Performance notes (very busy boards, multi-pass stereo): torch/lamp shadows and the sun shadow distance were the big
costs (limiting them about doubled the frame rate). What remains is mostly CPU: SetPass calls, submitted once per eye.
Render scale barely matters.

Tools: `tools\env.ps1` uses a local .NET 8 SDK in `tools\dotnet` if there is one (ilspycmd in `tools\ilspy` decompiles
TaleSpire into `_decompiled\` for reference; neither is in the repository, and decompiled TaleSpire code must never be).
