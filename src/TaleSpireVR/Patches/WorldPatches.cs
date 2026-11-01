using System;
using System.Reflection;
using HarmonyLib;
using Unity.Mathematics;
using UnityEngine;

namespace TaleSpireVR.Patches
{
    internal static class PointerMath
    {
        public static bool IsVRCamera(Camera cam)
        {
            var mgr = VRManager.Instance;
            return mgr != null && mgr.Rig != null && cam != null && cam == mgr.Rig.GameCamera;
        }

        /// <summary>
        /// Treat the VR camera's "screen" as a virtual camera sitting on the controller and looking down the laser.
        /// TaleSpire's mouse is parked at that screen's centre, so screen positions around it become rays around the laser.
        /// </summary>
        public static Ray VirtualRay(Camera cam, Vector3 screenPos)
        {
            var ray = VRPointer.WorldRay;
            Vector2 c = VRPointer.CameraCenter;
            float half = Mathf.Max(1f, cam.pixelHeight * 0.5f);
            float k = Mathf.Tan(30f * Mathf.Deg2Rad) / half;
            var local = new Vector3((screenPos.x - c.x) * k, (screenPos.y - c.y) * k, 1f).normalized;
            var up = VRManager.Instance.Rig.RigTransform != null ? VRManager.Instance.Rig.RigTransform.up : Vector3.up;
            var rot = Quaternion.LookRotation(ray.direction, up);
            return new Ray(ray.origin, rot * local);
        }
    }

    [HarmonyPatch(typeof(MouseManager), nameof(MouseManager.Position), MethodType.Getter)]
    internal static class MousePositionPatch
    {
        private static bool Prefix(ref Vector2 __result)
        {
            if (!VRPointer.WorldMode) return true;
            __result = VRPointer.CameraCenter;
            return false;
        }
    }

