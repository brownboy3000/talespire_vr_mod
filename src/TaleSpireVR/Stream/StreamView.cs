using System;
using System.Collections;
using System.IO;
using Bouncyrock.Physics;
using HarmonyLib;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

namespace TaleSpireVR
{
    /// <summary>
    /// The stream view for players watching over Discord / OBS: a second desktop window (StreamWindow) that shows
    ///  - normally: the headset's left eye, cropped to 16:9 (like the game's own window), and
    ///  - while HELD: a fixed camera left where your head was when you held it. It keeps showing the live board
    ///    (creatures you move, doors, lighting) while you walk, zoom or take the headset off.
    /// Hold / release: press both trackpads together for a second. A green wireframe camera shows where the held
    /// camera is and what it sees (HeldCameraMarker).
    ///
    /// TaleSpire draws its tiles only for one camera, culled to that camera's view, so the held camera gets its own
    /// pass: HeldViewRenderPatch runs BoardPresentation.CullAndRender for it (with its own frustum and draw distance)
    /// right before TaleSpire does the headset's, and renders it at once, before the headset's culling overwrites
    /// the shared GPU buffers. Torch/lamp light command buffers are shared with the headset camera.
    /// </summary>
    [DefaultExecutionOrder(30040)]
    internal class StreamView : MonoBehaviour
    {
        public static StreamView Instance { get; private set; }

        private enum Mode { Follow, Held, Returning }
        private Mode _mode = Mode.Follow;

        /// <summary>True while the stream shows the held camera (held, or flying back to your view after a release).</summary>
        public bool Held => _mode != Mode.Follow && !_firstPerson;

        /// <summary>
        /// First person overrides the held camera: the stream shows your first-person view (the headset's), and a held
        /// camera (with its orbit) waits, paused, until you leave first person.
        /// </summary>
        private bool _firstPerson;

        public void SetFirstPerson(bool on)
        {
            if (_firstPerson == on) return;
            _firstPerson = on;
            if (on && _mode == Mode.Returning) _mode = Mode.Follow;   // was flying back anyway
            if (on && _marker != null) _marker.SetVisible(false);
            _nextFrameTime = 0f;
            Plugin.Log.LogInfo(on ? $"Stream view: first person (held camera {(_mode == Mode.Held ? "paused" : "not in use")})" : $"Stream view: first person over ({(_mode == Mode.Held ? "held camera resumes" + (_orbiting ? ", orbiting" : "") : "headset view")})");
        }

        /// <summary>Inside our own CullAndRender call for the held camera (TaleSpire's picking is skipped then).</summary>
        internal static bool RenderingHeld;

        /// <summary>Debug ("stream hold &lt;pitch&gt;"): hold looking down this many degrees instead of along the head.</summary>
        internal static float? DebugPitch;

        private const float ComboSeconds = 1f;
        private static readonly string[] SharedLightBuffers = { "TsDynamicLights", "TsNormalStaticLights", "TsRecentStaticLights", "TsIsolatedStaticLights" };

        private StreamWindow _window;
        private int _w, _h;
        private RenderTexture _mirrorRT, _heldRT;
        private Camera _heldCam;
        private PostProcessLayer _heldPost;
        private HeldCameraMarker _marker;
        private float _nextFrameTime;
        private int _pendingReadbacks;
        private float _heldFov, _heldNear, _heldFar, _heldShadow;
        private Vector4 _tan = new Vector4(0.9f, 0.9f, 0.5f, 0.5f);   // frustum extents: tan of left, right, bottom, top edges
        // Orbit (right stick click while held): circle the point the laser hit at a fixed height, facing it.
        private bool _orbiting;
        private Vector3 _orbitCenter;
        private float _orbitAngle, _orbitRadius, _orbitHeight, _orbitOmega;
        // Flight back to your view after a release.
        private Vector3 _retPos;
        private Quaternion _retRot;
        private Vector4 _retTan;
        private float _retNear, _retFar, _retShadow, _retArc, _returnTime, _returnDuration;
        private float _comboTime;
        private bool _comboFired;
        private bool _saveNextFrame;
        private int _heldFrames, _mirrorFrames;
        private float _cullMs, _renderMs;   // smoothed main-thread cost of one held frame (board pass, camera render)
        private BoardPresentation _heldBoard;
        private string _lastError;

        private void Start()
        {
            Instance = this;
            StreamWindow.PrimaryMonitorSize(out int mw, out int mh);
            _h = VRConfig.StreamHeight.Value > 0 ? VRConfig.StreamHeight.Value : mh;
            _h = Mathf.Clamp(_h, 360, 2160) & ~1;
            _w = Mathf.RoundToInt(_h * 16f / 9f) & ~1;
            Plugin.Log.LogInfo($"Stream view: {_w}x{_h} (monitor {mw}x{mh}), {VRConfig.StreamFps.Value} fps ({VRConfig.HeldViewFps.Value} held)");
            if (Roles.GmOrOffBoard) OpenWindow();
            else _playerBlocked = true;
            StartCoroutine(EndOfFrameLoop());
        }

