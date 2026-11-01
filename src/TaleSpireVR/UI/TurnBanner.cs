using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// A line of text in TaleSpire's own font, floating in the sky (overlay layer, so the stream view shows it too).
    /// </summary>
    internal sealed class SkyText
    {
        public readonly GameObject Go;
        private readonly TMPro.TextMeshProUGUI _text;
        private readonly RectTransform _rt;
        private bool _fontSet;

        public SkyText(string name, float fontSize = 64f)
        {
            Go = new GameObject(name, typeof(RectTransform));
            Object.DontDestroyOnLoad(Go);
            var canvas = Go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 32001;
            _rt = (RectTransform)Go.transform;
            _rt.sizeDelta = new Vector2(1600f, 140f);
            var t = new GameObject("Text", typeof(RectTransform));
            t.transform.SetParent(Go.transform, false);
            var trt = (RectTransform)t.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            _text = t.AddComponent<TMPro.TextMeshProUGUI>();
            _text.alignment = TMPro.TextAlignmentOptions.Center;
            _text.fontSize = fontSize;
            _text.color = new Color(1f, 0.92f, 0.75f, 1f);
            _text.outlineWidth = 0.2f;
            _text.outlineColor = new Color32(0, 0, 0, 220);
            _text.raycastTarget = false;
            VRLayers.SetRecursive(Go, VRLayers.Overlay);
            Go.SetActive(false);
        }

        public bool Visible
        {
            get => Go.activeSelf;
            set { if (Go.activeSelf != value) Go.SetActive(value); }
        }

        public string Text
        {
            set
            {
                if (!_fontSet && TaleSpireFont.TryGet(out var font))
                {
                    _text.font = font;
                    _fontSet = true;
                }
                if (_text.text != value) _text.text = value;
            }
        }

        /// <summary>Centre at a world point, facing along forward; worldHeight = the line's height in the world.</summary>
        public void Place(Vector3 center, Quaternion rotation, float worldHeight)
        {
            float s = worldHeight / _rt.sizeDelta.y;
            Go.transform.SetPositionAndRotation(center, rotation);
            Go.transform.localScale = new Vector3(s, s, s);
        }
    }

    /// <summary>TaleSpire's menu font (taken from its UI once it is loaded), for our own texts.</summary>
    internal static class TaleSpireFont
    {
        private static TMPro.TMP_FontAsset _font;

        public static bool TryGet(out TMPro.TMP_FontAsset font)
        {
            if (_font == null)
            {
                foreach (var t in Object.FindObjectsOfType<TMPro.TextMeshProUGUI>())
                {
                    if (t == null || t.font == null || t.gameObject.name.StartsWith("TaleSpireVR")) continue;
                    if (t.transform.root.name != "GUIManager" && t.transform.root.name != "UI") continue;
                    _font = t.font;
                    break;
                }
            }
            font = _font;
            return font != null;
        }
    }

    /// <summary>
    /// Turn-based mode in VR: whenever the turn order changes (a new turn, a new lineup), TaleSpire's own initiative bar
    /// (the portraits at the top of its screen - what every player sees, decided and sent by TaleSpire) also floats in
    /// the sky above where the dice results show, for a few seconds, with whose turn it is written above it ("Your turn"
    /// when it is one of your creatures'). Only how it is shown is ours. Note() puts a short message in the same place
    /// (e.g. "Not Goblin's turn").
    /// </summary>
    [DefaultExecutionOrder(30016)]
    internal class TurnBanner : MonoBehaviour
    {
        public const float ShowSeconds = 9f, Elevation = 36f, Distance = 1.8f, MetersPerPixel = 0.9f / 540f, MaxWidth = 1.5f;

        private static TurnBanner _instance;
        private UIQuad _quad;
        private SkyText _title;
        private float _until, _noteUntil, _yaw;
        private string _note;
        private bool _shown;
        private QueueElement _lastTurn;
        private int _lastQueueHash;
        private UI_InitativeManager _ui;
        private static readonly FieldInfo NormalPanelField = AccessTools.Field(typeof(UI_InitativeManager), "_normalPanel");

        /// <summary>A short message in the sky (a few seconds).</summary>
        public static void Note(string text)
        {
            if (_instance == null) return;
            _instance._note = text;
            _instance._noteUntil = Time.unscaledTime + 3f;
        }

        private void Awake()
        {
            _instance = this;
            _quad = new UIQuad("TaleSpireVR_TurnBanner", 3998);
            _title = new SkyText("TaleSpireVR_TurnTitle");
        }

        private void OnDisable()
        {
            if (_quad != null) _quad.Visible = false;
            if (_title != null) _title.Visible = false;
        }

        private void OnDestroy()
        {
            if (_quad != null) Destroy(_quad.Go);
            if (_title != null) Destroy(_title.Go);
        }

        private void LateUpdate()
        {
            var rig = VRManager.Instance != null ? VRManager.Instance.Rig : null;
            bool onBoard = rig != null && rig.OnBoard && rig.RigTransform != null;
            WatchTurns(onBoard);
            float now = Time.unscaledTime;
            bool showTurn = onBoard && now < _until && Roles.TurnBased && !UICapture.Isolated && TryGetRegion(out Rect px);
            bool showNote = onBoard && now < _noteUntil && !string.IsNullOrEmpty(_note);
            if (!showTurn && !showNote)
            {
                _quad.Visible = _title.Visible = false;
                _shown = false;
                return;
            }

            Vector3 look = rig.HeadLocalRotation * Vector3.forward;
            float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            if (!_shown) { _yaw = yaw; _shown = true; }
            else _yaw = Mathf.LerpAngle(_yaw, yaw, 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.8f));
            Transform rt = rig.RigTransform;
            Quaternion facing = Quaternion.Euler(-Elevation, _yaw, 0f);
            Vector3 dir = facing * Vector3.forward;
            Vector3 center = rig.HeadLocalPosition + dir * Distance;
            Quaternion rot = rt.rotation * Quaternion.LookRotation(dir, Vector3.up);
            float height = 0f;
            if (showTurn)
            {
                TryGetRegion(out px);
                float w = UICapture.Width, h = UICapture.Height;
                _quad.SetTexture(UICapture.Texture);
                _quad.SetRegion(new Rect(px.xMin / w, px.yMin / h, px.width / w, px.height / h));
                float width = Mathf.Min(px.width * MetersPerPixel, MaxWidth);
                height = width * px.height / Mathf.Max(1f, px.width);
                _quad.T.position = rt.TransformPoint(center);
                _quad.T.rotation = rot;
                _quad.T.localScale = new Vector3(width, height, 1f) * rig.Scale;
            }
            _quad.Visible = showTurn;

            string title = showNote ? _note : TurnTitle();
            _title.Visible = !string.IsNullOrEmpty(title);
            if (_title.Visible)
            {
                _title.Text = title;
                Vector3 up = facing * Vector3.up;
                _title.Place(rt.TransformPoint(center + up * (height * 0.5f + 0.07f)), rot, 0.12f * rig.Scale);
            }
        }

        /// <summary>A new turn or a new lineup (TaleSpire's own turn data, the same for everyone): show it.</summary>
        private void WatchTurns(bool onBoard)
        {
            if (!onBoard || !Roles.TurnBased) { _lastTurn = default; _lastQueueHash = 0; return; }
            QueueElement cur;
            int hash = 17;
            try
            {
                cur = InitiativeManager.CurrentTurnElement;
                var list = InitiativeManager.TurnQueueList;
                for (int i = 0; i < list.Count; i++) hash = hash * 31 + list[i].GetHashCode();
            }
            catch { return; }
            if (cur != _lastTurn || hash != _lastQueueHash)
            {
                _lastTurn = cur;
                _lastQueueHash = hash;
                if (cur != default(QueueElement))
                {
                    _until = Time.unscaledTime + ShowSeconds;
                    Plugin.Log.LogInfo("Turn banner: " + TurnTitle());
                }
            }
        }

        private string TurnTitle()
        {
            var cur = _lastTurn;
            if (cur == default(QueueElement) || cur.Type != QueueElement.ElementType.Creature) return null;
            try
            {
                if (!Roles.IsGm && LocalPlayer.HasControlOfCreature(cur.CreatureGuid)) return "Your turn";
                if (CreaturePresenter.TryGetAsset(cur.CreatureGuid, out var c) && c != null) return c.Name + "'s turn";
            }
            catch { }
            return null;
        }

        /// <summary>Where TaleSpire draws its initiative bar (portraits and names) in the menu texture.</summary>
        private bool TryGetRegion(out Rect region)
        {
            region = default;
            if (_ui == null) _ui = FindObjectOfType<UI_InitativeManager>();
            if (_ui == null) return false;
            var panel = NormalPanelField?.GetValue(_ui) as GameObject;
            if (panel == null || !panel.activeInHierarchy) return false;
            bool any = false;
            foreach (var g in panel.GetComponentsInChildren<UnityEngine.UI.Graphic>())
            {
                if (!g.isActiveAndEnabled || g.color.a <= 0.01f || g.canvasRenderer.GetInheritedAlpha() <= 0.01f) continue;
                if (!DiceUi.TryGetRect(g.rectTransform, out Rect r)) continue;
                region = any ? Rect.MinMaxRect(Mathf.Min(region.xMin, r.xMin), Mathf.Min(region.yMin, r.yMin), Mathf.Max(region.xMax, r.xMax), Mathf.Max(region.yMax, r.yMax)) : r;
                any = true;
            }
            if (!any) return false;
            // (Clamped to the texture: parts of it may still be sliding in from above.)
            region = Rect.MinMaxRect(Mathf.Max(0f, region.xMin - 12f), Mathf.Max(0f, region.yMin - 12f), Mathf.Min(UICapture.Width, region.xMax + 12f), Mathf.Min(UICapture.Height, region.yMax + 12f));
            return region.width > 20f && region.height > 20f;
        }
    }
}
