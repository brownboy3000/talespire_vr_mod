using System.Reflection;
using Bounce.Singletons;
using Bouncyrock.Physics;
using Dice;
using HarmonyLib;
using Unity.Physics;
using UnityEngine;
using Ray = UnityEngine.Ray;

namespace TaleSpireVR
{
    /// <summary>
    /// Taking dice out of TaleSpire's tray with a laser. TaleSpire itself takes them out only when the mouse is pressed on
    /// the tray (or its GM button) and dragged off it, and moves them only while the button stays down. In VR a trigger
    /// click on the tray picks them up too, a press that leaves the menu window altogether takes them out at once (the
    /// cursor stops at the window's edge, so TaleSpire never sees it leave the tray), and dice in hand are held right by
    /// the hand in control, a little way along its pointer (the pointer stops at them), until the next press: hold the
    /// trigger, swing and let go to throw them like real dice - they fly off with their own speed in your hand.
    /// </summary>
    internal static class DiceHand
    {
        private static readonly MethodInfo SpawnMethod = AccessTools.Method(typeof(UIDiceTray), "Spawn");
        private static readonly MethodInfo HasDiceMethod = AccessTools.Method(typeof(UIDiceTray), "HasActiveChildren");
        private static readonly FieldInfo GmTrayField = AccessTools.Field(typeof(UIDiceTray), "_gmSpawnTray");
        private static readonly FieldInfo RollIdField = AccessTools.Field(typeof(DiceBoardTool), "_diceRollId");
        private static readonly FieldInfo RayLayersField = AccessTools.Field(typeof(DiceBoardTool), "_diceRayCastLayers");

        /// <summary>Dice in hand are held this far along the pointer from its start (real metres at your zoom).</summary>
        public const float HoldDistance = 0.15f;
        /// <summary>...and at least this far above whatever is below them (board units).</summary>
        private const float HoldClearance = 0.4f;

        private static Vector3 _hold, _lastHold, _velocity;
        private static int _holdFrame = -10;

        private enum Spot { None, Tray, GmButton }
        private static Spot _pressed, _clicked;
        private static int _clickWait;
        /// <summary>Frames between a click's release and taking the dice out: TaleSpire must see the button go up first, or its dice tool takes that release as the throw.</summary>
        private const int ClickWaitFrames = 4;

        /// <summary>Dice are out of the tray and not thrown yet (TaleSpire's dice tool is on).</summary>
        public static bool InHand => BoardToolManager.CurrentTool is DiceBoardTool;

        /// <summary>
        /// The trigger of the hand in control, each frame. onPanel / uiPos: the menu window the press is on and the
        /// cursor there; laserOnPanel: whether that laser still actually hits the window.
        /// </summary>
        public static void UpdatePress(bool trigger, bool triggerLast, bool onPanel, Vector2 uiPos, bool laserOnPanel)
        {
            if (InHand) { _pressed = _clicked = Spot.None; _clickWait = 0; return; }
            if (_clickWait > 0 && --_clickWait == 0)
            {
                if (!trigger) Spawn(_clicked, "clicked");
                _clicked = Spot.None;
            }
            if (trigger && !triggerLast) { _pressed = onPanel ? SpotAt(uiPos) : Spot.None; _clickWait = 0; }
            if (_pressed == Spot.None) return;
            if (trigger && !laserOnPanel) { Spawn(_pressed, "dragged off the window"); _pressed = Spot.None; }
            else if (!trigger)
            {
                if (onPanel && SpotAt(uiPos) == _pressed) { _clicked = _pressed; _clickWait = ClickWaitFrames; }
                _pressed = Spot.None;
            }
        }

        /// <summary>
        /// Each frame while the dice are in hand (VRPointer): where they are held - HoldDistance along the pointer of the
        /// hand in control, kept off the board - and how fast that point moves (their throw). gather: no button is down,
        /// so TaleSpire is not moving them itself: hold them there (while a button is down TaleSpire gathers them each
        /// frame, and Patches/InputPatches.cs puts that at the same point).
        /// </summary>
        public static void Hold(Ray ray, VRRig rig, bool gather)
        {
            if (!InHand || RollIdField == null || rig == null) return;
            var tool = BoardToolManager.CurrentTool as DiceBoardTool;
            CollisionFilter filter = RayLayersField != null ? (SerializableCollisionQueryFilter)RayLayersField.GetValue(tool) : (CollisionFilter)BouncePhysicsLayers.QueryAll;
            var rc = CameraController.Raycast;
            float reach = HoldDistance * rig.Scale;
            // Pointing at the board closer than that: just in front of it.
            if (rc != null && rc.GetRayPoint(ray, out HitInfo hit, filter, 0.01f)) reach = Mathf.Min(reach, Mathf.Max(0f, hit.distance - HoldClearance));
            Vector3 p = ray.GetPoint(reach);
            // Your hand below the board's surface (reaching into a hill, a table): on top of it instead.
            if (rc != null && !rc.GetRayPoint(new Ray(p, Vector3.down), out _, filter, 0.01f)
                && rc.GetRayPoint(new Ray(p, Vector3.up), out HitInfo above, filter, 0.01f) && above.distance < 20f)
                p.y = above.point.y + HoldClearance;

            float dt = Mathf.Max(1e-4f, Time.unscaledDeltaTime);
            Vector3 v = Time.frameCount - _holdFrame == 1 ? (p - _lastHold) / dt : Vector3.zero;
            _velocity = Vector3.Lerp(_velocity, v, 1f - Mathf.Exp(-dt / 0.03f));
            _lastHold = _hold = p;
            _holdFrame = Time.frameCount;
            if (gather) SimpleSingletonBehaviour<DiceRollManager>.Instance.GatherDice(p, (RollId)RollIdField.GetValue(tool));
        }