        /// <summary>
        /// The stream view is a Game Master's tool: while you play as a player its window is closed and the held camera
        /// can't be used; it opens again when you are a Game Master (or back on the menus).
        /// </summary>
        private bool _playerBlocked;

        private void CheckRole()
        {
            bool allowed = Roles.GmOrOffBoard;
            if (!allowed && !_playerBlocked)
            {
                _playerBlocked = true;
                Release("you are playing as a player", smooth: false);
                if (_marker != null) _marker.SetVisible(false);
                SetWindow(false);
                Plugin.Log.LogInfo("Stream view: closed while you play as a player");
            }
            else if (allowed && _playerBlocked)
            {
                _playerBlocked = false;
                OpenWindow();
                Plugin.Log.LogInfo("Stream view: open again (Game Master)");
            }
        }

        private void OpenWindow()
        {
            if (_window == null) _window = new StreamWindow();
            if (_window.IsOpen) return;
            StreamWindow.PrimaryMonitorSize(out int mw, out int mh);
            // Start at three quarters of the screen; double-click it for borderless fullscreen (full resolution).
            int ch = Mathf.Min(_h, Mathf.RoundToInt(mh * 0.75f));
            _window.Open("TaleSpire VR - Stream View", Mathf.RoundToInt(ch * 16f / 9f), ch);
        }

        private void Update()
        {
            if (!VRManager.Active) return;
            var rig = VRManager.Instance != null ? VRManager.Instance.Rig : null;
            CheckRole();

            // Both trackpads (grips on controllers without trackpads) pressed together for a second toggles the held view.
            bool both = SteamVRInput.RotateModifier.Held && SteamVRInput.ElevateModifier.Held;
            if (!both || _playerBlocked || CreatureHold.IsHolding || _firstPerson || FirstPersonMode.Transitioning)
            {
                _comboTime = 0f;
                _comboFired = false;
            }
            else
            {
                _comboTime += Time.unscaledDeltaTime;
                if (!_comboFired && _comboTime >= ComboSeconds)
                {
                    _comboFired = true;
                    Toggle();
                }
            }

            if (_mode != Mode.Follow && (rig == null || !rig.OnBoard)) { _firstPerson = false; Release("left the board", smooth: false); }
            if (_orbiting && _mode == Mode.Held && !_firstPerson && _heldCam != null) UpdateOrbit(Time.unscaledDeltaTime);
            if (_mode == Mode.Returning)
            {
                _returnTime += Time.unscaledDeltaTime;
                if (_returnTime >= _returnDuration)
                {
                    _mode = Mode.Follow;
                    _nextFrameTime = 0f;
                    Plugin.Log.LogInfo("Stream view is back on your headset view");
                }
            }
        }

        private void LateUpdate()
        {
            var rig = VRManager.Instance != null ? VRManager.Instance.Rig : null;
            if (_marker != null && _mode == Mode.Held && !_firstPerson && rig != null && rig.Head != null)
            {
                _marker.SetVisible(VRConfig.ShowHeldCameraMarker.Value);
                _marker.Refresh(rig.Head.position, rig.Scale);
            }
        }

        public void Toggle()
        {
            if (_mode == Mode.Held) Release("toggled");
            else Hold();
        }

