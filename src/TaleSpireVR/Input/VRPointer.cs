using Bouncyrock.Physics;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TaleSpireVR
{
    /// <summary>
    /// Works out what each controller's laser points at (menu panel, radial menu, hotkeys, or the board) and drives
    /// the virtual mouse + keyboard from the hand in control (see ArbitrateHands). Static state is read by the
    /// Harmony patches.
    /// </summary>
    [DefaultExecutionOrder(30020)]
    internal class VRPointer : MonoBehaviour
    {
        public enum Target { None, MainPanel, RadialPanel, Hotkeys, Keyboard, World, DicePanel }

        /// <summary>True while the right stick's up/down scrolls the menu under the laser (instead of moving you).</summary>
        public static bool StickScrollsMenu { get; private set; }

        /// <summary>True while the laser drives TaleSpire's 3D board interactions.</summary>
        public static bool WorldMode { get; private set; }
        public static Ray WorldRay { get; private set; }
        public static Target Current { get; private set; }
        /// <summary>A press that began on a menu window is still held (a slider pin may be being dragged).</summary>
        public static bool UiPressHeld { get; private set; }
        /// <summary>Debug: each hand's target and who is in control (status.txt).</summary>
        public static string HandSummary = "-";
        /// <summary>Where TaleSpire thinks the mouse is (centre of the camera) while in world mode.</summary>
        public static Vector2 CameraCenter;
        /// <summary>UI pixel position of the active pointer when it is on a panel (for the on-panel cursor).</summary>
        public static Vector2? UiCursor { get; private set; }

        /// <summary>Debug: override the laser with a fixed ray for a while (testing without holding controllers).</summary>
        public static Ray? DebugRay;
        public static float DebugRayUntil;
        private static bool DebugRayActive => DebugRay.HasValue && Time.unscaledTime < DebugRayUntil;
        /// <summary>Debug: pretend the active trigger is held for this many frames.</summary>
        public static int DebugTriggerFrames;
        public static int TraceFrames;

        private class HandPointer
        {
            public Hand Hand;
            public Target Target;
            public Vector2 UiPos;
            public float HitDist;
            public int Hotkey;
            public readonly PanelStabilizer Stab = new PanelStabilizer();
        }

        private readonly HandPointer[] _hp = { new HandPointer { Hand = Hand.Left }, new HandPointer { Hand = Hand.Right } };
        private readonly bool[] _onBoard = new bool[2];

        /// <summary>Mouse-wheel units per second at full stick (a wheel notch is 120).</summary>
        private const float ScrollPerSecond = 600f;
        private Target _captured = Target.None;
        private bool _anyHeldLast, _triggerLast;
        private Vector2 _lastUiPos = new Vector2(-100, -100);
        private Target _lastHover = Target.None;
        private Hand _lastHoverHand;
        private int _pressLogs;

        public static void EarlyUpdate(VRHands hands)
        {
            if (DebugRayActive) WorldRay = DebugRay.Value;
            else if (hands.Active.Valid) WorldRay = hands.Active.Ray;
        }

        private void LateUpdate()
        {
            var mgr = VRManager.Instance;
            var hands = mgr.Hands;
            var panels = mgr.Panels;
            var rig = mgr.Rig;
            if (hands == null || panels == null || rig == null || rig.GameCamera == null) return;

            // TaleSpire's world cursor while the laser is on the board. Some tools (rulers, AoE) reject clicks outside
            // the monitor's resolution, so use the monitor centre (it lies inside the VR camera's pixel rect too).
            CameraCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            // Laser pointers are less steady than a mouse: require a bigger move before UI treats it as a drag.
            var es = UnityEngine.EventSystems.EventSystem.current;
            int dragThreshold = Mathf.RoundToInt(35f * UICapture.Height / 1080f);
            if (es != null && es.pixelDragThreshold < dragThreshold) es.pixelDragThreshold = dragThreshold;

            hands.RefreshPoses();

            // A is always a quick right-click (a few frames) however long it is held: TaleSpire only reacts to right
            // "taps" released within 0.2 s, and a VR button press is often longer. (Its double-clicks are made taps too,
            // see Patches/InputPatches.cs, so a second A right after the first works at once.)
            if (SteamVRInput.RightClick.Down) _rightPulse = 3;
            bool rightBtn = _rightPulse > 0;
            if (_rightPulse > 0) _rightPulse--;
            bool middleBtn = SteamVRInput.MiddleClick.Held;
            bool anyHeldBefore = _anyHeldLast;
            bool fp = FirstPersonMode.Active;

            // 1) What does each controller point at?
            for (int i = 0; i < 2; i++)
            {
                var hp = _hp[i];
                var hs = hands.Get(hp.Hand);
                // A (right click) and the stick click are on the right controller.
                bool pressed = SteamVRInput.Trigger(hp.Hand).Down || (hp.Hand == Hand.Right && (SteamVRInput.RightClick.Down || SteamVRInput.MiddleClick.Down));
                // (In first person the lasers point exactly the same; they are only drawn while a grip is held or a
                // radial menu is open: VRHands / FirstPersonMode.PointerVisible.)
                bool valid = hs.Valid || (DebugRayActive && hp.Hand == hands.ActiveHand);
                // Palm turned up for the dice window on that forearm: that laser points sideways across your view, so it is off.
                if (hp.Hand == Hand.Left && DiceForearm.Wanted) valid = false;
                Ray ray = DebugRayActive && hp.Hand == hands.ActiveHand ? DebugRay.Value : hs.Ray;
                Evaluate(hp, valid, ray, panels, rig, pressed);
                _onBoard[i] = hp.Target == Target.World;
            }

            // Right stick click while the stream view is held: start / stop its orbit (not a middle click then, unless
            // something is carried or being placed - in Build Mode it still turns the held tile).
            if (SteamVRInput.MiddleClick.Down && !anyHeldBefore && !PlacementLocked() && StreamView.Instance != null
                && StreamView.Instance.TryStickClick(hands.Right.Ray, hands.Right.Valid && _hp[(int)Hand.Right].Target == Target.World))
                _stickClickUsed = true;
            if (!SteamVRInput.MiddleClick.Held) _stickClickUsed = false;
            if (_stickClickUsed || fp) middleBtn = false;   // first person: the right stick jumps / crouches

            // 2) Which hand is in control of TaleSpire's cursor (only a button press changes it).
            ArbitrateHands(hands, anyHeldBefore);

            // Grip + trigger gestures (measure / lasso) on the board: that trigger drives the gesture, not a click.
            // (Not in first person: there a grip just shows that hand's pointer.)
            if (!fp)
            {
                Safe("measure", () => MeasureTool.Update(rig, hands, _onBoard));
                Safe("lasso", () => CreatureLasso.Update(rig, hands, _onBoard));
            }
            bool gesture = MeasureTool.Active || CreatureLasso.Active;

            var activeHand = hands.ActiveHand;
            var activeState = hands.Get(activeHand);
            var a = _hp[(int)activeHand];
            var o = _hp[1 - (int)activeHand];
            if (DebugRayActive) WorldRay = DebugRay.Value;
            else if (activeState.Valid) WorldRay = activeState.Ray;
            if (activeHand != _worldRayHand) { _worldRayHand = activeHand; _worldRayChangedFrame = Time.frameCount; }

            bool trigger = SteamVRInput.Trigger(activeHand).Held || DebugTriggerFrames > 0;
            if ((MeasureTool.Active && MeasureTool.ActiveHand == activeHand) || (CreatureLasso.Active && CreatureLasso.ActiveHand == activeHand))
                trigger = false;
            if (DebugTriggerFrames > 0) DebugTriggerFrames--;
            if (fp) FirstPersonA(hands, ref rightBtn);
            else
            {
                _fpAPending = null;
                AOnRadialCloses(ref rightBtn);
            }
            bool retapArms = !fp;
            Safe("radial retap", () => RadialRetap(a.Target == Target.World, retapArms, ref rightBtn));
            bool buttonsDown = trigger || rightBtn || middleBtn;

            // A ruler left up after measuring: a plain press on the board puts it away first (TaleSpire's ruler tool
            // would take the click), then goes through as usual once the normal tool is back.
            if (buttonsDown && !anyHeldBefore && _pressDelay == 0 && !gesture && a.Target == Target.World && MeasureTool.TryPutAwayRuler())
                _pressDelay = PressDelayFrames;

            // A press on the board right after TaleSpire's board cursor was off (the other laser was resting on the menu,
            // or this laser just came off it) also waits: TaleSpire needs a few frames on the board to see what is under
            // the laser, and a press in the very first frame missed - every time, for as long as the other laser stayed
            // on the menu.
            if (buttonsDown && !anyHeldBefore && _pressDelay == 0 && !gesture && a.Target == Target.World && _worldFrames < PressDelayFrames)
                _pressDelay = PressDelayFrames - _worldFrames;

            // A press that just moved control to the other hand reaches TaleSpire a few frames late: its object picking
            // lags the laser by 2-3 frames, so an immediate click would land on whatever the previous laser pointed at
            // (the pickup or radial menu "misses"). A press released during the wait still becomes a short click.
            if (_pressDelay > 0)
            {
                _pressDelay--;
                if (trigger) _tapLeft = 2;
                if (rightBtn) _tapRight = 2;
                if (middleBtn) _tapMiddle = 2;
                trigger = rightBtn = middleBtn = false;
            }
            else
            {
                if (_tapLeft > 0) { _tapLeft--; trigger = true; }
                if (_tapRight > 0) { _tapRight--; rightBtn = true; }
                if (_tapMiddle > 0) { _tapMiddle--; middleBtn = true; }
            }
            bool anyHeld = trigger || rightBtn || middleBtn;
            bool pressedNow = anyHeld && !_anyHeldLast;

            // Hovering a menu with the other laser (no buttons down, nothing being placed): the menu cursor follows
            // that laser so its buttons light up, but the board cursor and picking stay with the hand in control.
            bool uiHover = !anyHeldBefore && !buttonsDown && !gesture && _pressDelay == 0
                           && !IsUi(a.Target) && IsPanel(o.Target) && !PlacementLocked();
            var d = uiHover ? o : a;
            hands.HighlightHand = d.Hand;
            HandSummary = $"L:{_hp[0].Target} R:{_hp[1].Target} inControl={activeHand} cursor={d.Hand} delay={_pressDelay} locked={PlacementLocked()}{(fp ? $" fp(aPending={_fpAPending?.ToString() ?? "-"} extraPress={_extraPress})" : "")}";

            Target target = d.Target;
            Vector2 uiPos = d.UiPos;
            if (TraceFrames > 0)
            {
                TraceFrames--;
                Plugin.Log.LogInfo($"[trace] active={activeHand} L:{_hp[0].Target}@{_hp[0].UiPos} R:{_hp[1].Target}@{_hp[1].UiPos} debugRay={DebugRayActive} quadPos={panels.Main.T.position}");
            }

            // Our own VR buttons (hotkey row, keyboard). Isolated so a fault there can never stop normal clicking.
            Safe("hotkeys", () =>
            {
                if (target == Target.Hotkeys && SteamVRInput.Trigger(activeHand).Down && _captured == Target.None)
                    panels.Strip.Press(a.Hotkey);
                panels.Strip.SetHover(a.Target == Target.Hotkeys ? a.Hotkey : (o.Target == Target.Hotkeys ? o.Hotkey : -1));
            });
            // Keyboard: both hands type independently (like two fingers), each with its own hover and trigger.
            Safe("keyboard", () =>
            {
                for (int i = 0; i < 2; i++)
                {
                    var hp = _hp[i];
                    bool onKey = hp.Target == Target.Keyboard;
                    var trig = SteamVRInput.Trigger(hp.Hand);
                    if (onKey && trig.Down) panels.Keyboard.Press(hp.Hotkey, i);
                    panels.Keyboard.Hold(i, onKey ? hp.Hotkey : -1, trig.Held);
                }
                panels.Keyboard.SetHover(_hp[0].Target == Target.Keyboard ? _hp[0].Hotkey : -1,
                                         _hp[1].Target == Target.Keyboard ? _hp[1].Hotkey : -1);
            });

            // Keep sending input to whatever the press started on until every button is released.
            if (pressedNow) _captured = target;
            if (!anyHeld) _captured = Target.None;
            UiPressHeld = IsPanel(_captured);
            if (_captured != Target.None && _captured != target)
            {
                if (_captured == Target.World) target = Target.World;
                else
                {
                    target = _captured;
                    uiPos = _lastUiPos;
                }
            }
            // Dice in hand (taken out of the tray, see DiceHand): the lasers drive the board only (the menu windows step
            // aside), so the dice follow the laser and are thrown where it points, though the press began on a menu.
            bool diceInHand = DiceHand.InHand;
            if (diceInHand)
            {
                target = Target.World;
                if (anyHeld) _captured = Target.World;
            }
            Safe("dice tray", () => DiceHand.UpdatePress(trigger, _triggerLast, IsPanel(target), uiPos, d.Target == target));

            Current = target;
            WorldMode = target == Target.World;
            _worldFrames = WorldMode ? _worldFrames + 1 : 0;
            if (diceInHand) Safe("dice hold", () => DiceHand.Hold(WorldRay, rig, !anyHeld));

            if (IsPanel(target))
            {
                VirtualInput.Position = uiPos;
                _lastUiPos = uiPos;
                UiCursor = uiPos;
                if (pressedNow) LogPress("press", activeHand, target, uiPos);
            }
            else
            {
                // Off any panel: park the UI cursor outside the screen so no TaleSpire UI is hovered or clicked.
                VirtualInput.Position = new Vector2(-50f, -50f);
                UiCursor = null;
            }
            if (!anyHeld && _anyHeldLast && IsPanel(target)) LogPress("release", activeHand, target, _lastUiPos);

            // While lassoing, nothing is clicked (TaleSpire would start its own drag-select or pick a creature up).
            // Right stick scrolls the menu under the laser (lists, the library, side menus) instead of moving or turning
            // you: up/down scrolls vertically, left/right horizontally (whichever direction the stick is pushed more).
            Vector2 stick = SteamVRInput.TurnHeight.Value;
            StickScrollsMenu = target == Target.MainPanel;
            if (StickScrollsMenu && stick.magnitude > 0.2f)
            {
                float rate = ScrollPerSecond * Mathf.Max(0f, VRConfig.MenuScrollSpeed.Value) * Time.unscaledDeltaTime;
                bool vertical = Mathf.Abs(stick.y) >= Mathf.Abs(stick.x);
                float amount = vertical ? stick.y : stick.x;
                float v = Mathf.Sign(amount) * Mathf.InverseLerp(0.2f, 1f, Mathf.Abs(amount));
                // TaleSpire's sideways lists (library grids) scroll with the ordinary wheel and ignore sideways wheel
                // input, so left/right is sent as the wheel there (right = onward). Over up/down-only lists it does
                // nothing rather than scroll in a confusing direction.
                if (vertical) VirtualInput.Scroll += new Vector2(0f, v * rate);
                else if (SidewaysListUnder(VirtualInput.Position)) VirtualInput.Scroll += new Vector2(0f, -v * rate);
            }

            // Our buttons above the radial ring (first person in / out, hand colours and their arc). While a laser is on
            // one, or on the empty part of the colour arc, TaleSpire's cursor waits in the middle of the menu (TaleSpire
            // picks its options by direction from there), and the click is ours. Our own exit-only menu takes no clicks to
            // TaleSpire at all: any other trigger press just closes it.
            RadialExtraButton.HoverAt(target == Target.RadialPanel ? uiPos : (Vector2?)null);
            bool extraHover = RadialExtraButton.Hovered;
            if (extraHover) VirtualInput.Position = RadialExtraButton.MenuCenter;
            bool anyTriggerDown = SteamVRInput.TriggerLeft.Down || SteamVRInput.TriggerRight.Down;
            if (extraHover && SteamVRInput.Trigger(d.Hand).Down && !_extraPress)
            {
                _extraPress = true;
                _tapLeft = _tapRight = _tapMiddle = _pressDelay = 0;
                SteamVRInput.Haptic(d.Hand, 0.03f, 160f, 0.5f);
                Safe("radial extra button", RadialExtraButton.Press);
            }
            else if (RadialExtraButton.OwnMenuOpen && anyTriggerDown)
            {
                _extraPress = true;
                RadialExtraButton.CloseOwn();
            }
            if (_extraPress && !SteamVRInput.TriggerLeft.Held && !SteamVRInput.TriggerRight.Held && DebugTriggerFrames == 0) _extraPress = false;
            bool ours = extraHover || _extraPress || RadialExtraButton.OwnMenuOpen;
            // First person with a weapon in the right hand: its trigger fires / strikes / burns, unless the press is on a
            // menu with that laser (radial options, our buttons, the menu panel...).
            bool rightOnMenu = IsUi(_hp[(int)Hand.Right].Target) || (extraHover && d.Hand == Hand.Right) || _extraPress;
            Safe("weapons", () => FPWeapons.Trigger(rightOnMenu));

            bool allowClicks = !ours && target != Target.None && target != Target.Hotkeys && target != Target.Keyboard && !CreatureLasso.Active;
            // First person: the trigger does nothing on the board (no picking up or selecting other creatures, no dice);
            // it clicks menus (radial options) only.
            VirtualInput.Left = allowClicks && trigger && !(fp && target == Target.World);
            VirtualInput.Right = allowClicks && rightBtn;
            if (Patches.ClickLogPatch.Remaining > 0 && (rightBtn || SteamVRInput.RightClick.Held))
                Plugin.Log.LogInfo($"[clicklog] frame {Time.frameCount} A held={SteamVRInput.RightClick.Held} down={SteamVRInput.RightClick.Down} rightBtn={rightBtn} sent={VirtualInput.Right} target={target} delay={_pressDelay}");
            VirtualInput.Middle = allowClicks && middleBtn;

            // Haptics: a tick when a controller starts pointing at a menu, a stronger one on click.
            if (IsUi(target) && (target != _lastHover || d.Hand != _lastHoverHand))
                SteamVRInput.Haptic(d.Hand, 0.01f, 120f, 0.15f);
            _lastHover = target;
            _lastHoverHand = d.Hand;
            if (SteamVRInput.Trigger(activeHand).Down && target != Target.None)
                SteamVRInput.Haptic(activeHand, 0.015f, 160f, 0.3f);

            // 3) Laser length and colour for both controllers.
            bool pickFresh = Time.frameCount - _worldRayChangedFrame > PressDelayFrames;
            for (int i = 0; i < 2; i++)
            {
                var hp = _hp[i];
                var hs = hands.Get(hp.Hand);
                hs.DotColor = Color.white;
                Target t = hp.Hand == d.Hand ? target : hp.Target;
                if (t == Target.World)
                {
                    // TaleSpire's picked point belongs to the hand in control (once picking has caught up with it).
                    hs.Length = WorldHitDistance(hs.Ray, rig, hp.Hand == activeHand && pickFresh);
                    hs.Color = new Color(1f, 0.85f, 0.3f, 0.95f);
                    // Grip squeezed = a grip + trigger gesture is ready (or running): tint the laser and its end dots
                    // to say which.
                    if (SteamVRInput.Measure.Armed[i] || (MeasureTool.Active && hp.Hand == MeasureTool.ActiveHand))
                        hs.Color = hs.DotColor = new Color(0.35f, 1f, 0.45f, 1f);
                    if (SteamVRInput.Lasso.Armed[i] || (CreatureLasso.Active && hp.Hand == CreatureLasso.ActiveHand))
                        hs.Color = hs.DotColor = new Color(0.85f, 0.45f, 1f, 1f);
                }
                else
                {
                    hs.Length = hp.HitDist;
                    hs.Color = new Color(0.35f, 0.85f, 1f, 0.95f);
                }
                if (fp) { hs.Color = new Color(1f, 1f, 1f, 0.95f); hs.DotColor = Color.white; }   // first person: plain white pointers
            }

            UpdateLaserGroundVelocity();
            Safe("ping", () => PingTap.Update(hands, _onBoard, activeHand == Hand.Left && pickFresh, activeHand == Hand.Right && pickFresh));
            Safe("creature hold", () => CreatureHold.Update(rig));
            Safe("dice cleanup", DiceCleanup.Update);
            Safe("keys", UpdateKeys);
            DebugSnapshot.PollCommands();
            VirtualInput.Flush();
            _anyHeldLast = anyHeld;
            _triggerLast = trigger;
        }

        /// <summary>
        /// How fast the laser's spot on TaleSpire's ground plane moves (board units per second, one frame's movement at
        /// most 3 units, as TaleSpire measures the mouse): the speed dice are thrown with (Patches/InputPatches.cs).
        /// </summary>
        public static Vector3 LaserGroundVelocity { get; private set; }
        private Vector3 _laserGround;
        private bool _hasLaserGround;

        private void UpdateLaserGroundVelocity()
        {
            var plane = new Plane(Vector3.up, -CameraController.CameraPlaneHeight);
            if (!WorldMode || !plane.Raycast(WorldRay, out float enter) || enter > 5000f)
            {
                LaserGroundVelocity = Vector3.zero;
                _hasLaserGround = false;
                return;
            }
            Vector3 p = WorldRay.GetPoint(enter);
            LaserGroundVelocity = _hasLaserGround ? Vector3.ClampMagnitude(p - _laserGround, 3f) / Mathf.Max(1e-4f, Time.deltaTime) : Vector3.zero;
            _laserGround = p;
            _hasLaserGround = true;
        }

        /// <summary>Frames a press waits after control moves to the other hand (TaleSpire's picking lags 2-3 frames).</summary>
        private const int PressDelayFrames = 7;
        private int _pressDelay, _tapLeft, _tapRight, _tapMiddle;
        private int _worldFrames;   // consecutive frames TaleSpire's board cursor has been on (WorldMode)
        private bool _stickClickUsed;   // this right stick click went to the stream view's orbit
        private Hand _worldRayHand = Hand.Right;
        private int _worldRayChangedFrame;

        /// <summary>
        /// Gives control to the hand that presses a button: the trigger's own hand, or the right hand for A / stick click
        /// (they are on the right controller). Hovering never changes it, so a laser resting on a menu can't take the
        /// board away from the other hand. While one hand is mid-press (dragging, carrying) the other can't take over,
        /// and whatever is being carried or placed (a mini taken from a menu, a tile, a prop, a slab) stays with its
        /// hand: the other hand's presses on the board are ignored until it is placed or dropped. Grip + trigger
        /// gestures pick their own hand (MeasureTool / CreatureLasso).
        /// </summary>
        private void ArbitrateHands(VRHands hands, bool anyHeldBefore)
        {
            if (DebugRayActive) return;
            Hand active = hands.ActiveHand;
            bool l = SteamVRInput.TriggerLeft.Down, r = SteamVRInput.TriggerRight.Down;
            Hand presser;
            if (l && r) presser = active;
            else if (l) presser = Hand.Left;
            else if (r) presser = Hand.Right;
            // (First person: A works with the laser it points with, which may be the left one: FirstPersonA.)
            else if (SteamVRInput.RightClick.Down) presser = FirstPersonMode.Active ? AButtonPointer(AnyRadialOpen) ?? Hand.Right : Hand.Right;
            else if (SteamVRInput.MiddleClick.Down && !_stickClickUsed) presser = Hand.Right;
            else return;
            if (presser == active || anyHeldBefore) return;
            var target = _hp[(int)presser].Target;
            bool gestureArmed = (l || r) && target == Target.World
                                && (SteamVRInput.Measure.Armed[(int)presser] || SteamVRInput.Lasso.Armed[(int)presser]);
            if (gestureArmed) return;
            if (target == Target.World && PlacementLocked()) return;
            hands.SetActive(presser);
            if (target == Target.World) _pressDelay = PressDelayFrames;
        }

        private int _rightPulse;
        private bool _aMask, _extraPress;
        private Hand? _fpAPending;
        private int _fpAWait;

        /// <summary>A radial menu is open (TaleSpire's, or our first-person exit-only one).</summary>
        private static bool AnyRadialOpen => (SingletonBehaviour<MapMenuManager>.HasInstance && MapMenuManager.IsOpen) || RadialExtraButton.OwnMenuOpen;

        private static void CloseRadials()
        {
            RadialExtraButton.CloseOwn();
            RadialExtraButton.CloseTaleSpireMenu();
        }

        /// <summary>
        /// The laser A points with in first person: a hand whose grip is squeezed (its laser is drawn; the right one
        /// first), or with a radial menu open the right one (both are drawn then). None: no pointer (lasers hidden).
        /// </summary>
        private static Hand? AButtonPointer(bool radialOpen)
        {
            if (FirstPersonMode.GripHeld(Hand.Right)) return Hand.Right;
            if (FirstPersonMode.GripHeld(Hand.Left)) return Hand.Left;
            return radialOpen ? Hand.Right : (Hand?)null;
        }

        /// <summary>
        /// First person, A. No pointer (no grip squeezed, no menu open): the menu of the creature you are. A pointer on
        /// another creature or a door / chest (something with its own menu): that one's menu, replacing any open one. A
        /// pointer anywhere else (the open menu's window, the floor, the sky): an open menu closes; with none open, yours
        /// opens. The A button itself never reaches TaleSpire here: a synthetic tap does when TaleSpire is to open a menu,
        /// once its picking follows the pointer's laser.
        /// </summary>
        private void FirstPersonA(VRHands hands, ref bool rightBtn)
        {
            rightBtn = false;
            _rightPulse = 0;
            // The ESC menu (B) up: A closes it, like A on an open radial menu.
            if (UIPanels.EscOnly)
            {
                _fpAPending = null;
                if (SteamVRInput.RightClick.Down) UIPanels.CloseEscMenu();
                return;
            }
            bool open = AnyRadialOpen;
            if (SteamVRInput.RightClick.Down && !FirstPersonMode.Transitioning)
            {
                Hand? p = AButtonPointer(open);
                _fpAPending = null;
                if (p == null) OpenOwnCreatureMenu();
                else if (_hp[(int)p.Value].Target == Target.World) { _fpAPending = p; _fpAWait = 30; }
                else if (open) CloseRadials();
                else OpenOwnCreatureMenu();
            }
            if (!_fpAPending.HasValue) return;
            Hand h = _fpAPending.Value;
            bool fresh = hands.ActiveHand == h && _pressDelay == 0 && Time.frameCount - _worldRayChangedFrame > PressDelayFrames;
            if (!fresh && --_fpAWait > 0) return;
            _fpAPending = null;
            object thing = fresh && _hp[(int)h].Target == Target.World && PointsAtSomethingWithMenu() ? PickedThing() : null;
            if (thing != null)
            {
                var openCreature = RadialExtraButton.OpenCreatureMenuTarget();
                if (open && openCreature != null && thing.Equals(openCreature.CreatureId)) return;   // its menu is the open one
                if (SingletonBehaviour<MapMenuManager>.HasInstance && MapMenuManager.IsOpen)
                {
                    RadialExtraButton.CloseTaleSpireMenu();
                    _retapFrames = 8;   // then TaleSpire's own A on it (RadialRetap)
                }
                else
                {
                    RadialExtraButton.CloseOwn();
                    _tapRight = 2;
                }
                Plugin.Log.LogInfo($"First person: A with the {h} laser on {thing}: its menu");
            }
            else if (open) CloseRadials();
            else OpenOwnCreatureMenu();
        }

        private static void OpenOwnCreatureMenu()
        {
            if (FirstPersonMode.Instance == null || !FirstPersonMode.Instance.OpenCreatureMenu()) RadialExtraButton.OpenOwn();
        }

        /// <summary>
        /// A with the right laser on an open radial menu's window closes it (TaleSpire takes right-clicks on its menus
        /// for nothing). The press is kept from TaleSpire until A is let go.
        /// </summary>
        private void AOnRadialCloses(ref bool rightBtn)
        {
            if (SteamVRInput.RightClick.Down && AnyRadialOpen && _hp[(int)Hand.Right].Target == Target.RadialPanel)
            {
                CloseRadials();
                _aMask = true;
                Plugin.Log.LogInfo("A on the radial menu: closed it");
            }
            if (!_aMask) return;
            rightBtn = false;
            _rightPulse = 0;
            if (!SteamVRInput.RightClick.Held) _aMask = false;
        }

        /// <summary>Is the laser in control on another creature, or on a door / chest (a prop with its own menu)?</summary>
        private static bool PointsAtSomethingWithMenu()
        {
            var kind = PixelPickingManager.TryGetPicked(out float3 _, out DataModel.PlaceableRef placeable, out CreatureBoardAsset creature, out Component _, out PickedExternalSystemData _);
            if (kind == PixelPickingManager.PickedKind.CreatureBoardAsset) return creature != null && creature != FirstPersonMode.Creature;
            if (kind == PixelPickingManager.PickedKind.Placeable) return placeable.IsCreated && placeable.HasStateMachineState;
            return false;
        }

        private bool _radialWasOpen, _retapArmed;
        private object _radialOwner;   // what the open radial menu belongs to (creature id / placeable)
        private int _retapFrames;

        /// <summary>
        /// With a radial menu open, TaleSpire uses an A press only to close it. Pressed on a different creature or object
        /// (one with its own menu), follow up with a second, synthetic A tap a few frames after the menu has closed, so
        /// that object's menu opens - one press, like pointing at something else. On the same thing or the ground: just
        /// closes. (First person decides in FirstPersonA and only uses the follow-up tap: arm = false.)
        /// </summary>
        private void RadialRetap(bool onBoard, bool arm, ref bool rightBtn)
        {
            bool open = SingletonBehaviour<MapMenuManager>.HasInstance && MapMenuManager.IsOpen;
            if (open && !_radialWasOpen) _radialOwner = PickedThing();
            if (SteamVRInput.RightClick.Down && open && onBoard && arm)
            {
                object under = PickedThing();
                _retapArmed = under != null && !under.Equals(_radialOwner);
            }
            if (_retapArmed && !open && !SteamVRInput.RightClick.Held)
            {
                _retapArmed = false;
                _retapFrames = 8;   // let TaleSpire finish closing, then a 2-frame tap
            }
            if (_retapFrames > 0)
            {
                _retapFrames--;
                if (_retapFrames < 2) rightBtn = true;
            }
            _radialWasOpen = open;
        }

        /// <summary>
        /// What TaleSpire's picking has under the laser in control: a creature id, or for other things (doors, props) the
        /// picked spot to the nearest 2 units (their references can't be kept); null for nothing.
        /// </summary>
        private static object PickedThing()
        {
            var kind = PixelPickingManager.TryGetPicked(out float3 pos, out _, out CreatureBoardAsset creature, out _, out _);
            if (kind == PixelPickingManager.PickedKind.None) return null;
            if (kind == PixelPickingManager.PickedKind.CreatureBoardAsset) return creature != null ? (object)creature.CreatureId : null;
            return kind.ToString() + Vector3Int.RoundToInt((Vector3)pos * 0.5f);
        }

        /// <summary>Something is attached to the board cursor: a carried mini or group, or an item being placed from a menu.</summary>
        private static bool PlacementLocked()
        {
            try
            {
                var tool = BoardToolManager.CurrentTool;
                return tool is CreatureMoveBoardTool || tool is CreatureGroupMoveBoardTool || tool is CreatureSpawnerBoardTool
                       || tool is PropPlaceBoardTool || tool is TileBuilderBoardTool || tool is SlabBuilderBoardTool
                       || tool is GMBlockPlacerBoardTool || tool is DiceBoardTool;
            }
            catch { return false; }
        }

        private static readonly System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult> _scrollHits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();

        /// <summary>Is the menu element under this UI position inside a list that scrolls sideways?</summary>
        private static bool SidewaysListUnder(Vector2 pos)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) return false;
            _scrollHits.Clear();
            es.RaycastAll(new UnityEngine.EventSystems.PointerEventData(es) { position = pos }, _scrollHits);
            if (_scrollHits.Count == 0 || _scrollHits[0].gameObject == null) return false;
            var list = _scrollHits[0].gameObject.GetComponentInParent<UnityEngine.UI.ScrollRect>();
            return list != null && list.horizontal;
        }

        private static readonly System.Collections.Generic.HashSet<string> _faultLogged = new System.Collections.Generic.HashSet<string>();

        /// <summary>Run a side feature; if it throws, log it once and carry on so pointing and clicking keep working.</summary>
        private static void Safe(string feature, System.Action action)
        {
            try { action(); }
            catch (System.Exception e)
            {
                if (_faultLogged.Add(feature)) Plugin.Log.LogError($"[pointer] {feature} failed (pointing keeps working): {e}");
            }
        }

        private static bool IsPanel(Target t) => t == Target.MainPanel || t == Target.RadialPanel || t == Target.DicePanel;
        private static bool IsUi(Target t) => IsPanel(t) || t == Target.Hotkeys || t == Target.Keyboard;

        private static void Evaluate(HandPointer hp, bool valid, Ray ray, UIPanels panels, VRRig rig, bool pressed)
        {
            hp.Target = Target.None;
            hp.HitDist = -1f;
            hp.Hotkey = -1;
            if (!valid) { hp.Stab.Reset(); return; }

            // The radial window only catches a laser on the menu itself (its rings and options), not the empty space around it.
            if (panels.Radial.Raycast(ray, out float dr, out Vector2 pr) && RadialExtraButton.InCatchArea(pr)) { hp.Target = Target.RadialPanel; hp.UiPos = pr; hp.HitDist = dr; }
            else if (panels.Dice.Raycast(ray, out float dd, out Vector2 pd)) { hp.Target = Target.DicePanel; hp.UiPos = pd; hp.HitDist = dd; }
            else if (panels.Strip.Raycast(ray, out float dk, out int key)) { hp.Target = Target.Hotkeys; hp.Hotkey = key; hp.HitDist = dk; }
            else if (panels.Keyboard.Raycast(ray, out float dkb, out int kbKey)) { hp.Target = Target.Keyboard; hp.Hotkey = kbKey; hp.HitDist = dkb; }
            else if (panels.Main.Raycast(ray, out float dm, out Vector2 pm)) { hp.Target = Target.MainPanel; hp.UiPos = pm; hp.HitDist = dm; }
            else if (rig.OnBoard) { hp.Target = Target.World; }

            if (IsPanel(hp.Target)) hp.UiPos = hp.Stab.Update(hp.Target, hp.UiPos, pressed);
            else hp.Stab.Reset();
        }

        private void LogPress(string what, Hand hand, Target target, Vector2 pos)
        {
            if (_pressLogs++ > 300) return;
            string hit = "-";
            try
            {
                var es = UnityEngine.EventSystems.EventSystem.current;
                if (es != null)
                {
                    var ped = new UnityEngine.EventSystems.PointerEventData(es) { position = pos };
                    var results = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
                    es.RaycastAll(ped, results);
                    hit = results.Count > 0 ? Diagnostics.PathOf(results[0].gameObject.transform) : "nothing";
                }
            }
            catch { }
            Plugin.Log.LogInfo($"[pointer] {what} {hand} on {target} at {pos} -> {hit}");
        }

        internal static float WorldHitDistance(Ray ray, VRRig rig, bool active)
        {
            if (active)
            {
                try
                {
                    if (PixelPickingManager.TryGetPickedPosition(out float3 p))
                    {
                        float d = Vector3.Distance(ray.origin, p);
                        if (d > 0.001f && d < 5000f) return d;
                    }
                }
                catch { }
            }
            try
            {
                var rc = CameraController.Raycast;
                if (rc != null && rc.GetRayPoint(ray, out HitInfo hit, BouncePhysicsLayers.QueryAll, 0.01f))
                    return Vector3.Distance(ray.origin, (Vector3)hit.point);
            }
            catch { }
            var plane = new Plane(Vector3.up, -CameraController.CameraPlaneHeight);
            if (plane.Raycast(ray, out float enter) && enter < 2000f) return enter;
            return 3f * rig.Scale;
        }

        private static void UpdateKeys()
        {
            // Left stick click: Build Mode (B); in first person it shows / hides the ruler instead (no Build Mode there).
            if (SteamVRInput.BuildModeToggle.Down)
            {
                // (In first person only a click with the stick at rest counts: walking pushes it hard, and a hard push
                // easily clicks it too.)
                if (FirstPersonMode.Active)
                {
                    if (SteamVRInput.Move.Value.magnitude < 0.5f) FirstPersonMode.Instance.ToggleRuler();
                    else Plugin.Log.LogInfo("First person: left stick click while walking ignored (not a ruler toggle)");
                }
                else if (!FirstPersonMode.Transitioning) BuildModeSwitch.Toggle("left stick");
            }
            // (In first person the trackpads are Predator Vision, nothing is carried: no Alt / Ctrl. Nor during a measure or
            // lasso: on controllers without trackpads the same grip does both.)
            bool fpKeys = FirstPersonMode.Active || MeasureTool.Active || CreatureLasso.Active;
            VirtualInput.SetKey(Key.LeftAlt, !fpKeys && SteamVRInput.RotateModifier.Held);
            VirtualInput.SetKey(Key.LeftCtrl, !fpKeys && (SteamVRInput.ElevateModifier.Held || CreatureHold.ElevatingWithButtons));
            VirtualInput.SetKey(Key.LeftShift, HotkeyStrip.ShiftLatched);
            // B: shows / hides the menu panel.
            var panels = VRManager.Instance.Panels;
            if (SteamVRInput.MenuToggle.Down && panels != null) panels.ToggleMain();
        }
    }

    /// <summary>
    /// Hand tremor and the small jerk of pulling the trigger move a laser by several pixels, which Unity's UI treats
    /// as a drag (cancelling clicks in scroll lists). Smooth slow jitter, and on a press use where the laser pointed
    /// just before the pull and hold it still briefly.
    /// </summary>
    internal class PanelStabilizer
    {
        private const int HistSize = 32;
        private readonly Vector2[] _hist = new Vector2[HistSize];
        private readonly float[] _histT = new float[HistSize];
        private int _count, _head;
        private Vector2 _smoothed;
        private VRPointer.Target _target = VRPointer.Target.None;
        private float _freezeUntil;
        private Vector2 _frozen;

        public void Reset() { _target = VRPointer.Target.None; _count = 0; _freezeUntil = 0f; }

        public Vector2 Update(VRPointer.Target t, Vector2 raw, bool pressedNow)
        {
            float now = Time.unscaledTime;
            float px = UICapture.Height / 1080f;   // pixels per "1080p pixel"
            if (t != _target)
            {
                _target = t;
                _smoothed = raw;
                _count = 0;
            }
            else
            {
                float speed = (raw - _smoothed).magnitude / px;
                float k = Mathf.Clamp01(Mathf.InverseLerp(3f, 50f, speed));
                float tau = Mathf.Lerp(0.06f, 0.01f, k);
                _smoothed = Vector2.Lerp(_smoothed, raw, 1f - Mathf.Exp(-Time.unscaledDeltaTime / tau));
            }
            _hist[_head] = _smoothed;
            _histT[_head] = now;
            _head = (_head + 1) % HistSize;
            if (_count < HistSize) _count++;

            if (pressedNow)
            {
                _frozen = At(now - 0.06f);
                _freezeUntil = now + 0.3f;
            }
            if (now < _freezeUntil)
            {
                if ((_smoothed - _frozen).magnitude > 45f * px) _freezeUntil = 0f;   // a deliberate drag
                else return _frozen;
            }
            return _smoothed;
        }

        private Vector2 At(float time)
        {
            Vector2 best = _smoothed;
            for (int i = 0; i < _count; i++)
            {
                int idx = (_head - 1 - i + HistSize) % HistSize;
                best = _hist[idx];
                if (_histT[idx] <= time) break;
            }
            return best;
        }
    }
}
