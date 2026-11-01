using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// The main menu's warning in the top left: TaleSpire's "Early Access" note (whose last line, "Features and
    /// usability.", spilled over the copyright line at VR menu sizes) becomes a note about this mod, 2 points bigger,
    /// with TaleSpire's own copyright line (unchanged, also 2 points bigger) moved down below it.
    /// </summary>
    internal static class MainMenuText
    {
        private const string Warning = "You are playing a hurriedly vibe-coded VR mod of TaleSpire. Expect bugs, broken integrations, and implementations and control schemes that are not fully thought through. Usability highly depends on your VR setup as only one headset and controller combination has been tested.";
        private const float Bigger = 2f;

        private static TMPro.TMP_Text _warning, _copyright;
        private static float _nextLook, _warningSize, _copyrightSize, _placedHeight = -1f;
        private static string _copyrightText;
        private static Vector2 _copyrightPos, _copyrightRect;

        public static void Update()
        {
            if (Time.unscaledTime < _nextLook) return;
            _nextLook = Time.unscaledTime + 0.25f;
            try
            {
                if (_warning == null)
                {
                    var go = GameObject.Find("UI/TopBar/Panel_BetaWarning/Early Access Text");
                    _warning = go != null ? go.GetComponent<TMPro.TMP_Text>() : null;
                    var cg = GameObject.Find("UI/TopBar/Copyright Text");
                    _copyright = cg != null ? cg.GetComponent<TMPro.TMP_Text>() : null;
                    if (_warning == null || _copyright == null) return;
                    // As TaleSpire made them (this menu scene's copy).
                    _warningSize = _warning.fontSize;
                    _originalWarning = _warning.text;
                    _panelSaved = false;
                    _copyrightSize = _copyright.fontSize;
                    _copyrightText = _copyright.text;
                    _copyrightPos = _copyright.rectTransform.anchoredPosition;
                    _copyrightRect = _copyright.rectTransform.rect.size;
                    _placedHeight = -1f;
                }
                if (_warning.text != Warning) { SetTexts(); return; }   // laid out by the next look
                Place();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Main menu warning text: " + e.Message);
                _nextLook = Time.unscaledTime + 30f;
            }
        }

        /// <summary>Normal mode: TaleSpire's own texts and layout back (its warning, its copyright line where it was).</summary>
        public static void Restore()
        {
            try
            {
                if (_warning != null && _copyright != null && _placedHeight >= 0f)
                {
                    _warning.fontSize = _warningSize;
                    _warning.text = _originalWarning ?? _warning.text;
                    _copyright.text = _copyrightText;
                    _copyright.fontSize = _copyrightSize;
                    _copyright.rectTransform.anchoredPosition = _copyrightPos;
                    _copyright.rectTransform.sizeDelta = _copyrightRect;
                    var panel = _warning.rectTransform.parent as RectTransform;
                    if (panel != null && _panelSaved) panel.offsetMin = _panelOffsetMin;
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Main menu text restore: " + e.Message); }
            _warning = _copyright = null;
            _placedHeight = -1f;
            _nextLook = 0f;
        }

        private static string _originalWarning;
        private static Vector2 _panelOffsetMin;
        private static bool _panelSaved;

        private static void SetTexts()
        {
            _warning.fontSize = _warningSize + Bigger;
            _warning.enableWordWrapping = true;
            _warning.overflowMode = TMPro.TextOverflowModes.Overflow;
            _warning.text = Warning;
            // The copyright line ("<size=18>...</size>"), as it was but 2 points bigger.
            string c = _copyrightText;
            var m = System.Text.RegularExpressions.Regex.Match(c, @"<size=(\d+(\.\d+)?)>");
            if (m.Success && float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float size))
                c = c.Replace(m.Value, "<size=" + (size + Bigger).ToString(System.Globalization.CultureInfo.InvariantCulture) + ">");
            else _copyright.fontSize = _copyrightSize + Bigger;
            _copyright.text = c;
            _placedHeight = -1f;
        }

        /// <summary>The warning's panel grows down to the text's laid-out height; the copyright line goes just below it.</summary>
        private static void Place()
        {
            var info = _warning.textInfo;
            if (info == null || info.lineCount == 0) return;
            float height = Mathf.Ceil(info.lineInfo[0].ascender - info.lineInfo[info.lineCount - 1].descender) + 4f;
            if (Mathf.Abs(height - _placedHeight) < 1f) return;
            _placedHeight = height;
            var panel = _warning.rectTransform.parent as RectTransform;   // Panel_BetaWarning: stretched across the top bar, pinned to its top
            if (panel == null) return;
            if (!_panelSaved) { _panelOffsetMin = panel.offsetMin; _panelSaved = true; }
            float top = panel.offsetMax.y;
            panel.offsetMin = new Vector2(panel.offsetMin.x, top - height);

            var crt = _copyright.rectTransform;
            // Keep its left edge where it was (it is placed by its centre).
            float left = _copyrightPos.x - _copyrightRect.x * crt.pivot.x;
            Vector2 pref = _copyright.GetPreferredValues(_copyright.text, 2000f, 0f);
            float lineH = Mathf.Ceil(pref.y);
            crt.sizeDelta = new Vector2(Mathf.Ceil(pref.x) + 4f, lineH);
            const float gap = 3f;
            crt.anchoredPosition = new Vector2(left + crt.sizeDelta.x * crt.pivot.x, top - height - gap - lineH * (1f - crt.pivot.y));
            Plugin.Log.LogInfo($"Main menu: warning text replaced ({_warning.fontSize} pt, {info.lineCount} lines, {height:F0} high), copyright moved below it");
        }
    }
}
