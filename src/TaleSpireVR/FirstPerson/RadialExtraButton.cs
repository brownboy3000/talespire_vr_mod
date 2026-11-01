using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace TaleSpireVR
{
    /// <summary>
    /// Our own buttons just above the top of TaleSpire's radial menu ring (TaleSpire's own options stay where they are):
    ///  - teal eye: "First Person" on a creature's menu, "Exit First Person" on every radial menu while in first person;
    ///  - blue hands (unique creatures only): opens an arc of hand colours above the menu - the colour of that creature's
    ///    hands when you are it in first person (HandColors);
    ///  - red crossed sword and staff (unique creatures only): opens an arc of that creature's weapons (Unarmed, the
    ///    weapons added to it, and + for the Weapon Catalogue, which takes the radial's place until Return or A).
    /// In first person, A pointed at nothing opens a radial menu of our own with only those buttons.
    ///
    /// They are drawn into the captured menu texture on the radial layer (so they show on the floating radial window like
    /// TaleSpire's options). VRPointer hit-tests them in UI pixels and keeps TaleSpire's cursor in the middle of the menu
    /// while one is hovered (TaleSpire picks options by direction from the centre); their clicks are ours.
    /// </summary>
    [DefaultExecutionOrder(30018)]
    internal class RadialExtraButton : MonoBehaviour
    {
        public enum Kind { None, Enter, Exit }

        public static RadialExtraButton Instance { get; private set; }
        /// <summary>Our exit-only radial menu (first person, A on nothing) is open.</summary>
        public static bool OwnMenuOpen { get; private set; }
        public static Kind Showing { get; private set; }
        /// <summary>A laser is on one of our buttons, or on the empty part of the colour arc (HoverAt): its clicks are ours.</summary>
        public static bool Hovered { get; private set; }
        /// <summary>UI pixel position of the radial menu's centre (where TaleSpire's cursor waits while ours are hovered).</summary>
        public static Vector2 MenuCenter { get; private set; }
        /// <summary>How far from the menu's centre our buttons reach (UI pixels; 0 = within TaleSpire's usual window).</summary>
        public static float RequiredRadius { get; private set; }

        private static float _radius, _buttonsY, _ringTop, _arcRadius, _fanFrom, _fanTo;
        private static CreatureBoardAsset _creature, _menuCreature, _arcCreature;
        private static bool _arcOpen, _weaponArcOpen, _catalogueOpen;
        private static int _hoverIndex = -1;   // HoverMain, HoverHands, HoverSwatch0 + i, HoverWeapons, HoverArm0 + i, HoverReturn, HoverTile0 + i
        private const int HoverMain = 0, HoverHands = 1, HoverSwatch0 = 2, HoverWeapons = 100, HoverArm0 = 200, HoverReturn = 300, HoverTile0 = 400;

        private static readonly FieldInfo StackField = AccessTools.Field(typeof(MapMenuManager), "_menuStack");
        private static readonly FieldInfo ItemsField = AccessTools.Field(typeof(MapMenu), "_mapTransformParent");
        private static readonly FieldInfo SelectedField = AccessTools.Field(typeof(CreatureMenuBoardTool), "_selectedCreature");

        /// <summary>The colour arc: from the left (degrees, 0 = right of the centre) over the top to the right.</summary>
        private const float ArcFrom = 168f, ArcTo = 12f;
        /// <summary>The weapons arc: its items this far apart (degrees), centred over the top.</summary>
        private const float ArmStep = 17f;
        private static int SwatchCount => HandColors.Palette.Length + 1;

        private RectTransform _canvasRt, _ring;
        private Image _ringImg;
        private RoundButton _main, _hands, _weapons;
        private RoundButton[] _swatches, _arms;
        private TMPro.TextMeshProUGUI _name;
        private readonly List<TMPro.TextMeshProUGUI> _labels = new List<TMPro.TextMeshProUGUI>();
        private Texture2D _eye, _eyeExit, _handIcon, _plusIcon;
        private Sprite _rimThin, _rimThick;
        private static readonly Dictionary<string, Texture2D> WeaponIcons = new Dictionary<string, Texture2D>();
        /// <summary>The weapons arc's items this frame: "" = Unarmed, a weapon id, "+" = the catalogue.</summary>
        private static readonly List<string> _armItems = new List<string>();

        // The Weapon Catalogue (in place of the radial menu).
        private RectTransform _cat;
        private TMPro.TextMeshProUGUI _catTitle;
        private Tile _catReturn;
        private Tile[] _tiles;
        private static Rect _catRect;
        private CanvasGroup _hiddenMenu;

        private static readonly Color Fill = new Color(0.07f, 0.26f, 0.30f, 1f);
        private static readonly Color FillHover = new Color(0.11f, 0.42f, 0.48f, 1f);
        private static readonly Color HandsFill = new Color(0.08f, 0.18f, 0.46f, 1f);
        private static readonly Color HandsFillHover = new Color(0.13f, 0.29f, 0.68f, 1f);
        private static readonly Color ArmsFill = new Color(0.40f, 0.09f, 0.08f, 1f);
        private static readonly Color ArmsFillHover = new Color(0.62f, 0.16f, 0.13f, 1f);
        private static readonly Color TileFill = new Color(0.06f, 0.07f, 0.09f, 0.72f);
        private static readonly Color TileFillHover = new Color(0.17f, 0.20f, 0.26f, 0.85f);

        /// <summary>A dark, rounded, semi-translucent square of the catalogue (or its Return button): icon, name, "added" tick.</summary>
        private sealed class Tile
        {
            public RectTransform Rt;
            public Image Back, Edge;
            public RawImage Icon, Tick;
            public TMPro.TextMeshProUGUI Name;
            public float Grow;
            public Rect Area;

            public bool Contains(Vector2 p) => Rt.gameObject.activeSelf && Area.Contains(p);
        }

        /// <summary>A round button: disc, rim, optional icon, and its name above it while hovered.</summary>
        private sealed class RoundButton
        {
            public RectTransform Rt;
            public Image Disc, Rim;
            public RawImage Icon;
            public TMPro.TextMeshProUGUI Label;
            public Vector2 Center;
            public float Radius, Grow;
            public bool Shown;

            /// <summary>On it (a little generous, like TaleSpire's own hit areas)?</summary>
            public bool Contains(Vector2 p) => Shown && (p - Center).sqrMagnitude <= Radius * Radius * 1.44f;

            public void Show(bool on)
            {
                Shown = on;
                if (Rt.gameObject.activeSelf != on) Rt.gameObject.SetActive(on);
                if (!on) Grow = 0f;
            }

            /// <summary>
            /// Placed at a UI pixel; grows 15 % while hovered, with its name fading in above it - or, given an outward
            /// direction (the colour arc), beyond it on that side, clear of its neighbours.
            /// </summary>
            public void Layout(Vector2 center, float radius, bool hovered, string label, float fontSize, float scale, Vector2? outward = null)
            {
                Center = center;
                Radius = radius;
                Grow = Mathf.MoveTowards(Grow, hovered ? 1f : 0f, Time.unscaledDeltaTime * (hovered ? 10f : 4f));
                float size = radius * 2f * (1f + 0.15f * Grow);
                Rt.anchoredPosition = center;
                Rt.sizeDelta = new Vector2(size, size);
                Label.text = label ?? "";
                Label.fontSize = fontSize;
                Label.gameObject.SetActive(label != null && Grow > 0.01f);
                Label.alpha = Grow;
                // (Drawn over its neighbours while hovered, so its name stays clear of the weapons' outer arcs.)
                if (hovered && Rt.GetSiblingIndex() != Rt.parent.childCount - 1) Rt.SetAsLastSibling();
                var lrt = Label.rectTransform;
                Vector2 dir = outward ?? Vector2.up;
                float gap = size * 0.5f + 10f * scale;
                if (dir.x > 0.35f) { lrt.pivot = new Vector2(0f, 0.5f); Label.alignment = TMPro.TextAlignmentOptions.Left; }
                else if (dir.x < -0.35f) { lrt.pivot = new Vector2(1f, 0.5f); Label.alignment = TMPro.TextAlignmentOptions.Right; }
                else { lrt.pivot = new Vector2(0.5f, 0f); Label.alignment = TMPro.TextAlignmentOptions.Bottom; }
                lrt.anchoredPosition = dir * gap;
                lrt.sizeDelta = new Vector2(600f * scale, 40f * scale);
            }
        }

        private void Awake()
        {
            Instance = this;
            Build();
        }

        /// <summary>Is this UI pixel on one of our buttons (or, while it is open, on the Weapon Catalogue)?</summary>
        public static bool Contains(Vector2 uiPixel)
        {
            var me = Instance;
            if (me == null) return false;
            if (_catalogueOpen) return _catRect.Contains(uiPixel);
            if (me._main.Contains(uiPixel) || me._hands.Contains(uiPixel) || me._weapons.Contains(uiPixel)) return true;
            if (_arcOpen) foreach (var s in me._swatches) if (s.Contains(uiPixel)) return true;
            if (_weaponArcOpen) foreach (var s in me._arms) if (s.Contains(uiPixel)) return true;
            return false;
        }

        /// <summary>The open arc's fan: between its two ends, out to just past its buttons (beyondRing: and outside TaleSpire's ring).</summary>
        private static bool InArcFan(Vector2 uiPixel, bool beyondRing)
        {
            if (!_arcOpen && !_weaponArcOpen) return false;
            Vector2 d = uiPixel - MenuCenter;
            float dist = d.magnitude;
            if (dist > _arcRadius + _radius * 1.4f) return false;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            if (angle < _fanTo - 8f || angle > _fanFrom + 8f) return false;
            return !beyondRing || dist > _ringTop + _radius * 1.3f;
        }

        /// <summary>
        /// Set by VRPointer each frame: where the laser in control is on the radial window (null: not on it). On our
        /// buttons, on the empty part of an open arc above TaleSpire's ring, or anywhere on the open Weapon Catalogue,
        /// the laser is ours (Hovered).
        /// </summary>
        public static void HoverAt(Vector2? uiPixel)
        {
            _hoverIndex = -1;
            Hovered = false;
            var me = Instance;
            if (me == null || !uiPixel.HasValue) return;
            Vector2 p = uiPixel.Value;
            if (_catalogueOpen)
            {
                if (me._catReturn.Contains(p)) _hoverIndex = HoverReturn;
                else
                    for (int i = 0; i < me._tiles.Length; i++)
                        if (me._tiles[i].Contains(p)) { _hoverIndex = HoverTile0 + i; break; }
                Hovered = _catRect.Contains(p);
                return;
            }
            if (me._main.Contains(p)) _hoverIndex = HoverMain;
            else if (me._hands.Contains(p)) _hoverIndex = HoverHands;
            else if (me._weapons.Contains(p)) _hoverIndex = HoverWeapons;
            else if (_arcOpen)
            {
                for (int i = 0; i < me._swatches.Length; i++)
                    if (me._swatches[i].Contains(p)) { _hoverIndex = HoverSwatch0 + i; break; }
            }
            else if (_weaponArcOpen)
                for (int i = 0; i < me._arms.Length; i++)
                    if (me._arms[i].Contains(p)) { _hoverIndex = HoverArm0 + i; break; }
            Hovered = _hoverIndex >= 0 || InArcFan(p, beyondRing: true);
        }

        /// <summary>The Weapon Catalogue is open (in place of the radial menu).</summary>
        public static bool CatalogueOpen => _catalogueOpen;

        /// <summary>The VR mod switches off (Player mode): everything of ours closed, TaleSpire's radial menu visible again.</summary>
        public static void Suspend()
        {
            _arcOpen = _weaponArcOpen = _catalogueOpen = false;
            OwnMenuOpen = false;
            Hovered = false;
            _hoverIndex = -1;
            RequiredRadius = 0f;
            Instance?.HideTaleSpireMenu(false);
        }

        public static void OpenOwn()
        {
            if (!FirstPersonMode.Active) return;
            OwnMenuOpen = true;
            Plugin.Log.LogInfo("First person: exit menu opened");
        }

        public static void CloseOwn()
        {
            if (!OwnMenuOpen) return;
            OwnMenuOpen = false;
            Plugin.Log.LogInfo("First person: exit menu closed");
        }

        /// <summary>The trigger on the hovered button.</summary>
        public static void Press()
        {
            int i = _hoverIndex;
            if (i < 0) return;   // the empty part of an arc or of the catalogue
            var c = _menuCreature;
            if (i == HoverHands)
            {
                _arcOpen = !_arcOpen;
                _weaponArcOpen = false;
                _arcCreature = c;
                Plugin.Log.LogInfo($"Hand colours {(_arcOpen ? "shown" : "hidden")} for '{c?.Name}'");
                return;
            }
            if (i == HoverWeapons)
            {
                _weaponArcOpen = !_weaponArcOpen;
                _arcOpen = false;
                _arcCreature = c;
                Plugin.Log.LogInfo($"Weapons {(_weaponArcOpen ? "shown" : "hidden")} for '{c?.Name}'");
                return;
            }
            if (i >= HoverSwatch0 && i < HoverWeapons)
            {
                string key = i == HoverSwatch0 ? null : HandColors.Palette[i - HoverSwatch0 - 1].Key;
                HandColors.Set(c, key);
                if (FirstPersonMode.Instance != null) FirstPersonMode.Instance.HandColorChanged(c);
                _arcOpen = false;
                return;
            }
            if (i >= HoverArm0 && i < HoverReturn)
            {
                int k = i - HoverArm0;
                string item = k < _armItems.Count ? _armItems[k] : null;
                if (item == null) return;
                if (item == "+")
                {
                    _catalogueOpen = true;
                    Plugin.Log.LogInfo($"Weapon Catalogue opened for '{c?.Name}'");
                    return;
                }
                WeaponLoadouts.Equip(c, item == "" ? null : item);
                if (FirstPersonMode.Instance != null) FirstPersonMode.Instance.WeaponChanged(c);
                _weaponArcOpen = false;
                return;
            }
            if (i == HoverReturn)
            {
                _catalogueOpen = false;
                Plugin.Log.LogInfo("Weapon Catalogue: back to the radial menu");
                return;
            }
            if (i >= HoverTile0)
            {
                var w = WeaponLoadouts.Catalogue[i - HoverTile0];
                bool has = WeaponLoadouts.Has(c, w.Id);
                WeaponLoadouts.SetAdded(c, w.Id, !has);
                if (has && FirstPersonMode.Instance != null) FirstPersonMode.Instance.WeaponChanged(c);   // it may have been the one held
                return;
            }
            var kind = Showing;
            var creature = _creature;
            Plugin.Log.LogInfo($"Radial extra button pressed: {kind}");
            CloseOwn();
            CloseTaleSpireMenu();
            if (FirstPersonMode.Instance == null) return;
            if (kind == Kind.Enter && creature != null) FirstPersonMode.Instance.Enter(creature);
            else if (kind == Kind.Exit) FirstPersonMode.Instance.Exit();
        }

        internal static void CloseTaleSpireMenu()
        {
            try
            {
                if (!SingletonBehaviour<MapMenuManager>.HasInstance || !MapMenuManager.IsOpen) return;
                var tool = BoardToolManager.CurrentTool;
                if (tool is MenuBoardTool) tool.SwitchToOwnDefaultTool();
                else MapMenuManager.ForceCloseAll();
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Closing the radial menu failed: " + e.Message); }
        }

        private void LateUpdate()
        {
            Showing = Kind.None;
            _creature = null;
            _menuCreature = null;
            var rig = VRManager.Instance != null ? VRManager.Instance.Rig : null;
            if (rig == null || !rig.OnBoard || !FirstPersonMode.Active) OwnMenuOpen = false;
            bool tsOpen = rig != null && rig.OnBoard && SingletonBehaviour<MapMenuManager>.HasInstance && MapMenuManager.IsOpen;
            if (tsOpen && OwnMenuOpen) CloseOwn();   // TaleSpire opened a real one
            if (rig != null && rig.OnBoard) { HandColors.Prune(); WeaponLoadouts.Prune(); }

            float scale = 1f;
            float top = 0f, labelTop = 0f;
            bool rootShown = false;
            if (OwnMenuOpen)
            {
                Showing = Kind.Exit;
                MenuCenter = new Vector2(UICapture.Width * 0.5f, UICapture.Height * 0.5f);
                scale = SingletonBehaviour<MapMenuManager>.HasInstance ? SingletonBehaviour<MapMenuManager>.Instance.transform.lossyScale.y : 1f;
                top = 58f * scale;   // where TaleSpire puts a one-option menu's option
                rootShown = true;
                _menuCreature = FirstPersonMode.Creature;
            }
            else if (tsOpen && !FirstPersonMode.Transitioning && TryGetRootMenu(out MapMenu root, out top, out labelTop))
            {
                if (FirstPersonMode.Active) Showing = Kind.Exit;
                else if (CreatureMenuTarget(out var c)) { Showing = Kind.Enter; _creature = c; }
                MenuCenter = root.transform.position;
                scale = root.transform.lossyScale.y;
                rootShown = true;
                _menuCreature = OpenCreatureMenuTarget();
            }
            // (Only for creatures TaleSpire lets you control: a player can't dress up or arm someone else's.)
            bool mine = rootShown && Roles.CanControl(_menuCreature);
            bool showHands = mine && HandColors.CanColor(_menuCreature);
            bool showWeapons = mine && WeaponLoadouts.CanArm(_menuCreature);
            if (!showHands || _menuCreature != _arcCreature) _arcOpen = false;   // an arc belongs to one creature's open menu
            if (!showWeapons || _menuCreature != _arcCreature) _weaponArcOpen = false;
            if (!_weaponArcOpen || FirstPersonMode.Transitioning) _catalogueOpen = false;   // (the catalogue goes with its menu: A, or the menu closing)
            HideTaleSpireMenu(_catalogueOpen && tsOpen);
            UpdateCatchArea(tsOpen, top, scale);

            // First person: whose menu it is, written in the middle of the ring.
            string name = FirstPersonMode.Active && !FirstPersonMode.Transitioning && (OwnMenuOpen || tsOpen) && !_catalogueOpen ? RadialTargetName() : null;
            bool showName = !string.IsNullOrEmpty(name) && _innerRadius > 30f * _ringScale;
            _shownName = showName ? name : "-";
            bool showMain = Showing != Kind.None;
            bool show = (showMain || showHands || showWeapons || showName) && _canvasRt != null;
            if (_canvasRt != null && _canvasRt.gameObject.activeSelf != show) _canvasRt.gameObject.SetActive(show);
            if (!show) { Hovered = false; _hoverIndex = -1; RequiredRadius = 0f; return; }
            StyleLabels();
            if (_name.gameObject.activeSelf != showName) _name.gameObject.SetActive(showName);
            if (showName) PlaceName(name);

            // Above the top option AND clear of its name label (TaleSpire writes the top option's name just above it),
            // the size of TaleSpire's options; side by side when more than one shows.
            _radius = 27f * scale;
            _ringTop = top;
            float grownRadius = _radius * 1.15f;
            _buttonsY = Mathf.Max(top + 100f * scale, Mathf.Max(top, labelTop) + 10f * scale + grownRadius);
            float fontSize = _labelFontSize * scale;
            bool cat = _catalogueOpen;
            int count = (showMain ? 1 : 0) + (showHands ? 1 : 0) + (showWeapons ? 1 : 0);
            float slot = -(count - 1) * 0.5f;
            float Next() { float x = slot * 2.7f * _radius; slot += 1f; return x; }
            // (While an arc is open their names would sit among its buttons: only the arc's buttons are named.)
            bool arc = _arcOpen || _weaponArcOpen;
            _main.Show(showMain && !cat);
            if (showMain)
            {
                float x = Next();
                if (!cat)
                {
                    _main.Layout(MenuCenter + new Vector2(x, _buttonsY), _radius, _hoverIndex == HoverMain,
                        arc ? null : Showing == Kind.Enter ? "First Person" : "Exit First Person", fontSize, scale);
                    _main.Disc.color = Color.Lerp(Fill, FillHover, _main.Grow);
                    _main.Icon.texture = Showing == Kind.Enter ? _eye : _eyeExit;
                }
            }
            _hands.Show(showHands && !cat);
            if (showHands)
            {
                float x = Next();
                if (!cat)
                {
                    _hands.Layout(MenuCenter + new Vector2(x, _buttonsY), _radius, _hoverIndex == HoverHands, arc ? null : "Hand Colour", fontSize, scale);
                    _hands.Disc.color = Color.Lerp(HandsFill, HandsFillHover, Mathf.Max(_hands.Grow, _arcOpen ? 0.6f : 0f));
                }
            }
            _weapons.Show(showWeapons && !cat);
            if (showWeapons)
            {
                float x = Next();
                if (!cat)
                {
                    _weapons.Layout(MenuCenter + new Vector2(x, _buttonsY), _radius, _hoverIndex == HoverWeapons, arc ? null : "Weapons", fontSize, scale);
                    _weapons.Disc.color = Color.Lerp(ArmsFill, ArmsFillHover, Mathf.Max(_weapons.Grow, _weaponArcOpen ? 0.6f : 0f));
                }
            }
            RequiredRadius = 0f;
            LayoutArc(fontSize, scale);
            LayoutWeaponArc(fontSize, scale);
            LayoutCatalogue(scale);

            // Our own menu: a faint ring where TaleSpire's options would be, so it reads as a radial menu.
            bool ring = OwnMenuOpen && !cat;
            if (_ring.gameObject.activeSelf != ring) _ring.gameObject.SetActive(ring);
            if (ring)
            {
                _ring.anchoredPosition = MenuCenter;
                _ring.sizeDelta = Vector2.one * (top * 2f + _radius * 2f);
            }
        }

        /// <summary>
        /// The hand colours: "Default" (the config's colour) then the palette, on an arc above the menu, past our buttons.
        /// The creature's current colour has a bright, thick rim.
        /// </summary>
        private void LayoutArc(float fontSize, float scale)
        {
            bool open = _arcOpen && !_catalogueOpen;
            foreach (var s in _swatches) s.Show(open);
            if (!open) return;
            string current = HandColors.TryGet(_menuCreature, out var cur) ? cur.Key : null;
            float swatchRadius = _radius * 0.78f;
            _arcRadius = _buttonsY + _radius * 2.4f;
            _fanFrom = ArcFrom;
            _fanTo = ArcTo;
            for (int i = 0; i < _swatches.Length; i++)
            {
                float a = Mathf.Lerp(ArcFrom, ArcTo, i / (float)(_swatches.Length - 1)) * Mathf.Deg2Rad;
                var s = _swatches[i];
                bool isDefault = i == 0;
                var p = isDefault ? default : HandColors.Palette[i - 1];
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                s.Layout(MenuCenter + dir * _arcRadius, swatchRadius, _hoverIndex == HoverSwatch0 + i, isDefault ? "Default" : p.Name, fontSize, scale, dir);
                s.Disc.color = isDefault ? HandColors.DefaultColor : p.Color;
                bool selected = (isDefault ? null : p.Key) == current;
                s.Rim.sprite = selected ? _rimThick : _rimThin;
                s.Rim.color = selected ? Color.white : new Color(0.85f, 0.85f, 0.85f, 0.7f);
            }
            RequiredRadius = _arcRadius + swatchRadius * 1.15f + 140f * scale;   // (names of the side swatches reach out sideways)
        }

        /// <summary>
        /// The weapons: "Unarmed" at the left end, then the weapons added to this creature, then + (the Weapon
        /// Catalogue), on an arc centred above the menu. The one held has a bright, thick rim.
        /// </summary>
        private void LayoutWeaponArc(float fontSize, float scale)
        {
            _armItems.Clear();
            bool open = _weaponArcOpen && !_catalogueOpen;
            if (open)
            {
                _armItems.Add("");
                foreach (var w in WeaponLoadouts.WeaponsOf(_menuCreature)) _armItems.Add(w.Id);
                _armItems.Add("+");
            }
            for (int i = 0; i < _arms.Length; i++) _arms[i].Show(open && i < _armItems.Count);
            if (!open) return;
            var held = WeaponLoadouts.EquippedOf(_menuCreature);
            // (Very many weapons: smaller buttons, closer together, so the arcs stay inside the menu window.)
            float k = _armItems.Count > 24 ? 0.8f : 1f;
            float r = _radius * 0.9f * k;
            // Well above our buttons, its buttons a little apart along the arc. More than fit within the colour arc's
            // spread go on further arcs outside it, the items shared evenly (the first ones on the inner arc).
            float inner = _buttonsY + _radius * 3.4f, rowGap = _radius * 2.5f * k;
            float StepAt(float radius) => Mathf.Min(ArmStep * k, 2.5f * _radius * k / radius * Mathf.Rad2Deg);
            int n = _armItems.Count, rows = 1;
            int innerFits = Mathf.FloorToInt((ArcFrom - ArcTo) / StepAt(inner)) + 1;
            while ((n + rows - 1) / rows > innerFits) rows++;
            int perRow = (n + rows - 1) / rows;
            _arcRadius = inner + (rows - 1) * rowGap;
            _fanFrom = 90f;
            _fanTo = 90f;
            for (int i = 0; i < n; i++)
            {
                int row = i / perRow, inRow = Mathf.Min(perRow, n - row * perRow), at = i - row * perRow;
                float radius = inner + row * rowGap, step = StepAt(radius);
                float from = 90f + (inRow - 1) * step * 0.5f;
                _fanFrom = Mathf.Max(_fanFrom, from);
                _fanTo = Mathf.Min(_fanTo, 180f - from);
                float a = (from - at * step) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                string item = _armItems[i];
                var w = WeaponLoadouts.Find(item);
                string label = item == "" ? "Unarmed" : item == "+" ? "Weapon Catalogue" : w != null ? w.Name : item;
                var s = _arms[i];
                s.Layout(MenuCenter + dir * radius, r, _hoverIndex == HoverArm0 + i, label, fontSize, scale, dir);
                s.Icon.texture = item == "" ? _handIcon : item == "+" ? _plusIcon : Icon(item);
                bool selected = item == "" ? held == null : item != "+" && held != null && held.Id == item;
                s.Disc.color = Color.Lerp(item == "+" ? new Color(0.12f, 0.12f, 0.14f, 1f) : ArmsFill, ArmsFillHover, s.Grow);
                s.Rim.sprite = selected ? _rimThick : _rimThin;
                s.Rim.color = selected ? Color.white : new Color(0.85f, 0.85f, 0.85f, 0.7f);
            }
            RequiredRadius = Mathf.Max(RequiredRadius, _arcRadius + r * 1.15f + 160f * scale);
        }

        /// <summary>
        /// The Weapon Catalogue, where the radial menu was: its title and Return at the top, then a grid of dark,
        /// rounded, semi-translucent squares, one per weapon (a tick on those this creature has; a press adds or removes).
        /// </summary>
        private void LayoutCatalogue(float scale)
        {
            bool open = _catalogueOpen;
            if (_cat.gameObject.activeSelf != open) _cat.gameObject.SetActive(open);
            if (!open) { _catRect = default; return; }
            int n = _tiles.Length;
            // (More weapons: more, smaller squares per row, so the catalogue stays about square.)
            int cols = Mathf.Min(n > 30 ? 7 : n > 20 ? 6 : n > 12 ? 5 : n > 6 ? 4 : 3, n), rows = (n + cols - 1) / cols;
            float tile = (n > 30 ? 100f : n > 20 ? 108f : n > 12 ? 118f : 132f) * scale, gap = (n > 30 ? 12f : 16f) * scale, head = 68f * scale;
            float gridW = cols * tile + (cols - 1) * gap, gridH = rows * tile + (rows - 1) * gap;
            float w = Mathf.Max(gridW, 470f * scale), h = gridH + head;
            Vector2 c = MenuCenter;
            float topY = c.y + h * 0.5f;
            _catRect = new Rect(c.x - w * 0.5f - 20f * scale, c.y - h * 0.5f - 20f * scale, w + 40f * scale, h + 40f * scale);

            _catTitle.fontSize = 28f * scale;
            var trt = _catTitle.rectTransform;
            trt.anchoredPosition = new Vector2(c.x + 40f * scale, topY - head * 0.42f);
            trt.sizeDelta = new Vector2(w, head);

            // Return: a small rounded button at the top left.
            var ret = _catReturn;
            ret.Grow = Mathf.MoveTowards(ret.Grow, _hoverIndex == HoverReturn ? 1f : 0f, Time.unscaledDeltaTime * 10f);
            Vector2 rs = new Vector2(124f, 40f) * scale * (1f + 0.06f * ret.Grow);
            Vector2 rc = new Vector2(c.x - w * 0.5f + 62f * scale, topY - head * 0.42f);
            ret.Rt.anchoredPosition = rc;
            ret.Rt.sizeDelta = rs;
            ret.Area = new Rect(rc - rs * 0.6f, rs * 1.2f);
            ret.Back.pixelsPerUnitMultiplier = ret.Edge.pixelsPerUnitMultiplier = 16f / (10f * scale);
            ret.Back.color = Color.Lerp(TileFill, TileFillHover, ret.Grow);
            ret.Edge.color = new Color(1f, 1f, 1f, 0.18f + 0.4f * ret.Grow);
            ret.Name.fontSize = 17f * scale;

            var creature = _menuCreature;
            for (int i = 0; i < n; i++)
            {
                var t = _tiles[i];
                var def = WeaponLoadouts.Catalogue[i];
                int col = i % cols, row = i / cols;
                Vector2 tc = new Vector2(c.x - gridW * 0.5f + tile * 0.5f + col * (tile + gap), topY - head - tile * 0.5f - row * (tile + gap));
                t.Grow = Mathf.MoveTowards(t.Grow, _hoverIndex == HoverTile0 + i ? 1f : 0f, Time.unscaledDeltaTime * 10f);
                float size = tile * (1f + 0.06f * t.Grow);
                t.Rt.anchoredPosition = tc;
                t.Rt.sizeDelta = new Vector2(size, size);
                t.Area = new Rect(tc - Vector2.one * (tile + gap) * 0.5f, Vector2.one * (tile + gap));
                bool has = WeaponLoadouts.Has(creature, def.Id);
                t.Back.pixelsPerUnitMultiplier = t.Edge.pixelsPerUnitMultiplier = 16f / (14f * scale);
                t.Back.color = Color.Lerp(TileFill, TileFillHover, t.Grow);
                t.Edge.color = has ? new Color(0.55f, 0.95f, 0.55f, 0.9f) : new Color(1f, 1f, 1f, 0.18f + 0.4f * t.Grow);
                t.Tick.gameObject.SetActive(has);
                t.Name.text = t.Grow > 0.5f ? (has ? "Remove" : "Add") : def.Name;
                // (Smaller for a long name, e.g. Rocket Launcher, so it stays inside its square.)
                t.Name.fontSize = 17f * scale;
                float nameW = t.Name.GetPreferredValues(t.Name.text).x;
                if (nameW > tile * 0.92f) t.Name.fontSize = 17f * scale * tile * 0.92f / nameW;
            }
            RequiredRadius = Mathf.Max(RequiredRadius, 0.5f * Mathf.Sqrt(_catRect.width * _catRect.width + _catRect.height * _catRect.height) + 30f * scale);
        }

        /// <summary>TaleSpire's radial menu, hidden (still open) while the catalogue takes its place.</summary>
        private void HideTaleSpireMenu(bool hide)
        {
            if (!hide && _hiddenMenu == null) return;
            if (hide && _hiddenMenu == null && SingletonBehaviour<MapMenuManager>.HasInstance)
            {
                var go = SingletonBehaviour<MapMenuManager>.Instance.gameObject;
                _hiddenMenu = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
            }
            if (_hiddenMenu == null) return;
            _hiddenMenu.alpha = hide ? 0f : 1f;
            if (!hide) _hiddenMenu = null;
        }

        private Texture2D Icon(string weaponId)
        {
            if (!WeaponIcons.TryGetValue(weaponId, out var tex))
            {
                // A picture of its model (made with weapons.bin); the hand powers (and anything without one) drawn here.
                var def = WeaponLoadouts.Find(weaponId);
                tex = def == null ? _plusIcon : FPWeapons.IconOf(def.Mesh);
                if (tex == null)
                    tex = def.Id == "ice" ? MakeSnowflake(128) : def.Kind == WeaponKind.Ranged ? MakeShotgun(128) : def.Kind == WeaponKind.Melee ? MakeSword(128) : MakeFlame(128);
                WeaponIcons[weaponId] = tex;
            }
            return tex;
        }

        // ---- Where an open radial menu catches a laser, and the name in its middle --------------------------------

        private static readonly FieldInfo MenuTypeField = AccessTools.Field(typeof(MapMenu), "_menuType");
        private static readonly FieldInfo PlaceableField = AccessTools.Field(typeof(InteractivePlaceableMenuBoardTool), "_selected");
        private static readonly List<Rect> _catchRects = new List<Rect>();
        private static readonly List<Vector3> _catchRings = new List<Vector3>();   // x, y = centre, z = radius
        private static bool _catchKnown;
        private static float _innerRadius, _ringScale = 1f;
        private static Vector2 _ringCenter;
        private static string _shownName = "-";
        private static readonly Vector3[] _corners = new Vector3[4];

        /// <summary>
        /// Is this UI pixel on the open radial menu: inside a ring of options (with a margin of half an option), on an
        /// option of a branch or list sub-menu (with the same margin), on one of our buttons, or within the open colour
        /// arc's fan? Elsewhere on the floating window the lasers go through to the board. Unknown layout (menu just
        /// opening): the whole window counts.
        /// </summary>
        public static bool InCatchArea(Vector2 uiPixel)
        {
            if (_catalogueOpen) return _catRect.Contains(uiPixel);   // (TaleSpire's hidden options catch nothing)
            if (!_catchKnown || Contains(uiPixel) || InArcFan(uiPixel, beyondRing: false)) return true;
            foreach (var ring in _catchRings)
                if ((uiPixel - new Vector2(ring.x, ring.y)).sqrMagnitude <= ring.z * ring.z) return true;
            foreach (var r in _catchRects)
                if (r.Contains(uiPixel)) return true;
            return false;
        }

        /// <summary>The open menu's rings, options and inner free radius (UI pixels), measured from its option buttons.</summary>
        private static void UpdateCatchArea(bool tsOpen, float ownTop, float ownScale)
        {
            _catchRects.Clear();
            _catchRings.Clear();
            _catchKnown = false;
            _innerRadius = 0f;
            if (OwnMenuOpen)
            {
                // Our exit-only menu: its faint ring and a margin; nothing inside the ring.
                float optionRadius = 27f * ownScale;
                _catchRings.Add(new Vector3(MenuCenter.x, MenuCenter.y, ownTop + optionRadius * 2f));
                _ringCenter = MenuCenter;
                _ringScale = ownScale;
                _innerRadius = ownTop - optionRadius;
                _catchKnown = true;
                return;
            }
            if (!tsOpen || StackField == null || ItemsField == null) return;
            var stack = StackField.GetValue(SingletonBehaviour<MapMenuManager>.Instance) as Stack<MapMenu>;
            if (stack == null || stack.Count == 0) return;
            MapMenu root = null;
            foreach (var m in stack) root = m;   // a Stack enumerates from the top: the last one is the root menu
            if (root == null) return;
            _ringCenter = root.transform.position;
            _ringScale = root.transform.lossyScale.y;
            float inner = float.MaxValue;
            foreach (var menu in stack)
            {
                if (menu == null || !menu.gameObject.activeInHierarchy) continue;
                var items = ItemsField.GetValue(menu) as Transform;
                if (items == null) continue;
                string type = MenuTypeField != null ? MenuTypeField.GetValue(menu)?.ToString() : "";
                bool ring = type == "ROOT" || type == "SUBROOT";
                Vector2 c = menu.transform.position;
                float outer = 0f, margin = 0f;
                for (int i = 0; i < items.childCount; i++)
                {
                    var rt = items.GetChild(i) as RectTransform;
                    if (rt == null || !rt.gameObject.activeInHierarchy) continue;
                    rt.GetWorldCorners(_corners);
                    var r = Rect.MinMaxRect(Mathf.Min(_corners[0].x, _corners[2].x), Mathf.Min(_corners[0].y, _corners[2].y),
                                            Mathf.Max(_corners[0].x, _corners[2].x), Mathf.Max(_corners[0].y, _corners[2].y));
                    if (r.width < 1f || r.height < 1f) continue;
                    float half = 0.5f * Mathf.Max(r.width, r.height);
                    _catchRects.Add(new Rect(r.x - half * 0.5f, r.y - half * 0.5f, r.width + half, r.height + half));
                    outer = Mathf.Max(outer, Vector2.Distance(r.center, c) + half);
                    margin = Mathf.Max(margin, half * 0.5f);
                    inner = Mathf.Min(inner, Vector2.Distance(r.center, _ringCenter) - half);
                }
                if (ring && outer > 0f) _catchRings.Add(new Vector3(c.x, c.y, outer + margin));
            }
            _catchKnown = _catchRects.Count > 0;
            _innerRadius = inner == float.MaxValue ? 0f : Mathf.Max(0f, inner);
        }

        /// <summary>Whose radial menu is open: a creature's name, a door's / chest's asset name, "Dice"; null if unknown.</summary>
        private static string RadialTargetName()
        {
            try
            {
                if (OwnMenuOpen) return FirstPersonMode.Creature != null ? FirstPersonMode.Creature.Name : null;
                var tool = BoardToolManager.CurrentTool;
                if (tool is CreatureMenuBoardTool) return OpenCreatureMenuTarget()?.Name;
                if (tool is DiceMenuBoardTool) return "Dice";
                if (tool is InteractivePlaceableMenuBoardTool && PlaceableField != null)
                {
                    object handle = PlaceableField.GetValue(tool);
                    object entry = handle != null ? Traverse.Create(handle).Property("DbEntry").GetValue() : null;
                    if (entry == null) return null;
                    var t = Traverse.Create(entry);
                    return (t.Property("Name").GetValue() ?? t.Field("Name").GetValue())?.ToString();
                }
            }
            catch { }
            return null;
        }

        /// <summary>The creature whose radial menu is open (null if the open menu is not a creature's).</summary>
        internal static CreatureBoardAsset OpenCreatureMenuTarget()
        {
            try
            {
                return BoardToolManager.CurrentTool is CreatureMenuBoardTool tool && SelectedField != null ? SelectedField.GetValue(tool) as CreatureBoardAsset : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// The name in the middle of the ring: inside a box (1.6 x 0.8 of the free radius, so its corners stay inside the
        /// ring), shrinking to fit and cut with "..." if it is still too long, in TaleSpire's option label font.
        /// </summary>
        private void PlaceName(string name)
        {
            float r = _innerRadius;
            var rt = _name.rectTransform;
            rt.anchoredPosition = _ringCenter;
            rt.sizeDelta = new Vector2(1.6f * r, 0.8f * r);
            _name.fontSizeMax = _labelFontSize * _ringScale * 1.3f;
            _name.fontSizeMin = _labelFontSize * _ringScale * 0.5f;
            if (_name.text != name) _name.text = name;
        }

        /// <summary>
        /// The root radial menu when it is the only one open (no sub-menu), how far above its centre its top option is,
        /// and how far up that option's name label reaches (UI pixels).
        /// </summary>
        private static bool TryGetRootMenu(out MapMenu root, out float top, out float labelTop)
        {
            root = null;
            top = labelTop = 0f;
            if (StackField == null || ItemsField == null) return false;
            var stack = StackField.GetValue(SingletonBehaviour<MapMenuManager>.Instance) as Stack<MapMenu>;
            if (stack == null || stack.Count != 1) return false;
            root = stack.Peek();
            if (root == null || !root.gameObject.activeInHierarchy) return false;
            var items = ItemsField.GetValue(root) as Transform;
            if (items == null) return false;
            float cy = root.transform.position.y;
            Transform highest = null;
            for (int i = 0; i < items.childCount; i++)
            {
                var c = items.GetChild(i);
                if (!c.gameObject.activeSelf || c.position.y - cy < top) continue;
                top = c.position.y - cy;
                highest = c;
            }
            // Options grow out of the centre as the menu opens; never sit on top of them.
            float scale = root.transform.lossyScale.y;
            top = Mathf.Max(top, 58f * scale * 0.5f);
            // Its label sits above it at full size even before it fades in (its rect is sized to the text).
            var item = highest != null ? highest.GetComponent<MapMenuItem>() : null;
            var text = item != null && TextField != null ? TextField.GetValue(item) as TMPro.TMP_Text : null;
            if (text != null)
            {
                var itemRt = (RectTransform)highest;
                float labelHeight = Mathf.Max(text.rectTransform.rect.height, text.fontSize * 1.1f);
                labelTop = (highest.position.y - cy) + (itemRt.rect.width * 0.5f + 10f + labelHeight) * highest.lossyScale.y;
            }
            return true;
        }

        private static readonly FieldInfo TextField = AccessTools.Field(typeof(MapMenuItem), "_textMesh");
        private float _labelFontSize = 15f;
        private bool _labelStyled;

        /// <summary>Use the font and look of TaleSpire's own option labels (taken from one of its menu options).</summary>
        private void StyleLabels()
        {
            if (_labelStyled || TextField == null) return;
            foreach (var item in Resources.FindObjectsOfTypeAll<MapMenuItem>())
            {
                if (item == null || item.gameObject.scene.name == null) continue;
                var t = TextField.GetValue(item) as TMPro.TMP_Text;
                if (t == null || t.font == null) continue;
                foreach (var l in _labels)
                {
                    l.font = t.font;
                    l.fontSharedMaterial = t.fontSharedMaterial;
                    l.fontStyle = t.fontStyle;
                    l.characterSpacing = t.characterSpacing;
                    l.color = new Color(t.color.r, t.color.g, t.color.b, 1f);   // (TaleSpire's labels fade in on hover: its alpha may be 0)
                }
                _labelFontSize = t.fontSize;
                _labelStyled = true;
                Plugin.Log.LogInfo($"Radial extra button label: font '{t.font.name}' size {t.fontSize} style {t.fontStyle}");
                return;
            }
        }

        private static bool CreatureMenuTarget(out CreatureBoardAsset creature)
        {
            // First person only as a creature TaleSpire lets you control (a Game Master: any; a player: their own).
            creature = OpenCreatureMenuTarget();
            return Roles.CanControl(creature);
        }

        // ---- Visuals -------------------------------------------------------------------------------------------

        private void Build()
        {
            var go = new GameObject("TaleSpireVR_RadialExtraCanvas", typeof(RectTransform));
            DontDestroyOnLoad(go);
            go.layer = UICapture.RadialLayer;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = UICapture.UICamera;
            canvas.planeDistance = 98f;
            canvas.sortingOrder = 32000;
            _canvasRt = (RectTransform)go.transform;

            _ring = NewRect("Ring", go.transform);
            _ringImg = _ring.gameObject.AddComponent<Image>();
            _ringImg.sprite = Sprite.Create(MakeCircle(256, 0.97f, 0.012f, false), new Rect(0, 0, 256, 256), new Vector2(0.5f, 0.5f));
            _ringImg.color = new Color(1f, 1f, 1f, 0.25f);
            _ringImg.raycastTarget = false;

            var disc = Sprite.Create(MakeCircle(128, 0.94f, 0f, true), new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f));
            var rim = Sprite.Create(MakeCircle(128, 0.94f, 0.08f, false), new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f));
            _rimThin = Sprite.Create(MakeCircle(128, 0.94f, 0.06f, false), new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f));
            _rimThick = Sprite.Create(MakeCircle(128, 0.94f, 0.17f, false), new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f));
            _eye = MakeEye(128, false);
            _eyeExit = MakeEye(128, true);

            _main = MakeButton("Button", go.transform, disc, rim, new Color(0.55f, 0.88f, 0.92f, 1f), true);
            _handIcon = MakeHand(128);
            _plusIcon = MakePlus(128);
            _hands = MakeButton("Hands", go.transform, disc, rim, new Color(0.62f, 0.74f, 1f, 1f), true);
            _hands.Icon.texture = _handIcon;
            _weapons = MakeButton("Weapons", go.transform, disc, rim, new Color(1f, 0.72f, 0.62f, 1f), true);
            _weapons.Icon.texture = MakeCrossedArms(128);
            _swatches = new RoundButton[SwatchCount];
            for (int i = 0; i < _swatches.Length; i++) _swatches[i] = MakeButton("Swatch" + i, go.transform, disc, _rimThin, Color.white, false);
            _arms = new RoundButton[WeaponLoadouts.Catalogue.Length + 2];
            for (int i = 0; i < _arms.Length; i++) _arms[i] = MakeButton("Arm" + i, go.transform, disc, _rimThin, Color.white, true);
            BuildCatalogue(go.transform);

            // First person: the name of whoever / whatever the menu belongs to, in the middle of the ring.
            var name = NewRect("Name", go.transform);
            _name = name.gameObject.AddComponent<TMPro.TextMeshProUGUI>();
            _name.alignment = TMPro.TextAlignmentOptions.Center;
            _name.enableWordWrapping = true;
            _name.overflowMode = TMPro.TextOverflowModes.Ellipsis;
            _name.enableAutoSizing = true;
            _name.raycastTarget = false;
            _labels.Add(_name);
            name.gameObject.SetActive(false);

            VRLayers.SetRecursive(go, UICapture.RadialLayer);
            go.SetActive(false);
        }

        private RoundButton MakeButton(string name, Transform parent, Sprite disc, Sprite rim, Color rimColor, bool icon)
        {
            var b = new RoundButton { Rt = NewRect(name, parent) };
            b.Disc = b.Rt.gameObject.AddComponent<Image>();
            b.Disc.sprite = disc;
            b.Disc.raycastTarget = false;
            var r = NewRect("Rim", b.Rt);
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            b.Rim = r.gameObject.AddComponent<Image>();
            b.Rim.sprite = rim;
            b.Rim.color = rimColor;
            b.Rim.raycastTarget = false;
            if (icon)
            {
                var ic = NewRect("Icon", b.Rt);
                ic.anchorMin = new Vector2(0.2f, 0.2f);
                ic.anchorMax = new Vector2(0.8f, 0.8f);
                ic.offsetMin = ic.offsetMax = Vector2.zero;
                b.Icon = ic.gameObject.AddComponent<RawImage>();
                b.Icon.raycastTarget = false;
            }
            var label = NewRect("Label", b.Rt);
            label.anchorMin = label.anchorMax = new Vector2(0.5f, 0.5f);
            b.Label = label.gameObject.AddComponent<TMPro.TextMeshProUGUI>();
            b.Label.alignment = TMPro.TextAlignmentOptions.Bottom;
            b.Label.enableWordWrapping = false;
            b.Label.overflowMode = TMPro.TextOverflowModes.Overflow;
            b.Label.raycastTarget = false;
            _labels.Add(b.Label);
            b.Show(false);
            return b;
        }

        private void BuildCatalogue(Transform parent)
        {
            _cat = NewRect("WeaponCatalogue", parent);
            _cat.anchorMin = Vector2.zero;
            _cat.anchorMax = Vector2.one;
            _cat.offsetMin = _cat.offsetMax = Vector2.zero;
            var fill = Sprite.Create(MakeRoundedSquare(64, 16f, false), new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(20, 20, 20, 20));
            var edge = Sprite.Create(MakeRoundedSquare(64, 16f, true), new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(20, 20, 20, 20));
            var tick = MakeTick(64);

            _catTitle = NewText("Title", _cat, "Weapon Catalogue", TMPro.TextAlignmentOptions.Center);
            _catTitle.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            _catReturn = MakeTile("Return", fill, edge, null);
            _catReturn.Icon.texture = MakeReturnArrow(64);
            SetAnchors(_catReturn.Icon.rectTransform, new Vector2(0.07f, 0.18f), new Vector2(0.30f, 0.82f));
            SetAnchors(_catReturn.Name.rectTransform, new Vector2(0.30f, 0f), new Vector2(0.97f, 1f));
            _catReturn.Name.alignment = TMPro.TextAlignmentOptions.Center;
            _catReturn.Name.text = "Return";

            _tiles = new Tile[WeaponLoadouts.Catalogue.Length];
            for (int i = 0; i < _tiles.Length; i++)
            {
                var t = MakeTile("Tile" + i, fill, edge, tick);
                t.Icon.texture = Icon(WeaponLoadouts.Catalogue[i].Id);
                _tiles[i] = t;
            }
            _cat.gameObject.SetActive(false);
        }

        private Tile MakeTile(string name, Sprite fill, Sprite edge, Texture2D tick)
        {
            var t = new Tile { Rt = NewRect(name, _cat) };
            t.Back = t.Rt.gameObject.AddComponent<Image>();
            t.Back.sprite = fill;
            t.Back.type = Image.Type.Sliced;
            t.Back.raycastTarget = false;
            var e = NewRect("Edge", t.Rt);
            SetAnchors(e, Vector2.zero, Vector2.one);
            t.Edge = e.gameObject.AddComponent<Image>();
            t.Edge.sprite = edge;
            t.Edge.type = Image.Type.Sliced;
            t.Edge.raycastTarget = false;
            t.Edge.color = new Color(1f, 1f, 1f, 0.18f);
            var ic = NewRect("Icon", t.Rt);
            SetAnchors(ic, new Vector2(0.2f, 0.3f), new Vector2(0.8f, 0.9f));
            t.Icon = ic.gameObject.AddComponent<RawImage>();
            t.Icon.raycastTarget = false;
            t.Name = NewText("Name", t.Rt, "", TMPro.TextAlignmentOptions.Center);
            SetAnchors(t.Name.rectTransform, new Vector2(0f, 0.04f), new Vector2(1f, 0.3f));
            var tk = NewRect("Tick", t.Rt);
            SetAnchors(tk, new Vector2(0.72f, 0.72f), new Vector2(0.95f, 0.95f));
            t.Tick = tk.gameObject.AddComponent<RawImage>();
            t.Tick.texture = tick;
            t.Tick.color = new Color(0.55f, 0.95f, 0.55f, 1f);
            t.Tick.raycastTarget = false;
            tk.gameObject.SetActive(false);
            return t;
        }

        private TMPro.TextMeshProUGUI NewText(string name, Transform parent, string text, TMPro.TextAlignmentOptions align)
        {
            var rt = NewRect(name, parent);
            var tmp = rt.gameObject.AddComponent<TMPro.TextMeshProUGUI>();
            tmp.text = text;
            tmp.alignment = align;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TMPro.TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            _labels.Add(tmp);
            return tmp;
        }

        private static void SetAnchors(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        /// <summary>A white disc, or (not filled) just its rim of the given width; Image.color tints it.</summary>
        private static Texture2D MakeCircle(int size, float outer, float rim, bool filled)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            float c = (size - 1) * 0.5f, aa = 1.5f / c;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float inside = Mathf.Clamp01((outer - d) / aa);
                    float rimA = Mathf.Clamp01((d - (outer - rim)) / aa) * inside;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, filled ? inside : rimA));
                }
            tex.Apply();
            return tex;
        }

        /// <summary>A simple white eye (looking out of the creature's eyes); crossed out for "exit".</summary>
        private static Texture2D MakeEye(int size, bool crossed)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            float c = (size - 1) * 0.5f, px = 1f / c;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x - c) / c, v = (y - c) / c;
                    // Almond outline: |v| = 0.5 (1 - u^2 / 0.85^2)
                    float lid = 0.5f * (1f - u * u / (0.85f * 0.85f));
                    float a = 0f;
                    if (Mathf.Abs(u) < 0.85f) a = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Abs(v) - lid) / (0.07f + px));
                    float r = Mathf.Sqrt(u * u + v * v);
                    a = Mathf.Max(a, Mathf.Clamp01((0.24f - r) / (2f * px)));                                   // pupil
                    a = Mathf.Max(a, Mathf.Clamp01(1f - Mathf.Abs(r - 0.36f) / (0.05f + px)) * (Mathf.Abs(v) < lid ? 1f : 0f));   // iris
                    if (crossed)
                    {
                        float line = Mathf.Abs(u - v) / 1.4142f;
                        if (r < 0.95f) a = Mathf.Max(a, Mathf.Clamp01(1f - line / (0.07f + px)));
                    }
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return tex;
        }

        /// <summary>A simple white open hand (palm, four fingers, thumb to the side).</summary>
        private static Texture2D MakeHand(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            float c = (size - 1) * 0.5f, px = 1.2f / c;
            // Fingers: x, top (pinky to index), all starting at the palm.
            var fingers = new[] { new Vector3(-0.36f, 0.42f, 0.11f), new Vector3(-0.12f, 0.70f, 0.12f), new Vector3(0.12f, 0.78f, 0.12f), new Vector3(0.36f, 0.64f, 0.11f) };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x - c) / c, (y - c) / c);
                    // Palm: a rounded box.
                    Vector2 q = new Vector2(Mathf.Abs(p.x) - 0.36f, Mathf.Abs(p.y + 0.30f) - 0.36f);
                    float d = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - 0.12f;
                    foreach (var f in fingers) d = Mathf.Min(d, Capsule(p, new Vector2(f.x, -0.1f), new Vector2(f.x, f.y), f.z));
                    d = Mathf.Min(d, Capsule(p, new Vector2(-0.42f, -0.42f), new Vector2(-0.78f, -0.02f), 0.12f));   // thumb
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d / px)));
                }
            tex.Apply();
            return tex;
        }

        private static float Capsule(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - r;
        }

        /// <summary>A box centred at c with half sizes h, turned by angle (radians), with rounded corners r.</summary>
        private static float Box(Vector2 p, Vector2 c, Vector2 h, float angle, float r = 0f)
        {
            Vector2 d = p - c;
            float cs = Mathf.Cos(-angle), sn = Mathf.Sin(-angle);
            d = new Vector2(d.x * cs - d.y * sn, d.x * sn + d.y * cs);
            Vector2 q = new Vector2(Mathf.Abs(d.x) - h.x + r, Mathf.Abs(d.y) - h.y + r);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        }

        private static float SmoothMin(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        /// <summary>A white icon from a signed distance (negative inside) over [-1, 1]^2; alpha may be scaled by a second function.</summary>
        private static Texture2D SdfIcon(int size, System.Func<Vector2, float> dist, System.Func<Vector2, float> alpha = null)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            float c = (size - 1) * 0.5f, px = 1.2f / c;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x - c) / c, (y - c) / c);
                    float a = Mathf.Clamp01(0.5f - dist(p) / px);
                    if (alpha != null) a *= alpha(p);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return tex;
        }

        /// <summary>The weapons button: a sword and a magic staff, crossed.</summary>
        private static Texture2D MakeCrossedArms(int size)
        {
            Vector2 sd = new Vector2(1f, 1f).normalized, sn = new Vector2(-sd.y, sd.x);   // sword: bottom left to top right
            Vector2 td = new Vector2(-1f, 1f).normalized;                                    // staff: bottom right to top left
            return SdfIcon(size, p =>
            {
                Vector2 g = -0.42f * sd;   // the sword's guard
                float d = Capsule(p, g, 0.70f * sd, 0.075f);                        // blade
                d = Mathf.Min(d, Capsule(p, 0.66f * sd, 0.86f * sd, 0.02f));        // point
                d = Mathf.Min(d, Capsule(p, g - sn * 0.22f, g + sn * 0.22f, 0.055f)); // guard
                d = Mathf.Min(d, Capsule(p, g, -0.74f * sd, 0.045f));               // grip
                d = Mathf.Min(d, (p + 0.80f * sd).magnitude - 0.075f);              // pommel
                Vector2 top = 0.60f * td;
                d = Mathf.Min(d, Capsule(p, -0.86f * td, top, 0.045f));              // staff
                float orb = (p - 0.76f * td).magnitude;
                d = Mathf.Min(d, Mathf.Abs(orb - 0.15f) - 0.04f);                    // its orb, a ring
                d = Mathf.Min(d, orb - 0.06f);                                       // with a glowing core
                return d;
            });
        }

        private static Texture2D MakeShotgun(int size)
        {
            float a = 0.18f;   // pointing a little up to the left
            Vector2 R(Vector2 v) => new Vector2(v.x * Mathf.Cos(a) - v.y * Mathf.Sin(a), v.x * Mathf.Sin(a) + v.y * Mathf.Cos(a));
            return SdfIcon(size, p =>
            {
                float d = Capsule(p, R(new Vector2(-0.92f, 0.12f)), R(new Vector2(0.30f, 0.12f)), 0.045f);       // barrel
                d = Mathf.Min(d, Capsule(p, R(new Vector2(-0.70f, 0.0f)), R(new Vector2(0.25f, 0.0f)), 0.04f)); // magazine tube
                d = Mathf.Min(d, Box(p, R(new Vector2(-0.38f, 0.02f)), new Vector2(0.17f, 0.075f), a, 0.03f)); // pump
                d = Mathf.Min(d, Box(p, R(new Vector2(0.30f, 0.07f)), new Vector2(0.17f, 0.11f), a, 0.03f));   // receiver
                d = Mathf.Min(d, Capsule(p, R(new Vector2(0.38f, -0.02f)), R(new Vector2(0.50f, -0.36f)), 0.075f)); // grip
                d = Mathf.Min(d, Box(p, R(new Vector2(0.70f, 0.0f)), new Vector2(0.24f, 0.10f), a - 0.22f, 0.04f)); // stock
                return d;
            });
        }

        private static Texture2D MakeSword(int size)
        {
            Vector2 sd = new Vector2(1f, 1f).normalized, sn = new Vector2(-sd.y, sd.x);
            return SdfIcon(size, p =>
            {
                Vector2 g = -0.38f * sd;
                float d = Capsule(p, g, 0.68f * sd, 0.09f);
                d = Mathf.Min(d, Capsule(p, 0.62f * sd, 0.86f * sd, 0.025f));
                d = Mathf.Min(d, Capsule(p, g - sn * 0.28f, g + sn * 0.28f, 0.065f));
                d = Mathf.Min(d, Capsule(p, g, -0.72f * sd, 0.055f));
                d = Mathf.Min(d, (p + 0.80f * sd).magnitude - 0.09f);
                return d;
            });
        }

        /// <summary>A flame: a teardrop curling up to a point, with a hollow heart.</summary>
        private static Texture2D MakeFlame(int size)
        {
            float Drop(Vector2 p, float s)
            {
                p /= s;
                float d = (p - new Vector2(0f, -0.38f)).magnitude - 0.42f;
                d = SmoothMin(d, (p - new Vector2(0.04f, -0.02f)).magnitude - 0.30f, 0.12f);
                d = SmoothMin(d, (p - new Vector2(0.0f, 0.28f)).magnitude - 0.18f, 0.12f);
                d = SmoothMin(d, (p - new Vector2(-0.05f, 0.50f)).magnitude - 0.10f, 0.12f);
                d = SmoothMin(d, Capsule(p, new Vector2(-0.05f, 0.50f), new Vector2(0.03f, 0.70f), 0.03f), 0.08f);   // the point
                return d * s;
            }
            return SdfIcon(size, p =>
            {
                float outer = Drop(p, 1f);
                float inner = Drop((p - new Vector2(0f, -0.30f)) / 0.55f, 1f) * 0.55f;
                return Mathf.Max(outer, -inner);
            });
        }

        /// <summary>The ice power: a six-armed snowflake, each arm with a pair of branches.</summary>
        private static Texture2D MakeSnowflake(int size) =>
            SdfIcon(size, p =>
            {
                float d = float.MaxValue;
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI / 3f + Mathf.PI / 2f;
                    Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    d = Mathf.Min(d, Capsule(p, Vector2.zero, dir * 0.82f, 0.06f));
                    foreach (float side in new[] { -1f, 1f })
                    {
                        float b = a + side * Mathf.PI / 4f;
                        Vector2 from = dir * 0.48f;
                        d = Mathf.Min(d, Capsule(p, from, from + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * 0.26f, 0.05f));
                    }
                }
                return d;
            });

        private static Texture2D MakePlus(int size) =>
            SdfIcon(size, p => Mathf.Min(Capsule(p, new Vector2(-0.62f, 0f), new Vector2(0.62f, 0f), 0.11f), Capsule(p, new Vector2(0f, -0.62f), new Vector2(0f, 0.62f), 0.11f)));

        private static Texture2D MakeTick(int size) =>
            SdfIcon(size, p => Mathf.Min(Capsule(p, new Vector2(-0.6f, 0.02f), new Vector2(-0.18f, -0.42f), 0.14f), Capsule(p, new Vector2(-0.18f, -0.42f), new Vector2(0.62f, 0.5f), 0.14f)));

        /// <summary>Return: an arrow pointing left.</summary>
        private static Texture2D MakeReturnArrow(int size) =>
            SdfIcon(size, p => Mathf.Min(Capsule(p, new Vector2(-0.6f, 0f), new Vector2(0.65f, 0f), 0.12f),
                Mathf.Min(Capsule(p, new Vector2(-0.62f, 0f), new Vector2(-0.12f, 0.48f), 0.12f), Capsule(p, new Vector2(-0.62f, 0f), new Vector2(-0.12f, -0.48f), 0.12f))));

        /// <summary>A white rounded square filling the texture (or only its outline), corner radius in pixels.</summary>
        private static Texture2D MakeRoundedSquare(int size, float radius, bool outline)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float h = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Box(new Vector2(x + 0.5f, y + 0.5f), new Vector2(h, h), new Vector2(h - 0.5f, h - 0.5f), 0f, radius);
                    float a = Mathf.Clamp01(0.5f - d);
                    if (outline) a *= Mathf.Clamp01(d + 2.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return tex;
        }

        public static string Status()
        {
            return $"radialExtra={Showing} own={OwnMenuOpen} hovered={Hovered}({_hoverIndex}) arc={_arcOpen} weaponArc={_weaponArcOpen}[{string.Join(",", _armItems)}] catalogue={_catalogueOpen} hands={(Instance != null && Instance._hands.Shown)} weapons={(Instance != null && Instance._weapons.Shown)} r={_radius:F0} menuCentre={MenuCenter} need={RequiredRadius:F0} catch(known={_catchKnown} rings={_catchRings.Count} options={_catchRects.Count} ring0={(_catchRings.Count > 0 ? _catchRings[0].z : 0f):F0}) inner={_innerRadius:F0} scale={_ringScale:F2} name='{_shownName}'";
        }

        /// <summary>Debug: a button's centre in UI pixels ("main", "hands", "swatch" n, "weapons", "arm" n, "return", "tile" n), to aim a laser at it.</summary>
        public static Vector2 DebugPoint(string what, int n = 0)
        {
            var me = Instance;
            if (me == null) return MenuCenter;
            if (what == "hands") return me._hands.Center;
            if (what == "swatch") return me._swatches[Mathf.Clamp(n, 0, me._swatches.Length - 1)].Center;
            if (what == "weapons") return me._weapons.Center;
            if (what == "arm") return me._arms[Mathf.Clamp(n, 0, me._arms.Length - 1)].Center;
            if (what == "return") return me._catReturn.Rt.anchoredPosition;
            if (what == "tile") return me._tiles[Mathf.Clamp(n, 0, me._tiles.Length - 1)].Rt.anchoredPosition;
            return me._main.Center;
        }

        /// <summary>Debug: the eye / exit button's centre in UI pixels (to aim a laser at it).</summary>
        public static Vector2 DebugCenter => DebugPoint("main");
    }
}
