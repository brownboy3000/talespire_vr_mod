using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// TaleSpire picks tiles/props/minis by rendering object IDs and reading the pixels under the mouse.
    /// In VR that pass is moved onto this small hidden camera that sits on the controller and looks down the laser,
    /// so the pixel at its centre is exactly what you are pointing at.
    /// </summary>
    [DefaultExecutionOrder(30040)]
    internal class PickCamera : MonoBehaviour
    {
        public const int Size = 64;
        public const float Fov = 2f;

        public static Camera Cam { get; private set; }
        public static bool InUse => Cam != null && Cam.enabled;
        public static bool DebugDisabled;
        public static Vector2 Center => new Vector2(Size * 0.5f, Size * 0.5f);

        private RenderTexture _rt;

        private void Awake()
        {
            var go = new GameObject("TaleSpireVR_PickCamera");
            DontDestroyOnLoad(go);
            Cam = go.AddComponent<Camera>();
            _rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { name = "TaleSpireVR_Pick" };
            Cam.targetTexture = _rt;
            Cam.stereoTargetEye = StereoTargetEyeMask.None;
            Cam.cullingMask = 1 << 3;          // an unused layer: the camera itself draws nothing
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = Color.black;
            Cam.renderingPath = RenderingPath.DeferredShading;
            Cam.allowHDR = false;
            Cam.allowMSAA = false;
            Cam.useOcclusionCulling = false;
            Cam.fieldOfView = Fov;
            Cam.depth = -500;
            Cam.enabled = false;
        }

        private void LateUpdate() { UpdatePose(); }

        public void UpdatePose()
        {
            var rig = VRManager.Instance.Rig;
            bool use = VRManager.Active && rig != null && rig.OnBoard && rig.GameCamera != null && !DebugDisabled;
            if (Cam.enabled != use) Cam.enabled = use;
            if (!use) return;

            var ray = VRPointer.WorldRay;
            if (ray.direction.sqrMagnitude < 0.5f) return;
            Cam.transform.SetPositionAndRotation(ray.origin, Quaternion.LookRotation(ray.direction, rig.RigTransform.up));
            Cam.nearClipPlane = rig.GameCamera.nearClipPlane;
            Cam.farClipPlane = rig.GameCamera.farClipPlane;
            Cam.fieldOfView = Fov;
        }

        private void OnDestroy()
        {
            if (Cam != null) Destroy(Cam.gameObject);
            if (_rt != null) _rt.Release();
        }
    }
}
