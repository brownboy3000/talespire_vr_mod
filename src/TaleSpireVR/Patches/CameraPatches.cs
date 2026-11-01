using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TaleSpireVR.Patches
{
    /// <summary>
    /// TaleSpire pans the camera while the right mouse button is held and the pointer moves over the ground (and moves
    /// it up and down with Ctrl). In VR the right button is A and the "pointer" is your laser, so holding A - for
    /// example to close a radial menu - dragged the whole board around you. In VR you move with the sticks instead:
    /// while TaleSpire's camera updates with the right button down, the ground / vertical-plane deltas it pans by read
    /// as zero. Taps on A (radial menus, cancel) are unaffected.
    /// </summary>
    [HarmonyPatch(typeof(RootTargetCameraMode), nameof(RootTargetCameraMode.CallLateUpdate))]
    internal static class NoRightDragPanPatch
    {
        internal static bool Suppress;

        private static void Prefix()
        {
            Suppress = VRManager.Active && MouseManager.RightButton.IsDown() && !ControllerManager.Modifiers.BtnRotationMod.IsDown;
        }

        private static void Postfix() { Suppress = false; }
        private static System.Exception Finalizer(System.Exception __exception) { Suppress = false; return __exception; }
    }

    [HarmonyPatch(typeof(MouseManager), nameof(MouseManager.groundDelta), MethodType.Getter)]
    internal static class NoRightDragGroundDeltaPatch
    {
        private static bool Prefix(ref Vector3 __result)
        {
            if (!NoRightDragPanPatch.Suppress) return true;
            __result = Vector3.zero;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class NoRightDragVerticalDeltaPatch
    {
        private static MethodBase TargetMethod() => AccessTools.PropertyGetter(AccessTools.TypeByName("MouseManager+DeltaWorldCalc"), "Delta");

        private static bool Prefix(ref Unity.Mathematics.float3 __result)
        {
            if (!NoRightDragPanPatch.Suppress) return true;
            __result = Unity.Mathematics.float3.zero;
            return false;
        }
    }

    /// <summary>Pings (the three animated arrows) twice the size TaleSpire draws them, so they read well in the headset.</summary>
    [HarmonyPatch(typeof(PingGesture), nameof(PingGesture.Setup))]
    internal static class BiggerPingPatch
    {
        private const float Factor = 2f;
        // Pings are pooled and their scale may be reset when returned, so always set it from each one's own size.
        private static readonly Dictionary<int, Vector3> _baseScale = new Dictionary<int, Vector3>();

        private static void Postfix(PingGesture __instance)
        {
            if (!VRManager.Active) return;
            var t = __instance.transform;
            int id = __instance.GetInstanceID();
            if (!_baseScale.TryGetValue(id, out var baseScale))
            {
                baseScale = t.parent != null ? Vector3.Scale(t.localScale, t.parent.lossyScale) : t.localScale;
                _baseScale[id] = baseScale;
            }
            // World size (it is parented to TaleSpire's gesture manager right after this, keeping its world size).
            Vector3 parentScale = t.parent != null ? t.parent.lossyScale : Vector3.one;
            Vector3 want = baseScale * Factor;
            t.localScale = new Vector3(want.x / parentScale.x, want.y / parentScale.y, want.z / parentScale.z);
            if (_logs++ < 5) Plugin.Log.LogInfo($"[ping] drawn x{Factor}: base {baseScale} -> world {t.lossyScale} (parent '{(t.parent != null ? t.parent.name : "-")}')");
        }
        private static int _logs;
    }
}
