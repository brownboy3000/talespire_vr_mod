using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace TaleSpireVR
{
    /// <summary>
    /// First person's ruler: a line ruler from where you entered first person to where the creature is now (shown or
    /// hidden with the left stick click), and its distance, always written in blue on the back of your left hand.
    /// The distance is worked out and written exactly like TaleSpire's line ruler (campaign distance units).
    /// </summary>
    internal class FPRuler
    {
        private static readonly Color LineColor = new Color(1f, 1f, 1f, 0.85f);
        private static readonly Color LabelColor = new Color(0.25f, 0.62f, 1f, 1f);

        public bool LineVisible;
        public string Text { get; private set; } = "";

        private GameObject _lineGo, _startDot, _endDot, _labelGo;
        private LineRenderer _line;
        private Text _label;
        private static Material _lineMat;

        private static Material LineMaterial()
        {
            if (_lineMat != null) return _lineMat;
            _lineMat = new Material(Shader.Find("Hidden/Internal-Colored")) { name = "TaleSpireVR_FPRuler", hideFlags = HideFlags.HideAndDontSave };
            _lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _lineMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _lineMat.SetInt("_ZWrite", 0);
            _lineMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            _lineMat.renderQueue = 3100;
            return _lineMat;
        }

        private void Build()
        {
            _lineGo = new GameObject("TaleSpireVR_FPRuler");
            Object.DontDestroyOnLoad(_lineGo);
            _line = _lineGo.AddComponent<LineRenderer>();
            _line.sharedMaterial = LineMaterial();
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.numCapVertices = 4;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.startColor = _line.endColor = LineColor;
            _startDot = Dot("Start");
            _endDot = Dot("End");

            _labelGo = new GameObject("TaleSpireVR_FPRulerLabel", typeof(RectTransform));
            Object.DontDestroyOnLoad(_labelGo);
            var canvas = _labelGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            // Drawn on top of the hand (the back of a hand isn't flat, so part of it sank into the skin); only shown
            // while the back of the hand faces you (see Update).
            _labelMat = new Material(Canvas.GetDefaultCanvasMaterial()) { renderQueue = 4003 };
            _labelMat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            var rt = (RectTransform)_labelGo.transform;
            rt.sizeDelta = new Vector2(DialPixels, DialPixels);

            // A dotted ring (the tape spool) around the distance; it turns as the distance changes.
            var dialGo = new GameObject("Dial", typeof(RectTransform));
            dialGo.transform.SetParent(_labelGo.transform, false);
            _dial = (RectTransform)dialGo.transform;
            _dial.anchorMin = Vector2.zero;
            _dial.anchorMax = Vector2.one;
            _dial.offsetMin = _dial.offsetMax = Vector2.zero;
            var dial = dialGo.AddComponent<RawImage>();
            dial.texture = MakeDottedRing(256, 28);
            dial.color = LabelColor;
            dial.raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(_labelGo.transform, false);
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(40f, 40f);
            trt.offsetMax = new Vector2(-40f, -40f);
            _label = textGo.AddComponent<Text>();
            _label.font = HotkeyStrip.LoadFont();
            _label.fontSize = 84;
            _label.fontStyle = FontStyle.Bold;
            _label.alignment = TextAnchor.MiddleCenter;
            _label.color = LabelColor;
            _label.supportRichText = true;
            _label.lineSpacing = 0.85f;
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _label.raycastTarget = false;
            var outline = textGo.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0.05f, 0.15f, 0.9f);
            outline.effectDistance = new Vector2(3f, -3f);
            dial.material = _labelMat;
            _label.material = _labelMat;
            VRLayers.SetRecursive(_labelGo, VRLayers.Overlay);
            _labelGo.SetActive(false);
        }

        private Material _labelMat;

        private const float DialPixels = 360f;
        private RectTransform _dial;
        private float _dialAngle, _lastDistance = -1f;

        /// <summary>A ring of round dots (white; tinted by the image colour).</summary>
        private static Texture2D MakeDottedRing(int size, int dots)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "TaleSpireVR_TapeDial" };
            float c = (size - 1) * 0.5f, ringR = c * 0.86f, dotR = c * 0.075f;
            var centres = new Vector2[dots];
            for (int i = 0; i < dots; i++)
            {
                float a = i / (float)dots * Mathf.PI * 2f;
                centres[i] = new Vector2(c + Mathf.Cos(a) * ringR, c + Mathf.Sin(a) * ringR);
            }
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float best = float.MaxValue;
                    foreach (var p in centres) best = Mathf.Min(best, (new Vector2(x, y) - p).magnitude);
                    // One dot a little bigger, so the turning is easy to see.
                    float bigR = (new Vector2(x, y) - centres[0]).magnitude <= dotR * 1.6f ? dotR * 1.6f : dotR;
                    float a = Mathf.Clamp01(bigR + 0.75f - best);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return tex;
        }

        /// <summary>The hand's distance text: the number, and the campaign's unit under it.</summary>
        private static string DialText(float units)
        {
            string name = "units";
            float per = 5f;
            try
            {
                var u = CampaignSessionManager.DistanceUnits[0];
                name = u.Name;
                per = u.NumberPerTile;
            }
            catch { }
            return (units * per).ToString("0.0", CultureInfo.CurrentCulture) + "\n<size=50>" + name + "</size>";
        }

        private GameObject Dot(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(_lineGo.transform, false);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = LineMaterial();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        /// <summary>TaleSpire's line ruler text for a distance in board units ("12.5 ft").</summary>
        public static string Format(float units)
        {
            try
            {
                var u = CampaignSessionManager.DistanceUnits[0];
                return (units * u.NumberPerTile).ToString(".0 " + u.Name, CultureInfo.CurrentCulture);
            }
            catch { return (units * 5f).ToString(".0 ft", CultureInfo.CurrentCulture); }
        }

        /// <summary>Per frame while in first person (after the hands are posed). size: the creature's height.</summary>
        public void Update(Vector3 start, Vector3 end, float size, FPHands hands)
        {
            if (_lineGo == null) Build();
            float distance = Vector3.Distance(start, end);
            Text = Format(distance);
            // The dial turns like a tape measure's spool: clockwise (as you look at it) while the distance grows, back
            // the other way while it shrinks.
            if (_lastDistance < 0f) _lastDistance = distance;
            _dialAngle -= (distance - _lastDistance) * 240f;
            _lastDistance = distance;

            if (_lineGo.activeSelf != LineVisible) _lineGo.SetActive(LineVisible);
            if (LineVisible)
            {
                // Sized for the creature (a human mini is about 1.15 units tall): thin enough not to fill a small one's view.
                float k = Mathf.Clamp(size / 1.15f, 0.3f, 4f);
                Vector3 lift = Vector3.up * 0.03f * k;
                _line.SetPosition(0, start + lift);
                _line.SetPosition(1, end + lift);
                _line.startWidth = _line.endWidth = 0.035f * k;
                _startDot.transform.position = start + lift;
                _endDot.transform.position = end + lift;
                _startDot.transform.localScale = Vector3.one * 0.09f * k;
                _endDot.transform.localScale = Vector3.one * 0.05f * k;
            }

            // On the back of the left hand, reading along the fingers (like a watch face when you raise your hand).
            Vector3 pos = Vector3.zero, normal = Vector3.up, fingers = Vector3.forward;
            float length = 0f;
            bool label = hands != null && hands.TryGetBackOfHand(Hand.Left, out pos, out normal, out fingers, out length);
            // Only while you can see the back of your hand (it is drawn over the hand, so it would show through the palm).
            var eye = VRManager.Instance != null && VRManager.Instance.Rig != null ? VRManager.Instance.Rig.Head : null;
            if (label && eye != null && Vector3.Dot(normal, (eye.position - pos).normalized) < 0.15f) label = false;
            if (_labelGo.activeSelf != label) _labelGo.SetActive(label);
            if (!label) return;
            _label.text = DialText(distance);
            _dial.localRotation = Quaternion.Euler(0f, 0f, _dialAngle);
            Vector3 right = Vector3.ProjectOnPlane(fingers, normal).normalized;
            Vector3 fwd = -normal;   // a world-space canvas is read looking along its forward
            _labelGo.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, Vector3.Cross(fwd, right)));
            // A dial about 6 cm across on a typical hand (scaled with the hand).
            float pxToWorld = length * (0.06f / 0.09f) / DialPixels;
            _labelGo.transform.localScale = Vector3.one * pxToWorld;
        }

        /// <summary>A new visit: the dial starts from zero.</summary>
        public void Reset()
        {
            _lastDistance = -1f;
            _dialAngle = 0f;
        }

        public void Hide()
        {
            if (_lineGo != null) _lineGo.SetActive(false);
            if (_labelGo != null) _labelGo.SetActive(false);
        }

        public void Destroy()
        {
            if (_lineGo != null) Object.Destroy(_lineGo);
            if (_labelGo != null) Object.Destroy(_labelGo);
        }
    }
}