        public void Hold()
        {
            var rig = VRManager.Instance != null ? VRManager.Instance.Rig : null;
            if (rig == null || !rig.OnBoard || rig.GameCamera == null || _playerBlocked)
            {
                Plugin.Log.LogInfo("Stream view: hold ignored (not on a board, or not the Game Master)");
                return;
            }
            if (_window == null || !_window.IsOpen) OpenWindow();
            var src = rig.GameCamera;
            EnsureHeldCamera(src);
            StopOrbit(null);

            // Pose: where your eyes are, looking where you look, with the horizon kept level (no head tilt).
            Transform head = src.transform;
            Vector3 fwd = head.forward;
            if (DebugPitch.HasValue)
            {
                float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
                fwd = Quaternion.Euler(DebugPitch.Value, yaw, 0f) * Vector3.forward;
                DebugPitch = null;
            }
            Vector3 up = Mathf.Abs(Vector3.Dot(fwd, Vector3.up)) > 0.995f ? head.up : Vector3.up;
            _heldCam.transform.SetPositionAndRotation(head.position, Quaternion.LookRotation(fwd, up));

            // Framing: exactly the stream's crop of the left eye (what the players were just watching), or a set FOV.
            if (VRConfig.HeldViewFov.Value > 1f)
            {
                float t = Mathf.Tan(Mathf.Clamp(VRConfig.HeldViewFov.Value, 10f, 120f) * 0.5f * Mathf.Deg2Rad);
                _tan = new Vector4(t * _w / _h, t * _w / _h, t, t);
            }
            else _tan = EyeCropTangents(src);
            _heldFov = 2f * Mathf.Atan((_tan.z + _tan.w) * 0.5f) * Mathf.Rad2Deg;
            _heldNear = Mathf.Max(0.02f, 0.03f * rig.Scale);

            // Its own draw distance, independent of the headset's zoom-dependent one. Looking down at the board, nothing
            // in view can be farther than where the view's corners meet the board (with some depth to spare), so it
            // draws only that (each extra board area costs draw calls, the main cost here). Looking towards the
            // horizon: at least what you see now, and well past the point it looks at.
            var centre = new Ray(_heldCam.transform.position, _heldCam.transform.forward);
            float maxFar = Mathf.Max(60f, VRConfig.MaxDrawDistance.Value);
            bool centreHit = BoardPoint(centre, maxFar, out Vector3 centrePoint);
            float centreDist = centreHit ? Vector3.Distance(centre.origin, centrePoint) : 0f;
            float anchorY = CameraController.ControllerPosition.y;
            _heldFar = maxFar;
            SyncHeldCamera(src);
            float groundY = Mathf.Min(centreHit ? centrePoint.y : anchorY, anchorY) - 10f;
            float footprint = FarthestCorner(groundY);
            float auto = footprint > 0f
                ? 1.2f * footprint + 30f
                : Mathf.Max(src.farClipPlane, 2f * centreDist + 30f);
            _heldFar = VRConfig.HeldViewDrawDistance.Value > 0f ? VRConfig.HeldViewDrawDistance.Value : Mathf.Clamp(auto, 60f, maxFar);
            float baseShadow = rig.TaleSpireShadowDistance;
            _heldShadow = Mathf.Clamp(Mathf.Max(QualitySettings.shadowDistance, 1.5f * centreDist), Mathf.Min(5f, baseShadow), baseShadow);
            SyncHeldCamera(src);

            PlaceMarker();
            _heldBoard = null;
            _mode = Mode.Held;
            _nextFrameTime = 0f;
            Plugin.Log.LogInfo($"Stream view HELD at {head.position} looking {fwd} fov={_heldFov:F1} tan={_tan} near={_heldNear:F2} far={_heldFar:F0} shadow={_heldShadow:F0} (centre {(centreHit ? centreDist.ToString("F1") : "no hit")})");
        }

        /// <summary>
        /// Back to following the headset. smooth: the stream camera first flies from the held spot to your eyes (see
        /// UpdateReturn) instead of cutting, so the players aren't thrown into a new view.
        /// </summary>
        public void Release(string why, bool smooth = true)
        {
            if (!Held) return;
            StopOrbit(null);
            if (_marker != null) _marker.SetVisible(false);
            var rig = VRManager.Instance != null ? VRManager.Instance.Rig : null;
            if (!smooth || _heldCam == null || rig == null || !rig.OnBoard || rig.GameCamera == null)
            {
                _mode = Mode.Follow;
                _nextFrameTime = 0f;
                Plugin.Log.LogInfo("Stream view released (" + why + "): following the headset again");
                return;
            }
            if (_mode == Mode.Returning) return;

            var src = rig.GameCamera;
            var t = _heldCam.transform;
            _retPos = t.position;
            _retRot = t.rotation;
            _retTan = _tan;
            _retNear = _heldNear;
            _retFar = _heldFar;
            _retShadow = _heldShadow;
            EyePose(src, out Vector3 eyePos, out Quaternion eyeRot);
            // Half your fastest stick speed at this zoom, never more than 3 s, and at least long enough to turn gently.
            float speed = 0.5f * Mathf.Max(0.05f, VRConfig.MoveSpeed.Value) * (VRConfig.MoveSpeedFollowsZoom.Value ? rig.Scale : Mathf.Max(rig.Scale, VRConfig.StartScale.Value));
            float dist = Vector3.Distance(_retPos, eyePos);
            float angle = Quaternion.Angle(_retRot, eyeRot);
            _returnDuration = Mathf.Clamp(Mathf.Max(dist / speed, angle / 120f), 0.75f, 3f);
            // A gentle rise along the way, so a long flight goes over walls rather than through them.
            Vector3 flat = eyePos - _retPos;
            flat.y = 0f;
            _retArc = 0.15f * flat.magnitude;
            _returnTime = 0f;
            _mode = Mode.Returning;
            Plugin.Log.LogInfo($"Stream view released ({why}): flying back to your view over {_returnDuration:F2} s ({dist:F0} units, {angle:F0} deg)");
        }

        /// <summary>
        /// Right stick click (from VRPointer). While the view is held: not orbiting -> start orbiting the point the right
        /// laser hits (the top of a hill, a slab, a creature...; the ground, level 0, where it hits nothing); orbiting ->
        /// stop where it is. Returns true if it used the click (then it is not also a middle click for TaleSpire).
        /// </summary>
        public bool TryStickClick(Ray rightRay, bool rightOnBoard)
        {
            if (_mode != Mode.Held || _firstPerson || _heldCam == null) return false;
            if (_orbiting)
            {
                StopOrbit("stick click");
                SteamVRInput.Haptic(Hand.Right, 0.02f, 120f, 0.3f);
                return true;
            }
            if (!rightOnBoard || !PivotPoint(rightRay, out Vector3 pivot)) return false;
            StartOrbit(pivot);
            SteamVRInput.Haptic(Hand.Right, 0.03f, 160f, 0.5f);
            return true;
        }