    [HarmonyPatch(typeof(MouseManager), nameof(MouseManager.IsHoveringOverUI), MethodType.Getter)]
    internal static class HoveringOverUIPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!VRPointer.WorldMode) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(CameraController), nameof(CameraController.GetCameraRay))]
    internal static class GetCameraRayPatch
    {
        private static bool Prefix(ref Ray __result)
        {
            if (!VRPointer.WorldMode) return true;
            __result = VRPointer.WorldRay;
            return false;
        }
    }

    [HarmonyPatch(typeof(Camera), nameof(Camera.ScreenPointToRay), new[] { typeof(Vector3) })]
    internal static class ScreenPointToRayPatch
    {
        private static bool Prefix(Camera __instance, Vector3 pos, ref Ray __result)
        {
            if (!VRPointer.WorldMode || !PointerMath.IsVRCamera(__instance)) return true;
            __result = PointerMath.VirtualRay(__instance, pos);
            return false;
        }
    }

    [HarmonyPatch(typeof(Camera), nameof(Camera.ScreenPointToRay), new[] { typeof(Vector3), typeof(Camera.MonoOrStereoscopicEye) })]
    internal static class ScreenPointToRayEyePatch
    {
        private static bool Prefix(Camera __instance, Vector3 pos, ref Ray __result)
        {
            if (!VRPointer.WorldMode || !PointerMath.IsVRCamera(__instance)) return true;
            __result = PointerMath.VirtualRay(__instance, pos);
            return false;
        }
    }

    /// <summary>
    /// Creature dragging: TaleSpire moves the held mini by mouse-movement deltas. In VR we instead make the mini
    /// chase the point on the table under the laser (TaleSpire's navigation still handles walls and the drop).
    /// </summary>
    internal static class DragHelper
    {
        private static int _logCount;
        /// <summary>Debug ("carrylog n"): log the next n frames of carrying in full.</summary>
        public static int DebugFrames;

        /// <summary>
        /// Velocity that moves the held mini's navigation capsule smoothly toward the point on its floor under the laser.
        /// The mini does not move at all until the laser has moved a little (so picking it up never nudges it), and the
        /// approach is exponential (time constant ~60 ms) so it can never overshoot and oscillate.
        /// </summary>
        public static float3 Chase(object tool, FieldInfo navField, FieldInfo hasMovedField, ref bool started, ref bool moving, ref Vector3 grabOffset)
        {
            var nav = navField.GetValue(tool) as CreatureNavigationController;
            if (nav == null) return float3.zero;
            float3 bottom = nav.GetCapsuleBottomPosition();
            var plane = new Plane(Vector3.up, -bottom.y);
            var ray = VRPointer.WorldRay;
            bool hitPlane = plane.Raycast(ray, out float enter);
            if (DebugFrames > 0)
            {
                DebugFrames--;
                CreatureMoveBoardTool.TryGetHeldCreatureBoardAsset(out var held);
                Plugin.Log.LogInfo($"[carry] f{Time.frameCount} {tool.GetType().Name} worldMode={VRPointer.WorldMode} target={VRPointer.Current} ray={ray.origin:F2}->{ray.direction:F3} hitPlane={hitPlane} hit={(hitPlane ? ray.GetPoint(enter).ToString("F2") : "-")} bottom={(Vector3)bottom:F2} started={started} moving={moving} offset={grabOffset:F2} creature={(held != null ? held.transform.position.ToString("F2") : "-")} hasMoved={hasMovedField.GetValue(tool)}");
            }
            if (!hitPlane) return float3.zero;
            Vector3 hit = ray.GetPoint(enter);
            if (!started)
            {
                started = true;
                moving = false;
                grabOffset = (Vector3)bottom - hit;
                grabOffset.y = 0f;
            }
            Vector3 delta = hit + grabOffset - (Vector3)bottom;
            delta.y = 0f;

            float scale = VRManager.Instance != null && VRManager.Instance.Rig != null ? VRManager.Instance.Rig.Scale : 12f;
            if (!moving)
            {
                // ~1.5 cm of real laser movement before the mini starts to follow.
                if (delta.magnitude < 0.015f * scale) return float3.zero;
                moving = true;
                hasMovedField.SetValue(tool, true);
            }

            const float timeConstant = 0.06f;
            Vector3 v = delta / Mathf.Max(timeConstant, Time.deltaTime);
            v = Vector3.ClampMagnitude(v, 4f * scale);
            if (_logCount < 200 && Time.frameCount % 30 == 0)
            {
                _logCount++;
                Plugin.Log.LogInfo($"[drag] {tool.GetType().Name} bottom={(Vector3)bottom} target={hit + grabOffset} delta={delta} v={v}");
            }
            return (float3)v;
        }
    }

    /// <summary>
    /// TaleSpire turns a carried mini to face away from where it was picked up. In VR that reads as violent spinning;
    /// keep the facing fixed while carrying (turn it with the right stick instead, see CreatureHold). The same call
    /// is how a single mini turns while the rotate modifier (Alt / right trackpad) is held, so allow it then.
    /// </summary>
    [HarmonyPatch(typeof(MovableBoardAsset), nameof(MovableBoardAsset.RotatePieceAwayFrom))]
    internal static class NoAutoFacingPatch
    {
        private static bool Prefix()
        {
            if (!VRManager.Active) return true;
            if (CarryModes.Rotate) return true;
            var tool = BoardToolManager.CurrentTool;
            return !(tool is CreatureMoveBoardTool || tool is CreatureGroupMoveBoardTool);
        }
    }

    /// <summary>Raise / lower a held creature with the zoom buttons: drive TaleSpire's elevate mode with our height.</summary>
    [HarmonyPatch(typeof(BoardTool), nameof(BoardTool.SampleOnVerticalPlane))]
    internal static class ElevateWithButtonsPatch
    {
        private static bool Prefix(float3 planeOrigin, ref float3 __result)
        {
            if (!CreatureHold.ElevatingWithButtons) return true;
            __result = new float3(planeOrigin.x, CreatureHold.ElevY, planeOrigin.z);
            return false;
        }
    }

    /// <summary>Double right-click jumps the camera to the clicked spot; in VR you move yourself, so turn it off.</summary>
    [HarmonyPatch(typeof(ControllerManager), "OnDoubleClickPerformed")]
    internal static class NoDoubleClickJumpPatch
    {
        private static bool Prefix() => !VRManager.Active;
    }

    [HarmonyPatch(typeof(CreatureMoveBoardTool), "GetDeltaMovement")]
    internal static class CreatureMoveDeltaPatch
    {
        private static readonly FieldInfo Nav = AccessTools.Field(typeof(CreatureMoveBoardTool), "_nav");
        private static readonly FieldInfo HasMoved = AccessTools.Field(typeof(CreatureMoveBoardTool), "_hasMoved");
        public static bool Started;
        private static bool _moving;
        private static Vector3 _offset;

        private static bool Prefix(CreatureMoveBoardTool __instance, ref float3 __result)
        {
            if (!VRManager.Active || !VRPointer.WorldMode) return true;
            __result = DragHelper.Chase(__instance, Nav, HasMoved, ref Started, ref _moving, ref _offset);
            return false;
        }
    }

    [HarmonyPatch(typeof(CreatureMoveBoardTool), "Begin")]
    internal static class CreatureMoveBeginPatch
    {
        private static void Prefix() { CreatureMoveDeltaPatch.Started = false; }
    }

    [HarmonyPatch(typeof(CreatureGroupMoveBoardTool), "GetDeltaMovement")]
    internal static class CreatureGroupMoveDeltaPatch
    {
        private static readonly FieldInfo Nav = AccessTools.Field(typeof(CreatureGroupMoveBoardTool), "_nav");
        private static readonly FieldInfo HasMoved = AccessTools.Field(typeof(CreatureGroupMoveBoardTool), "_hasMoved");
        public static bool Started;
        private static bool _moving;
        private static Vector3 _offset;

        private static bool Prefix(CreatureGroupMoveBoardTool __instance, ref float3 __result)
        {
            if (!VRManager.Active || !VRPointer.WorldMode) return true;
            __result = DragHelper.Chase(__instance, Nav, HasMoved, ref Started, ref _moving, ref _offset);
            return false;
        }
    }

    [HarmonyPatch(typeof(CreatureGroupMoveBoardTool), "Begin")]
    internal static class CreatureGroupMoveBeginPatch
    {
        private static void Prefix() { CreatureGroupMoveDeltaPatch.Started = false; }
    }

    /// <summary>
    /// Render TaleSpire's object-ID picking pass from the pointer camera instead of the headset.
    /// TaleSpire remembers (in statics) which camera already has its picking command buffers. When a board is left and
    /// re-entered, the picking system is rebuilt with new buffers, but our pointer camera survives, so TaleSpire thought
    /// it was still set up and never attached the new buffers: picking silently stopped (no pickups, doors or radial
    /// menus). Detect new buffers and attach them afresh.
    /// </summary>
    [HarmonyPatch]
    internal static class PickRenderPatch
    {
        private static readonly System.Type ModeType = AccessTools.TypeByName("PixelPickingManager+SingleSelectMode");
        private static readonly FieldInfo PickBufferField = AccessTools.Field(ModeType, "_pickCommandBuffer");
        private static readonly FieldInfo LastCameraField = AccessTools.Field(ModeType, "_lastCamera");
        private static object _seenBuffer;

        private static MethodBase TargetMethod() => AccessTools.Method(ModeType, "UpdateAndSetupForRender");

        private static void Prefix(ref Camera cam)
        {
            if (!(VRManager.Active && PickCamera.InUse && PointerMath.IsVRCamera(cam))) return;
            var buffer = PickBufferField?.GetValue(null);
            if (buffer != null && !ReferenceEquals(buffer, _seenBuffer))
            {
                bool first = _seenBuffer == null;
                _seenBuffer = buffer;
                PickCamera.Cam.RemoveAllCommandBuffers();
                LastCameraField?.SetValue(null, null);
                if (!first) Plugin.Log.LogInfo("Picking was rebuilt (board reloaded): re-attached it to the pointer camera");
            }
            cam = PickCamera.Cam;
        }
    }

    [HarmonyPatch]
    internal static class PickCaptureRectPatch
    {
        private static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("PixelPickingManager+SingleSelectMode");
            return AccessTools.Method(t, "GetCaptureRect");
        }

        private static bool Prefix(ref ValueTuple<Rect, float2> __result)
        {
            if (!VRManager.Active || !PickCamera.InUse) return true;
            Vector2 c = PickCamera.Center;
            var rect = new Rect { min = c - new Vector2(2f, 2f), max = c + new Vector2(2f, 2f) };
            __result = new ValueTuple<Rect, float2>(rect, new float2(c.x, c.y));
            return false;
        }
    }

    /// <summary>In VR the radial menu is always drawn at the screen centre; a floating quad shows it at the creature.</summary>
    [HarmonyPatch(typeof(MapMenuManager), "LateUpdate")]
    internal static class MapMenuPositionPatch
    {
        private static bool Prefix(MapMenuManager __instance)
        {
            if (!VRManager.Active) return true;
            __instance.transform.position = new Vector3(VRScreen.Width * 0.5f, VRScreen.Height * 0.5f, 0f);
            return false;
        }
    }
}
