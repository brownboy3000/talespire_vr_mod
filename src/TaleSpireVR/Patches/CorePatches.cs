using HarmonyLib;
using Steamworks;
using UnityEngine;

namespace TaleSpireVR.Patches
{
    /// <summary>
    /// For developing the mod in a copy of the game: keep the copy running from its own folder instead of relaunching the
    /// Steam install. Only on a development setup (the VRMod\_debug folder exists, as in the debug channel); a normal
    /// install runs from Steam, whose answer is "no restart" anyway. On in normal mode too.
    /// </summary>
    [HarmonyPatch(typeof(SteamAPI), nameof(SteamAPI.RestartAppIfNecessary))]
    [AlwaysOn]
    internal static class SteamRestartPatch
    {
        private static bool Prepare() => System.IO.Directory.Exists(DebugSnapshot.DebugDir);

        private static bool Prefix(ref bool __result)
        {
            __result = false;
            return false;
        }
    }

    /// <summary>TaleSpire's camera modes all apply their pose here. In VR the headset owns the camera.</summary>
    [HarmonyPatch(typeof(CameraModeBase.CameraSettings), nameof(CameraModeBase.CameraSettings.ApplyToCamera))]
    internal static class ApplyToCameraPatch
    {
        public static Vector3 LastDesktopPosition;
        public static Quaternion LastDesktopRotation = Quaternion.identity;
        public static float LastDesktopFov = 40f;

        private static bool Prefix(ref CameraModeBase.CameraSettings __instance, Camera cam)
        {
            if (!VRManager.Active) return true;
            LastDesktopPosition = __instance.Position;
            LastDesktopRotation = __instance.Rotation;
            LastDesktopFov = __instance.Fov;
            return false;
        }
    }

    /// <summary>
    /// On a board TaleSpire shows the 3D view as an image inside its UI. In VR the 3D view goes to the headset,
    /// so hide that image to keep the menu panel transparent.
    /// </summary>
    [HarmonyPatch(typeof(GameViewport), "Update")]
    internal static class GameViewportPatch
    {
        private static UnityEngine.UI.Graphic _backdrop;

        /// <summary>The VR mod switches off (Player mode): TaleSpire's 3D view image and its backdrop back on.</summary>
        public static void Restore()
        {
            if (_backdrop != null) _backdrop.enabled = true;
            foreach (var v in Object.FindObjectsOfType<GameViewport>())
            {
                var img = AccessTools.Field(typeof(GameViewport), "_rawImage")?.GetValue(v) as UnityEngine.UI.RawImage;
                if (img != null) img.enabled = true;
            }
        }

        private static void Postfix(GameViewport __instance, UnityEngine.UI.RawImage ____rawImage)
        {
            if (!VRManager.Active) return;
            if (____rawImage != null && ____rawImage.enabled) ____rawImage.enabled = false;
            // The black full-screen backdrop that sits behind the viewport image.
            if (_backdrop == null)
            {
                var gui = __instance.GetComponentInParent<Canvas>();
                if (gui != null)
                {
                    foreach (var g in gui.rootCanvas.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                    {
                        if (g.name == "Backdrop") { _backdrop = g; break; }
                    }
                }
            }
            if (_backdrop != null && _backdrop.enabled) _backdrop.enabled = false;
        }
    }

    /// <summary>When SteamVR asked us to quit, answer TaleSpire's "Do you want to quit?" with Yes.</summary>
    [HarmonyPatch(typeof(SystemMessage), nameof(SystemMessage.SendSystemMessage), new[] { typeof(string), typeof(string), typeof(string), typeof(System.Action), typeof(string), typeof(System.Action) })]
    internal static class QuitPromptPatch
    {
        private static bool Prefix(string title, System.Action onAccept)
        {
            if (!XRBoot.QuitRequested || title != "Exit TaleSpire") return true;
            Plugin.Log.LogInfo("Auto-confirming TaleSpire's quit prompt (SteamVR requested exit).");
            onAccept?.Invoke();
            return false;
        }
    }

    /// <summary>Log build-mode undo/redo so shortcut problems are visible in the log.</summary>
    [HarmonyPatch(typeof(BoardBuilder), nameof(BoardBuilder.Undo))]
    internal static class UndoLogPatch
    {
        private static void Prefix() { Plugin.Log.LogInfo($"Undo requested (build mode={BoardBuilder.InBuilderMode})"); }
    }

    [HarmonyPatch(typeof(BoardBuilder), nameof(BoardBuilder.Redo))]
    internal static class RedoLogPatch
    {
        private static void Prefix() { Plugin.Log.LogInfo($"Redo requested (build mode={BoardBuilder.InBuilderMode})"); }
    }

    /// <summary>TaleSpire freezes mouse handling when its window loses focus; in VR the window is usually unfocused.</summary>
    [HarmonyPatch(typeof(MouseManager), "OnApplicationFocus")]
    internal static class MouseFocusPatch
    {
        private static void Prefix(ref bool focus)
        {
            if (VRManager.Active) focus = true;
        }
    }

    /// <summary>Unity ignores FOV changes on stereo cameras but logs a warning every time; skip them.</summary>
    [HarmonyPatch(typeof(Camera), "set_fieldOfView")]
    internal static class FovPatch
    {
        private static bool Prefix(Camera __instance)
        {
            return !(VRManager.Active && __instance.stereoEnabled);
        }
    }
}
