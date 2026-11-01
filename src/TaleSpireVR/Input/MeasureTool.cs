using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// Measure = hold the Measure grip (default: left) and pull that hand's trigger while pointing at the board:
    /// TaleSpire's ruler starts where the laser points; release the trigger: the ruler ends where it points now.
    /// Uses the ruler type last picked in TaleSpire's ruler menu (line, circle, cone, box). A cone needs a third point
    /// (its width): it follows the laser until the next trigger pull. A quick pull without moving clears the ruler.
    /// The ruler's distance label is shown in 3D next to it.
    /// </summary>
    internal static class MeasureTool
    {
        public static bool Active { get; private set; }
        public static Hand ActiveHand { get; private set; }

        private static int _lastMode;
        private static bool _subscribed;
        private static int _firstClickFrame = -1;
        private static bool _firstPlaced;
        private static Vector3 _startPoint;

        private static readonly FieldInfo LastModeField = AccessTools.Field(typeof(RulerBoardTool), "_lastModeIndex");
        private static readonly FieldInfo StatesField = AccessTools.Field(typeof(RulerBoardTool), "_states");
        private static readonly FieldInfo ModeField = AccessTools.Field(typeof(Ruler), "_mode");

        // A cone we started is still waiting for its width point: the next trigger pull (grip or not) places it.
        private static bool _awaitingMore;

        // A finished ruler of ours is still up (TaleSpire's ruler tool stays active to show it).
        private static bool _rulerLeftUp;

        /// <summary>
        /// The ruler stays up after measuring so you can read it, but TaleSpire's ruler tool takes every click while it is
        /// shown, so a plain press on the board did nothing (no pickups, no doors). VRPointer calls this on such a press:
        /// put the ruler away (back to the normal tool) and let the press through a few frames later.
        /// </summary>
        public static bool TryPutAwayRuler()
        {
            if (!_rulerLeftUp) return false;
            _rulerLeftUp = false;
            if (_awaitingMore || Active || !(BoardToolManager.CurrentTool is RulerBoardTool tool)) return false;
            try
            {
                tool.SwitchToOwnDefaultTool();
                Plugin.Log.LogInfo("[measure] ruler put away (board press)");
                return true;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[measure] could not put the ruler away: " + e.Message); return false; }
        }

        /// <param name="onBoard">Per hand: is its laser on the board (not on a menu)?</param>
        public static void Update(VRRig rig, VRHands hands, bool[] onBoard)
        {
            if (!_subscribed)
            {
                _subscribed = true;
                RulerBoardTool.OnRulerModeChange += i => _lastMode = i;
            }
            if (rig == null || !rig.OnBoard)
            {
                Active = false;
                _awaitingMore = false;
                return;
            }

            if (!Active)
            {
                if (_awaitingMore && !(BoardToolManager.CurrentTool is RulerBoardTool t && InSettingHandles(t))) _awaitingMore = false;
                for (int h = 0; h < 2; h++)
                {
                    if (SteamVRInput.Measure.Armed[h] && SteamVRInput.Trigger((Hand)h).Down && onBoard[h]
                        && !_awaitingMore && !CreatureLasso.Active && !CreatureHold.IsHolding)
                    {
                        Begin((Hand)h, hands);
                        break;
                    }
                }
                return;
            }

            var tool = BoardToolManager.CurrentTool as RulerBoardTool;
            if (tool == null)
            {
                // Cancelled from TaleSpire (right click, Esc, another tool).
                Active = false;
                return;
            }

            if (!_firstPlaced && Time.frameCount >= _firstClickFrame)
            {
                // The ruler spawned last frame and its first handle now follows the laser: pin it here.
                _startPoint = PointerPoint(rig, hands);
                Click(tool, 0);
                _firstPlaced = true;
            }

            if (!SteamVRInput.Trigger(ActiveHand).Held)
                End(rig, hands, tool);
        }

        private static void Begin(Hand hand, VRHands hands)
        {
            ActiveHand = hand;
            hands.SetActive(hand);
            SteamVRInput.Measure.Disarm(hand);   // one measurement per squeeze of the grip
            _rulerLeftUp = false;
            try
            {
                var tool = SingletonBehaviour<BoardToolManager>.Instance.GetTool<RulerBoardTool>();
                if (tool != null && LastModeField != null) LastModeField.SetValue(tool, _lastMode);
                RulerBoardTool.SwitchToTool();   // also restarts an existing ruler
                tool = BoardToolManager.CurrentTool as RulerBoardTool;
                if (tool == null) { Plugin.Log.LogInfo("[measure] ruler tool not available here"); return; }
                tool.SetRulerMode(_lastMode);
                Active = true;
                _firstPlaced = false;
                _firstClickFrame = Time.frameCount + 2;
                SteamVRInput.Haptic(hand, 0.015f, 160f, 0.3f);
                Plugin.Log.LogInfo($"[measure] start ({hand} hand, ruler type {_lastMode})");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[measure] could not start: " + e.Message); Active = false; }
        }

        private static void End(VRRig rig, VRHands hands, RulerBoardTool tool)
        {
            Active = false;
            try
            {
                if (!_firstPlaced)
                {
                    tool.SwitchToOwnDefaultTool();
                    return;
                }
                Vector3 end = PointerPoint(rig, hands);
                if (Vector3.Distance(end, _startPoint) < 0.02f * rig.Scale)
                {
                    // Quick pull without moving: clear the ruler instead of leaving a zero-length one.
                    tool.SwitchToOwnDefaultTool();
                    Plugin.Log.LogInfo("[measure] cleared");
                    return;
                }
                Click(tool, 0);
                if (InSettingHandles(tool) && tool.TryGetRuler(out var ruler) && ModeField?.GetValue(ruler) is LineRulerMode)
                    Click(tool, 1);   // line ruler: finish with these two points
                SteamVRInput.Haptic(ActiveHand, 0.015f, 160f, 0.3f);
                _awaitingMore = InSettingHandles(tool);   // cone: width point still to come
                _rulerLeftUp = true;
                string mode = tool.TryGetRuler(out var r) ? ModeField?.GetValue(r)?.GetType().Name : "-";
                Plugin.Log.LogInfo($"[measure] end ({mode}, still placing points: {InSettingHandles(tool)})");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[measure] could not finish: " + e.Message); }
        }

        private static void Click(RulerBoardTool tool, int button)
        {
            var click = new CameraController.CameraClickEvent(button, VRPointer.CameraCenter, VRPointer.WorldRay, Vector3.zero, 0f, Vector3.zero,
                false, CameraController.ClickEventInteraction.TAP, Time.time, Vector3.zero);
            tool.OnCameraClick(click);
        }

        private static bool InSettingHandles(RulerBoardTool tool)
        {
            var states = StatesField?.GetValue(tool);
            var current = states?.GetType().GetProperty("CurrentState")?.GetValue(states);
            return current != null && current.ToString() == "SettingHandles";
        }

        private static Vector3 PointerPoint(VRRig rig, VRHands hands)
        {
            var ray = VRPointer.WorldRay;   // the measuring hand's laser (it is the active hand)
            return ray.GetPoint(Mathf.Max(0f, VRPointer.WorldHitDistance(ray, rig, true)));
        }
    }
}
