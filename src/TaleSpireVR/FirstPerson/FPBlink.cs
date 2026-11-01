using System;
using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// The one-second "blink" into and out of first person: darkness slides in from above and below (0.5 s), the view
    /// changes while it is fully dark, then the darkness slides back out (0.5 s).
    ///
    /// In the headset it is a sphere around your head that stays level with the world (it does not turn with your
    /// head): the sky darkens downwards and the ground upwards until they meet at the horizon, so it feels like the
    /// world closing in rather than a shutter in front of your eyes. The stream window shows the same bands: the left
    /// eye copy contains the sphere, and the held stream camera draws matching bands from its own pitch (Bands).
    /// </summary>
    internal class FPBlink
    {
        private const float HalfSeconds = 0.5f;
        private const float SoftDegrees = 8f;
        private const int Rings = 36, Segments = 48;

        /// <summary>0 = open, 1 = fully dark.</summary>
        public float Closure
        {
            get => DebugClosure ?? _closure;
            private set => _closure = value;
        }
        private float _closure;
        /// <summary>Debug ("fp blink c|off"): hold the darkness at this amount (to look at it).</summary>
        public static float? DebugClosure;
        public bool Running => _phase != 0;

        private int _phase;   // 0 idle, 1 closing, 2 dark (switching), 3 opening
        private float _t;
        private int _hold;
        private Action _atDark;
        private GameObject _go;
        private Mesh _mesh;
        private Color32[] _colors;
        private float[] _ringLat;

        public void Start(Action atDark)
        {
            if (Running) return;
            _atDark = atDark;
            _phase = 1;
            _t = 0f;
        }

        /// <summary>Stop at once, open, without switching (the VR mod switching off).</summary>
        public void Cancel()
        {
            _phase = 0;
            _atDark = null;
            Closure = 0f;
            if (_go != null) _go.SetActive(false);
        }

        public void Update(float dt)
        {
            switch (_phase)
            {
                case 1:
                    _t += dt;
                    Closure = Ease(_t / HalfSeconds);
                    if (_t >= HalfSeconds) { Closure = 1f; _phase = 2; _hold = 3; }
                    break;
                case 2:
                    if (_atDark != null)
                    {
                        var a = _atDark;
                        _atDark = null;
                        try { a(); }
                        catch (Exception e) { Plugin.Log.LogError("First person: switch failed: " + e); }
                    }
                    // A few dark frames so the new view (rig moved, scale changed) is in place before it opens.
                    if (--_hold <= 0) { _phase = 3; _t = 0f; }
                    break;
                case 3:
                    _t += dt;
                    Closure = 1f - Ease(_t / HalfSeconds);
                    if (_t >= HalfSeconds) { Closure = 0f; _phase = 0; }
                    break;
            }
        }

        private static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>
        /// Elevation (degrees above the horizon) down to which the sky is dark, and below minus which the ground is.
        /// Quick at first (the top and bottom of the view are reached early), then steadily to the horizon.
        /// </summary>
        public static float EdgeDegrees(float closure)
        {
            if (closure <= 0f) return 90f + SoftDegrees;
            if (closure < 0.15f) return Mathf.Lerp(90f + SoftDegrees, 55f, closure / 0.15f);
            return Mathf.Lerp(55f, -SoftDegrees, (closure - 0.15f) / 0.85f);
        }

        /// <summary>Darkness (alpha) at an elevation for this closure.</summary>
        public static float Darkness(float elevationDegrees, float closure)
        {
            if (closure <= 0f) return 0f;
            return Mathf.Clamp01((Mathf.Abs(elevationDegrees) - EdgeDegrees(closure)) / SoftDegrees);
        }

        /// <summary>Keep the sphere on the head, level with the world (call after the head pose is final).</summary>
        public void Draw(VRRig rig)
        {
            bool show = Closure > 0f && rig != null && rig.Head != null;
            if (!show)
            {
                if (_go != null && _go.activeSelf) _go.SetActive(false);
                return;
            }
            if (_go == null) Build();
            if (_go.transform.parent != rig.Head) _go.transform.SetParent(rig.Head, false);
            if (!_go.activeSelf) _go.SetActive(true);
            _go.transform.localPosition = Vector3.zero;
            _go.transform.localScale = Vector3.one * 0.3f;   // meters (the head's scale is the rig's)
            _go.transform.rotation = Quaternion.identity;
            for (int ring = 0; ring <= Rings; ring++)
            {
                byte a = (byte)Mathf.RoundToInt(255f * Darkness(_ringLat[ring], Closure));
                for (int s = 0; s <= Segments; s++) _colors[ring * (Segments + 1) + s].a = a;
            }
            _mesh.colors32 = _colors;
        }

        /// <summary>Level it again after the head pose changed (just before rendering).</summary>
        public void Relevel()
        {
            if (_go != null && _go.activeSelf) _go.transform.rotation = Quaternion.identity;
        }

        private void Build()
        {
            _go = new GameObject("TaleSpireVR_Blink");
            UnityEngine.Object.DontDestroyOnLoad(_go);
            _go.layer = VRLayers.Overlay;
            _mesh = new Mesh { name = "TaleSpireVR_Blink" };
            int stride = Segments + 1;
            var v = new Vector3[(Rings + 1) * stride];
            _colors = new Color32[v.Length];
            _ringLat = new float[Rings + 1];
            for (int ring = 0; ring <= Rings; ring++)
            {
                float lat = Mathf.Lerp(-90f, 90f, ring / (float)Rings);
                _ringLat[ring] = lat;
                float y = Mathf.Sin(lat * Mathf.Deg2Rad), rr = Mathf.Cos(lat * Mathf.Deg2Rad);
                for (int s = 0; s <= Segments; s++)
                {
                    float lon = s / (float)Segments * Mathf.PI * 2f;
                    v[ring * stride + s] = new Vector3(Mathf.Cos(lon) * rr, y, Mathf.Sin(lon) * rr);
                    _colors[ring * stride + s] = new Color32(0, 0, 0, 0);
                }
            }
            var tri = new int[Rings * Segments * 6];
            int k = 0;
            for (int ring = 0; ring < Rings; ring++)
                for (int s = 0; s < Segments; s++)
                {
                    int a = ring * stride + s, b = a + stride;
                    tri[k++] = a; tri[k++] = b; tri[k++] = a + 1;
                    tri[k++] = a + 1; tri[k++] = b; tri[k++] = b + 1;
                }
            _mesh.vertices = v;
            _mesh.colors32 = _colors;
            _mesh.triangles = tri;
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = _go.AddComponent<MeshRenderer>();
            var mat = new Material(VRHands.OverlayMaterial) { name = "TaleSpireVR_Blink", renderQueue = 5000 };
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>
        /// The same darkness drawn into a flat camera picture (the held stream camera): each row of the picture is dark
        /// by its elevation, from the camera's pitch and the picture's vertical extents (tangents below / above centre).
        /// </summary>
        public static void Bands(RenderTexture rt, float closure, float pitchDegrees, float tanBottom, float tanTop)
        {
            if (closure <= 0f || rt == null) return;
            const int rows = 48;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.PushMatrix();
            GL.LoadOrtho();
            VRHands.OverlayMaterial.SetPass(0);
            GL.Begin(GL.QUADS);
            for (int i = 0; i < rows; i++)
            {
                float y0 = i / (float)rows, y1 = (i + 1) / (float)rows;
                float a0 = Darkness(pitchDegrees + Mathf.Atan(Mathf.Lerp(-tanBottom, tanTop, y0)) * Mathf.Rad2Deg, closure);
                float a1 = Darkness(pitchDegrees + Mathf.Atan(Mathf.Lerp(-tanBottom, tanTop, y1)) * Mathf.Rad2Deg, closure);
                if (a0 <= 0f && a1 <= 0f) continue;
                GL.Color(new Color(0f, 0f, 0f, a0)); GL.Vertex3(0f, y0, 0f); GL.Vertex3(1f, y0, 0f);
                GL.Color(new Color(0f, 0f, 0f, a1)); GL.Vertex3(1f, y1, 0f); GL.Vertex3(0f, y1, 0f);
            }
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        public void Destroy()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
        }
    }
}