        /// <summary>Where the laser meets the board (as its dot does), else the ground (level 0).</summary>
        private static bool PivotPoint(Ray ray, out Vector3 point)
        {
            try
            {
                var rc = CameraController.Raycast;
                if (rc != null && rc.GetRayPoint(ray, out HitInfo hit, BouncePhysicsLayers.QueryAll, 0.01f))
                {
                    point = hit.point;
                    return true;
                }
            }
            catch (Exception e) { Plugin.Log.LogDebug("Stream view pivot raycast failed: " + e.Message); }
            point = default;
            if (!new Plane(Vector3.up, -FloorY).Raycast(ray, out float d)) return false;
            point = ray.GetPoint(d);
            return true;
        }

        private void StartOrbit(Vector3 center)
        {
            var rig = VRManager.Instance.Rig;
            Vector3 p = _heldCam.transform.position;
            Vector3 off = p - center;
            off.y = 0f;
            _orbitCenter = center;
            _orbitRadius = off.magnitude;
            _orbitAngle = Mathf.Atan2(off.x, off.z);
            _orbitHeight = p.y;
            // A sixteenth of your fastest stick speed at this zoom, along the circle. Straight above the point it
            // turns in place, gently (at most 12.5 degrees a second).
            float maxSpeed = Mathf.Max(0.05f, VRConfig.MoveSpeed.Value) * (VRConfig.MoveSpeedFollowsZoom.Value ? rig.Scale : Mathf.Max(rig.Scale, VRConfig.StartScale.Value));
            _orbitOmega = Mathf.Min(maxSpeed / 16f / Mathf.Max(_orbitRadius, 0.5f * rig.Scale), 12.5f * Mathf.Deg2Rad);
            _orbiting = true;
            if (_marker != null) _marker.SetPivot(center);
            Plugin.Log.LogInfo($"Stream view orbiting {center} (radius {_orbitRadius:F1}, height {_orbitHeight:F1}, {_orbitOmega * Mathf.Rad2Deg:F2} deg/s, {maxSpeed / 16f:F2} units/s)");
        }

        private void StopOrbit(string why)
        {
            if (!_orbiting) return;
            _orbiting = false;
            if (_marker != null) _marker.SetPivot(null);
            if (why != null) Plugin.Log.LogInfo("Stream view orbit stopped (" + why + ")");
        }

        /// <summary>Move along the circle at a fixed height and turn (smoothly at first) to face the point.</summary>
        private void UpdateOrbit(float dt)
        {
            _orbitAngle += _orbitOmega * dt;
            var t = _heldCam.transform;
            Vector3 radial = new Vector3(Mathf.Sin(_orbitAngle), 0f, Mathf.Cos(_orbitAngle));
            Vector3 pos = _orbitCenter + radial * _orbitRadius;
            pos.y = _orbitHeight;
            Vector3 look = _orbitCenter - pos;
            Quaternion rot = t.rotation;
            if (look.sqrMagnitude > 1e-8f)
            {
                look.Normalize();
                // Looking straight down: keep "up" in the picture along the direction of travel so the view turns.
                Vector3 up = Mathf.Abs(Vector3.Dot(look, Vector3.up)) > 0.995f ? new Vector3(Mathf.Cos(_orbitAngle), 0f, -Mathf.Sin(_orbitAngle)) : Vector3.up;
                rot = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(look, up), 1f - Mathf.Exp(-dt / 0.4f));
            }
            t.SetPositionAndRotation(pos, rot);
            PlaceMarker();
        }

        /// <summary>The left eye's pose (the stream follows the left eye), including any head tilt.</summary>
        private static void EyePose(Camera src, out Vector3 pos, out Quaternion rot)
        {
            Matrix4x4 camToWorld = src.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse;
            pos = camToWorld.GetColumn(3);
            Vector3 fwd = -(Vector3)camToWorld.GetColumn(2);   // view matrices look down -z
            Vector3 up = camToWorld.GetColumn(1);
            rot = fwd.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(fwd, up) : src.transform.rotation;
        }

        /// <summary>
        /// The stream's crop of the left eye as frustum extents (tangents of the left, right, bottom and top edges): the
        /// eye's full width and the middle band of its height, exactly what CaptureMirror copies. Eye projections are
        /// off-centre, so this matches the stream picture better than a plain field of view.
        /// </summary>
        private Vector4 EyeCropTangents(Camera src)
        {
            Matrix4x4 p = src.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
            if (Mathf.Abs(p.m00) < 1e-4f || Mathf.Abs(p.m11) < 1e-4f) return new Vector4(0.9f, 0.9f, 0.5f, 0.5f);
            float right = (1f + p.m02) / p.m00, left = (1f - p.m02) / p.m00;
            float top = (1f + p.m12) / p.m11, bottom = (1f - p.m12) / p.m11;
            var eye = XRBoot.Display != null && XRBoot.Display.GetRenderPassCount() > 0 ? XRBoot.Display.GetRenderTextureForRenderPass(0) : null;
            float eyeAspect = eye != null ? eye.width / (float)eye.height : (left + right) / (top + bottom);
            float keep = Mathf.Clamp01(eyeAspect * _h / _w);
            float middle = (top - bottom) * 0.5f;          // tangent at the eye picture's vertical middle
            float half = (top + bottom) * 0.5f * keep;    // half the kept band
            return new Vector4(left, right, half - middle, middle + half);
        }

