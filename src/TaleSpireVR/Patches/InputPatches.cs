using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TaleSpireVR.Patches
{
    /// <summary>
    /// TaleSpire's mouse buttons have Tap, Hold, Press and MultiTap interactions. After a click the MultiTap waits ~0.75 s
    /// for a second click, and a second click within that time arrives only as a double-click, which no board tool uses:
    /// with the radial menu that was "A works again only after about a second" (closing the menu right after opening it,
    /// or opening the next one right after closing). In VR a double-click is passed on as a plain click.
    /// </summary>
    [HarmonyPatch(typeof(CameraController), "RightButton_OnMultiTap")]
    internal static class RightDoubleClickIsClickPatch
    {
        private static readonly MethodInfo Tap = AccessTools.Method(typeof(CameraController), "RightButton_OnTap");
        private static bool Prefix(CameraController __instance, InputAction.CallbackContext obj) => DoubleClicks.AsClick(__instance, Tap, obj);
    }

    [HarmonyPatch(typeof(CameraController), "LeftButton_OnMultiTap")]
    internal static class LeftDoubleClickIsClickPatch
    {
        private static readonly MethodInfo Tap = AccessTools.Method(typeof(CameraController), "LeftButton_OnTap");
        private static bool Prefix(CameraController __instance, InputAction.CallbackContext obj) => DoubleClicks.AsClick(__instance, Tap, obj);
    }

    [HarmonyPatch(typeof(CameraController), "MiddleButton_OnMultiTap")]
    internal static class MiddleDoubleClickIsClickPatch
    {
        private static readonly MethodInfo Tap = AccessTools.Method(typeof(CameraController), "MiddleButton_OnTap");
        private static bool Prefix(CameraController __instance, InputAction.CallbackContext obj) => DoubleClicks.AsClick(__instance, Tap, obj);
    }

    /// <summary>Debug ("clicklog n"): log TaleSpire's next n camera click events (button, kind, over UI).</summary>
    [HarmonyPatch(typeof(CameraController), "ProcessButtonForEvent")]
    internal static class ClickLogPatch
    {
        public static int Remaining;
        private static void Prefix(int buttonID, CameraController.ClickEventInteraction interactionEvent)
        {
            if (Remaining <= 0) return;
            Remaining--;
            Plugin.Log.LogInfo($"[clicklog] frame {Time.frameCount} button {buttonID} {interactionEvent} overUI={MouseManager.IsHoveringOverUI} worldMode={VRPointer.WorldMode} tool={BoardToolManager.CurrentTool?.GetType().Name}");
        }
    }

    internal static class DoubleClicks
    {
        private static int _logs;

        public static bool AsClick(CameraController controller, MethodInfo tap, InputAction.CallbackContext context)
        {
            if (!VRManager.Active || tap == null) return true;
            if (_logs++ < 10) Plugin.Log.LogInfo($"Double-click passed on as a click ({tap.Name})");
            tap.Invoke(controller, new object[] { context });
            return false;
        }
    }

    /// <summary>
    /// TaleSpire throws dice (and measures other flicks) by how fast the mouse's spot on the ground moved. In VR the mouse
    /// stays still while the laser moves, so that speed was always zero and thrown dice just dropped: dice in hand fly
    /// off with their own speed in your hand (DiceHand), anything else on the board with the speed of the laser's spot
    /// (VRPointer.LaserGroundVelocity).
    /// </summary>
    [HarmonyPatch(typeof(MouseManager), nameof(MouseManager.GroundDeltaPerSecond), MethodType.Getter)]
    internal static class LaserThrowSpeedPatch
    {
        private static void Postfix(ref Vector3 __result)
        {
            if (!VRManager.Active) return;
            if (DiceHand.TryHoldVelocity(out Vector3 v)) __result = v;
            else if (VRPointer.WorldMode) __result = VRPointer.LaserGroundVelocity;
        }
    }

    /// <summary>Log how fast dice are thrown (checking the laser throw speed); a throw restarts their removal wait (DiceCleanup).</summary>
    [HarmonyPatch(typeof(Dice.DiceRollManager), nameof(Dice.DiceRollManager.ThrowDice))]
    internal static class DiceThrowLogPatch
    {
        private static int _logs;
        private static void Prefix(RollId rollId, Unity.Mathematics.float3 velocity)
        {
            if (!VRManager.Active) return;
            DiceCleanup.Thrown(rollId);
            if (_logs++ < 50) Plugin.Log.LogInfo($"[dice] thrown at {Unity.Mathematics.math.length(velocity):F1} units/s ({(Vector3)velocity:F1})");
        }
    }

    /// <summary>A roll of yours came to rest (TaleSpire gathers its result): it leaves the board a little later (DiceCleanup).</summary>
    [HarmonyPatch]
    internal static class DiceLandedPatch
    {
        private static readonly System.Type RollType = AccessTools.TypeByName("Dice.Roll");
        private static readonly FieldInfo OwnerField = AccessTools.Field(RollType, "OwnerId");
        private static readonly FieldInfo RollIdField = AccessTools.Field(RollType, "RollId");

        private static MethodBase TargetMethod() => AccessTools.Method(RollType, "DoUpdate");

        private static void Postfix(object __instance, object __result)
        {
            if (!VRManager.Active || __result == null || __result.ToString() != "UpdatedWithResult") return;
            if (OwnerField == null || RollIdField == null || !LocalPlayer.Id.Equals(OwnerField.GetValue(__instance))) return;
            DiceCleanup.Landed((RollId)RollIdField.GetValue(__instance));
        }
    }

    /// <summary>Dice in hand are held by your hand (DiceHand.Hold), not out where the laser meets the board as TaleSpire does.</summary>
    [HarmonyPatch(typeof(Dice.DiceRollManager), nameof(Dice.DiceRollManager.GatherDice))]
    internal static class DiceInHandPatch
    {
        private static void Prefix(ref Vector3 position)
        {
            if (VRManager.Active && DiceHand.TryHoldPoint(out Vector3 p)) position = p;
        }
    }

    /// <summary>A roll's result panel stays up 5 s in TaleSpire; in VR (where it is the floating results banner) 15 s.</summary>
    [HarmonyPatch(typeof(UIDiceRollResult), "HideAfterDelay")]
    internal static class LongerDiceResultPatch
    {
        private static void Prefix(ref float delayInSeconds)
        {
            if (VRManager.Active) delayInSeconds = Mathf.Max(delayInSeconds, DiceResultBanner.ShowSeconds);
        }
    }
}
