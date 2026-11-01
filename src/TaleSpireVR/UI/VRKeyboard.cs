using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TaleSpireVR
{
    /// <summary>
    /// An on-screen keyboard under the menu panel, shown while one of TaleSpire's text fields is selected (search
    /// boxes, names, chat). Point at a key and pull the trigger. TaleSpire's text fields read real OS keystrokes, which
    /// a virtual keyboard device cannot produce, so the keys edit the selected field directly. It disappears when you
    /// click anything that is not a text field, press Done / Enter, or close the menu panel.
    /// </summary>
    internal class VRKeyboard
    {
        private class Key
        {
            public string Lower, Upper;     // characters typed (null for special keys)
            public Action Special;
            public string Label;
            public float X, W;              // in key units, left edge from the row start
            public int Row;
            public Image Bg;
            public Text Txt;
        }

        public readonly GameObject Go;
        public readonly Transform T;
        private readonly List<Key> _keys = new List<Key>();
        private bool _shift;

        private const float Unit = 100f;
        private const float RowsWidth = 11.5f;   // key units per row
        private const int Rows = 5;

        public bool Visible
        {
            get => Go.activeSelf;
            set { if (Go.activeSelf != value) Go.SetActive(value); }
        }

        public VRKeyboard()
        {
            AddRow(0, "1234567890", null, Special("Back", Backspace, 1.5f));
            AddRow(1, "qwertyuiop-", null, null);
            AddRow(2, "asdfghjkl'", null, Special("Enter", Enter, 1.5f));
            AddRow(3, "zxcvbnm,.?", Special("Shift", () => { _shift = !_shift; UpdateLabels(); }, 1.5f), null);
            AddSpecialRow(4, Special("Clear", Clear, 2f), Special("Space", () => TypeText(" "), 6f), Special("Done", Done, 2f));

            Go = new GameObject("TaleSpireVR_Keyboard", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(Go);
            var canvas = Go.AddComponent<Canvas>();
            T = Go.transform;
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 32001;
            var rt = (RectTransform)T;
            rt.sizeDelta = new Vector2(RowsWidth * Unit, Rows * Unit);

            var mat = new Material(Canvas.GetDefaultCanvasMaterial()) { renderQueue = 4002 };
            mat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            var font = HotkeyStrip.LoadFont();

            // Backing plate.
            var plate = new GameObject("Plate", typeof(RectTransform));
            plate.transform.SetParent(T, false);
            var prt = (RectTransform)plate.transform;
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(-10f, -10f);
            prt.offsetMax = new Vector2(10f, 10f);
            var pimg = plate.AddComponent<Image>();
            pimg.material = mat;
            pimg.color = new Color(0.02f, 0.02f, 0.03f, 0.8f);

            foreach (var k in _keys)
            {
                var cell = new GameObject("Key " + k.Label, typeof(RectTransform));
                cell.transform.SetParent(T, false);
                var crt = (RectTransform)cell.transform;
                crt.anchorMin = crt.anchorMax = new Vector2(0f, 1f);
                crt.pivot = new Vector2(0f, 1f);
                crt.anchoredPosition = new Vector2(k.X * Unit + 4f, -k.Row * Unit - 4f);
                crt.sizeDelta = new Vector2(k.W * Unit - 8f, Unit - 8f);
                k.Bg = cell.AddComponent<Image>();
                k.Bg.material = mat;

                var txtGo = new GameObject("Label", typeof(RectTransform));
                txtGo.transform.SetParent(cell.transform, false);
                var trt = (RectTransform)txtGo.transform;
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = Vector2.zero;
                trt.offsetMax = Vector2.zero;
                k.Txt = txtGo.AddComponent<Text>();
                k.Txt.material = mat;
                k.Txt.font = font;
                k.Txt.fontSize = k.Lower != null ? 44 : 30;
                k.Txt.alignment = TextAnchor.MiddleCenter;
                k.Txt.color = new Color(1f, 0.85f, 0.55f, 1f);
            }
            UpdateLabels();
            SetHover(-1, -1);
            VRLayers.SetRecursive(Go, VRLayers.Overlay);
            Visible = false;
        }

        private static Key Special(string label, Action action, float width) => new Key { Label = label, Special = action, W = width };

        private void AddRow(int row, string chars, Key before, Key after)
        {
            float width = chars.Length + (before?.W ?? 0f) + (after?.W ?? 0f);
            float x = (RowsWidth - width) * 0.5f;
            if (before != null) { before.Row = row; before.X = x; x += before.W; _keys.Add(before); }
            foreach (char c in chars)
            {
                string s = c.ToString();
                _keys.Add(new Key { Lower = s, Upper = UpperOf(c), Label = s, Row = row, X = x, W = 1f });
                x += 1f;
            }
            if (after != null) { after.Row = row; after.X = x; _keys.Add(after); }
        }

        private void AddSpecialRow(int row, params Key[] keys)
        {
            float width = 0f;
            foreach (var k in keys) width += k.W;
            float x = (RowsWidth - width) * 0.5f;
            foreach (var k in keys) { k.Row = row; k.X = x; x += k.W; _keys.Add(k); }
        }

        private static string UpperOf(char c)
        {
            switch (c)
            {
                case '1': return "!"; case '2': return "@"; case '3': return "#"; case '4': return "$"; case '5': return "%";
                case '6': return "^"; case '7': return "&"; case '8': return "*"; case '9': return "("; case '0': return ")";
                case '-': return "_"; case '\'': return "\""; case ',': return ";"; case '.': return ":"; case '?': return "/";
                default: return char.ToUpperInvariant(c).ToString();
            }
        }

        private void UpdateLabels()
        {
            foreach (var k in _keys)
            {
                if (k.Txt == null) continue;
                k.Txt.text = k.Lower != null ? (_shift ? k.Upper : k.Lower) : k.Label;
            }
        }

        /// <summary>Place the keyboard under a point (world), facing like the panel, tilted toward you a little.</summary>
        public void PlaceBelow(Vector3 topCenter, Quaternion panelRotation, float worldWidth)
        {
            var rt = (RectTransform)T;
            float s = worldWidth / rt.sizeDelta.x;
            float height = rt.sizeDelta.y * s;
            Quaternion rot = panelRotation * Quaternion.Euler(25f, 0f, 0f);   // top edge away, like a desk keyboard
            T.rotation = rot;
            T.position = topCenter - (rot * Vector3.up) * (height * 0.5f);
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
            // Local point relative to the top-left corner, in key units.
            float ux = (p.x + size.x * 0.5f) / Unit;
            float uy = (size.y * 0.5f - p.y) / Unit;
            if (ux < -0.1f || ux > RowsWidth + 0.1f || uy < -0.1f || uy > Rows + 0.1f) return false;
            distance = Vector3.Distance(ray.origin, T.TransformPoint(p));
            int row = Mathf.FloorToInt(uy);
            for (int i = 0; i < _keys.Count; i++)
            {
                var k = _keys[i];
                if (k.Row == row && ux >= k.X && ux < k.X + k.W) { index = i; break; }
            }
            return true;   // on the keyboard (maybe between keys): it still blocks the panel/board behind
        }

        /// <summary>Debug: press the keys that type this text ('~' = Back, '#' = Enter).</summary>
        public void DebugType(string text)
        {
            foreach (char c in text)
            {
                string label = c == '~' ? "Back" : c == '#' ? "Enter" : c == '_' ? "Space" : null;
                int idx = _keys.FindIndex(k => label != null ? k.Label == label : k.Lower == c.ToString());
                if (idx >= 0) Press(idx, 1);
            }
        }

        /// <summary>Debug: world position of a key's centre (by its label, e.g. "a", "Enter").</summary>
        public bool TryGetKeyCenter(string label, out Vector3 world)
        {
            world = Vector3.zero;
            var k = _keys.Find(x => x.Label == label || x.Lower == label);
            if (k == null) return false;
            var size = ((RectTransform)T).sizeDelta;
            var local = new Vector3((k.X + k.W * 0.5f) * Unit - size.x * 0.5f, size.y * 0.5f - (k.Row + 0.5f) * Unit, 0f);
            world = T.TransformPoint(local);
            return true;
        }

        /// <summary>Light up the key under each hand's laser (-1 = none), so you can type with both hands.</summary>
        public void SetHover(int leftIndex, int rightIndex)
        {
            for (int i = 0; i < _keys.Count; i++)
            {
                var k = _keys[i];
                bool on = k.Label == "Shift" && _shift;
                var c = on ? new Color(0.55f, 0.3f, 0.05f, 0.95f) : new Color(0.1f, 0.1f, 0.13f, 0.95f);
                if (i == leftIndex || i == rightIndex) c = Color.Lerp(c, new Color(0.3f, 0.6f, 0.9f, 0.95f), 0.6f);
                k.Bg.color = c;
            }
        }

        // Back repeats while a trigger stays held on it (tracked per hand).
        private readonly int[] _held = { -1, -1 };
        private readonly float[] _repeatAt = new float[2];

        /// <summary>A hand's trigger went down on a key (hand: 0 = left, 1 = right).</summary>
        public void Press(int index, int hand)
        {
            if (index < 0 || index >= _keys.Count) return;
            _held[hand] = index;
            _repeatAt[hand] = Time.unscaledTime + 0.5f;
            Activate(_keys[index]);
        }

        public void Hold(int hand, int index, bool triggerHeld)
        {
            if (!triggerHeld || index < 0 || index != _held[hand]) { _held[hand] = -1; return; }
            if (_keys[index].Label != "Back" || Time.unscaledTime < _repeatAt[hand]) return;
            _repeatAt[hand] = Time.unscaledTime + 0.07f;
            Activate(_keys[index]);
        }

        private void Activate(Key k)
        {
            try
            {
                if (k.Special != null) k.Special();
                else
                {
                    TypeText(_shift ? k.Upper : k.Lower);
                    if (_shift) { _shift = false; UpdateLabels(); }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Keyboard key failed: " + e.Message); }
        }

        // ---- Editing the selected text field -------------------------------------------------------------------

        public static bool TryGetField(out TMPro.TMP_InputField tmp, out InputField legacy)
        {
            tmp = null;
            legacy = null;
            var es = EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            if (go == null || !go.activeInHierarchy) return false;
            tmp = go.GetComponent<TMPro.TMP_InputField>();
            if (tmp != null && tmp.interactable && !tmp.readOnly) return true;
            tmp = null;
            legacy = go.GetComponent<InputField>();
            if (legacy != null && legacy.interactable && !legacy.readOnly) return true;
            legacy = null;
            return false;
        }

        private static bool Accepts(bool integerOnly, bool decimalOnly, string s)
        {
            if (!integerOnly && !decimalOnly) return true;
            foreach (char c in s)
                if (!char.IsDigit(c) && c != '-' && !(decimalOnly && (c == '.' || c == ','))) return false;
            return true;
        }

        private static void TypeText(string s)
        {
            if (string.IsNullOrEmpty(s) || !TryGetField(out var tmp, out var legacy)) return;
            if (tmp != null)
            {
                if (!Accepts(tmp.contentType == TMPro.TMP_InputField.ContentType.IntegerNumber,
                        tmp.contentType == TMPro.TMP_InputField.ContentType.DecimalNumber, s)) return;
                string t = tmp.text ?? "";
                int a = Mathf.Clamp(Mathf.Min(tmp.selectionStringAnchorPosition, tmp.selectionStringFocusPosition), 0, t.Length);
                int b = Mathf.Clamp(Mathf.Max(tmp.selectionStringAnchorPosition, tmp.selectionStringFocusPosition), 0, t.Length);
                t = t.Remove(a, b - a);
                if (tmp.characterLimit > 0 && t.Length + s.Length > tmp.characterLimit) return;
                tmp.text = t.Insert(a, s);
                SetTmpCaret(tmp, a + s.Length);
            }
            else
            {
                if (!Accepts(legacy.contentType == InputField.ContentType.IntegerNumber,
                        legacy.contentType == InputField.ContentType.DecimalNumber, s)) return;
                string t = legacy.text ?? "";
                int a = Mathf.Clamp(Mathf.Min(legacy.selectionAnchorPosition, legacy.selectionFocusPosition), 0, t.Length);
                int b = Mathf.Clamp(Mathf.Max(legacy.selectionAnchorPosition, legacy.selectionFocusPosition), 0, t.Length);
                t = t.Remove(a, b - a);
                if (legacy.characterLimit > 0 && t.Length + s.Length > legacy.characterLimit) return;
                legacy.text = t.Insert(a, s);
                legacy.caretPosition = a + s.Length;
            }
        }

        private static void SetTmpCaret(TMPro.TMP_InputField tmp, int pos)
        {
            tmp.selectionStringAnchorPosition = pos;
            tmp.selectionStringFocusPosition = pos;
            tmp.stringPosition = pos;
        }

        private static void Backspace()
        {
            if (!TryGetField(out var tmp, out var legacy)) return;
            if (tmp != null)
            {
                string t = tmp.text ?? "";
                int a = Mathf.Clamp(Mathf.Min(tmp.selectionStringAnchorPosition, tmp.selectionStringFocusPosition), 0, t.Length);
                int b = Mathf.Clamp(Mathf.Max(tmp.selectionStringAnchorPosition, tmp.selectionStringFocusPosition), 0, t.Length);
                if (b > a) { tmp.text = t.Remove(a, b - a); SetTmpCaret(tmp, a); }
                else if (a > 0) { tmp.text = t.Remove(a - 1, 1); SetTmpCaret(tmp, a - 1); }
            }
            else
            {
                string t = legacy.text ?? "";
                int a = Mathf.Clamp(Mathf.Min(legacy.selectionAnchorPosition, legacy.selectionFocusPosition), 0, t.Length);
                int b = Mathf.Clamp(Mathf.Max(legacy.selectionAnchorPosition, legacy.selectionFocusPosition), 0, t.Length);
                if (b > a) { legacy.text = t.Remove(a, b - a); legacy.caretPosition = a; }
                else if (a > 0) { legacy.text = t.Remove(a - 1, 1); legacy.caretPosition = a - 1; }
            }
        }

        private static void Clear()
        {
            if (!TryGetField(out var tmp, out var legacy)) return;
            if (tmp != null) { tmp.text = ""; SetTmpCaret(tmp, 0); }
            else { legacy.text = ""; legacy.caretPosition = 0; }
        }

        private static readonly System.Reflection.FieldInfo SearchFieldField = HarmonyLib.AccessTools.Field(typeof(SearchInputHandler), "_assetSearchField");
        private static int _doneAtFrame = -1;

        private static void Enter()
        {
            if (!TryGetField(out var tmp, out _)) return;
            var search = tmp != null ? FindSearchHandler(tmp) : null;
            if (search != null)
            {
                // Creature / slab / asset search boxes only search when TaleSpire's Accept input (the real Enter key)
                // fires as the box closes; start the search directly.
                search.SendSearchParameters();
                Plugin.Log.LogInfo("[keyboard] Enter: search sent");
                Done();
                return;
            }
            if (tmp != null) tmp.onSubmit?.Invoke(tmp.text);
            // Other boxes (dialogs) may react to the Enter key itself: press it, and close the box next frame while
            // TaleSpire still sees Enter as pressed.
            VirtualInput.Tap(UnityEngine.InputSystem.Key.Enter);
            _doneAtFrame = Time.frameCount + 1;
        }

        /// <summary>Call once per frame (finishes an Enter press started last frame).</summary>
        public static void Tick()
        {
            if (_doneAtFrame < 0 || Time.frameCount < _doneAtFrame) return;
            _doneAtFrame = -1;
            Done();
        }

        private static SearchInputHandler FindSearchHandler(TMPro.TMP_InputField field)
        {
            if (SearchFieldField == null) return null;
            var parent = field.GetComponentInParent<SearchInputHandler>();
            if (parent != null && ReferenceEquals(SearchFieldField.GetValue(parent), field)) return parent;
            foreach (var s in UnityEngine.Object.FindObjectsOfType<SearchInputHandler>())
                if (ReferenceEquals(SearchFieldField.GetValue(s), field)) return s;
            return null;
        }

        /// <summary>Finish typing: deselect the field (its normal end-of-edit handling runs) and hide the keyboard.</summary>
        public static void Done()
        {
            var es = EventSystem.current;
            if (es != null && TryGetField(out _, out _)) es.SetSelectedGameObject(null);
        }
    }
}
