using HarmonyLib;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

namespace TaleSpireVR
{
    /// <summary>
    /// Cutscenes for players in VR. When the Game Master starts a cutscene, TaleSpire takes every player's camera over
    /// and shows them the Game Master's chosen view; the headset can't be taken over like that, so instead that view -
    /// exactly what TaleSpire would show on your monitor - plays on a floating screen in front of you, the size of the
    /// menu panel, with "The Game Master is showing a cutscene" written above it in TaleSpire's font. It goes away when the cutscene ends. Game
    /// Masters (who run the cutscene) don't get it.
    ///
    /// Like the held stream camera, its picture needs its own pass of TaleSpire's board renderer (Patches.HeldViewRenderPatch
    /// calls Render just before TaleSpire draws the board for the headset).
    /// </summary>
    [DefaultExecutionOrder(30017)]
    internal class CutsceneScreen : MonoBehaviour
    {
        public static CutsceneScreen Instance { get; private set; }
        /// <summary>Inside our own board pass (TaleSpire's picking is skipped then).</summary>
        public static bool Rendering { get; private set; }

        private const int Width = 1280, Height = 720;
        private const float Fps = 30f;
        private UIQuad _quad;
        private SkyText _title;
        private Camera _cam;
        private RenderTexture _rt;
        private bool _showing;
        private Vector3 _localPos;
        private Quaternion _localRot;
        private float _nextFrame;
        private string _lastError;

        public bool Showing => _showing;
        /// <summary>Debug ("cutscene force on|off"): show the screen with TaleSpire's current camera view, as in a cutscene.</summary>
        public static bool DebugForce;

        private void Awake()
        {
            Instance = this;
            _quad = new UIQuad("TaleSpireVR_CutsceneScreen", 3997);
            _title = new SkyText("TaleSpireVR_CutsceneTitle", 72f);
        }

        private void OnDisable()
        {
            _showing = false;
            if (_quad != null) _quad.Visible = false;
            if (_title != null) _title.Visible = false;
        }

        private void OnDestroy()
        {
            if (_quad != null) Destroy(_quad.Go);
            if (_title != null) Destroy(_title.Go);
            if (_cam != null) Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release();
        }

        private void LateUpdate()
        {
            var rig = VRManager.Instance != null ? VRManager.Instance.Rig : null;
            bool want = rig != null && rig.OnBoard && rig.RigTransform != null && ((!Roles.IsGm && Roles.Cutscene) || DebugForce);
            if (want != _showing)
            {
                _showing = want;
                if (want)
                {
                    PlaceInFront(rig);
                    _nextFrame = 0f;
                    Plugin.Log.LogInfo("Cutscene: the Game Master's view plays on a screen in front of you");
                }
                else Plugin.Log.LogInfo("Cutscene over");
            }
            _quad.Visible = want && _rt != null;
            _title.Visible = want;
            if (!want) return;

            Transform t = rig.RigTransform;
            Vector3 scale = new Vector3(VRConfig.PanelWidth.Value, VRConfig.PanelWidth.Value * Height / Width, 1f);
            _quad.SetTexture(_rt);
            _quad.SetRegion(new Rect(0f, 0f, 1f, 1f));
            _quad.T.position = t.TransformPoint(_localPos);
            _quad.T.rotation = t.rotation * _localRot;
            _quad.T.localScale = scale * rig.Scale;
            _title.Text = "The Game Master is showing a cutscene";
            Vector3 above = _localPos + _localRot * Vector3.up * (scale.y * 0.5f + 0.08f);
            _title.Place(t.TransformPoint(above), t.rotation * _localRot, 0.1f * rig.Scale);
        }

        private void PlaceInFront(VRRig rig)
        {
            Vector3 head = rig.HeadLocalPosition;
            Vector3 fwd = rig.HeadLocalRotation * Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            _localPos = head + fwd * VRConfig.PanelDistance.Value + Vector3.down * 0.05f;
            _localRot = Quaternion.LookRotation(_localPos - head, Vector3.up);
        }

        /// <summary>From HeldViewRenderPatch, just before TaleSpire culls and draws the board for the headset.</summary>
        internal void Render(BoardPresentation board, Camera src, Bounds isolationBounds, bool useIsolationBounds)
        {
            if (!_showing || Rendering || Time.unscaledTime < _nextFrame) return;
            var rig = VRManager.Instance != null ? VRManager.Instance.Rig : null;
            if (rig == null || src != rig.GameCamera) return;
            _nextFrame = Time.unscaledTime + 1f / Fps;
            Rendering = true;
            try
            {
                Ensure(src);
                // Where TaleSpire puts its own camera for the cutscene (its camera modes still run in VR; Patches.ApplyToCameraPatch keeps their pose).
                _cam.transform.SetPositionAndRotation(Patches.ApplyToCameraPatch.LastDesktopPosition, Patches.ApplyToCameraPatch.LastDesktopRotation);
                _cam.fieldOfView = Mathf.Clamp(Patches.ApplyToCameraPatch.LastDesktopFov, 10f, 120f);
                _cam.aspect = (float)Width / Height;
                _cam.clearFlags = src.clearFlags;
                _cam.backgroundColor = src.backgroundColor;
                _cam.cullingMask = src.cullingMask & ~(1 << VRLayers.Overlay);
                _cam.renderingPath = src.renderingPath;
                _cam.allowHDR = src.allowHDR;
                _cam.nearClipPlane = 0.1f;
                _cam.farClipPlane = Mathf.Max(300f, src.farClipPlane);
                using (var planes = Unity.Rendering.FrustumPlanes.BuildSoaPlanePackets(_cam, Allocator.TempJob))
                    board.CullAndRender(_cam, planes, isolationBounds, useIsolationBounds);
                _cam.Render();
            }
            catch (System.Exception e)
            {
                if (_lastError != e.Message) Plugin.Log.LogError("Cutscene screen: render failed: " + e);
                _lastError = e.Message;
            }
            finally { Rendering = false; }
        }

        private void Ensure(Camera src)
        {
            if (_rt == null)
            {
                _rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "TaleSpireVR_CutsceneView" };
                _rt.Create();
            }
            if (_cam != null) return;
            var go = new GameObject("TaleSpireVR_CutsceneCamera");
            DontDestroyOnLoad(go);
            go.SetActive(false);
            _cam = go.AddComponent<Camera>();
            _cam.enabled = false;   // rendered by hand, above
            _cam.stereoTargetEye = StereoTargetEyeMask.None;
            _cam.targetTexture = _rt;
            var srcPost = src.GetComponent<PostProcessLayer>();
            var res = srcPost != null ? AccessTools.Field(typeof(PostProcessLayer), "m_Resources")?.GetValue(srcPost) as PostProcessResources : null;
            if (res != null)
            {
                var post = go.AddComponent<PostProcessLayer>();
                post.Init(res);
                post.volumeLayer = srcPost.volumeLayer;
                post.volumeTrigger = go.transform;
                post.fog.enabled = srcPost.fog.enabled;
                post.antialiasingMode = PostProcessLayer.Antialiasing.SubpixelMorphologicalAntialiasing;
            }
            // TaleSpire's torch / lamp lights are command buffers on the headset camera: share them.
            foreach (var cb in src.GetCommandBuffers(CameraEvent.AfterLighting))
                if (cb.name.StartsWith("Ts")) _cam.AddCommandBuffer(CameraEvent.AfterLighting, cb);
            go.SetActive(true);
        }
    }
}
