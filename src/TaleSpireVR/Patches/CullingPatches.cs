using HarmonyLib;
using Unity.Collections;
using UnityEngine;

namespace TaleSpireVR.Patches
{
    /// <summary>
    /// TaleSpire culls the board (tiles, props) itself, with planes from the camera's projectionMatrix. A VR camera's
    /// projectionMatrix is one symmetric frustum around the head, narrower than the two eyes' off-centre frustums (each
    /// eye sees further out on its own side, and the headset's eyes reach further down/up than the average), so tiles
    /// near the outer edges of the view were culled while still in view: black patches at the edge of view that came
    /// and went as you moved (most visible close up, in first person, on dark boards). The headset
    /// camera is culled with a frustum that contains both eyes' instead.
    /// </summary>
    [HarmonyPatch(typeof(Unity.Rendering.FrustumPlanes), nameof(Unity.Rendering.FrustumPlanes.BuildSoaPlanePackets), new[] { typeof(Camera), typeof(Allocator) })]
    internal static class StereoCullingPatch
    {
        private static bool Prefix(Camera cam, Allocator allocator, ref NativeArray<Unity.Rendering.FrustumPlanes.PlanePacket4> __result)
        {
            // (Not cam.stereoEnabled: TaleSpire has just given the camera its own render texture, which makes that false.)
            if (!VRManager.Active || cam == null || cam.stereoTargetEye != StereoTargetEyeMask.Both) return true;
            var rig = VRManager.Instance != null ? VRManager.Instance.Rig : null;
            if (rig == null || cam != rig.GameCamera) return true;
            if (Mathf.Abs(cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left).m00) < 1e-4f) return true;
            var planes = new NativeArray<Plane>(6, Allocator.Temp);
            try
            {
                Bounce.Mathematics.CullingUtilities.CalculateFrustumPlanes(StereoCulling.Matrix(cam), planes);
                __result = Unity.Rendering.FrustumPlanes.BuildSoaPlanePackets(planes, allocator);
            }
            finally { planes.Dispose(); }
            return false;
        }
    }

    internal static class StereoCulling
    {
        private static int _logs;

        /// <summary>
        /// projection x world-to-camera of one frustum around both eyes: the widest of the two eyes' extents on every
        /// side (plus 5%), from a point a little behind the head so each eye's frustum lies inside it.
        /// </summary>
        public static Matrix4x4 Matrix(Camera cam)
        {
            Tangents(cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left), out float l0, out float r0, out float b0, out float t0);
            Tangents(cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right), out float l1, out float r1, out float b1, out float t1);
            // Never narrower than TaleSpire's own frustum (on many headsets that one is already the wider).
            Tangents(cam.projectionMatrix, out float ml, out float mr, out float mb, out float mt);
            float left = Mathf.Max(Mathf.Max(l0, l1) * 1.05f, ml), right = Mathf.Max(Mathf.Max(r0, r1) * 1.05f, mr);
            float bottom = Mathf.Max(Mathf.Max(b0, b1) * 1.05f, mb), top = Mathf.Max(Mathf.Max(t0, t1) * 1.05f, mt);
            Vector3 le = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse.GetColumn(3);
            Vector3 re = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse.GetColumn(3);
            float back = Vector3.Distance(le, re) * 0.5f / Mathf.Max(0.2f, Mathf.Min(left, right)) * 1.2f + cam.nearClipPlane;
            float n = cam.nearClipPlane, f = cam.farClipPlane + back;
            Matrix4x4 proj = Matrix4x4.Frustum(-left * n, right * n, -bottom * n, top * n, n, f);
            // View matrices look down -z: the apex moves back along +z.
            Matrix4x4 view = Matrix4x4.Translate(new Vector3(0f, 0f, -back)) * cam.worldToCameraMatrix;
            if (_logs < 1)
            {
                _logs++;
                Plugin.Log.LogInfo($"Stereo culling: camera frustum tangents L{ml:F2} R{mr:F2} B{mb:F2} T{mt:F2}; left eye L{l0:F2} R{r0:F2} B{b0:F2} T{t0:F2}; right eye L{l1:F2} R{r1:F2} B{b1:F2} T{t1:F2}; culling with L{left:F2} R{right:F2} B{bottom:F2} T{top:F2} from {back:F3} behind the head");
            }
            return proj * view;
        }

        /// <summary>Tangents of a projection's left, right, bottom and top edges.</summary>
        private static void Tangents(Matrix4x4 p, out float left, out float right, out float bottom, out float top)
        {
            right = (1f + p.m02) / p.m00;
            left = (1f - p.m02) / p.m00;
            top = (1f + p.m12) / p.m11;
            bottom = (1f - p.m12) / p.m11;
        }
    }
}