        /// <summary>One frame of the flight back to your view: ease in and out, aiming at where your eyes are now.</summary>
        private void UpdateReturn(Camera src)
        {
            float s = Mathf.Clamp01(_returnTime / Mathf.Max(0.01f, _returnDuration));
            float e = s * s * (3f - 2f * s);
            EyePose(src, out Vector3 eyePos, out Quaternion eyeRot);
            Vector3 pos = Vector3.Lerp(_retPos, eyePos, e) + Vector3.up * (_retArc * 4f * e * (1f - e));
            _heldCam.transform.SetPositionAndRotation(pos, Quaternion.Slerp(_retRot, eyeRot, e));
            _tan = Vector4.Lerp(_retTan, EyeCropTangents(src), e);
            _heldNear = Mathf.Lerp(_retNear, src.nearClipPlane, e);
            _heldFar = Mathf.Lerp(_retFar, Mathf.Max(src.farClipPlane, _heldNear + 1f), e);
            _heldShadow = Mathf.Lerp(_retShadow, QualitySettings.shadowDistance, e);
        }

        private void EnsureHeldCamera(Camera src)
        {
            if (_heldRT == null)
            {
                _heldRT = new RenderTexture(_w, _h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "TaleSpireVR_HeldView" };
                _heldRT.Create();
            }
            if (_heldCam == null)
            {
                var go = new GameObject("TaleSpireVR_HeldCamera");
                DontDestroyOnLoad(go);
                go.SetActive(false);
                _heldCam = go.AddComponent<Camera>();
                _heldCam.enabled = false;   // rendered by hand, see RenderHeld
                _heldCam.stereoTargetEye = StereoTargetEyeMask.None;
                _heldCam.targetTexture = _heldRT;

                var srcPost = src.GetComponent<PostProcessLayer>();
                var res = srcPost != null ? AccessTools.Field(typeof(PostProcessLayer), "m_Resources")?.GetValue(srcPost) as PostProcessResources : null;
                if (res != null)
                {
                    _heldPost = go.AddComponent<PostProcessLayer>();
                    _heldPost.Init(res);
                    _heldPost.volumeLayer = srcPost.volumeLayer;
                    _heldPost.volumeTrigger = go.transform;
                    _heldPost.stopNaNPropagation = srcPost.stopNaNPropagation;
                    _heldPost.fog.enabled = srcPost.fog.enabled;
                    _heldPost.fog.excludeSkybox = srcPost.fog.excludeSkybox;
                    // A still camera: SMAA gives clean edges without TAA's trails behind moving creatures.
                    var aa = srcPost.antialiasingMode;
                    _heldPost.antialiasingMode = aa == PostProcessLayer.Antialiasing.TemporalAntialiasing ? PostProcessLayer.Antialiasing.SubpixelMorphologicalAntialiasing : aa;
                    _heldPost.subpixelMorphologicalAntialiasing.quality = SubpixelMorphologicalAntialiasing.Quality.High;
                }
                else Plugin.Log.LogWarning("Stream view: no post-processing on the held camera (resources not found)");
                go.SetActive(true);
                _marker = new HeldCameraMarker();
            }
        }

        private void SyncHeldCamera(Camera src)
        {
            var c = _heldCam;
            c.clearFlags = src.clearFlags;
            c.backgroundColor = src.backgroundColor;
            c.cullingMask = src.cullingMask & ~(1 << VRLayers.Overlay);
            c.renderingPath = src.renderingPath;
            c.allowHDR = src.allowHDR;
            c.allowMSAA = false;
            c.depthTextureMode = src.depthTextureMode;
            c.useOcclusionCulling = src.useOcclusionCulling;
            c.stereoTargetEye = StereoTargetEyeMask.None;
            c.targetTexture = _heldRT;
            c.nearClipPlane = _heldNear;
            c.farClipPlane = _heldFar;
            // An explicit 16:9 off-centre frustum: rendering, culling and the green view lines all use exactly this
            // (left alone, Unity may take the aspect from the game window rather than the 16:9 stream picture).
            c.aspect = (float)_w / _h;
            float n = _heldNear;
            c.projectionMatrix = Matrix4x4.Frustum(-_tan.x * n, _tan.y * n, -_tan.z * n, _tan.w * n, n, _heldFar);
        }

