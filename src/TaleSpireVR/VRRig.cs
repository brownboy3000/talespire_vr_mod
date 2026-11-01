using Bounce.Singletons;
using Bouncyrock.Physics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace TaleSpireVR
{
    /// <summary>
    /// Puts TaleSpire's own camera inside a tracking-space rig that we control.
    ///
    /// On a board the rig is anchored to TaleSpire's camera root (CameraController.ControllerPosition), so the
    /// game's own camera features (camera height, jump-to-creature, go-to-board-center, cut plane) keep working.
    /// We move that root with the sticks, and keep our own yaw / scale / offset on top of it.
    /// </summary>
    [DefaultExecutionOrder(30000)]
    internal class VRRig : MonoBehaviour
    {
        public Camera GameCamera { get; private set; }
        public Transform RigTransform { get; private set; }
        public bool OnBoard { get; private set; }

        /// <summary>World units per real meter.</summary>
        public float Scale { get; private set; } = 1f;
        public float Yaw { get; private set; }

        public Transform Head => GameCamera != null ? GameCamera.transform : null;
        public bool HeadTracked { get; private set; }
        public Vector3 HeadLocalPosition { get; private set; } = new Vector3(0f, 1.6f, 0f);
        public Quaternion HeadLocalRotation { get; private set; } = Quaternion.identity;

        private void OnEnable() { Application.onBeforeRender += OnBeforeRender; }
        private void OnDisable() { Application.onBeforeRender -= OnBeforeRender; }
        private void OnBeforeRender() { UpdateHeadPose(); }

        /// <summary>Drive the camera from the HMD pose ourselves (Unity's implicit tracking is disabled on it).</summary>
        private void UpdateHeadPose()
        {
            if (GameCamera == null) return;
            var hmd = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (!hmd.isValid) hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (DebugHead.HasValue)
            {
                HeadLocalPosition = DebugHead.Value.position;
                HeadLocalRotation = DebugHead.Value.rotation;
                HeadTracked = true;
            }
            else if (!hmd.isValid) return;
            else if (hmd.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 p) &&
                hmd.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion r))
            {
                HeadLocalPosition = p;
                HeadLocalRotation = r;
                HeadTracked = true;
            }
            else if (hmd.TryGetFeatureValue(CommonUsages.devicePosition, out p) &&
                     hmd.TryGetFeatureValue(CommonUsages.deviceRotation, out r))
            {
                HeadLocalPosition = p;
                HeadLocalRotation = r;
                HeadTracked = true;
            }
            var t = GameCamera.transform;
            if (t.parent == RigTransform)
            {
                t.localPosition = HeadLocalPosition;
                t.localRotation = HeadLocalRotation;
            }
        }

        private Transform _originalParent;
        private Camera _overlay;
        private Vector3 _menuCamPos;
        private bool _needsRecenter;
        private CameraController _boardController;
        private Vector3 _offset;           // rig origin relative to TaleSpire's camera root
        private bool _snapReady = true;
        private float _baseNear = 0.05f, _baseFar = 3000f;
        private int _ipdLogFrames;

        private void LateUpdate()
        {
            var cam = FindGameCamera();
            if (cam != GameCamera) AttachTo(cam);
            if (GameCamera == null) return;

            bool hasController = SimpleSingletonBehaviour<CameraController>.HasInstance;
            var controller = hasController ? SimpleSingletonBehaviour<CameraController>.Instance : null;
            if (controller != _boardController)
            {
                _boardController = controller;
                if (controller != null) EnterBoard();
            }
            OnBoard = controller != null;

            UpdateHeadPose();
            if (!OnBoard && _needsRecenter && HeadTracked)
            {
                // Put your head exactly where TaleSpire's menu camera was.
                RigTransform.rotation = Quaternion.Euler(0f, Yaw, 0f);
                Vector3 headFlat = HeadLocalRotation * Vector3.forward;
                float headYaw = Mathf.Atan2(headFlat.x, headFlat.z) * Mathf.Rad2Deg;
                Yaw -= headYaw;
                RigTransform.rotation = Quaternion.Euler(0f, Yaw, 0f);
                RigTransform.position = _menuCamPos - RigTransform.rotation * (HeadLocalPosition * Scale);
                _needsRecenter = false;
            }
            if (OnBoard)
            {
                if (!FirstPerson) UpdateBoard();
                HidePivotRing(controller);
            }
            ApplyRig();
            LogIpdOnce();
        }

        private static Camera FindGameCamera()
        {
            if (SimpleSingletonBehaviour<CameraController>.HasInstance)
            {
                var c = ActiveCameraManager.ActiveCamera;
                if (c != null) return c;
                return CameraController.GetCamera();
            }
            var main = Camera.main;
            if (main != null && main.isActiveAndEnabled) return main;
            Camera best = null;
            foreach (var c in Camera.allCameras)
            {
                if (c.stereoTargetEye == StereoTargetEyeMask.None) continue;
                if (best == null || c.depth < best.depth) best = c;
            }
            return best;
        }

        private void AttachTo(Camera cam)
        {
            Detach();
            GameCamera = cam;
            if (cam == null) return;

            var rigGo = new GameObject("TaleSpireVR_Rig");
            RigTransform = rigGo.transform;
            _originalParent = cam.transform.parent;
            if (_originalParent != null)
            {
                RigTransform.SetParent(_originalParent, false);
            }
            else if (cam.gameObject.scene.IsValid())
            {
                SceneManager.MoveGameObjectToScene(rigGo, cam.gameObject.scene);
            }

            // Start the rig where the flat camera was, at eye height, facing the same way.
            var p = cam.transform.position;
            Yaw = cam.transform.eulerAngles.y;
            Scale = 1f;
            _menuCamPos = p;
            _needsRecenter = true;
            RigTransform.position = p - Vector3.up * 1.6f;
            RigTransform.rotation = Quaternion.Euler(0f, Yaw, 0f);

            _baseNear = Mathf.Max(0.01f, cam.nearClipPlane);
            _baseFar = Mathf.Max(100f, cam.farClipPlane);

            cam.transform.SetParent(RigTransform, false);
            cam.transform.localPosition = HeadLocalPosition;
            cam.transform.localRotation = HeadLocalRotation;
            cam.stereoTargetEye = StereoTargetEyeMask.Both;
            XRDevice.DisableAutoXRCameraTracking(cam, true);

            // Overlay camera for the mod's panels and lasers (drawn after the headset camera, on top of everything).
            var ovGo = new GameObject("TaleSpireVR_OverlayCamera");
            ovGo.transform.SetParent(cam.transform, false);
            _overlay = ovGo.AddComponent<Camera>();
            _overlay.clearFlags = CameraClearFlags.Depth;
            _overlay.cullingMask = 1 << VRLayers.Overlay;
            _overlay.depth = cam.depth + 10f;
            _overlay.stereoTargetEye = StereoTargetEyeMask.Both;
            _overlay.renderingPath = RenderingPath.Forward;
            _overlay.allowHDR = false;
            _overlay.allowMSAA = false;
            _overlay.useOcclusionCulling = false;
            XRDevice.DisableAutoXRCameraTracking(_overlay, true);
            _ipdLogFrames = 0;
            Plugin.Log.LogInfo($"VR rig attached to camera '{cam.name}' (scene '{cam.gameObject.scene.name}', parent '{(_originalParent ? _originalParent.name : "<root>")}') near={cam.nearClipPlane} far={cam.farClipPlane} path={cam.actualRenderingPath}");
        }

        private void Detach()
        {
            if (GameCamera != null && RigTransform != null && GameCamera.transform.parent == RigTransform)
            {
                GameCamera.transform.SetParent(_originalParent, true);
            }
            if (_overlay != null) Destroy(_overlay.gameObject);
            _overlay = null;
            if (RigTransform != null) Destroy(RigTransform.gameObject);
            RigTransform = null;
            GameCamera = null;
            _boardController = null;
            OnBoard = false;
        }

        // ---- First person (FirstPersonMode places the rig; your table view is kept and given back) -------------

        /// <summary>FirstPersonMode is placing the rig (sticks, zoom and TaleSpire's camera root are not used).</summary>
        public bool FirstPerson { get; private set; }
        private Vector3 _fpOrigin;
        private float _savedScale, _savedYaw, _savedSunk;
        private Vector3 _savedOffset, _savedAnchor;

        public void BeginFirstPerson()
        {
            _savedScale = Scale;
            _savedYaw = Yaw;
            _savedSunk = _sunk;
            _savedOffset = _offset;
            _savedAnchor = _lastAnchorTarget != Vector3.zero ? _lastAnchorTarget : CameraController.ControllerPosition;
            _fpOrigin = RigTransform != null ? RigTransform.position : Vector3.zero;
            FirstPerson = true;
        }

        public void SetFirstPersonPose(Vector3 origin, float yaw, float scale)
        {
            _fpOrigin = origin;
            Yaw = yaw;
            Scale = scale;
        }

        /// <summary>Back to the table view you left, moved sideways by how far the creature went (so it is where you'd expect).</summary>
        public void EndFirstPerson(Vector3 shift)
        {
            if (!FirstPerson) return;
            FirstPerson = false;
            Scale = _savedScale;
            Yaw = _savedYaw;
            _sunk = _savedSunk;
            _offset = _savedOffset;
            Vector3 anchor = _savedAnchor + new Vector3(shift.x, 0f, shift.z);
            if (OnBoard) CameraController.MoveToPosition(anchor, false);
            _lastAnchorTarget = anchor;
        }

        private void EnterBoard()
        {
            FirstPerson = false;
            Scale = Mathf.Clamp(VRConfig.StartScale.Value, VRConfig.MinScale.Value, VRConfig.MaxScale.Value);
            var fwd = CameraController.Forward;
            fwd.y = 0f;
            float camYaw = fwd.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(fwd).eulerAngles.y : 0f;
            // Face the way TaleSpire's camera faced, standing a little behind its focus point, with the board at table height.
            Vector3 headFlat = HeadLocalRotation * Vector3.forward;
            float headYaw = Mathf.Atan2(headFlat.x, headFlat.z) * Mathf.Rad2Deg;
            Yaw = camYaw - headYaw;
            var rot = Quaternion.Euler(0f, Yaw, 0f);
            Vector3 anchor = CameraController.ControllerPosition;
            Vector3 camFwd = Quaternion.Euler(0f, camYaw, 0f) * Vector3.forward;
            Vector3 desiredHead = anchor - camFwd * VRConfig.StandOff.Value * Scale;
            Vector3 origin = desiredHead - rot * new Vector3(HeadLocalPosition.x, 0f, HeadLocalPosition.z) * Scale;
            origin.y = anchor.y - VRConfig.TableHeight.Value * Scale;
            _offset = origin - anchor;
            _sunk = 0f;
            _lastAnchorTarget = Vector3.zero;
            _ipdLogFrames = 0;
            Plugin.Log.LogInfo($"Entered board: anchor={CameraController.ControllerPosition} yaw={Yaw:F1} scale={Scale}");
        }

        private void UpdateBoard()
        {
            float dt = Time.unscaledDeltaTime;
            Vector3 anchor = CameraController.ControllerPosition;
            Vector3 anchorTarget = anchor;

            bool debugDrive = Time.unscaledTime < DebugDriveUntil;

            // --- Move over the board (left stick), relative to where the head faces.
            Vector2 move = debugDrive ? DebugMove : Deadzone(SteamVRInput.Move.Value, 0.15f);
            if (move != Vector2.zero && Head != null)
            {
                Vector3 f = Head.forward; f.y = 0f;
                if (f.sqrMagnitude < 1e-4f) f = RigTransform.forward;
                f.Normalize();
                Vector3 r = Vector3.Cross(Vector3.up, f);
                // Zoomed in: the starting speed. Zoomed out: faster with the zoom, so big boards stay quick to cross.
                float speed = VRConfig.MoveSpeed.Value * (VRConfig.MoveSpeedFollowsZoom.Value ? Scale : Mathf.Max(Scale, VRConfig.StartScale.Value));
                anchorTarget += (f * move.y + r * move.x) * speed * dt;
            }

            // While a creature is held, the right stick turns the creature and B/A raise/lower it (CreatureHold).
            bool holding = CreatureHold.IsHolding;

            // --- Camera height (right stick up/down; same as Page Up / Page Down in the flat game).
            Vector2 zt = SteamVRInput.TurnHeight.Value;
            // (While the laser is on the menu panel, up/down scrolls the menu instead: see VRPointer.)
            float vertical = !VRPointer.StickScrollsMenu && Mathf.Abs(zt.y) > 0.25f && Mathf.Abs(zt.y) > Mathf.Abs(zt.x) ? zt.y : 0f;
            if (vertical != 0f)
            {
                float v = Mathf.Sign(vertical) * Mathf.InverseLerp(0.25f, 1f, Mathf.Abs(vertical));
                float dy = v * VRConfig.CameraHeightSpeed.Value * Mathf.Max(1f, Scale / VRConfig.StartScale.Value) * dt;
                if (dy < 0f)
                {
                    // TaleSpire's camera stops at the bottom of the board; past that, lower yourself instead so you
                    // can reach the ground and look underneath (up to MaxSink real meters).
                    float down = Mathf.Max(dy, -Mathf.Max(0f, anchorTarget.y));
                    anchorTarget.y += down;
                    float rest = Mathf.Max(dy - down, -Mathf.Max(0f, MaxSink * Scale - _sunk));
                    _offset.y += rest;
                    _sunk -= rest;
                }
                else
                {
                    float back = Mathf.Min(dy, _sunk);   // come back up out of the ground first
                    _offset.y += back;
                    _sunk -= back;
                    anchorTarget.y = Mathf.Min(anchorTarget.y + dy - back, 1024f);
                }
            }

            // --- Zoom (left B = in, left A = out): scale the world around the point you are looking at.
            float zoom = holding ? 0f : (SteamVRInput.ZoomIn.Held ? 1f : 0f) - (SteamVRInput.ZoomOut.Held ? 1f : 0f);
            if (debugDrive) zoom = DebugZoom;
            if (zoom != 0f && Head != null)
            {
                float k = Mathf.Pow(2f, -zoom * VRConfig.ZoomSpeed.Value * dt);
                float newScale = Mathf.Clamp(Scale * k, VRConfig.MinScale.Value, VRConfig.MaxScale.Value);
                k = newScale / Scale;
                if (!Mathf.Approximately(k, 1f))
                {
                    Vector3 pivot = GazePoint(anchor.y);
                    Vector3 origin = anchorTarget + _offset;
                    Vector3 newOrigin = pivot + (origin - pivot) * k;
                    Vector3 newAnchor = new Vector3(pivot.x + (anchorTarget.x - pivot.x) * k, anchorTarget.y, pivot.z + (anchorTarget.z - pivot.z) * k);
                    _offset = newOrigin - newAnchor;
                    anchorTarget = newAnchor;
                    Scale = newScale;
                    _sunk *= k;
                }
            }

            // --- Turn (right stick X): snap or smooth, around the head. Not while the laser is on the menu panel
            // (the stick scrolls the menu then); a stick still pushed when the laser leaves must recentre first.
            bool scrolling = VRPointer.StickScrollsMenu;
            if (scrolling && Mathf.Abs(zt.x) > 0.3f) _snapReady = false;
            float turnInput = !holding && !scrolling && Mathf.Abs(zt.x) > 0.2f && Mathf.Abs(zt.x) > Mathf.Abs(zt.y) ? zt.x : 0f;
            float turn = 0f;
            if (VRConfig.SmoothTurn.Value)
            {
                turn = turnInput * VRConfig.SmoothTurnSpeed.Value * dt;
            }
            else
            {
                if (Mathf.Abs(turnInput) > 0.7f && _snapReady) { turn = Mathf.Sign(turnInput) * VRConfig.SnapTurnDegrees.Value; _snapReady = false; }
                if (Mathf.Abs(zt.x) < 0.3f) _snapReady = true;
            }
            if (DebugTurnDegrees != 0f) { turn = DebugTurnDegrees; DebugTurnDegrees = 0f; }
            if (turn != 0f && Head != null)
            {
                Vector3 origin = anchorTarget + _offset;
                Vector3 head = Head.position;
                head.y = origin.y;
                Vector3 newOrigin = head + Quaternion.Euler(0f, turn, 0f) * (origin - head);
                _offset = newOrigin - anchorTarget;
                Yaw += turn;
            }

            if (anchorTarget != anchor)
            {
                CameraController.MoveToPosition(anchorTarget, false);
            }
            _lastAnchorTarget = anchorTarget;
        }

        private Vector3 _lastAnchorTarget;

        /// <summary>How far (board units) the stick has lowered you below the bottom of TaleSpire's camera range.</summary>
        private float _sunk;
        private const float MaxSink = 5f;   // real meters

        private readonly System.Collections.Generic.List<Renderer> _pivotRings = new System.Collections.Generic.List<Renderer>();
        private CameraController _pivotRingsFor;

        /// <summary>
        /// TaleSpire marks its camera pivot with a glowing circle under the camera mode's rotator. In VR the camera
        /// is your head, so that circle just floats in mid-air; hide it.
        /// </summary>
        private void HidePivotRing(CameraController controller)
        {
            if (controller == null) return;
            if (_pivotRingsFor != controller)
            {
                _pivotRingsFor = controller;
                _pivotRings.Clear();
                foreach (var r in controller.transform.root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var p = r.transform.parent;
                    if (r.sharedMaterial == null || r.sharedMaterial.shader == null) continue;
                    if (r.sharedMaterial.shader.name == "Indicators/Circle" && p != null && p.name.StartsWith("MODE_"))
                        _pivotRings.Add(r);
                }
                Plugin.Log.LogInfo($"Hiding {_pivotRings.Count} camera pivot ring(s)");
            }
            foreach (var r in _pivotRings)
                if (r != null && r.enabled) r.enabled = false;
        }

        /// <summary>Back to the default zoom, standing behind TaleSpire's camera focus point facing the same way.</summary>
        public void ResetBoardView()
        {
            if (OnBoard) EnterBoard();
        }

        /// <summary>Debug: stand-ins for the sticks / zoom buttons (cmd "drive" and "turn"), so views can be tested without controllers.</summary>
        public static Vector2 DebugMove;
        public static float DebugZoom, DebugDriveUntil, DebugTurnDegrees;

        /// <summary>Debug ("head y pitch [yaw]"): a fixed head pose in tracking space instead of the headset's.</summary>
        public static Pose? DebugHead;

        /// <summary>Debug: lift the whole rig by some real-world meters (for testing with the headset on a desk).</summary>
        public void DebugRaise(float meters)
        {
            _offset += Vector3.up * meters * Scale;
            Plugin.Log.LogInfo($"Debug raise {meters} m -> offset {_offset}");
        }

        private Vector3 GazePoint(float floorHeight)
        {
            var ray = new Ray(Head.position, Head.forward);
            float reach = GameCamera != null ? GameCamera.farClipPlane : 300f;
            try
            {
                var rc = CameraController.Raycast;
                if (rc != null && rc.GetRayPoint(ray, out HitInfo hit, BouncePhysicsLayers.QueryAll, 0.05f)
                    && Vector3.Distance(ray.origin, (Vector3)hit.point) < reach)
                    return hit.point;
            }
            catch (System.Exception e) { Plugin.Log.LogDebug("Gaze raycast failed: " + e.Message); }
            var plane = new Plane(Vector3.up, -floorHeight);
            if (plane.Raycast(ray, out float enter) && enter < reach) return ray.GetPoint(enter);
            // Looking at the sky or the horizon: zoom around the board point straight below you. (Zooming around a
            // point in mid-air dragged you up and away from the board.)
            return new Vector3(Head.position.x, floorHeight, Head.position.z);
        }

        private void ApplyRig()
        {
            if (RigTransform == null) return;
            if (OnBoard && FirstPerson) RigTransform.position = _fpOrigin;
            else if (OnBoard)
            {
                Vector3 anchor = CameraController.ControllerPosition;
                // MoveToPosition is applied on TaleSpire's next LateUpdate; use the target we just requested so the
                // rig does not lag a frame behind the sticks.
                if (_lastAnchorTarget != Vector3.zero) anchor = _lastAnchorTarget;
                RigTransform.position = anchor + _offset;
            }
            RigTransform.rotation = Quaternion.Euler(0f, Yaw, 0f);
            Vector3 parentScale = RigTransform.parent != null ? RigTransform.parent.lossyScale : Vector3.one;
            RigTransform.localScale = new Vector3(Scale / parentScale.x, Scale / parentScale.y, Scale / parentScale.z);

            var cam = GameCamera;
            cam.nearClipPlane = Mathf.Max(0.01f, 0.03f * Scale);
            cam.farClipPlane = OnBoard && FirstPerson ? Mathf.Max(30f, VRConfig.FPDrawDistance.Value)
                             : OnBoard ? BoardDrawDistance() : Mathf.Max(_baseFar, 300f * Scale);
            ApplyShadowDistance();
            if (cam.targetTexture != null) cam.targetTexture = null;
            cam.cullingMask &= ~(1 << VRLayers.Overlay);
            if (_overlay != null)
            {
                _overlay.nearClipPlane = cam.nearClipPlane;
                _overlay.farClipPlane = cam.farClipPlane;
                _overlay.depth = cam.depth + 10f;
            }
        }

        /// <summary>
        /// Far clip in board units. The draw distance is set in real meters, so it looks the same at any zoom: zoomed in,
        /// fewer board units are drawn (TaleSpire culls whole zones against this frustum); zoomed out, it is capped.
        /// It always reaches well past the board below you, however high you are, so the board can't vanish.
        /// </summary>
        private float BoardDrawDistance()
        {
            float far = Mathf.Max(1f, VRConfig.DrawDistance.Value) * Scale;
            if (Head != null) far = Mathf.Max(far, 3f * Mathf.Abs(Head.position.y - CameraController.ControllerPosition.y) + 30f);
            float max = Mathf.Max(MinDrawDistance, VRConfig.MaxDrawDistance.Value);
            return Mathf.Clamp(far, MinDrawDistance, max);
        }

        private const float MinDrawDistance = 60f;
        private float _shadowBase = -1f, _shadowWritten = -1f;

        /// <summary>TaleSpire's own sun shadow distance (before our zoom-dependent limit).</summary>
        internal float TaleSpireShadowDistance => _shadowBase > 0f ? _shadowBase : QualitySettings.shadowDistance;

        /// <summary>
        /// Sun shadow distance, also set in real meters so it follows zoom, never beyond TaleSpire's own value. In
        /// multi-pass stereo every shadow cascade is drawn once per eye, so this is a big cost. Restored off the board.
        /// </summary>
        private void ApplyShadowDistance()
        {
            float current = QualitySettings.shadowDistance;
            if (_shadowBase < 0f || !Mathf.Approximately(current, _shadowWritten)) _shadowBase = current;   // TaleSpire (re)applied its setting
            float want = _shadowBase;
            if (OnBoard && FirstPerson && VRConfig.FPShadowDistance.Value > 0f)
                want = Mathf.Min(VRConfig.FPShadowDistance.Value, _shadowBase);   // board units: your eyes are at creature height
            else if (OnBoard && VRConfig.ShadowDistance.Value > 0f)
                want = Mathf.Clamp(VRConfig.ShadowDistance.Value * Scale, Mathf.Min(5f, _shadowBase), _shadowBase);
            if (!Mathf.Approximately(current, want)) QualitySettings.shadowDistance = want;
            _shadowWritten = want;
        }

        private void LogIpdOnce()
        {
            if (_ipdLogFrames++ != 90 || GameCamera == null) return;
            var cam = GameCamera;
            var l = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse.GetColumn(3);
            var r = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse.GetColumn(3);
            Vector3 eyeCenter = (l + r) * 0.5f;
            Plugin.Log.LogInfo($"Stereo check: eyeSeparation(world)={Vector3.Distance(l, r):F4} scale={Scale} stereoEnabled={cam.stereoEnabled} headTracked={HeadTracked} hmdLocal={HeadLocalPosition} camLocalPos={cam.transform.localPosition} camWorld={cam.transform.position} eyeCenter={eyeCenter} pixel={cam.pixelWidth}x{cam.pixelHeight} targetTex={(cam.targetTexture ? cam.targetTexture.name : "none")}");
        }

        private static Vector2 Deadzone(Vector2 v, float dz)
        {
            float m = v.magnitude;
            if (m < dz) return Vector2.zero;
            return v * ((m - dz) / (1f - dz) / m);
        }

        /// <summary>
        /// The VR mod switches off (Player mode): TaleSpire's camera back as it was (out of the rig, its clip planes and
        /// shadow distance, the pivot ring); TaleSpire places it again from the next frame.
        /// </summary>
        public void Suspend()
        {
            var cam = GameCamera;
            if (cam != null)
            {
                cam.nearClipPlane = _baseNear;
                cam.farClipPlane = _baseFar;
            }
            if (_shadowBase > 0f) QualitySettings.shadowDistance = _shadowBase;
            _shadowBase = _shadowWritten = -1f;
            foreach (var r in _pivotRings) if (r != null) r.enabled = true;
            _pivotRings.Clear();
            _pivotRingsFor = null;
            Detach();
        }

        private void OnDestroy() { Detach(); }
    }
}
