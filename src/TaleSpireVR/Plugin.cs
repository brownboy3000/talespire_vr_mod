using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TaleSpireVR
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.talespirevr.plugin";
        public const string Name = "TaleSpire VR";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        internal static string PluginDir;
        internal static Harmony HarmonyInstance;

        private void Awake()
        {
            Log = Logger;
            PluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Log.LogInfo($"{Name} {Version} loading from {PluginDir}");

            // TaleSpire's own flag for a modded client (its build id gets "_m"): it then keeps errors, which may be ours,
            // out of its automatic reports to Bouncyrock.
            AppStateManager.UsingCodeInjection = true;

            // Our settings, one .ini file per kind of feature in the VRMod folder (read once, here).
            ModFiles.Prepare();
            ConfigFile core = OpenIni(ModFiles.CoreIni), controls = OpenIni(ModFiles.ControlsIni), menus = OpenIni(ModFiles.MenusIni),
                graphics = OpenIni(ModFiles.GraphicsIni), stream = OpenIni(ModFiles.StreamCameraIni), firstPerson = OpenIni(ModFiles.FirstPersonIni);
            VRConfig.Bind(core, controls, menus, graphics, stream, firstPerson);
            foreach (var ini in new[] { core, controls, menus, graphics, stream, firstPerson }) DropOrphans(ini);
            Log.LogInfo($"Settings: {ModFiles.Dir} (start in VR: {VRConfig.StartInVR.Value}, stream view: {VRConfig.StreamViewEnabled.Value})");

            HarmonyInstance = new Harmony(Guid);
            Patches.PatchManager.ApplyAlwaysOn(HarmonyInstance);

            // The mode switch lives for the whole session (its main menu button switches VR on and off); VR itself is
            // only set up in VR mode - in normal mode nothing else of the mod runs.
            var root = new GameObject("TaleSpireVR_Mode");
            DontDestroyOnLoad(root);
            root.hideFlags = HideFlags.HideAndDontSave;
            root.AddComponent<ModeSwitch>();
        }

        private ConfigFile OpenIni(string path) => new ConfigFile(path, true, Info.Metadata);

        /// <summary>Options an older version of the mod had and this one doesn't: out of the file.</summary>
        private static void DropOrphans(ConfigFile cfg)
        {
            try
            {
                var orphans = HarmonyLib.AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(cfg) as System.Collections.IDictionary;
                if (orphans == null || orphans.Count == 0) return;
                orphans.Clear();
                cfg.Save();
                Log.LogInfo("Settings: options that no longer exist removed from " + cfg.ConfigFilePath);
            }
            catch (System.Exception e) { Log.LogWarning("Settings: tidying old entries failed: " + e.Message); }
        }
    }

    internal static class VRConfig
    {
        public static ConfigEntry<bool> StartInVR;
        public static ConfigEntry<bool> StreamViewEnabled;
        public static ConfigEntry<int> MirrorEye;
        public static ConfigEntry<float> RenderScale;
        public static ConfigEntry<int> MenuResolution;
        public static ConfigEntry<float> StartScale;
        public static ConfigEntry<float> MinScale;
        public static ConfigEntry<float> MaxScale;
        public static ConfigEntry<float> TableHeight;
        public static ConfigEntry<float> StandOff;
        public static ConfigEntry<float> MoveSpeed;
        public static ConfigEntry<float> ZoomSpeed;
        public static ConfigEntry<bool> MoveSpeedFollowsZoom;
        public static ConfigEntry<float> CreatureRotateSpeed;
        public static ConfigEntry<float> CreatureElevateSpeed;
        public static ConfigEntry<float> BloomScale;
        public static ConfigEntry<bool> DisableReflections;
        public static ConfigEntry<string> AntiAliasing;
        public static ConfigEntry<float> DrawDistance;
        public static ConfigEntry<float> MaxDrawDistance;
        public static ConfigEntry<float> ShadowDistance;
        public static ConfigEntry<int> MaxShadowedLights;
        public static ConfigEntry<float> LightShadowDistance;
        public static ConfigEntry<float> SnapTurnDegrees;
        public static ConfigEntry<bool> SmoothTurn;
        public static ConfigEntry<float> SmoothTurnSpeed;
        public static ConfigEntry<float> CameraHeightSpeed;
        public static ConfigEntry<float> PointerPitchOffset;
        public static ConfigEntry<float> PanelDistance;
        public static ConfigEntry<float> PanelWidth;
        public static ConfigEntry<float> RadialMenuSize;
        public static ConfigEntry<float> RadialMenuDistance;
        public static ConfigEntry<float> MenuSharpness;
        public static ConfigEntry<float> MenuScale;
        public static ConfigEntry<float> SidePanelScale;
        public static ConfigEntry<float> FloorSliderSpeed;
        public static ConfigEntry<float> MenuScrollSpeed;
        public static ConfigEntry<float> DiceMenuWidth;
        public static ConfigEntry<int> StreamHeight;
        public static ConfigEntry<int> StreamFps;
        public static ConfigEntry<int> HeldViewFps;
        public static ConfigEntry<float> HeldViewFov;
        public static ConfigEntry<float> HeldViewDrawDistance;
        public static ConfigEntry<bool> ShowHeldCameraMarker;
        public static ConfigEntry<float> HeldViewFrameInset;
        public static ConfigEntry<float> FPWalkSpeed;
        public static ConfigEntry<float> FPStepHeight;
        public static ConfigEntry<float> FPJumpHeight;
        public static ConfigEntry<float> FPGravity;
        public static ConfigEntry<float> FPEyeHeight;
        public static ConfigEntry<float> FPBodyRadius;
        public static ConfigEntry<float> FPDrawDistance;
        public static ConfigEntry<float> FPShadowDistance;
        public static ConfigEntry<string> FPHandColor;
        public static ConfigEntry<string> WeaponRangedAttack;
        public static ConfigEntry<string> WeaponMeleeAttack;
        public static ConfigEntry<bool> Diagnostics;
        public static ConfigEntry<int> UICaptureMode;

        /// <summary>One file per kind of feature (ModFiles). A file's options and comments are written back if any are missing.</summary>
        public static void Bind(ConfigFile core, ConfigFile controls, ConfigFile menus, ConfigFile graphics, ConfigFile stream, ConfigFile firstPerson)
        {
            StartInVR = core.Bind("Mode", "VR", true, "Which mode TaleSpire starts in. true = the headset (SteamVR must be running); false = normal TaleSpire, exactly as without the mod apart from one button on the main menu. That VR MODE button switches and saves this for you; you can also change it here while TaleSpire is closed, for example if VR doesn't start.");
            MirrorEye = core.Bind("VR", "DesktopMirror", 1, "What the desktop window shows: 0 = nothing, 1 = left eye, 2 = right eye.");
            RenderScale = core.Bind("VR", "RenderScale", 1.0f, "Multiplier on SteamVR's per-eye render resolution (0.5 - 2.0). Lower it if the frame rate drops.");
            Diagnostics = core.Bind("Debug", "Diagnostics", false, "Write camera/canvas diagnostics and frame timings to the BepInEx log (for reporting a problem).");
            UICaptureMode = core.Bind("Debug", "UICaptureMode", 0, "How the menus are copied from the desktop window: 0 = command buffer blit, 1 = ScreenCapture API.");

            StartScale = controls.Bind("Board", "StartScale", 12f, "World units per real-world meter when you enter a board. Higher = you are a bigger giant (minis look smaller).");
            MinScale = controls.Bind("Board", "MinScale", 0.5f, "Smallest allowed scale (zoomed all the way in; ~life-size minis).");
            MaxScale = controls.Bind("Board", "MaxScale", 400f, "Largest allowed scale (zoomed all the way out).");
            TableHeight = controls.Bind("Board", "TableHeight", 0.85f, "Height in meters above your real floor where the board surface sits.");
            StandOff = controls.Bind("Board", "StandOff", 0.45f, "How far (meters) in front of you the camera focus point is placed when a board loads.");
            MoveSpeed = controls.Bind("Controls", "MoveSpeed", 1.2f, "Left-stick movement speed in real meters per second at the starting zoom.");
            MoveSpeedFollowsZoom = controls.Bind("Controls", "MoveSpeedFollowsZoom", false, "If true, left-stick speed scales with zoom (faster when zoomed out). If false, it is always the starting-zoom speed across the board.");
            ZoomSpeed = controls.Bind("Controls", "ZoomSpeed", 1.0f, "Zoom speed for the zoom buttons (scale doublings per second).");
            CreatureRotateSpeed = controls.Bind("Controls", "CreatureRotateSpeed", 150f, "Degrees per second a held creature turns with the right thumbstick.");
            CreatureElevateSpeed = controls.Bind("Controls", "CreatureElevateSpeed", 0.25f, "Real meters per second a held creature rises/sinks with the zoom buttons.");
            SnapTurnDegrees = controls.Bind("Controls", "SnapTurnDegrees", 30f, "Degrees per snap turn on the right stick.");
            SmoothTurn = controls.Bind("Controls", "SmoothTurn", false, "Use smooth turning instead of snap turning.");
            SmoothTurnSpeed = controls.Bind("Controls", "SmoothTurnSpeed", 90f, "Smooth turn speed in degrees per second.");
            CameraHeightSpeed = controls.Bind("Controls", "CameraHeightSpeed", 10f, "Camera up/down speed in board units per second (same as Page Up / Page Down).");
            PointerPitchOffset = controls.Bind("Controls", "PointerPitchOffset", 0f, "Tilts the laser pointer up (+) or down (-) in degrees relative to the controller.");

            MenuResolution = menus.Bind("Menus", "MenuResolutionHeight", 2160, "Vertical resolution TaleSpire's menus are rendered at in VR (2160 = 4K, sharp text). 0 = leave the game's resolution alone.");
            PanelDistance = menus.Bind("Menus", "PanelDistance", 1.4f, "Distance in meters of the floating menu panel when it is summoned.");
            PanelWidth = menus.Bind("Menus", "PanelWidth", 1.6f, "Width in meters of the floating menu panel.");
            RadialMenuSize = menus.Bind("Menus", "RadialMenuSize", 0.7f, "Width in meters of the floating radial (creature / door) menu window.");
            RadialMenuDistance = menus.Bind("Menus", "RadialMenuDistance", 0.85f, "Distance in meters of the radial menu window when it opens.");
            MenuScale = menus.Bind("Menus", "MenuScale", 1.2f, "Extra size for TaleSpire's buttons, sliders and menus in VR (1 = the flat game's size; 1.2 = 20% bigger).");
            FloorSliderSpeed = menus.Bind("Menus", "FloorSliderSpeed", 0.333f, "How far the right-hand floor slider pins (water, view level, build grid, fog) move for a given laser movement. 0.333 = a third (fine control), 1 = TaleSpire's.");
            MenuScrollSpeed = menus.Bind("Menus", "MenuScrollSpeed", 1f, "How fast the right thumbstick scrolls a menu while the laser is on the menu panel.");
            SidePanelScale = menus.Bind("Menus", "SidePanelScale", 1.5f, "Further size for the left-hand buttons with the menus they open (boards, atmosphere, ...) and the right-hand floor slider, on top of MenuScale (1.5 = 50% bigger).");
            MenuSharpness = menus.Bind("Menus", "MenuSharpness", 0.8f, "How crisp menu text looks (0 = soft, as before; 1 = sharpest, may shimmer slightly).");
            DiceMenuWidth = menus.Bind("Menus", "DiceMenuWidth", 0.22f, "Width in meters of the dice window on the inside of your left forearm (shown while you turn that palm up).");

            BloomScale = graphics.Bind("Graphics", "BloomScale", 0.35f, "Multiplier on TaleSpire's bloom (glow/glare) in VR. 1 = unchanged, 0 = no bloom. Lights are not dimmed.");
            DisableReflections = graphics.Bind("Graphics", "DisableScreenSpaceReflections", false, "Turn off screen-space reflections in VR. false = use TaleSpire's Reflections setting.");
            AntiAliasing = graphics.Bind("Graphics", "AntiAliasing", "Game", "Anti-aliasing in VR: Game (use TaleSpire's setting), SMAA, FXAA, None, or TAA (can flicker in VR).");
            DrawDistance = graphics.Bind("Graphics", "DrawDistance", 25f, "How far you can see, in real-world meters at your current zoom. 25 = the flat game's 300-unit view at the starting zoom; zooming in shortens it, zooming out lengthens it. Lower = faster.");
            MaxDrawDistance = graphics.Bind("Graphics", "MaxDrawDistance", 1500f, "Upper limit for the draw distance in board units (when zoomed far out).");
            ShadowDistance = graphics.Bind("Graphics", "ShadowDistance", 6f, "How far from you the sun/moon casts shadows, in real-world meters at your current zoom (6 = 72 board units at the starting zoom). Never more than TaleSpire's own shadow distance. Lower = faster. 0 = use TaleSpire's.");
            MaxShadowedLights = graphics.Bind("Graphics", "MaxShadowedLights", 4, "Only this many of the nearest torches/lamps cast shadows (each one is expensive in VR). -1 = no limit (TaleSpire's behaviour).");
            LightShadowDistance = graphics.Bind("Graphics", "LightShadowDistance", 3f, "Torches/lamps farther than this (real-world meters at your current zoom) never cast shadows.");

            StreamViewEnabled = stream.Bind("StreamView", "Enabled", true, "The stream view: a second desktop window, \"TaleSpire VR - Stream View\", to share on Discord or record with OBS, showing your headset view or the held camera. true = the window opens with the game (Game Master only, never while you play as a player); false = no stream window and no held camera at all.");
            StreamHeight = stream.Bind("Stream", "StreamHeight", 0, "Stream picture height in pixels (width is 16:9). 0 = your monitor's height.");
            StreamFps = stream.Bind("Stream", "StreamFps", 30, "Frames per second of the stream window while it shows your headset view (cheap: a copy of the left eye).");
            HeldViewFps = stream.Bind("Stream", "HeldViewFps", 30, "Frames per second of the held camera (30 = the usual stream rate). Each of its frames draws the board again from its own viewpoint, which costs headset frame rate on big boards; lower it if that is noticeable. Light boards are unaffected.");
            HeldViewFov = stream.Bind("Stream", "HeldViewFov", 0f, "Vertical field of view of the held camera in degrees. 0 = the same framing the stream showed from your headset.");
            HeldViewDrawDistance = stream.Bind("Stream", "HeldViewDrawDistance", 0f, "How far the held camera sees, in board units. 0 = automatic (at least what you saw when you held it, and well past the board it looks at). Not affected by your own zoom.");
            ShowHeldCameraMarker = stream.Bind("Stream", "ShowHeldCameraMarker", true, "Show the green wireframe camera, its view lines and the outline of what it sees on the board (only in your headset, never in the stream).");
            HeldViewFrameInset = stream.Bind("Stream", "HeldViewFrameInset", 0.1f, "How far inside the held camera's real picture edges its green lines and outline are drawn, as a fraction of the picture's width / height on each side (0.1 = 10% in from every edge), so what you frame stays clear of anything players' stream windows put around the edges. 0 = the exact edges.");

            FPWalkSpeed = firstPerson.Bind("FirstPerson", "WalkSpeed", 2f, "Left-stick walking speed in first person, board units (grid squares) per second. Turning uses the SnapTurnDegrees / SmoothTurn settings in TaleSpireVR.Controls.ini.");
            FPStepHeight = firstPerson.Bind("FirstPerson", "StepHeight", 2f, "Highest ledge or obstacle you climb onto while walking in first person, in board units.");
            FPJumpHeight = firstPerson.Bind("FirstPerson", "JumpHeight", 2.5f, "How high a jump (right stick up) clears in first person, in board units. Jumping into a ledge climbs it if its top is within StepHeight of your feet, so a jump reaches ledges up to JumpHeight + StepHeight high.");
            FPGravity = firstPerson.Bind("FirstPerson", "Gravity", 18f, "Falling and jumping gravity in first person, board units per second squared (lower = floatier, slower jumps).");
            FPEyeHeight = firstPerson.Bind("FirstPerson", "EyeHeight", 0.92f, "Your eye height in first person, as a fraction of the creature model's total height.");
            FPBodyRadius = firstPerson.Bind("FirstPerson", "BodyRadius", 0f, "Radius of your body for bumping into walls in first person, board units. 0 = from the creature's base size.");
            FPDrawDistance = firstPerson.Bind("FirstPerson", "DrawDistance", 150f, "How far you can see in first person, in board units.");
            FPShadowDistance = firstPerson.Bind("FirstPerson", "ShadowDistance", 40f, "How far from you the sun/moon casts shadows in first person, in board units (never more than TaleSpire's own).");
            FPHandColor = firstPerson.Bind("FirstPerson", "HandColor", "A87256", "Colour of your first-person hands (hex RGB).");
            WeaponRangedAttack = firstPerson.Bind("FirstPerson", "ShotgunAttack", "TLA_LaserRed", "TaleSpire attack the guns (shotgun, pistol, rifles, ...; not the sci-fi injector, always green) play on the creature their pointer is on when fired: Hit, TLA_MagicMissileAttack, TLA_LaserRed or TLA_LaserGreen.");
            WeaponMeleeAttack = firstPerson.Bind("FirstPerson", "SwordAttack", "Hit", "TaleSpire attack the melee weapons (longsword, axes, hammers, spears, shields ...) play on a creature they strike (trigger held, weapon in the creature): Hit, TLA_MagicMissileAttack, TLA_LaserRed or TLA_LaserGreen.");
        }
    }
}