        /// <summary>TaleSpire's torch/lamp lights are command buffers on the headset camera; use the same ones.</summary>
        private void SyncLightBuffers(Camera src)
        {
            var want = src.GetCommandBuffers(CameraEvent.AfterLighting);
            var have = _heldCam.GetCommandBuffers(CameraEvent.AfterLighting);
            foreach (var cb in have)
                if (Array.IndexOf(want, cb) < 0) _heldCam.RemoveCommandBuffer(CameraEvent.AfterLighting, cb);
            foreach (var cb in want)
                if (Array.IndexOf(SharedLightBuffers, cb.name) >= 0 && Array.IndexOf(have, cb) < 0)
                    _heldCam.AddCommandBuffer(CameraEvent.AfterLighting, cb);
        }

        /// <summary>From HeldViewRenderPatch, just before TaleSpire culls and draws the board for the headset.</summary>
        internal void RenderHeld(BoardPresentation board, Camera camera, Bounds isolationBounds, bool useIsolationBounds)
        {
            if (!Held || _heldCam == null || RenderingHeld) return;
            var rig = VRManager.Instance != null ? VRManager.Instance.Rig : null;
            if (rig == null || camera != rig.GameCamera) return;
            // Another board was loaded (or this one reloaded): the held spot belongs to the old one.
            if (_heldBoard == null) _heldBoard = board;
            else if (!ReferenceEquals(_heldBoard, board)) { Release("board changed", smooth: false); return; }
            if (!FrameDue()) return;

            float shadow = QualitySettings.shadowDistance;
            Pose? markerPose = null;
            RenderingHeld = true;
            try
            {
                if (_mode == Mode.Returning) UpdateReturn(camera);
                SyncHeldCamera(camera);
                if (_debugMarkerFrame)
                {
                    // Debug view of the marker: from a step behind the held camera, a little wider, overlay included.
                    _debugMarkerFrame = false;
                    _saveNextFrame = true;
                    markerPose = new Pose(_heldCam.transform.position, _heldCam.transform.rotation);
                    _heldCam.transform.position -= _heldCam.transform.forward * (_heldFar * 0.12f);
                    _heldCam.cullingMask |= 1 << VRLayers.Overlay;
                    float n = _heldNear;
                    Vector4 wide = _tan * 1.6f;
                    _heldCam.projectionMatrix = Matrix4x4.Frustum(-wide.x * n, wide.y * n, -wide.z * n, wide.w * n, n, _heldFar * 1.3f);
                }
                SyncLightBuffers(camera);
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                using (var planes = Unity.Rendering.FrustumPlanes.BuildSoaPlanePackets(_heldCam, Allocator.TempJob))
                    board.CullAndRender(_heldCam, planes, isolationBounds, useIsolationBounds);
                long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                QualitySettings.shadowDistance = _heldShadow;
                _heldCam.Render();
                // Blinking into / out of first person: the same darkness from above and below as in the headset.
                float closure = FirstPersonMode.BlinkClosure;
                if (closure > 0f)
                    FPBlink.Bands(_heldRT, closure, Mathf.Asin(Mathf.Clamp(_heldCam.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg, _tan.z, _tan.w);
                long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
                double ms = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                _cullMs = Mathf.Lerp(_cullMs, (float)((t1 - t0) * ms), 0.1f);
                _renderMs = Mathf.Lerp(_renderMs, (float)((t2 - t1) * ms), 0.1f);
                _heldFrames++;
            }
            catch (Exception e)
            {
                if (_lastError != e.Message) Plugin.Log.LogError("Stream view: held camera render failed: " + e);
                _lastError = e.Message;
            }
            finally
            {
                QualitySettings.shadowDistance = shadow;
                RenderingHeld = false;
                if (markerPose.HasValue) _heldCam.transform.SetPositionAndRotation(markerPose.Value.position, markerPose.Value.rotation);
            }
            Readback(_heldRT);
        }

        private IEnumerator EndOfFrameLoop()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                if (Held || !VRManager.Active) continue;
                try { CaptureMirror(); }
                catch (Exception e)
                {
                    if (_lastError != e.Message) Plugin.Log.LogError("Stream view: mirror capture failed: " + e);
                    _lastError = e.Message;
                }
            }
        }

        /// <summary>Copy the headset's left eye (this frame, menus and lasers included), cropped to 16:9, for the window.</summary>
        private void CaptureMirror()
        {
            var display = XRBoot.Display;
            if (display == null || display.GetRenderPassCount() < 1 || !FrameDue()) return;
            var eye = display.GetRenderTextureForRenderPass(0);
            if (eye == null) return;
            if (_mirrorRT == null)
            {
                _mirrorRT = new RenderTexture(_w, _h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "TaleSpireVR_StreamMirror" };
                _mirrorRT.Create();
            }
            // Full eye width, the middle band of its height (eyes are nearly square, the stream is 16:9).
            float keep = Mathf.Clamp01((eye.width / (float)eye.height) * _h / _w);
            Vector2 scale = new Vector2(1f, keep), offset = new Vector2(0f, (1f - keep) * 0.5f);
            if (SystemInfo.graphicsUVStartsAtTop) { scale.y = -keep; offset.y = 0.5f + keep * 0.5f; }   // eye images are stored top-down
            Graphics.Blit(eye, _mirrorRT, scale, offset);
            _mirrorFrames++;
            Readback(_mirrorRT);
        }

