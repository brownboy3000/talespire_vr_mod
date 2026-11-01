using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>The roll results banner: TaleSpire's result panel shown in the sky (UIPanels.UpdateBanner).</summary>
    internal static class DiceResultBanner
    {
        /// <summary>How long a result stays up in VR (TaleSpire: 5 s).</summary>
        public const float ShowSeconds = 15f;
        /// <summary>Degrees above the horizon and distance in meters (in your play space, so zoom doesn't change its size).</summary>
        public const float Elevation = 24f, Distance = 1.8f;
        /// <summary>Size: meters per UI pixel (a one-die result is about 0.9 m wide), so bigger rolls get a bigger banner, up to MaxWidth.</summary>
        public const float MetersPerPixel = 0.9f / 540f, MaxWidth = 1.6f;
    }

    /// <summary>
    /// Where TaleSpire draws its dice UI in the captured menu texture (canvas world space = UI pixels): the dice buttons
    /// (d4 ... d100) with the tray above them that fills as you pick dice (its "Roll" button pulls them out), and the
    /// result panel shown after a roll. The dice window and the results banner (UIPanels) show these regions.
    /// </summary>
    internal static class DiceUi
    {
        private static readonly Vector3[] _corners = new Vector3[4];

        public static bool TryGetRect(RectTransform rt, out Rect rect)
        {
            rect = default;
            if (rt == null || !rt.gameObject.activeInHierarchy) return false;
            rt.GetWorldCorners(_corners);
            float x0 = Mathf.Min(_corners[0].x, _corners[2].x), x1 = Mathf.Max(_corners[0].x, _corners[2].x);
            float y0 = Mathf.Min(_corners[0].y, _corners[2].y), y1 = Mathf.Max(_corners[0].y, _corners[2].y);
            rect = Rect.MinMaxRect(x0, y0, x1, y1);
            return rect.width > 1f && rect.height > 1f;
        }

        private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        /// <summary>Width of one dice button (d4 ... d100) in UI pixels, from the last TryGetTrayRegion.</summary>
        public static float ButtonWidth { get; private set; }

        /// <summary>The dice buttons, plus room above them for the tray and its Roll buttons (which only appear once you pick dice).</summary>
        public static bool TryGetTrayRegion(out Rect region)
        {
            region = default;
            bool any = false;
            foreach (var b in Object.FindObjectsOfType<UIAddDiceButton>())
            {
                if (!TryGetRect(b.transform as RectTransform, out Rect r)) continue;
                region = any ? Union(region, r) : r;
                ButtonWidth = any ? Mathf.Max(ButtonWidth, r.width) : r.width;
                any = true;
            }
            if (!any) return false;
            float h = region.height;
            // The tray slides up above the buttons when it has dice: keep room for it so the window doesn't jump.
            region = Rect.MinMaxRect(region.xMin - 0.3f * h, region.yMin - 0.25f * h, region.xMax + 0.3f * h, region.yMax + TrayRoom * h);
            var tray = GUIManager.DiceTray;
            if (tray != null && TryGetRect(tray.transform as RectTransform, out Rect t) && t.yMax <= region.yMax + 4f * h && t.yMin >= region.yMin - h)
                region = Union(region, Rect.MinMaxRect(t.xMin - 0.2f * h, t.yMin, t.xMax + 0.2f * h, t.yMax + 0.2f * h));
            return true;
        }

        /// <summary>Space kept above the dice buttons for the tray, in button heights.</summary>
        public static float TrayRoom = 6.3f;   // the tray, its "Drag" hint and GM-roll button (measured: up to ~6 button heights)

        /// <summary>TaleSpire's result panel (who rolled, the dice and the total) while it is shown.</summary>
        public static bool TryGetResultRegion(out Rect region)
        {
            region = default;
            var result = GUIManager.DiceRollResult;
            if (result == null || !result.gameObject.activeInHierarchy) return false;
            bool any = false;
            foreach (var g in result.GetComponentsInChildren<UnityEngine.UI.Graphic>())
            {
                if (!g.isActiveAndEnabled || g.color.a <= 0.01f) continue;
                if (!TryGetRect(g.rectTransform, out Rect r)) continue;
                region = any ? Union(region, r) : r;
                any = true;
            }
            if (!any) return false;
            float pad = 0.06f * Mathf.Max(region.width, region.height);
            region = Rect.MinMaxRect(region.xMin - pad, region.yMin - pad, region.xMax + pad, region.yMax + pad);
            return true;
        }

        /// <summary>Debug ("diceui"): log where the dice UI is.</summary>
        public static void LogLayout()
        {
            foreach (var b in Object.FindObjectsOfType<UIAddDiceButton>(true))
            {
                TryGetRect(b.transform as RectTransform, out Rect r);
                Plugin.Log.LogInfo($"[diceui] button {Diagnostics.PathOf(b.transform)} active={b.gameObject.activeInHierarchy} rect={r}");
            }
            var tray = GUIManager.DiceTray;
            if (tray != null)
            {
                TryGetRect(tray.transform as RectTransform, out Rect r);
                Plugin.Log.LogInfo($"[diceui] tray {Diagnostics.PathOf(tray.transform)} active={tray.gameObject.activeInHierarchy} rect={r}");
                foreach (var g in tray.GetComponentsInChildren<UnityEngine.UI.Graphic>())
                    if (g.isActiveAndEnabled && TryGetRect(g.rectTransform, out Rect gr))
                        Plugin.Log.LogInfo($"[diceui]   {Diagnostics.PathOf(g.transform)} {g.GetType().Name} rect={gr}");
            }
            var result = GUIManager.DiceRollResult;
            if (result != null)
            {
                TryGetRect(result.transform as RectTransform, out Rect r);
                Plugin.Log.LogInfo($"[diceui] result {Diagnostics.PathOf(result.transform)} active={result.gameObject.activeInHierarchy} rect={r} canvas={(result.GetComponentInParent<Canvas>() ? Diagnostics.PathOf(result.GetComponentInParent<Canvas>().transform) : "-")}");
            }
            Plugin.Log.LogInfo($"[diceui] {DiceHand.Describe()}");
            Plugin.Log.LogInfo($"[diceui] trayRegion={(TryGetTrayRegion(out Rect tr) ? tr.ToString() : "-")} resultRegion={(TryGetResultRegion(out Rect rr) ? rr.ToString() : "-")} ui={UICapture.Width}x{UICapture.Height}");
        }
    }
}