        /// <summary>Where dice in hand are held this frame (false: none in hand, or not worked out lately).</summary>
        public static bool TryHoldPoint(out Vector3 point)
        {
            point = _hold;
            return InHand && Time.frameCount - _holdFrame <= 1;
        }

        /// <summary>How fast the dice in hand are moving (world units/s): their speed when you let go.</summary>
        public static bool TryHoldVelocity(out Vector3 velocity)
        {
            velocity = _velocity;
            return InHand && Time.frameCount - _holdFrame <= 1;
        }

        private static Spot SpotAt(Vector2 uiPos)
        {
            var tray = GUIManager.DiceTray;
            if (tray == null || !tray.isActiveAndEnabled || !HasDice(tray)) return Spot.None;
            if (GmTrayField?.GetValue(tray) is GameObject gm && DiceUi.TryGetRect(gm.transform as RectTransform, out Rect g) && g.Contains(uiPos))
                return Spot.GmButton;
            return DiceUi.TryGetRect(tray.transform as RectTransform, out Rect t) && t.Contains(uiPos) ? Spot.Tray : Spot.None;
        }

        /// <summary>Debug ("dicein [kind] [count]"): put dice in the tray (kind: TaleSpire's die number, 1 = its first) and take them out.</summary>
        public static void DebugTakeOut(ushort kind, int count)
        {
            var tray = GUIManager.DiceTray;
            if (tray == null) { Plugin.Log.LogInfo("[dice] no tray"); return; }
            tray.UpdateTrayDice(new DieKind(kind), count);
            Spawn(Spot.Tray, "debug");
        }

        /// <summary>Debug (diceui): the click areas.</summary>
        public static string Describe()
        {
            var tray = GUIManager.DiceTray;
            if (tray == null) return "no tray";
            string gm = GmTrayField?.GetValue(tray) is GameObject g && DiceUi.TryGetRect(g.transform as RectTransform, out Rect gr) ? gr.ToString() : "-";
            string t = DiceUi.TryGetRect(tray.transform as RectTransform, out Rect tr) ? tr.ToString() : "-";
            return $"tray={t} gmButton={gm} hasDice={HasDice(tray)} inHand={InHand}";
        }

        private static bool HasDice(UIDiceTray tray) => HasDiceMethod != null && (bool)HasDiceMethod.Invoke(tray, null);

        /// <summary>The roll the dice in hand belong to (default when none).</summary>
        public static RollId RollInHand => InHand && RollIdField != null ? (RollId)RollIdField.GetValue(BoardToolManager.CurrentTool) : default;

        private static void Spawn(Spot spot, string how)
        {
            var tray = GUIManager.DiceTray;
            if (InHand || tray == null || SpawnMethod == null || !HasDice(tray)) return;   // TaleSpire already took them out
            bool gm = spot == Spot.GmButton;
            SpawnMethod.Invoke(tray, new object[] { gm });
            Plugin.Log.LogInfo($"[dice] {(gm ? "GM roll" : "roll")} taken out of the tray ({how})");
        }
    }

    /// <summary>
    /// Your own thrown dice leave the board on their own: RemoveAfter seconds after they land, the roll is removed (TaleSpire's
    /// "Remove", for everyone). Picking them up again or throwing them again starts the wait over; the result banner stays.
    /// </summary>
    internal static class DiceCleanup
    {
        public const float RemoveAfter = 10f;

        private static readonly MethodInfo TryAcquireRoll = AccessTools.Method(typeof(DiceRollManager), "TryAcquireRoll");
        private static readonly FieldInfo MenuRollField = AccessTools.Field(typeof(DiceMenuBoardTool), "_diceRollId");
        private static readonly System.Collections.Generic.Dictionary<RollId, float> _due = new System.Collections.Generic.Dictionary<RollId, float>();
        private static readonly System.Collections.Generic.List<RollId> _ready = new System.Collections.Generic.List<RollId>();

        /// <summary>One of your rolls came to rest (Patches: DiceLandedPatch).</summary>
        public static void Landed(RollId id) { _due[id] = Time.unscaledTime + RemoveAfter; }

        /// <summary>Thrown again: it waits for its new landing.</summary>
        public static void Thrown(RollId id) { _due.Remove(id); }

        public static void Update()
        {
            if (_due.Count == 0) return;
            _ready.Clear();
            foreach (var kv in _due) if (Time.unscaledTime >= kv.Value) _ready.Add(kv.Key);
            foreach (var id in _ready)
            {
                _due.Remove(id);
                if (DiceHand.InHand && DiceHand.RollInHand.Equals(id)) continue;   // picked up again: waits for the next landing
                if (!SimpleSingletonBehaviour<DiceRollManager>.HasInstance) continue;
                var mgr = SimpleSingletonBehaviour<DiceRollManager>.Instance;
                var args = new object[] { id, null };
                if (TryAcquireRoll != null && !(bool)TryAcquireRoll.Invoke(mgr, args)) continue;   // already removed
                // Its radial menu open? Close it first (its options would act on a roll that is gone).
                if (BoardToolManager.CurrentTool is DiceMenuBoardTool menu && MenuRollField != null && id.Equals(MenuRollField.GetValue(menu)))
                    RadialExtraButton.CloseTaleSpireMenu();
                mgr.RemoveRoll(id);
                Plugin.Log.LogInfo($"[dice] roll {id} removed {RemoveAfter:F0} s after it landed");
            }
        }
    }
}