        /// <summary>Time for another stream frame (StreamFps, or HeldViewFps while held), the window is open and the GPU is keeping up.</summary>
        private bool FrameDue()
        {
            if (_window == null || !_window.IsOpen || _pendingReadbacks >= 2) return false;
            float now = Time.unscaledTime;
            if (now < _nextFrameTime) return false;
            float interval = 1f / Mathf.Clamp(Held ? VRConfig.HeldViewFps.Value : VRConfig.StreamFps.Value, 5, 120);
            _nextFrameTime = now - _nextFrameTime > interval ? now + interval : _nextFrameTime + interval;
            return true;
        }

        private void Readback(RenderTexture rt)
        {
            _pendingReadbacks++;
            int w = rt.width, h = rt.height;
            bool save = _saveNextFrame;   // the snapshot is this frame (not one already on its way back)
            _saveNextFrame = false;
            // Read in the texture's own format (RGBA). Asking for BGRA made Unity log an error on every frame
            // (B8G8R8A8_SRGB can't be read back), which TaleSpire's error console flashed as a red "!".
            AsyncGPUReadback.Request(rt, 0, req =>
            {
                _pendingReadbacks--;
                if (req.hasError) return;
                var data = req.GetData<byte>();
                if (_window != null) _window.Submit(data, w, h);
                if (save) SavePng(data, w, h);
            });
        }

        private static void SavePng(NativeArray<byte> data, int w, int h)
        {
            try
            {
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.LoadRawTextureData(data);
                tex.Apply();
                string path = Path.Combine(DebugSnapshot.DebugDir, "stream.png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Destroy(tex);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Stream snapshot failed: " + e.Message); }
        }

        /// <summary>Debug: save the next stream frame as _debug/stream.png.</summary>
        public void RequestSnapshot() { _saveNextFrame = true; }

