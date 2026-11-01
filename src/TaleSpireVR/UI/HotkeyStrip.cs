using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TaleSpireVR
{
    /// <summary>
    /// A row of big VR buttons under the menu panel for keyboard shortcuts that have no controller button.
    /// </summary>
    internal class HotkeyStrip
    {
        private class Entry
        {
            public string Label;
            public Action Press;
            public Func<bool> IsOn;
            /// <summary>Usable now (else greyed out and a press does nothing); null = always.</summary>
            public Func<bool> Enabled;
            public Image Bg;
            public Text Text;
        }

        public readonly GameObject Go;
        public readonly Transform T;
        private readonly Entry[] _entries;
        private int _hover = -1;
        private static bool _shiftLatched;

        public static bool ShiftLatched => _shiftLatched;

        /// <summary>
        /// TaleSpire's grid is the build-plane grid, only drawn in Build Mode (its G key binding is no longer used).
        /// Toggle it through the build tools panel so that panel's own toggle stays in sync; enter Build Mode if needed
        /// (BuildModeSwitch, at once - not TaleSpire's B key, which may be ignored).
        /// </summary>
        private static void ToggleBuildGrid()
        {
            var plane = BoardToolManager.BuildPlane;
            if (plane == null) return;
            bool on = !(BoardBuilder.InBuilderMode && plane.IsInUse);
            if (on && !BoardBuilder.InBuilderMode) BuildModeSwitch.Set(true, "Build Grid button");
            if (on && !BoardBuilder.InBuilderMode) return;   // (could not: dice in hand, not the GM...)
            var panel = UnityEngine.Object.FindObjectOfType<UIPanel_buildTools>(true);
            if (panel != null) panel.EnableBuildPlane(on);
            else plane.EnableBuildPlane(on);
            Plugin.Log.LogInfo("Build grid " + (on ? "on" : "off"));
        }

        private static readonly System.Reflection.FieldInfo CutBoxField = HarmonyLib.AccessTools.Field(typeof(CameraController), "cutBox");

        private static bool CutBoxActive()
        {
            if (!Bounce.Singletons.SimpleSingletonBehaviour<CameraController>.HasInstance || CutBoxField == null) return false;
            var box = CutBoxField.GetValue(Bounce.Singletons.SimpleSingletonBehaviour<CameraController>.Instance);
            var p = box?.GetType().GetProperty("Active");
            return p != null && (bool)p.GetValue(box);
        }

        private static void ToggleCutBox()
        {
            if (!Bounce.Singletons.SimpleSingletonBehaviour<CameraController>.HasInstance) return;
            Bounce.Singletons.SimpleSingletonBehaviour<CameraController>.Instance.ToggleCutBox(!CutBoxActive());
        }

        public bool Visible
        {
            get => Go.activeSelf;
            set { if (Go.activeSelf != value) Go.SetActive(value); }
        }

        public HotkeyStrip()
        {
            _entries = new[]
            {
                new Entry { Label = "Menus\n(Space)", Press = () => VirtualInput.Tap(Key.Space) },
                new Entry { Label = "Build\nMode (B)", Press = () => BuildModeSwitch.Toggle("Build Mode button"), IsOn = () => BoardBuilder.InBuilderMode, Enabled = () => Roles.IsGm },
                new Entry { Label = "Undo", Press = () => VirtualInput.Chord(Key.LeftCtrl, Key.Z) },
                new Entry { Label = "Redo", Press = () => VirtualInput.Chord(Key.LeftCtrl, Key.Y) },
                new Entry { Label = "Copy", Press = () => VirtualInput.Chord(Key.LeftCtrl, Key.C) },
                new Entry { Label = "Paste", Press = () => VirtualInput.Chord(Key.LeftCtrl, Key.V) },
                new Entry { Label = "Delete", Press = () => VirtualInput.Tap(Key.Delete) },
                new Entry { Label = "Shift\n(hold)", Press = () => _shiftLatched = !_shiftLatched, IsOn = () => _shiftLatched },
                new Entry { Label = "Build\nGrid", Press = ToggleBuildGrid, IsOn = () => BoardBuilder.InBuilderMode && BoardToolManager.BuildPlane != null && BoardToolManager.BuildPlane.IsInUse, Enabled = () => Roles.IsGm },
                new Entry { Label = "Cut Box\n(N)", Press = ToggleCutBox, IsOn = CutBoxActive },
                new Entry { Label = "Esc", Press = () => VirtualInput.Tap(Key.Escape) },
                new Entry { Label = "Reset\nView", Press = () => VRManager.Instance.Rig.ResetBoardView() },
            };

            Go = new GameObject("TaleSpireVR_HotkeyStrip", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(Go);
            var canvas = Go.AddComponent<Canvas>();
            T = Go.transform;
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 32000;
            var rt = (RectTransform)T;
            int n = _entries.Length;
            rt.sizeDelta = new Vector2(n * 100f, 100f);

            var mat = new Material(Canvas.GetDefaultCanvasMaterial()) { renderQueue = 4002 };
            mat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            var font = LoadFont();

            for (int i = 0; i < n; i++)
            {
                var cell = new GameObject("Key" + i, typeof(RectTransform));
                cell.transform.SetParent(T, false);
                var crt = (RectTransform)cell.transform;
                crt.anchorMin = new Vector2((float)i / n, 0f);
                crt.anchorMax = new Vector2((float)(i + 1) / n, 1f);
                crt.offsetMin = new Vector2(4f, 4f);
                crt.offsetMax = new Vector2(-4f, -4f);
                var bg = cell.AddComponent<Image>();
                bg.material = mat;
                bg.color = new Color(0.08f, 0.08f, 0.1f, 0.85f);
                _entries[i].Bg = bg;

                var txtGo = new GameObject("Label", typeof(RectTransform));
                txtGo.transform.SetParent(cell.transform, false);
                var trt = (RectTransform)txtGo.transform;
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = Vector2.zero;
                trt.offsetMax = Vector2.zero;
                var txt = txtGo.AddComponent<Text>();
                txt.material = mat;
                txt.font = font;
                txt.fontSize = 22;
                txt.alignment = TextAnchor.MiddleCenter;
                txt.color = new Color(1f, 0.85f, 0.55f, 1f);
                txt.text = _entries[i].Label;
                _entries[i].Text = txt;
            }
            VRLayers.SetRecursive(Go, VRLayers.Overlay);
            Visible = false;
        }

        internal static Font LoadFont()
        {
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (f == null) try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 22);
            return f;
        }

        /// <summary>Place the strip just under a panel (world pose of the panel, its world width/height).</summary>
        public void PlaceUnder(Transform panel, float panelWorldWidth, float panelWorldHeight)
        {
            var rt = (RectTransform)T;
            float width = panelWorldWidth;
            float s = width / rt.sizeDelta.x;
            float height = rt.sizeDelta.y * s;
            T.position = panel.position - panel.up * (panelWorldHeight * 0.5f + height * 0.65f);
            T.rotation = panel.rotation;
            T.localScale = new Vector3(s, s, s);
        }

        public bool Raycast(Ray ray, out float distance, out int index)
        {
            distance = 0f;
            index = -1;
            if (!Visible) return false;
            var rt = (RectTransform)T;
            Vector3 o = T.InverseTransformPoint(ray.origin);
            Vector3 d = T.InverseTransformVector(ray.direction);
            if (Mathf.Abs(d.z) < 1e-6f) return false;
            float t = -o.z / d.z;
            if (t <= 0f) return false;
            Vector3 p = o + d * t;
            Vector2 size = rt.sizeDelta;
            float u = (p.x + size.x * 0.5f) / size.x;
            float v = (p.y + size.y * 0.5f) / size.y;
            if (u < 0f || u > 1f || v < 0f || v > 1f) return false;
            index = Mathf.Clamp((int)(u * _entries.Length), 0, _entries.Length - 1);
            distance = Vector3.Distance(ray.origin, T.TransformPoint(p));
            return true;
        }

        public void SetHover(int index)
        {
            _hover = index;
            for (int i = 0; i < _entries.Length; i++)
            {
                // (Build Mode and the Build Grid are for Game Masters: greyed out while you play as a player.)
                bool enabled = IsEnabled(i);
                bool on = enabled && _entries[i].IsOn != null && _entries[i].IsOn();
                var c = on ? new Color(0.55f, 0.3f, 0.05f, 0.9f) : new Color(0.08f, 0.08f, 0.1f, enabled ? 0.85f : 0.55f);
                if (i == _hover && enabled) c = Color.Lerp(c, new Color(0.3f, 0.6f, 0.9f, 0.95f), 0.6f);
                _entries[i].Bg.color = c;
                if (_entries[i].Text != null) _entries[i].Text.color = enabled ? new Color(1f, 0.85f, 0.55f, 1f) : new Color(0.5f, 0.5f, 0.5f, 0.6f);
            }
        }

        private bool IsEnabled(int i)
        {
            try { return _entries[i].Enabled == null || _entries[i].Enabled(); }
            catch { return true; }
        }

        public void Press(int index)
        {
            if (index < 0 || index >= _entries.Length) return;
            if (!IsEnabled(index)) { Plugin.Log.LogInfo("Hotkey greyed out (Game Masters only): " + _entries[index].Label.Replace("\n", " ")); return; }
            try { _entries[index].Press(); }
            catch (Exception e) { Plugin.Log.LogWarning("Hotkey failed: " + e.Message); }
            Plugin.Log.LogInfo("Hotkey pressed: " + _entries[index].Label.Replace("\n", " "));
        }
    }
}
