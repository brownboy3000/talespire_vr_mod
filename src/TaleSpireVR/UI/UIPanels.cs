using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace TaleSpireVR
{
    /// <summary>A quad that shows (part of) the captured UI texture and maps laser hits back to screen pixels.</summary>
    internal class UIQuad
    {
        public readonly GameObject Go;
        public readonly Transform T;
        private readonly Mesh _mesh;
        private readonly Material _mat;
        private Rect _region = new Rect(0, 0, 1, 1);    // normalized UV region of the UI texture
        private readonly Vector2[] _uvs = new Vector2[4];

        public bool Visible
        {
            get => Go.activeSelf;
            set { if (Go.activeSelf != value) Go.SetActive(value); }
        }

        public UIQuad(string name, int renderQueue)
        {
            Go = new GameObject(name);
            Object.DontDestroyOnLoad(Go);
            Go.layer = VRLayers.Overlay;
            T = Go.transform;
            _mesh = new Mesh { name = name };
            _mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            _mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            _mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            SetRegion(new Rect(0, 0, 1, 1));
            _mesh.RecalculateBounds();
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            Go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = Go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _mat = new Material(Canvas.GetDefaultCanvasMaterial()) { name = name, renderQueue = renderQueue };
            _mat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            mr.sharedMaterial = _mat;
            Visible = false;
        }

        public void SetRegion(Rect uvRegion)
        {
            _region = uvRegion;
            float y0 = uvRegion.yMin;
            float y1 = uvRegion.yMax;
            _uvs[0] = new Vector2(uvRegion.xMin, y0);
            _uvs[1] = new Vector2(uvRegion.xMax, y0);
            _uvs[2] = new Vector2(uvRegion.xMax, y1);
            _uvs[3] = new Vector2(uvRegion.xMin, y1);
            _mesh.uv = _uvs;
        }

        public void SetTexture(Texture t)
        {
            if (_mat.mainTexture != t) _mat.mainTexture = t;
        }

        public void SetTint(Color c) { _mat.color = c; }

        /// <summary>World position of a UI pixel on this quad (for debugging / aiming).</summary>
        public Vector3 WorldPointForPixel(Vector2 px)
        {
            float u = px.x / UICapture.Width, v = px.y / UICapture.Height;
            float lx = (u - _region.xMin) / _region.width - 0.5f;
            float ly = (v - _region.yMin) / _region.height - 0.5f;
            return T.TransformPoint(new Vector3(lx, ly, 0f));
        }

        /// <summary>Ray-quad intersection. Returns hit distance (world) and the UI pixel position.</summary>
        public bool Raycast(Ray worldRay, out float distance, out Vector2 uiPixel)
        {
            distance = 0f;
            uiPixel = Vector2.zero;
            if (!Visible) return false;
            // InverseTransformVector (not ...Direction): the panel is non-uniformly scaled, and the direction must be
            // mapped into the same scaled space as the origin or the hit point slides sideways.
            Vector3 o = T.InverseTransformPoint(worldRay.origin);
            Vector3 d = T.InverseTransformVector(worldRay.direction);
            if (Mathf.Abs(d.z) < 1e-6f) return false;
            float t = -o.z / d.z;
            if (t <= 0f) return false;
            Vector3 p = o + d * t;
            if (p.x < -0.5f || p.x > 0.5f || p.y < -0.5f || p.y > 0.5f) return false;
            float u = _region.xMin + (p.x + 0.5f) * _region.width;
            float v = _region.yMin + (p.y + 0.5f) * _region.height;
            uiPixel = new Vector2(u * UICapture.Width, v * UICapture.Height);
            distance = Vector3.Distance(worldRay.origin, T.TransformPoint(p));
            return true;
        }
    }

    /// <summary>
    /// The big floating menu panel plus the floating creature radial menu.
    /// </summary>
    [DefaultExecutionOrder(30015)]
    internal class UIPanels : MonoBehaviour
    {
        public UIQuad Main { get; private set; }
        public UIQuad Radial { get; private set; }
        /// <summary>The dice tray on its own, on your inner left forearm (palm turned to your face).</summary>
        public UIQuad Dice { get; private set; }
        /// <summary>TaleSpire's roll result panel, floating above your view while it is shown.</summary>
        public UIQuad Banner { get; private set; }
        public HotkeyStrip Strip { get; private set; }
        public VRKeyboard Keyboard { get; private set; }

        /// <summary>Square region (in 1080p-scale UI pixels) around the screen centre where TaleSpire draws the radial menu in VR.</summary>
        public const float RadialRegionPixels = 640f;

        private Transform _rig;
        private bool _mainWanted;
        private bool _wasOnBoard;

        private void Awake()
        {
            Main = new UIQuad("TaleSpireVR_MenuPanel", 4000);
            Radial = new UIQuad("TaleSpireVR_RadialPanel", 4001);
            Dice = new UIQuad("TaleSpireVR_DicePanel", 4002);
            Banner = new UIQuad("TaleSpireVR_DiceResults", 3999);
            Strip = new HotkeyStrip();
            Keyboard = new VRKeyboard();
        }

        /// <summary>Put the menu panel away (entering first person, where it is not available).</summary>
        public void HideMain()
        {
            _mainWanted = false;
            VRKeyboard.Done();
        }

        // ---- First person: B is TaleSpire's ESC menu, on its own ---------------------------------------------------

        private static readonly System.Reflection.FieldInfo ExitMenuField = HarmonyLib.AccessTools.Field(typeof(GUIManager), "_exitMenu");
        private static bool _escWanted;
        private static float _escSince;

        /// <summary>
        /// First person, B pressed: TaleSpire's ESC menu (and what it opens: Settings, its Yes / No questions) is open
        /// and shown on the menu panel all by itself - nothing else of TaleSpire's menus is drawn or can be clicked.
        /// </summary>
        public static bool EscOnly { get; private set; }

        /// <summary>TaleSpire's ESC menu, the settings window and its question boxes (the parts EscOnly shows).</summary>
        internal static IEnumerable<GameObject> EscMenuRoots()
        {
            if (SingletonBehaviour<GUIManager>.HasInstance && ExitMenuField?.GetValue(SingletonBehaviour<GUIManager>.Instance) is Component exit && exit != null)
                yield return exit.gameObject;
            if (SingletonBehaviour<GameSettings>.HasInstance)
                yield return SingletonBehaviour<GameSettings>.Instance.gameObject;
            if (Bounce.Singletons.SimpleSingletonBehaviour<SystemMessage>.HasInstance)
                yield return Bounce.Singletons.SimpleSingletonBehaviour<SystemMessage>.Instance.gameObject;
        }

        private static bool EscMenuShowing
        {
            get
            {
                try { return GUIManager.MainMenuIsOpen || GameSettings.IsOpen; }
                catch { return false; }
            }
        }

        private void ToggleEscMenu()
        {
            try
            {
                if (_escWanted || EscMenuShowing)
                {
                    CloseEscMenu();
                    return;
                }
                RadialExtraButton.CloseTaleSpireMenu();
                RadialExtraButton.CloseOwn();
                GUIManager.ToggleMainMenu();
                _escWanted = true;
                _escSince = Time.unscaledTime;
                PlaceMainInFront();
                Plugin.Log.LogInfo("First person: ESC menu opened");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("First person: ESC menu failed: " + e.Message); }
        }

        /// <summary>Close the first-person ESC menu (and the settings window if it is open).</summary>
        public static void CloseEscMenu()
        {
            if (!_escWanted) return;
            _escWanted = false;
            try
            {
                if (GameSettings.IsOpen) GameSettings.ToggleOpen();
                if (GUIManager.MainMenuIsOpen) GUIManager.ToggleMainMenu();
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("First person: closing the ESC menu failed: " + e.Message); }
            Plugin.Log.LogInfo("First person: ESC menu closed");
        }

        /// <summary>
        /// The menu panel closing (outside first person): TaleSpire's ESC menu and settings window opened in it close too -
        /// they are part of the panel, and left open they would still be drawn into the menu picture (the results
        /// banner shows a piece of it).
        /// </summary>
        private static void CloseTaleSpireEscMenu()
        {
            if (FirstPersonMode.Active) return;
            try
            {
                if (GameSettings.IsOpen) GameSettings.ToggleOpen();
                if (GUIManager.MainMenuIsOpen) GUIManager.ToggleMainMenu();
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Closing TaleSpire's ESC menu failed: " + e.Message); }
        }

        /// <summary>Is one of TaleSpire's question boxes up (Back to Main Menu? Exit TaleSpire?)?</summary>
        private static bool QuestionShowing()
        {
            if (!Bounce.Singletons.SimpleSingletonBehaviour<SystemMessage>.HasInstance) return false;
            var root = Bounce.Singletons.SimpleSingletonBehaviour<SystemMessage>.Instance.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i);
                if (c.gameObject.activeSelf && c.GetComponentInChildren<UnityEngine.UI.Button>() != null) return true;
            }
            return false;
        }

        private void UpdateEscOnly(bool radialOpened)
        {
            if (!FirstPersonMode.Active) { _escWanted = false; EscOnly = false; return; }
            // One menu at a time: a radial menu opening (A) closes it. (Only one that opens now: B with a radial open
            // closes the radial and opens this, and TaleSpire's radial may still count as open for a frame or two.)
            if (_escWanted && radialOpened) CloseEscMenu();
            if (_escWanted)
            {
                bool showing = EscMenuShowing || QuestionShowing();
                // Closed by its own buttons (Back, Settings' Close): done - after a moment for it to open.
                if (!showing && Time.unscaledTime - _escSince > 0.6f)
                {
                    _escWanted = false;
                    Plugin.Log.LogInfo("First person: ESC menu closed (by its own buttons)");
                }
            }
            EscOnly = _escWanted;
        }

        public void ToggleMain()
        {
            // First person: no menu panel - B is TaleSpire's ESC menu on its own (graphics settings, main menu, quit).
            if (FirstPersonMode.Active) { if (!FirstPersonMode.Transitioning) ToggleEscMenu(); return; }
            if (FirstPersonMode.Transitioning) return;
            var rig = VRManager.Instance.Rig;
            // B with a radial menu open: close the radial and open the panel (the panel is hidden while a radial is
            // open, so otherwise it only appeared later, unasked, when the radial closed).
            if (rig.OnBoard && ((SingletonBehaviour<MapMenuManager>.HasInstance && MapMenuManager.IsOpen) || RadialExtraButton.OwnMenuOpen))
            {
                RadialExtraButton.CloseTaleSpireMenu();
                RadialExtraButton.CloseOwn();
                _mainWanted = true;
                PlaceMainInFront();
                ExpandBoardToolbars();
                return;
            }
            if (Main.Visible && rig.OnBoard)
            {
                _mainWanted = false;
                VRKeyboard.Done();   // closing the menu also ends typing
                CloseTaleSpireEscMenu();
            }
            else
            {
                _mainWanted = true;
                PlaceMainInFront();
                if (VRManager.Instance.Rig.OnBoard) ExpandBoardToolbars();
            }
        }

        /// <summary>Open TaleSpire's side toolbars (what Space does) if they are collapsed.</summary>
        private static void ExpandBoardToolbars()
        {
            try
            {
                if (BoardToolManager.IsUIExpanded || !SingletonBehaviour<BoardToolManager>.HasInstance) return;
                SingletonBehaviour<BoardToolManager>.Instance.ExtendUI();
                Plugin.Log.LogInfo("Menu panel opened: expanded the board toolbars.");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Could not expand the board toolbars: " + e.Message); }
        }

        private void LateUpdate()
        {
            var rig = VRManager.Instance.Rig;
            if (rig == null || rig.RigTransform == null || UICapture.Texture == null)
            {
                Main.Visible = false;
                Radial.Visible = false;
                Dice.Visible = false;
                Banner.Visible = false;
                return;
            }
            Main.SetTexture(UICapture.Texture);
            Radial.SetTexture(UICapture.Texture);
            Dice.SetTexture(UICapture.Texture);
            Banner.SetTexture(UICapture.Texture);

            if (_rig != rig.RigTransform)
            {
                _rig = rig.RigTransform;
                _mainWanted = !rig.OnBoard;
                _placePending = true;
            }
            if (rig.OnBoard != _wasOnBoard)
            {
                _wasOnBoard = rig.OnBoard;
                _mainWanted = !rig.OnBoard;   // menus: always shown; boards: hidden until summoned
                _placePending = true;
            }
            if (_placePending && rig.HeadTracked)
            {
                _placePending = false;
                PlaceMainInFront();
            }

            // The panel lives in rig (tracking) space so it travels and scales with you.
            Main.T.position = _rig.TransformPoint(_mainLocalPos);
            Main.T.rotation = _rig.rotation * _mainLocalRot;
            Main.T.localScale = _mainLocalScale * rig.Scale;

            bool radialOpen = rig.OnBoard && ((SingletonBehaviour<MapMenuManager>.HasInstance && MapMenuManager.IsOpen) || RadialExtraButton.OwnMenuOpen);
            // One menu at a time: a radial menu opening while the panel (or, in first person, the ESC menu) is up
            // replaces it, so it does not come back when the radial closes (_radialWasOpen is still last frame's here).
            // B with a radial open does the reverse (ToggleMain). Closing any of them opens nothing else.
            bool radialOpened = radialOpen && !_radialWasOpen;
            if (radialOpened && _mainWanted)
            {
                _mainWanted = false;
                VRKeyboard.Done();
                CloseTaleSpireEscMenu();
                Plugin.Log.LogInfo("Radial menu opened: menu panel closed");
            }
            UpdateEscOnly(radialOpened);
            // Dice in hand: the menu steps aside until they are thrown, so you see where they go.
            Main.Visible = (_mainWanted && !radialOpen && !FirstPersonMode.Active && !(rig.OnBoard && DiceHand.InHand)) || EscOnly;
            Strip.Visible = Main.Visible && rig.OnBoard && !EscOnly;
            if (Strip.Visible) Strip.PlaceUnder(Main.T, Main.T.lossyScale.x, Main.T.lossyScale.y);

            // On-screen keyboard while a text field is selected, below the panel (and the hotkey row).
            VRKeyboard.Tick();
            Keyboard.Visible = Main.Visible && !EscOnly && VRKeyboard.TryGetField(out _, out _);
            if (Keyboard.Visible)
            {
                float mw = Main.T.lossyScale.x, mh = Main.T.lossyScale.y;
                float below = mh * 0.5f + (Strip.Visible ? mw / 12f * 1.25f : 0f) + 0.03f * rig.Scale;
                Keyboard.PlaceBelow(Main.T.position - Main.T.up * below, Main.T.rotation, mw * 0.75f);
            }
            UpdateRadial(rig, radialOpen);
            // (While a radial menu is open the menu texture holds only the radial menu, so these wait.)
            UpdateDice(rig, radialOpen);
            UpdateBanner(rig, radialOpen);
        }

        // ---- Dice: the tray on your inner left forearm, and roll results in the sky -------------------------------

        private bool _diceWasWanted;

        /// <summary>
        /// The dice buttons and tray on their own, on the inside of your left forearm while you turn that palm up
        /// (DiceForearm). Not in first person, with a radial menu open, or while dice are in hand.
        /// </summary>
        private void UpdateDice(VRRig rig, bool radialOpen)
        {
            bool allowed = rig.OnBoard && !FirstPersonMode.Active && !FirstPersonMode.Transitioning && !radialOpen && !DiceHand.InHand;
            bool wanted = DiceForearm.Update(rig, allowed, VRPointer.Current == VRPointer.Target.DicePanel);
            if (wanted && !_diceWasWanted)
            {
                ExpandBoardToolbars();   // the dice buttons are on TaleSpire's toolbar
                Plugin.Log.LogInfo($"Dice window shown on the left forearm (palm {DiceForearm.PalmDot:F2}, gaze {DiceForearm.GazeDot:F2})");
            }
            _diceWasWanted = wanted;
            Rect px = default;
            bool show = wanted && DiceUi.TryGetTrayRegion(out px);
            Dice.Visible = show;
            if (!show) return;
            float w = UICapture.Width, h = UICapture.Height;
            Dice.SetRegion(new Rect(px.xMin / w, px.yMin / h, px.width / w, px.height / h));
            float width = Mathf.Max(0.05f, VRConfig.DiceMenuWidth.Value);
            float height = width * px.height / Mathf.Max(1f, px.width);
            // Nearer the wrist than the forearm's middle by two dice buttons' width.
            float towardsHand = 2f * DiceUi.ButtonWidth / Mathf.Max(1f, px.width) * width;
            DiceForearm.Place(rig, height, towardsHand, out Vector3 center, out Quaternion rotation);
            Dice.T.position = _rig.TransformPoint(center);
            Dice.T.rotation = _rig.rotation * rotation;
            Dice.T.localScale = new Vector3(width, height, 1f) * rig.Scale;
            Dice.SetTint(new Color(1f, 1f, 1f, DiceForearm.Alpha));
        }

        private float _bannerYaw;
        private bool _bannerShown;

        /// <summary>
        /// TaleSpire's roll result panel (who rolled, the dice, the total), floating a little above the horizon in the
        /// direction you face (following your turns slowly), always the same size whatever your zoom. Look slightly up
        /// to read it; it stays out of the board below. Shown as long as TaleSpire shows the panel (15 s in VR).
        /// </summary>
        private void UpdateBanner(VRRig rig, bool radialOpen)
        {
            // (Not while TaleSpire's ESC menu, settings or a question box is up: they are drawn across the middle of the
            // menu picture, over the results.)
            bool show = rig.OnBoard && !radialOpen && !EscMenuShowing && !QuestionShowing() && DiceUi.TryGetResultRegion(out Rect px);
            Banner.Visible = show;
            if (!show) { _bannerShown = false; return; }
            DiceUi.TryGetResultRegion(out px);
            float w = UICapture.Width, h = UICapture.Height;
            Banner.SetRegion(new Rect(px.xMin / w, px.yMin / h, px.width / w, px.height / h));
            Vector3 look = rig.HeadLocalRotation * Vector3.forward;
            float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            if (!_bannerShown) { _bannerYaw = yaw; _bannerShown = true; }
            else _bannerYaw = Mathf.LerpAngle(_bannerYaw, yaw, 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.8f));
            Vector3 dir = Quaternion.Euler(-DiceResultBanner.Elevation, _bannerYaw, 0f) * Vector3.forward;
            Vector3 local = rig.HeadLocalPosition + dir * DiceResultBanner.Distance;
            float width = Mathf.Min(px.width * DiceResultBanner.MetersPerPixel, DiceResultBanner.MaxWidth);
            Banner.T.position = _rig.TransformPoint(local);
            Banner.T.rotation = _rig.rotation * Quaternion.LookRotation(dir, Vector3.up);
            Banner.T.localScale = new Vector3(width, width * px.height / Mathf.Max(1f, px.width), 1f) * rig.Scale;
        }

        private bool _placePending = true;
        private Vector3 _mainLocalPos = new Vector3(0f, 1.4f, 1.4f);
        private Quaternion _mainLocalRot = Quaternion.identity;
        private Vector3 _mainLocalScale = new Vector3(1.6f, 0.9f, 1f);

        private void PlaceMainInFront()
        {
            var rig = VRManager.Instance.Rig;
            if (rig == null || rig.Head == null || _rig == null) return;
            Vector3 headLocal = rig.HeadLocalPosition;
            Vector3 fwd = rig.HeadLocalRotation * Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            float dist = VRConfig.PanelDistance.Value;
            Vector3 pos = headLocal + fwd * dist + Vector3.down * 0.15f;
            _mainLocalPos = pos;
            _mainLocalRot = Quaternion.LookRotation(pos - headLocal, Vector3.up);
            float w = VRConfig.PanelWidth.Value;
            _mainLocalScale = new Vector3(w, w * UICapture.Height / Mathf.Max(1f, UICapture.Width), 1f);
        }

        private bool _radialWasOpen;
        private Vector3 _radialLocalPos;
        private Quaternion _radialLocalRot = Quaternion.identity;

        /// <summary>
        /// Creature / door / object radial menus: shown as a floating window straight in front of you when they open,
        /// and kept there (in your play space) until they close.
        /// </summary>
        private void UpdateRadial(VRRig rig, bool open)
        {
            Radial.Visible = open;
            if (!open) { _radialWasOpen = false; return; }

            float w = UICapture.Width, h = UICapture.Height;
            // TaleSpire draws the menu at the screen centre in VR; show a square region around it, bigger while our buttons
            // reach further out (the hand colour arc), at the same size per pixel.
            float baseSide = Mathf.Min(RadialRegionPixels * Mathf.Max(0.5f, SettingsUiScale()), Mathf.Min(w, h));
            float side = Mathf.Min(Mathf.Max(baseSide, 2f * RadialExtraButton.RequiredRadius), Mathf.Min(w, h));
            Radial.SetRegion(new Rect((w - side) * 0.5f / w, (h - side) * 0.5f / h, side / w, side / h));

            if (!_radialWasOpen)
            {
                _radialWasOpen = true;
                Vector3 headLocal = rig.HeadLocalPosition;
                Vector3 fwd = rig.HeadLocalRotation * Vector3.forward;
                // Follow the gaze a little downward, but keep it in a comfortable band.
                fwd.y = Mathf.Clamp(fwd.y, -0.6f, 0.2f);
                fwd.Normalize();
                _radialLocalPos = headLocal + fwd * VRConfig.RadialMenuDistance.Value;
                _radialLocalRot = Quaternion.LookRotation(_radialLocalPos - headLocal, Vector3.up);
            }
            float size = VRConfig.RadialMenuSize.Value * side / baseSide;
            Radial.T.position = _rig.TransformPoint(_radialLocalPos);
            Radial.T.rotation = _rig.rotation * _radialLocalRot;
            Radial.T.localScale = new Vector3(size, size, 1f) * rig.Scale;
        }

        private static float SettingsUiScale()
        {
            try { return TaleSpire.Settings.SettingsManager.UIScale; } catch { return 1f; }
        }

        private void OnDestroy()
        {
            if (Main != null) Destroy(Main.Go);
            if (Radial != null) Destroy(Radial.Go);
            if (Dice != null) Destroy(Dice.Go);
            if (Banner != null) Destroy(Banner.Go);
            if (Strip != null) Destroy(Strip.Go);
            if (Keyboard != null) Destroy(Keyboard.Go);
        }
    }
}