        private static readonly Vector2[] ViewCorners = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };

        /// <summary>
        /// The green marker's corners (viewport): HeldViewFrameInset inside the picture's real edges on every side, so
        /// what it frames stays clear of anything players' stream windows put around the edges of the picture.
        /// </summary>
        private static Vector2 MarkerCorner(int i)
        {
            float m = Mathf.Clamp(VRConfig.HeldViewFrameInset.Value, 0f, 0.45f);
            return new Vector2(Mathf.Lerp(m, 1f - m, ViewCorners[i].x), Mathf.Lerp(m, 1f - m, ViewCorners[i].y));
        }

        private Vector3 CornerDirection(int i)
        {
            Vector3 pos = _heldCam.transform.position;
            return (_heldCam.ViewportToWorldPoint(new Vector3(ViewCorners[i].x, ViewCorners[i].y, 1f)) - pos).normalized;
        }

        /// <summary>Distance to the farthest point where the view's corners meet a level plane at y, or -1 if any corner misses it.</summary>
        private float FarthestCorner(float y)
        {
            var plane = new Plane(Vector3.up, -y);
            float farthest = 0f;
            for (int i = 0; i < 4; i++)
            {
                if (!plane.Raycast(new Ray(_heldCam.transform.position, CornerDirection(i)), out float d)) return -1f;
                farthest = Mathf.Max(farthest, d);
            }
            return farthest;
        }

        /// <summary>TaleSpire's ground, "level 0": the dirt layer, the lowest floor minis stand on (its ground plane is y = 0).</summary>
        private const float FloorY = 0f;

        /// <summary>
        /// The green marker for the held camera, from its actual projection (inset by HeldViewFrameInset, MarkerCorner):
        /// each corner line ends where that corner of the framed picture meets the floor; a corner that sees sky ends at
        /// the view's draw distance instead. The outline is the floor area framed: the floor cut by the (inset) view
        /// frustum, so its far edge follows the draw distance when the top corners see sky.
        /// </summary>
        private void PlaceMarker()
        {
            if (_marker == null) return;
            var t = _heldCam.transform;
            Vector3 pos = t.position;
            var floor = new Plane(Vector3.up, -FloorY);
            var ends = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                Vector2 c = MarkerCorner(i);
                Vector3 farCorner = _heldCam.ViewportToWorldPoint(new Vector3(c.x, c.y, _heldFar));
                var ray = new Ray(pos, (farCorner - pos).normalized);
                ends[i] = floor.Raycast(ray, out float d) && d <= Vector3.Distance(pos, farCorner) ? ray.GetPoint(d) : farCorner;
            }
            _marker.Place(pos, t.rotation, ends, FloorSection(FloorY));
        }

        /// <summary>Where the level plane y meets the marker's (inset) view frustum (near to far), as a closed polygon, or null.</summary>
        private Vector3[] FloorSection(float y)
        {
            var near = new Vector3[4];
            var far = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                Vector2 mc = MarkerCorner(i);
                near[i] = _heldCam.ViewportToWorldPoint(new Vector3(mc.x, mc.y, _heldNear));
                far[i] = _heldCam.ViewportToWorldPoint(new Vector3(mc.x, mc.y, _heldFar));
            }
            var pts = new System.Collections.Generic.List<Vector3>();
            void Cut(Vector3 a, Vector3 b)
            {
                float da = a.y - y, db = b.y - y;
                if (Mathf.Approximately(da, 0f)) { pts.Add(a); return; }
                if (da * db < 0f) pts.Add(a + (b - a) * (da / (da - db)));
            }
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Cut(near[i], near[j]);
                Cut(far[i], far[j]);
                Cut(near[i], far[i]);
            }
            // The frustum is convex, so the cut is a convex polygon: order its points around their centre.
            for (int i = pts.Count - 1; i > 0; i--)
                for (int k = 0; k < i; k++)
                    if ((pts[i] - pts[k]).sqrMagnitude < 1e-6f) { pts.RemoveAt(i); break; }
            if (pts.Count < 3) return null;
            Vector3 c = Vector3.zero;
            foreach (var p in pts) c += p;
            c /= pts.Count;
            pts.Sort((p, q) => Mathf.Atan2(p.z - c.z, p.x - c.x).CompareTo(Mathf.Atan2(q.z - c.z, q.x - c.x)));
            return pts.ToArray();
        }

        /// <summary>Where a ray meets the board (tiles, props, creatures), like the headset's gaze point.</summary>
        private static bool BoardPoint(Ray ray, float reach, out Vector3 point)
        {
            try
            {
                var rc = CameraController.Raycast;
                if (rc != null && rc.GetRayPoint(ray, out HitInfo hit, BouncePhysicsLayers.QueryAll, 0.05f)
                    && Vector3.Distance(ray.origin, (Vector3)hit.point) < reach)
                {
                    point = hit.point;
                    return true;
                }
            }
            catch (Exception e) { Plugin.Log.LogDebug("Stream view raycast failed: " + e.Message); }
            var plane = new Plane(Vector3.up, -CameraController.ControllerPosition.y);
            if (plane.Raycast(ray, out float enter) && enter < reach)
            {
                point = ray.GetPoint(enter);
                return true;
            }
            point = default;
            return false;
        }

        public string Status()
        {
            return $"stream window={(_window != null && _window.IsOpen)} {_w}x{_h} mode={_mode}{(_firstPerson ? " (first person)" : "")}{(_mode == Mode.Returning ? $" {_returnTime:F2}/{_returnDuration:F2}s" : "")}{(_orbiting ? $" orbiting {_orbitCenter} r={_orbitRadius:F1}" : "")} cam={(_heldCam != null ? _heldCam.transform.position.ToString("F1") : "-")} fov={_heldFov:F1} far={_heldFar:F0} heldFrames={_heldFrames} (cull {_cullMs:F1} ms, render {_renderMs:F1} ms) mirrorFrames={_mirrorFrames} pendingReadbacks={_pendingReadbacks} combo={_comboTime:F1}s";
        }

        /// <summary>Debug: override the held camera's draw distance.</summary>
        public void DebugSetFar(float far) { _heldFar = far; }

        /// <summary>Debug: render the next held frame with the green marker in it and save it (stream.png), to check that
        /// the marker's lines meet the picture's corners and the outline follows the visible floor.</summary>
        public void DebugMarkerSnap()
        {
            _debugMarkerFrame = true;
            if (_heldCam == null || _marker == null) return;
            // Where the marker's corner lines end, in the held picture (0..1; HeldViewFrameInset inside each edge).
            var sb = new System.Text.StringBuilder($"[stream] marker at {_heldCam.transform.position:F2}, its corners in the picture:");
            for (int i = 0; i < 4; i++) sb.Append($" {(Vector2)_heldCam.WorldToViewportPoint(_marker.End(i)):F3}");
            Plugin.Log.LogInfo(sb.ToString());
        }
        private bool _debugMarkerFrame;

        /// <summary>The VR mod switches off (Player mode): no held camera, no stream window.</summary>
        public void Suspend()
        {
            Release("the VR mod switched off", smooth: false);
            if (_marker != null) _marker.SetVisible(false);
            SetWindow(false);
        }

        public void Resume()
        {
            if (Roles.GmOrOffBoard) SetWindow(true);
        }

        public void SetWindow(bool open)
        {
            if (open) OpenWindow();
            else if (_window != null) _window.Close();
        }

        private void OnDestroy()
        {
            if (_window != null) _window.Close();
            if (_marker != null) _marker.Destroy();
            if (_heldCam != null) Destroy(_heldCam.gameObject);
            if (_heldRT != null) _heldRT.Release();
            if (_mirrorRT != null) _mirrorRT.Release();
        }

        private void OnApplicationQuit()
        {
            if (_window != null) _window.Close();
        }
    }
}
