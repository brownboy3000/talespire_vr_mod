using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// The crouch ("sneak") indicator: the edges of your view darken a little while you crouch in first person. A
    /// sphere around your head that turns with it, clear in the middle of your view and darker towards the edges.
    /// The stream window shows it too (it copies the left eye).
    /// </summary>
    internal class FPVignette
    {
        private const int Rings = 40, Segments = 48;
        private const float ClearDegrees = 14f, DarkDegrees = 46f, MaxDarkness = 0.93f;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private GameObject _go;
        private Renderer _renderer;
        private MaterialPropertyBlock _block;
        private float _amount;

        /// <summary>0 = off .. 1 = fully crouched. head: the camera to follow.</summary>
        public void Set(float amount, Transform head)
        {
            _amount = Mathf.MoveTowards(_amount, Mathf.Clamp01(amount), Time.unscaledDeltaTime / 0.2f);
            bool show = _amount > 0.001f && head != null;
            if (!show)
            {
                if (_go != null && _go.activeSelf) _go.SetActive(false);
                return;
            }
            if (_go == null) Build();
            if (_go.transform.parent != head) _go.transform.SetParent(head, false);
            if (!_go.activeSelf) _go.SetActive(true);
            _go.transform.localPosition = Vector3.zero;
            _go.transform.localRotation = Quaternion.identity;
            _go.transform.localScale = Vector3.one * 0.25f;   // meters (the head's scale is the rig's)
            _block.SetColor(ColorId, new Color(1f, 1f, 1f, _amount));
            _renderer.SetPropertyBlock(_block);
        }

        private void Build()
        {
            _go = new GameObject("TaleSpireVR_CrouchVignette");
            Object.DontDestroyOnLoad(_go);
            _go.layer = VRLayers.Overlay;
            var mesh = new Mesh { name = "TaleSpireVR_CrouchVignette" };
            int stride = Segments + 1;
            var v = new Vector3[(Rings + 1) * stride];
            var col = new Color32[v.Length];
            for (int ring = 0; ring <= Rings; ring++)
            {
                // Rings by angle from straight ahead (+z): 0 = the middle of your view, 180 = behind you.
                float a = ring / (float)Rings * 180f;
                float z = Mathf.Cos(a * Mathf.Deg2Rad), rr = Mathf.Sin(a * Mathf.Deg2Rad);
                float t = Mathf.InverseLerp(ClearDegrees, DarkDegrees, a);
                byte alpha = (byte)Mathf.RoundToInt(255f * MaxDarkness * t * t * (3f - 2f * t));
                for (int s = 0; s <= Segments; s++)
                {
                    float lon = s / (float)Segments * Mathf.PI * 2f;
                    v[ring * stride + s] = new Vector3(Mathf.Cos(lon) * rr, Mathf.Sin(lon) * rr, z);
                    col[ring * stride + s] = new Color32(4, 6, 12, alpha);
                }
            }
            var tri = new int[Rings * Segments * 6];
            int k = 0;
            for (int ring = 0; ring < Rings; ring++)
                for (int s = 0; s < Segments; s++)
                {
                    int i = ring * stride + s, j = i + stride;
                    tri[k++] = i; tri[k++] = j; tri[k++] = i + 1;
                    tri[k++] = i + 1; tri[k++] = j; tri[k++] = j + 1;
                }
            mesh.vertices = v;
            mesh.colors32 = col;
            mesh.triangles = tri;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            _go.AddComponent<MeshFilter>().sharedMesh = mesh;
            _renderer = _go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = new Material(VRHands.OverlayMaterial) { name = "TaleSpireVR_CrouchVignette", renderQueue = 4990 };
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _block = new MaterialPropertyBlock();
        }

        public void Destroy()
        {
            if (_go != null) Object.Destroy(_go);
        }
    }
}
