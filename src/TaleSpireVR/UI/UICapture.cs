using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace TaleSpireVR
{
    /// <summary>
    /// Renders TaleSpire's screen-space UI into a high-resolution texture for the VR panels.
    ///
    /// Overlay canvases are switched to "Screen Space - Camera" on a private orthographic camera that renders into a
    /// texture of (Screen size x supersample), e.g. 3840x2160. The camera's world space equals that texture's pixel
    /// space (centred at (W/2, H/2), orthographic size H/2), so TaleSpire UI scripts that assume overlay canvases
    /// (positions in pixels, null-camera rect maths) keep working. TaleSpire's UI scale is multiplied by the same
    /// supersample factor and the few scripts that read Screen.width/height see the UI size instead: exactly how
    /// TaleSpire behaves on a real 4K monitor, just rendered for the headset.
    /// </summary>
    [DefaultExecutionOrder(30050)]
    internal class UICapture : MonoBehaviour
    {
        public static RenderTexture Texture { get; private set; }
        public static Camera UICamera { get; private set; }

        /// <summary>UI pixel space (mouse positions, canvas world coordinates) = texture size.</summary>
        public static int Width => Texture != null ? Texture.width : Screen.width;
        public static int Height => Texture != null ? Texture.height : Screen.height;
        public static bool Ready => Texture != null;

        private const int UILayer = 5;
        /// <summary>An unused layer for the radial menu (and our cursor), so it can be captured on its own.</summary>
        internal const int RadialLayer = 8;
        private const int NormalMask = (1 << UILayer) | (1 << RadialLayer);
        private const float PlaneDistance = 100f;
        private static readonly string[] Ignored = { "unityexplorer", "universelib" };

        private readonly Dictionary<Canvas, int> _redirected = new Dictionary<Canvas, int>();
        private float _nextScan;

        private void Awake()
        {
            UICamera = MakeCamera("TaleSpireVR_UICamera");
            UICamera.cullingMask = NormalMask;
            UICamera.clearFlags = CameraClearFlags.SolidColor;
            UICamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            UICamera.depth = 100;

            XRSettings.showDeviceView = VRConfig.MirrorEye.Value != 0;
            EnsureTexture();
            BuildCursor();
        }

        private static Camera MakeCamera(string name)
        {
            var go = new GameObject(name);
            DontDestroyOnLoad(go);
            var cam = go.AddComponent<Camera>();
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.orthographic = true;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.useOcclusionCulling = false;
            cam.renderingPath = RenderingPath.Forward;
            // Overlay canvases ignore depth, so some TaleSpire UI parts sit well in front of or behind the canvas plane
            // (the atmosphere dials' dot is 16 units out). Keep a deep range so none of them get clipped.
            cam.nearClipPlane = 1f;
            cam.farClipPlane = PlaneDistance + 400f;
            return cam;
        }

        public static float Supersample
        {
            get
            {
                int h = VRConfig.MenuResolution.Value;
                if (h <= 0) return 1f;
                return Mathf.Clamp((float)h / Mathf.Max(1, Screen.height), 1f, 3f);
            }
        }

        private void EnsureTexture()
        {
            int lw = Mathf.Max(64, Screen.width), lh = Mathf.Max(64, Screen.height);
            float ss = Supersample;
            int w = Mathf.RoundToInt(lw * ss), h = Mathf.RoundToInt(lh * ss);
            if (Texture == null || Texture.width != w || Texture.height != h)
            {
                if (Texture != null) { UICamera.targetTexture = null; Texture.Release(); }
                Texture = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = "TaleSpireVR_UI",
                    useMipMap = true,
                    autoGenerateMips = true,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 8,
                };
                Texture.Create();
                UICamera.targetTexture = Texture;
                Plugin.Log.LogInfo($"UI texture {w}x{h} (screen {lw}x{lh}, supersample x{ss:F2})");
                _scaleEventFrame = Time.frameCount + 2;
            }
            // The panel shows the 4K menu texture at roughly 2/3 size in the headset, so plain trilinear filtering
            // mostly reads the half-size mip (soft text). Bias toward the full-resolution level.
            Texture.mipMapBias = -Mathf.Clamp01(VRConfig.MenuSharpness.Value);
            UICamera.orthographicSize = h * 0.5f;
            UICamera.transform.SetPositionAndRotation(new Vector3(w * 0.5f, h * 0.5f, -PlaneDistance), Quaternion.identity);
        }

        private int _scaleEventFrame = -1;

        /// <summary>Tell TaleSpire's canvases to re-read the (now supersampled) UI scale.</summary>
        private void MaybeSendScaleEvent()
        {
            if (_scaleEventFrame < 0 || Time.frameCount < _scaleEventFrame) return;
            _scaleEventFrame = -1;
            SendUiScaleChanged();
        }

        /// <summary>TaleSpire's canvases re-read the UI scale (ours in VR, TaleSpire's own when the mod is off).</summary>
        public static void SendUiScaleChanged()
        {
            try
            {
                HarmonyLib.AccessTools.Method(typeof(TaleSpire.Settings.SettingsManager), "SendOnUiScaleChangeEvent")?.Invoke(null, null);
                Plugin.Log.LogInfo($"UI scale now {TaleSpire.Settings.SettingsManager.UIScale:F2}");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("UI scale refresh failed: " + e.Message); }
        }

        // ---- The VR mod switched off (Player mode) and on again ------------------------------------------------

        private readonly List<Canvas> _hiddenCanvases = new List<Canvas>();
        private readonly Dictionary<CanvasGroup, float> _hiddenGroups = new Dictionary<CanvasGroup, float>();
        private readonly List<GameObject> _hiddenSceneObjects = new List<GameObject>();
        private Canvas _cursorCanvas;

        /// <summary>TaleSpire's menus back on its window exactly as they were (canvases, hidden and enlarged parts, clicks).</summary>
        public void Suspend()
        {
            foreach (var kv in _redirected)
            {
                if (kv.Key == null || kv.Key == _cursorCanvas) continue;
                kv.Key.renderMode = RenderMode.ScreenSpaceOverlay;
                kv.Key.gameObject.layer = kv.Value;
            }
            _redirected.Clear();
            if (_cursorCanvas != null) _redirected[_cursorCanvas] = UILayer;
            foreach (var c in _hiddenCanvases) if (c != null) c.enabled = true;
            _hiddenCanvases.Clear();
            foreach (var kv in _hiddenGroups)
                if (kv.Key != null) { kv.Key.alpha = kv.Value; kv.Key.blocksRaycasts = true; kv.Key.interactable = true; }
            _hiddenGroups.Clear();
            foreach (var go in _hiddenSceneObjects) if (go != null) go.SetActive(true);
            _hiddenSceneObjects.Clear();
            foreach (var kv in _enlarged)
            {
                var rt = kv.Key;
                if (rt == null) continue;
                rt.offsetMin = new Vector2(rt.offsetMin.x, kv.Value.BaseBottom);
                rt.offsetMax = new Vector2(rt.offsetMax.x, kv.Value.BaseTop);
                rt.localScale = Vector3.one;
            }
            _enlarged.Clear();
            foreach (var r in _pausedRaycasters) if (r != null) r.enabled = true;
            _pausedRaycasters.Clear();
            _radialOnly = _isolated = false;
            RestoreEscLayers();
            _escIsolated = false;
            if (UICamera != null)
            {
                UICamera.cullingMask = NormalMask;
                UICamera.enabled = false;
            }
            Plugin.Log.LogInfo("TaleSpire's menus are back on its window");
        }

        public void Resume()
        {
            if (UICamera != null) UICamera.enabled = true;
            _nextScan = 0f;
            _nextNestedScan = 0f;
            _scaleEventFrame = Time.frameCount + 2;
        }

        /// <summary>Extra UI scale on top of TaleSpire's own (the supersample factor).</summary>
        public static float UiScaleMultiplier => Texture != null ? (float)Texture.height / Mathf.Max(1, Screen.height) : 1f;

        private void LateUpdate()
        {
            if (!VRManager.Active) return;
            EnsureTexture();
            MaybeSendScaleEvent();
            UpdateCursor();
            UpdateRadialIsolation();
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.25f;
                RedirectCanvases();
            }
        }

        /// <summary>
        /// Overlay canvases that pin icons/labels to 3D positions using the flat camera's projection (creature
        /// indicators, map markers, pings, AoE labels, chat bubbles). In VR they land in random places on the menu
        /// panels, so they are hidden.
        /// </summary>
        private static readonly string[] HiddenInVR =
        {
            "CreaturePresenter/IndicatorsCanvas",
            "MarkerManager/IndicatorsCanvas",
            "GestureManager/PingIconManager",
            "AoePresenter/Indicators/Canvas",
            "TextBubbles/TextBubbleManager",
        };

        private static bool ShouldHide(Canvas canvas)
        {
            string path = Diagnostics.PathOf(canvas.transform);
            foreach (var h in HiddenInVR) if (path.EndsWith(h)) return true;
            return false;
        }

        /// <summary>
        /// Pieces inside the main UI canvas (so not covered by <see cref="HiddenInVR"/>): the eye icons TaleSpire draws
        /// over every creature the selected one can see (they float around the radial menu in VR), and controls the
        /// VR controllers replace.
        /// </summary>
        private static readonly string[] HiddenObjectsInVR =
        {
            "GUIManager/Ts/WorldIcons",
            // The camera-height pin on the right-hand floor slider: the right stick does that in VR.
            "GUIManager/Ts/PANEL_STRUCTURE/MiddlePanel/#PANEL_UI_FloorControls/FloorControllers/CanvasTransform/Pins/FloorPin",
            // The compass at the bottom left: it follows the flat camera, not your view.
            "GUIManager/Ts/CompassPin",
        };

        /// <summary>
        /// Panels made bigger than the rest of the UI (SidePanelScale): the left-hand buttons with the menus they open,
        /// the right-hand buttons (unique creatures, chat) with theirs, and the right-hand floor slider. They stretch the
        /// full height, so they are scaled around their screen edge and made shorter by the same factor, keeping the
        /// same height on screen (lists scroll a little sooner).
        /// </summary>
        private static readonly string[] EnlargedPanels =
        {
            "GUIManager/Ts/PANEL_STRUCTURE/MiddlePanel/MiddlePanel/LeftPanel",
            "GUIManager/Ts/PANEL_STRUCTURE/MiddlePanel/MiddlePanel/RightPanel",
            "GUIManager/Ts/PANEL_STRUCTURE/MiddlePanel/#PANEL_UI_FloorControls",
        };

        // Only the vertical offsets are ours; TaleSpire is free to move the panels sideways (it slides them).
        private class Enlarged { public float BaseBottom, BaseTop, AppliedBottom, AppliedTop, Scale; }
        private static readonly Dictionary<RectTransform, Enlarged> _enlarged = new Dictionary<RectTransform, Enlarged>();

        private static void EnlargePanels()
        {
            // Not while a laser press on a menu is held: any change to the floor slider's size makes TaleSpire put every
            // pin back where the current heights are (UI_FloorControllers.OnRectTransformDimensionsChange), and the water
            // only eases towards the height being dragged, so a pin being dragged would jump back.
            if (VRPointer.UiPressHeld) return;
            float s = Mathf.Clamp(VRConfig.SidePanelScale.Value, 0.5f, 3f);
            foreach (var path in EnlargedPanels)
            {
                var go = GameObject.Find(path);
                if (go == null || !(go.transform is RectTransform rt) || rt.parent == null) continue;
                if (Mathf.Approximately(rt.anchorMin.y, rt.anchorMax.y)) continue;   // only full-height panels
                float bottom = rt.offsetMin.y, top = rt.offsetMax.y;
                if (!_enlarged.TryGetValue(rt, out var e))
                {
                    e = new Enlarged { BaseBottom = bottom, BaseTop = top, AppliedBottom = float.NaN, AppliedTop = float.NaN };
                    _enlarged[rt] = e;
                }
                else if (Mathf.Abs(bottom - e.AppliedBottom) > 0.01f || Mathf.Abs(top - e.AppliedTop) > 0.01f)
                {
                    // Never take one of our own (shortened) edges as TaleSpire's: the panel would shrink again on every
                    // such change, and the floor slider's pins with it.
                    bool bottomMoved = Mathf.Abs(bottom - e.AppliedBottom) > 0.01f, topMoved = Mathf.Abs(top - e.AppliedTop) > 0.01f;
                    float shift = bottom - e.AppliedBottom;
                    if (bottomMoved && topMoved && Mathf.Abs(top - e.AppliedTop - shift) < 0.01f)
                    {
                        // Moved up or down as a whole, our height kept: the base moves with it.
                        e.BaseBottom += shift;
                        e.BaseTop += shift;
                    }
                    else
                    {
                        // TaleSpire set an edge again: its value is the new base for that edge; the other keeps its base.
                        if (bottomMoved) e.BaseBottom = bottom;
                        if (topMoved) e.BaseTop = top;
                    }
                    Plugin.Log.LogInfo($"Enlarged '{path}': TaleSpire changed it ({e.AppliedBottom:F1}/{e.AppliedTop:F1} -> {bottom:F1}/{top:F1}); base now {e.BaseBottom:F1}/{e.BaseTop:F1}");
                }

                // Unscaled height the panel would have, from the parent's current height (it changes while the UI
                // loads and animates), and the offsets that make it 1/s of that so it is full height once scaled.
                float parentH = ((RectTransform)rt.parent).rect.height;
                float h = parentH * (rt.anchorMax.y - rt.anchorMin.y) + e.BaseTop - e.BaseBottom;
                float d = h - h / s;
                float wantBottom = e.BaseBottom + d * rt.pivot.y;
                float wantTop = e.BaseTop - d * (1f - rt.pivot.y);
                if (Mathf.Abs(bottom - wantBottom) < 0.01f && Mathf.Abs(top - wantTop) < 0.01f && Mathf.Approximately(rt.localScale.x, s)) continue;

                rt.offsetMin = new Vector2(rt.offsetMin.x, wantBottom);
                rt.offsetMax = new Vector2(rt.offsetMax.x, wantTop);
                rt.localScale = new Vector3(s, s, 1f);
                e.AppliedBottom = rt.offsetMin.y;
                e.AppliedTop = rt.offsetMax.y;
                if (!Mathf.Approximately(e.Scale, s)) Plugin.Log.LogInfo($"Enlarged '{path}' x{s:F2}");
                e.Scale = s;
            }
        }

        /// <summary>Debug ("floorpanel"): the enlarged panels' vertical offsets and heights, the floor slider's track and pins.</summary>
        public static void DebugDumpPanels()
        {
            foreach (var kv in _enlarged)
            {
                var rt = kv.Key;
                if (rt == null) continue;
                Plugin.Log.LogInfo($"[floorpanel] {rt.name}: offsets {rt.offsetMin.y:F1}/{rt.offsetMax.y:F1} base {kv.Value.BaseBottom:F1}/{kv.Value.BaseTop:F1} height {rt.rect.height:F1} scale {rt.localScale.x:F2} pos {rt.anchoredPosition:F1}");
            }
            var track = HarmonyLib.AccessTools.Field(typeof(UI_FloorPin), "_canvasTransform");
            foreach (var pin in Object.FindObjectsOfType<UI_FloorPin>())
            {
                var t = track?.GetValue(pin) as RectTransform;
                Plugin.Log.LogInfo($"[floorpanel] pin '{pin.name}' track height {(t != null ? t.rect.height : -1f):F1} lossy {(t != null ? t.lossyScale.y : 0f):F3} uiScale {TaleSpire.Settings.SettingsManager.UIScale:F2}");
            }
        }

        /// <summary>Scene props that don't hold up in the headset: the untextured low-poly chimera in the main menu.</summary>
        private static readonly string[] HiddenSceneObjectsInVR = { "The3dStuff/Chimera_Low" };

        private void HideObjects()
        {
            foreach (var path in HiddenObjectsInVR)
            {
                var go = GameObject.Find(path);
                if (go == null) continue;
                var group = go.GetComponent<CanvasGroup>();
                if (group == null) group = go.AddComponent<CanvasGroup>();
                if (group.alpha > 0f || group.blocksRaycasts)
                {
                    if (!_hiddenGroups.ContainsKey(group)) _hiddenGroups[group] = group.alpha;
                    group.alpha = 0f;
                    group.blocksRaycasts = false;
                    group.interactable = false;
                    Plugin.Log.LogInfo($"Hiding screen-pinned UI in VR: '{path}'");
                }
            }
            foreach (var path in HiddenSceneObjectsInVR)
            {
                var go = GameObject.Find(path);
                if (go != null && go.activeSelf)
                {
                    go.SetActive(false);
                    _hiddenSceneObjects.Add(go);
                    Plugin.Log.LogInfo($"Hiding scene object in VR: '{path}'");
                }
            }
        }

        private void RedirectCanvases()
        {
            HideObjects();
            EnlargePanels();
            foreach (var canvas in Object.FindObjectsOfType<Canvas>())
            {
                if (canvas == null || !canvas.isRootCanvas) continue;
                if (canvas.enabled && ShouldHide(canvas))
                {
                    canvas.enabled = false;
                    _hiddenCanvases.Add(canvas);
                    Plugin.Log.LogInfo($"Hiding screen-pinned indicators in VR: '{Diagnostics.PathOf(canvas.transform)}'");
                    continue;
                }
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    string n = canvas.name.ToLowerInvariant();
                    bool skip = false;
                    foreach (var i in Ignored) if (n.Contains(i)) skip = true;
                    if (skip) continue;
                    _redirected[canvas] = canvas.gameObject.layer;
                    canvas.gameObject.layer = UILayer;
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = UICamera;
                    canvas.planeDistance = PlaneDistance;
                    Plugin.Log.LogInfo($"UI canvas moved to VR panel: '{Diagnostics.PathOf(canvas.transform)}' order={canvas.sortingOrder}");
                }
                else if (canvas.renderMode == RenderMode.ScreenSpaceCamera && _redirected.ContainsKey(canvas) && canvas.worldCamera != UICamera)
                {
                    canvas.worldCamera = UICamera;
                }
            }
            if (Time.unscaledTime >= _nextNestedScan)
            {
                _nextNestedScan = Time.unscaledTime + 2f;
                EnsureRadialCanvas();
                FixNestedCanvasLayers();
            }
        }

        // ---- Radial menus on their own ------------------------------------------------------------------------
        // The radial window shows a square of the menu texture around the screen centre, so whatever else was open
        // (the library, side menus) showed through it. While a radial menu is open the big panel is hidden anyway,
        // so capture only the radial menu then, and let only its buttons take clicks.
        private Canvas _radialCanvas;
        private UnityEngine.UI.GraphicRaycaster _radialRaycaster;
        private readonly List<UnityEngine.UI.GraphicRaycaster> _pausedRaycasters = new List<UnityEngine.UI.GraphicRaycaster>();
        private bool _radialOnly;
        private static bool _isolated;

        /// <summary>The menu texture holds only one isolated layer now (a radial menu, or first person's ESC menu): no banners from it.</summary>
        public static bool Isolated => _isolated;

        private void EnsureRadialCanvas()
        {
            if (_radialCanvas != null) return;
            var go = GameObject.Find("GUIManager/Ts/MapMenuManager");
            if (go == null) return;
            _radialCanvas = go.GetComponent<Canvas>();
            if (_radialCanvas == null) _radialCanvas = go.AddComponent<Canvas>();
            _radialRaycaster = go.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            if (_radialRaycaster == null) _radialRaycaster = go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            go.layer = RadialLayer;
            Plugin.Log.LogInfo("Radial menu given its own canvas layer");
            // TaleSpire's mouse-hover text (some radial options name themselves that way) shows with the radial too.
            var mouseText = GameObject.Find("SingletonHub/_MouseManager");
            if (mouseText != null && mouseText.GetComponent<Canvas>() != null) mouseText.layer = RadialLayer;
        }

        /// <summary>
        /// Each radial option's name label (TXT_Title) is a nested canvas of its own, on the UI layer, so with only the
        /// radial layer drawn the labels never appeared on hover. Options are created as menus open, so check often.
        /// </summary>
        private void FixRadialNestedLayers()
        {
            if (_radialCanvas == null) return;
            foreach (var nested in _radialCanvas.GetComponentsInChildren<Canvas>(true))
                if (nested.gameObject.layer != RadialLayer) nested.gameObject.layer = RadialLayer;
        }

        private float _nextRadialLayerFix;

        private void UpdateRadialIsolation()
        {
            var rig = VRManager.Instance.Rig;
            // Also while the radial window is still on screen this frame (a menu closed after it was placed): otherwise
            // that frame showed the middle of the whole UI (the dice tray, toolbars) in the radial window.
            var panels = VRManager.Instance.Panels;
            bool esc = UIPanels.EscOnly;
            if (esc != _escIsolated)
            {
                _escIsolated = esc;
                if (esc) _nextRadialLayerFix = 0f;
                else RestoreEscLayers();
            }
            bool want = esc || (_radialCanvas != null && rig != null && rig.OnBoard
                && ((SingletonBehaviour<MapMenuManager>.HasInstance && MapMenuManager.IsOpen) || RadialExtraButton.OwnMenuOpen
                    || (panels != null && panels.Radial.Visible)));
            if (want && (want != _radialOnly || Time.unscaledTime >= _nextRadialLayerFix))
            {
                _nextRadialLayerFix = Time.unscaledTime + 0.25f;
                if (esc) FixEscLayers(); else FixRadialNestedLayers();
            }
            _isolated = want;
            if (want == _radialOnly) return;
            _radialOnly = want;
            UICamera.cullingMask = want ? 1 << RadialLayer : NormalMask;
            if (want)
            {
                foreach (var r in Object.FindObjectsOfType<UnityEngine.UI.GraphicRaycaster>())
                {
                    if (!r.enabled || r == _radialRaycaster || (_radialCanvas != null && r.transform.IsChildOf(_radialCanvas.transform)) || UnderEscMenu(r.transform)) continue;
                    r.enabled = false;
                    _pausedRaycasters.Add(r);
                }
            }
            else
            {
                foreach (var r in _pausedRaycasters) if (r != null) r.enabled = true;
                _pausedRaycasters.Clear();
            }
        }

        // ---- First person's ESC menu on its own (UIPanels.EscOnly) --------------------------------------------------
        // TaleSpire's ESC menu, settings window and question boxes go onto the isolated layer (each canvas in them,
        // re-checked as dropdown lists and the like appear) and keep their clicks; every other part of the UI is neither
        // drawn nor clickable, as with a radial menu.

        private bool _escIsolated;
        private readonly Dictionary<GameObject, int> _escLayers = new Dictionary<GameObject, int>();

        private bool UnderEscMenu(Transform t)
        {
            if (!_escIsolated) return false;
            foreach (var root in UIPanels.EscMenuRoots()) if (root != null && t.IsChildOf(root.transform)) return true;
            return false;
        }

        private void FixEscLayers()
        {
            foreach (var root in UIPanels.EscMenuRoots())
            {
                if (root == null) continue;
                // A nested canvas needs its own raycaster while the main canvas's is paused.
                var own = root.GetComponent<Canvas>();
                if (own != null && !own.isRootCanvas && root.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                    root.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                foreach (var c in root.GetComponentsInChildren<Canvas>(true))
                {
                    var go = c.gameObject;
                    if (go.layer == RadialLayer) continue;
                    if (!_escLayers.ContainsKey(go)) _escLayers[go] = go.layer;
                    go.layer = RadialLayer;
                }
            }
        }

        private void RestoreEscLayers()
        {
            foreach (var kv in _escLayers) if (kv.Key != null) kv.Key.layer = kv.Value;
            _escLayers.Clear();
        }

        private float _nextNestedScan;
        private const int KeptOffLayer = 14;   // ChatUI: not drawn by the menu camera

        /// <summary>
        /// The menu camera only draws the UI layer, and a nested canvas is drawn on its own layer. A few of TaleSpire's
        /// nested canvases sit on other layers (the right-hand floor slider), so they never appeared in VR.
        /// </summary>
        private void FixNestedCanvasLayers()
        {
            foreach (var root in _redirected.Keys)
            {
                if (root == null || root.renderMode != RenderMode.ScreenSpaceCamera || root.worldCamera != UICamera) continue;
                foreach (var nested in root.GetComponentsInChildren<Canvas>(true))
                {
                    if (nested == root || nested.gameObject.layer == UILayer || nested.gameObject.layer == KeptOffLayer
                        || nested.gameObject.layer == RadialLayer) continue;
                    if (_radialCanvas != null && nested.transform.IsChildOf(_radialCanvas.transform)) continue;   // FixRadialNestedLayers
                    string path = Diagnostics.PathOf(nested.transform);
                    if (path.Contains("TextBubble") || path.Contains("GMOverlay"))
                    {
                        // Screen-pinned chat bubbles and the GM overlay frame: wrong in VR, keep them off the panel.
                        nested.gameObject.layer = KeptOffLayer;
                        continue;
                    }
                    Plugin.Log.LogInfo($"UI canvas layer {LayerMask.LayerToName(nested.gameObject.layer)} -> UI: '{path}'");
                    nested.gameObject.layer = UILayer;
                }
            }
        }

        // ---- A cursor ring drawn into the UI texture where TaleSpire thinks the mouse is ------------------
        private RectTransform _cursor;

        private void BuildCursor()
        {
            var go = new GameObject("TaleSpireVR_UICursorCanvas", typeof(RectTransform));
            DontDestroyOnLoad(go);
            go.layer = RadialLayer;   // drawn with the radial menu too
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = UICamera;
            canvas.planeDistance = PlaneDistance - 1f;
            canvas.sortingOrder = 32767;

            var img = new GameObject("Cursor", typeof(RectTransform));
            img.layer = RadialLayer;
            img.transform.SetParent(go.transform, false);
            _cursor = (RectTransform)img.transform;
            _cursor.anchorMin = Vector2.zero;
            _cursor.anchorMax = Vector2.zero;
            _cursor.pivot = new Vector2(0.5f, 0.5f);
            var raw = img.AddComponent<UnityEngine.UI.RawImage>();
            raw.texture = MakeRing(128);
            raw.raycastTarget = false;
            _cursorCanvas = canvas;
            _redirected[canvas] = UILayer;   // never redirect our own canvas
        }

        private static Texture2D MakeRing(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "TaleSpireVR_CursorRing", wrapMode = TextureWrapMode.Clamp };
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;   // 0 centre .. 1 edge
                float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.75f) / 0.12f);
                float outline = Mathf.Clamp01(1f - Mathf.Abs(d - 0.75f) / 0.22f);
                float dot = Mathf.Clamp01(1f - d / 0.14f);
                float a = Mathf.Max(Mathf.Max(ring, dot), outline * 0.85f);
                var col = Color.Lerp(Color.black, new Color(1f, 0.9f, 0.3f), Mathf.Max(ring, dot));
                col.a = a;
                tex.SetPixel(x, y, col);
            }
            tex.Apply();
            return tex;
        }

        private void UpdateCursor()
        {
            if (_cursor == null) return;
            var pos = VRPointer.UiCursor;
            bool show = pos.HasValue;
            if (_cursor.gameObject.activeSelf != show) _cursor.gameObject.SetActive(show);
            if (!show) return;
            float size = 26f * Height / 1080f;
            _cursor.sizeDelta = new Vector2(size, size);
            _cursor.anchoredPosition = pos.Value;
        }

        private void OnDestroy()
        {
            foreach (var kv in _redirected)
            {
                if (kv.Key == null) continue;
                kv.Key.renderMode = RenderMode.ScreenSpaceOverlay;
                kv.Key.gameObject.layer = kv.Value;
            }
            if (Texture != null) Texture.Release();
        }
    }
}
