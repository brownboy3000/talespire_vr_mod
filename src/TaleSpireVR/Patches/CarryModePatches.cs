using HarmonyLib;
using UnityEngine.InputSystem;

namespace TaleSpireVR.Patches
{
    /// <summary>
    /// Carrying a mini (alone or a lassoed group), TaleSpire switches between moving it, turning it (Alt) and raising it
    /// (Ctrl) on those keys' down / up events - from any keyboard, the real one too. A stray Alt (pressed on the real
    /// keyboard, or an up event that never arrived) left a carry in its turning mode: the mini no longer followed the
    /// laser and could not be raised, only turned. In VR the modes follow the controllers instead (CarryModes); TaleSpire's
    /// own key events are ignored while carrying.
    /// </summary>
    internal static class CarryModes
    {
        /// <summary>Turn mode: right trackpad held.</summary>
        public static bool Rotate => SteamVRInput.RotateModifier.Held;
        /// <summary>Raise / lower mode: left trackpad held, or the zoom buttons raising / lowering it (CreatureHold).</summary>
        public static bool Vertical => SteamVRInput.ElevateModifier.Held || CreatureHold.ElevatingWithButtons;

        /// <summary>
        /// Each frame while carrying: put the carry tool in the mode the controllers ask for, from its current mode, so
        /// nothing can get out of step. (TaleSpire also enters the modes itself while the trigger is held, and leaves the
        /// raise mode itself, from the same modifier states.)
        /// </summary>
        public static void Sync(BoardTool tool)
        {
            if (!(tool is CreatureMoveBoardTool || tool is CreatureGroupMoveBoardTool)) return;
            var t = Traverse.Create(tool);
            var machine = t.Field("_stateMachine").FieldExists() ? t.Field("_stateMachine") : t.Field("stateMachine");
            object state = machine.Method("GetState").GetValue();
            if (state == null) return;
            string mode = state.GetType().Name;
            // (What TaleSpire's own checks see too: CarryModifierStatePatch.)
            bool rotate = ControllerManager.Modifiers.BtnRotationMod.IsDown;
            bool vertical = !rotate && ControllerManager.Modifiers.BtnVerticalMod.IsDown;
            if (mode == "RotateState" && !rotate) Send(tool, state, "RotationButton", false);
            else if (mode == "VerticalState" && !vertical) Send(tool, state, "VerticalButton", false);
            else if (mode == "MoveState")
            {
                if (rotate) Send(tool, state, "RotationButton", true);
                else if (vertical) Send(tool, state, "VerticalButton", true);
            }
        }

        private static void Send(BoardTool tool, object state, string button, bool down)
        {
            Traverse.Create(state).Method(button, new[] { typeof(bool) }).GetValue(down);
            Plugin.Log.LogInfo($"[carry] {tool.GetType().Name}: {(button == "RotationButton" ? "turn" : "raise / lower")} mode {(down ? "on" : "off")}");
        }
    }

    /// <summary>
    /// TaleSpire's "turn" (Alt) and "raise" (Ctrl) modifiers, as its carry tools read them every frame, are in VR exactly
    /// what the controllers say (CarryModes): a held real-keyboard key or a key event lost while a text box had focus
    /// can no longer leave them down. (Our virtual Alt / Ctrl still go out for everything else.)
    /// </summary>
    [HarmonyPatch(typeof(ControllerManager.Button), nameof(ControllerManager.Button.IsDown), MethodType.Getter)]
    internal static class CarryModifierStatePatch
    {
        private static void Postfix(ControllerManager.Button __instance, ref bool __result)
        {
            if (!VRManager.Active) return;
            if (ReferenceEquals(__instance, ControllerManager.Modifiers.BtnRotationMod)) __result = CarryModes.Rotate && !TaleSpire.Ui.InputFieldManager.IsInputFieldActive();
            else if (ReferenceEquals(__instance, ControllerManager.Modifiers.BtnVerticalMod)) __result = CarryModes.Vertical && !TaleSpire.Ui.InputFieldManager.IsInputFieldActive();
        }
    }

    [HarmonyPatch(typeof(CreatureMoveBoardTool), "RotateObjectModifier")]
    internal static class CarryAltPatch
    {
        private static bool Prefix(InputAction.CallbackContext obj) => !VRManager.Active;
    }

    [HarmonyPatch(typeof(CreatureMoveBoardTool), "VerticalModButton")]
    internal static class CarryCtrlPatch
    {
        private static bool Prefix(InputAction.CallbackContext obj) => !VRManager.Active;
    }

    [HarmonyPatch(typeof(CreatureGroupMoveBoardTool), "OnRotateObjectModifier")]
    internal static class GroupCarryAltPatch
    {
        private static bool Prefix(InputAction.CallbackContext obj) => !VRManager.Active;
    }

    [HarmonyPatch(typeof(CreatureGroupMoveBoardTool), "OnVerticalModButton")]
    internal static class GroupCarryCtrlPatch
    {
        private static bool Prefix(InputAction.CallbackContext obj) => !VRManager.Active;
    }
}
