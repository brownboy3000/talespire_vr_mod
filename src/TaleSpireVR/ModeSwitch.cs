using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace TaleSpireVR
{
    /// <summary>
    /// VR mode or normal mode, and the button on TaleSpire's main menu that switches between them.
    ///
    /// Normal mode is TaleSpire as it is without the mod: none of the mod's patches are in place and nothing of VR
    /// runs - the only addition is this button (on the main menu's bottom bar, between the welcome line and the build
    /// id). VR mode puts the patches in and starts
    /// VRManager (SteamVR must be running). Switching saves the choice to TaleSpireVR.Core.ini, so TaleSpire starts in
    /// that mode next time; the file can also be edited by hand while TaleSpire is closed.
    /// </summary>
    internal class ModeSwitch : MonoBehaviour
    {
        public static ModeSwitch Instance { get; private set; }

        private enum State { Normal, Starting, VR }
        private State _state = State.Normal;
        private GameObject _vrRoot;
        /// <summary>Why VR is not on although it was asked for (shown on the button), or null.</summary>
        private string _problem;
        /// <summary>The start under way was asked for with the button (so the choice is saved once it worked).</summary>
        private bool _saveWhenStarted;
        private bool _devChannel;

        private void Awake()
        {
            Instance = this;
            _devChannel = Directory.Exists(DebugSnapshot.DebugDir);
        }

        private void Start()
        {
            if (VRConfig.StartInVR.Value) StartVR(save: false);
            else Plugin.Log.LogInfo("Normal mode (TaleSpireVR.Core.ini): TaleSpire runs as without the mod; the main menu's VR button switches to VR");
        }

        public bool InVR => _state == State.VR && VRManager.Active;

        /// <summary>The button (or the debug channel): VR on or off.</summary>
        public void Switch(bool vr)
        {
            if (vr) { if (_state == State.Normal) StartVR(save: true); }
            else if (_state != State.Normal) StopVR();
        }

        private void StartVR(bool save)
        {
            _problem = null;
            _saveWhenStarted = save;
            Patches.PatchManager.ApplyVR(Plugin.HarmonyInstance);
            var mgr = VRManager.Instance;
            if (mgr == null)
            {
                _vrRoot = new GameObject("TaleSpireVR");
                DontDestroyOnLoad(_vrRoot);
                _vrRoot.hideFlags = HideFlags.HideAndDontSave;
                _vrRoot.AddComponent<VRManager>();   // (it starts OpenVR a couple of frames later)
                _state = State.Starting;
                return;
            }
            if (mgr.SwitchOn()) Started();
            else Failed();
        }

        private void Started()
        {
            _state = State.VR;
            if (_saveWhenStarted) VRConfig.StartInVR.Value = true;
            Plugin.Log.LogInfo("VR mode on");
        }

        private void Failed()
        {
            _state = State.Normal;
            _problem = "VR didn't start - is SteamVR running? Click to try again";
            if (_vrRoot != null && VRManager.Instance != null && !VRManager.Active && VRManager.Instance.StartFailed)
            {
                Destroy(_vrRoot);   // (a fresh one next time)
                _vrRoot = null;
            }
            Patches.PatchManager.RemoveVR(Plugin.HarmonyInstance);
            Plugin.Log.LogWarning("VR mode could not start; TaleSpire stays in normal mode");
        }

        private void StopVR()
        {
            var mgr = VRManager.Instance;
            if (mgr != null) mgr.SwitchOff();
            Patches.PatchManager.RemoveVR(Plugin.HarmonyInstance);
            UICapture.SendUiScaleChanged();   // (TaleSpire's own menu size again)
            _state = State.Normal;
            _problem = null;
            VRConfig.StartInVR.Value = false;
            Plugin.Log.LogInfo("Normal mode on (saved for the next start)");
        }

        private void Update()
        {
            if (_state == State.Starting)
            {
                var mgr = VRManager.Instance;
                if (mgr != null && mgr.StartFinished)
                {
                    if (mgr.StartFailed) Failed();
                    else Started();
                }
            }
            // (The developer's command channel, while VR - whose pointer reads it - is off.)
            if (_devChannel && !VRManager.Active) DebugSnapshot.PollCommands();
            UpdateButton();
        }

        // ---- The main menu button ---------------------------------------------------------------------------------

        private RectTransform _button;
        private TMPro.TextMeshProUGUI _label;
        private Image _back;
        private string _shown;

        /// <summary>
        /// Only on the main menu: on its black bottom bar, centred between the welcome line (below Begin) and the build
        /// id in the bottom right corner - well away from TaleSpire's settings and exit buttons at the top right. (If
        /// TaleSpire ever has no bottom bar, it goes to the top bar's right end instead.)
        /// </summary>
        private void UpdateButton()
        {
            var top = GameObject.Find("UI/TopBar");
            bool onMenu = top != null && top.activeInHierarchy && !BoardOpen();
            if (!onMenu)
            {
                if (_button != null && _button.gameObject.activeSelf) _button.gameObject.SetActive(false);
                return;
            }
            var bottom = GameObject.Find("UI/IMG_BottomBar");
            var bar = bottom != null && bottom.activeInHierarchy ? bottom : top;
            if (_button == null || _button.parent != bar.transform) Build(bar.transform as RectTransform, bar == bottom);
            if (_button == null) return;
            if (!_button.gameObject.activeSelf) _button.gameObject.SetActive(true);
            if (bar == bottom) PlaceOnBottomBar((RectTransform)bar.transform);
            bool vr = _state == State.VR;
            string text = _state == State.Starting ? "<b>VR MODE</b>\n<size=62%>Starting...</size>"
                : vr ? "<b>VR MODE: ON</b>\n<size=62%>Click to switch to normal TaleSpire</size>"
                : "<b>VR MODE: OFF</b>\n<size=62%>" + (_problem ?? "Click to play in VR (SteamVR must be running)") + "</size>";
            if (text != _shown) { _label.text = text; _shown = text; }
            _back.color = vr ? new Color(0.13f, 0.27f, 0.15f, 0.95f) : new Color(0.15f, 0.14f, 0.13f, 0.95f);
        }

        private const float ButtonWidth = 250f, ButtonHeight = 44f, Gap = 16f;

        /// <summary>Midway between the welcome line's right end (its decorations included) and the build id's left end.</summary>
        private void PlaceOnBottomBar(RectTransform bar)
        {
            float w = bar.rect.width;
            float welcomeRight = w * 0.5f + 155f, buildLeft = w - 240f;   // (TaleSpire's layout, if the parts aren't found)
            var welcome = GameObject.Find("UI/#PANEL_LoggedIn/TXT_Welcome");
            if (welcome != null && welcome.activeInHierarchy)
                welcomeRight = RectTransformUtility.CalculateRelativeRectTransformBounds(bar, welcome.transform).max.x + w * bar.pivot.x;
            var build = GameObject.Find("UI/IMG_BottomBar/Label_BuildID");
            if (build != null && build.activeInHierarchy)
                buildLeft = RectTransformUtility.CalculateRelativeRectTransformBounds(bar, build.transform).min.x + w * bar.pivot.x;
            float width = Mathf.Clamp(buildLeft - welcomeRight - 2f * Gap, 150f, ButtonWidth);
            float x = (welcomeRight + buildLeft) * 0.5f;
            var size = new Vector2(width, ButtonHeight);
            var pos = new Vector2(x, 0f);
            if (_button.sizeDelta != size) _button.sizeDelta = size;
            if ((_button.anchoredPosition - pos).sqrMagnitude > 0.25f) _button.anchoredPosition = pos;
        }

        private static bool BoardOpen()
        {
            try { return BoardSessionManager.InBoard; }
            catch { return false; }
        }

        private void Build(RectTransform bar, bool bottom)
        {
            if (bar == null) return;
            if (_button != null) Destroy(_button.gameObject);
            var go = new GameObject("TaleSpireVR_ModeButton", typeof(RectTransform));
            go.layer = bar.gameObject.layer;
            _button = (RectTransform)go.transform;
            _button.SetParent(bar, false);
            if (bottom)
            {
                // Placed every frame by PlaceOnBottomBar (x from the bar's left edge, centred in its height).
                _button.anchorMin = _button.anchorMax = new Vector2(0f, 0.5f);
                _button.pivot = new Vector2(0.5f, 0.5f);
            }
            else
            {
                _button.anchorMin = _button.anchorMax = new Vector2(1f, 1f);
                _button.pivot = new Vector2(1f, 1f);
                _button.anchoredPosition = new Vector2(-14f, -34f);
            }
            _button.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
            _back = go.AddComponent<Image>();
            // A thin border in TaleSpire's orange, so it reads as a button on the black bar.
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.88f, 0.62f, 0.22f, 0.6f);
            outline.effectDistance = new Vector2(1f, -1f);
            var button = go.AddComponent<Button>();
            button.targetGraphic = _back;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => Switch(_state != State.VR));

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.layer = go.layer;
            var trt = (RectTransform)textGo.transform;
            trt.SetParent(_button, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(8f, 3f);
            trt.offsetMax = new Vector2(-8f, -3f);
            _label = textGo.AddComponent<TMPro.TextMeshProUGUI>();
            _label.alignment = TMPro.TextAlignmentOptions.Center;
            _label.enableAutoSizing = true;
            _label.fontSizeMin = 8f;
            _label.fontSizeMax = 18f;
            _label.color = new Color(0.93f, 0.9f, 0.82f, 1f);
            _label.raycastTarget = false;
            // TaleSpire's own menu font (the copyright line's).
            var copy = GameObject.Find("UI/TopBar/Copyright Text");
            var tsText = copy != null ? copy.GetComponent<TMPro.TMP_Text>() : null;
            if (tsText != null) { _label.font = tsText.font; _label.fontSharedMaterial = tsText.fontSharedMaterial; }
            _shown = null;
            Plugin.Log.LogInfo("Main menu: VR mode button added");
        }
    }
}
