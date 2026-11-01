using System.Collections.Generic;
using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// Green wireframe shown in the headset while the stream view is held: a small movie-camera icon at the held
    /// camera, pointing where it looks, four lines along the edges of its view that end where each corner of the stream
    /// picture meets the board's floor (level 0), and the outline of the floor area the stream shows - all a little
    /// inside the picture's real edges (StreamView.MarkerCorner, [Stream] HeldViewFrameInset). Drawn on the
    /// overlay layer, which the held camera never renders, so the stream does not show it; the overlay camera's far
    /// clip (your draw distance) cuts the lines like everything else.
    /// </summary>
    internal sealed class HeldCameraMarker
    {
        private static readonly Color Green = new Color(0.3f, 1f, 0.42f, 0.95f);

        private readonly GameObject _root;          // icon, posed at the camera and scaled with your zoom
        private readonly GameObject _worldRoot;     // view lines + board outline, in world space
        private readonly List<LineRenderer> _icon = new List<LineRenderer>();
        private readonly List<Vector3[]> _iconLocal = new List<Vector3[]>();   // each icon line's points around the lens tip
        private readonly LineRenderer[] _edges = new LineRenderer[4];
        private readonly LineRenderer _outline;

        private Vector3 _camPos;
        private readonly LineRenderer _pivot;   // level ring around the point the camera orbits
        private Vector3? _pivotPos;
        private const int PivotPoints = 32;
        private readonly Vector3[] _ends = new Vector3[4];
        private Vector3[] _outlinePts = new Vector3[0];
        private bool _hasOutline;

        public HeldCameraMarker()
        {
            _root = new GameObject("TaleSpireVR_HeldCameraIcon");
            _worldRoot = new GameObject("TaleSpireVR_HeldCameraView");
            Object.DontDestroyOnLoad(_root);
            Object.DontDestroyOnLoad(_worldRoot);

            // Icon in local units (real meters at your zoom), looking along +z with the lens tip at the origin.
            const float bx = 0.04f, by = 0.03f, zb = -0.17f, zf = -0.05f;
            Loop(Rect(bx, by, zb));                            // body back
            Loop(Rect(bx, by, zf));                            // body front
            for (int i = 0; i < 4; i++) Line(Rect(bx, by, zb)[i], Rect(bx, by, zf)[i]);
            var lensBack = Rect(0.018f, 0.018f, zf);
            var lensFront = Rect(0.036f, 0.022f, 0f);          // lens hood, flared and wide like the picture
            Loop(lensFront);
            for (int i = 0; i < 4; i++) Line(lensBack[i], lensFront[i]);
            Loop(Circle(new Vector3(0f, by + 0.03f, -0.14f), 0.03f));   // film reels on top
            Loop(Circle(new Vector3(0f, by + 0.025f, -0.08f), 0.025f));

            for (int i = 0; i < 4; i++) _edges[i] = NewLine(_worldRoot, true, 2);
            _outline = NewLine(_worldRoot, true, 4);
            _outline.loop = true;
            _pivot = NewLine(_worldRoot, true, PivotPoints);
            _pivot.loop = true;
            _pivot.gameObject.SetActive(false);

            VRLayers.SetRecursive(_root, VRLayers.Overlay);
            VRLayers.SetRecursive(_worldRoot, VRLayers.Overlay);
            SetVisible(false);
        }

        private bool _visible;

        public void SetVisible(bool on)
        {
            _visible = on;
            if (!on && _root.activeSelf) _root.SetActive(false);
            if (_worldRoot.activeSelf != on) _worldRoot.SetActive(on);
        }

        /// <summary>
        /// Place the marker for a camera at pos/rot. ends = where each corner line stops (on the floor, or at the view's
        /// draw distance); outline = the floor area the view shows, as a closed polygon (null if it shows no floor).
        /// </summary>
        public void Place(Vector3 pos, Quaternion rot, Vector3[] ends, Vector3[] outline)
        {
            _camPos = pos;
            _root.transform.SetPositionAndRotation(pos, rot);
            for (int i = 0; i < 4; i++)
            {
                _ends[i] = ends[i];
                _edges[i].SetPosition(0, pos);
                _edges[i].SetPosition(1, ends[i]);
            }
            _hasOutline = outline != null && outline.Length >= 3;
            _outlinePts = _hasOutline ? (Vector3[])outline.Clone() : new Vector3[0];
            if (_hasOutline)
            {
                _outline.positionCount = _outlinePts.Length;
                _outline.SetPositions(_outlinePts);
            }
            _outline.gameObject.SetActive(_hasOutline);
        }

        /// <summary>Debug: where corner line i ends.</summary>
        public Vector3 End(int i) => _ends[i];

        /// <summary>Show a small level ring at the point the camera orbits (null = not orbiting).</summary>
        public void SetPivot(Vector3? point)
        {
            _pivotPos = point;
            _pivot.gameObject.SetActive(point.HasValue);
        }

        /// <summary>Every frame: keep the icon a constant size for you and the lines readable from where you stand.</summary>
        public void Refresh(Vector3 head, float scale)
        {
            if (_pivotPos.HasValue)
            {
                // About 4 cm across at your zoom (bigger when far away, so it stays visible).
                Vector3 c = _pivotPos.Value;
                float r = Mathf.Max(0.02f * scale, Vector3.Distance(head, c) * 0.015f);
                for (int i = 0; i < PivotPoints; i++)
                {
                    float a = i * Mathf.PI * 2f / PivotPoints;
                    _pivot.SetPosition(i, c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r);
                }
                float pw = Mathf.Max(0.002f * scale, Vector3.Distance(head, c) * 0.0015f);
                _pivot.startWidth = pw;
                _pivot.endWidth = pw;
            }

            // Right after holding you are standing inside the icon; it appears once you move ~35 cm (real) away.
            bool showIcon = _visible && Vector3.Distance(head, _camPos) > 0.35f * scale;
            if (_root.activeSelf != showIcon) _root.SetActive(showIcon);

            // Icon points are placed in world space here (line widths are then plain world units). It is about 17 cm
            // long (real size) up close; farther than 1.2 m it grows with distance so it never looks smaller than that.
            var t = _root.transform;
            float grow = Mathf.Max(1f, Vector3.Distance(head, _camPos) / scale / 1.2f);
            t.localScale = Vector3.one * scale * grow;
            float iconWidth = 0.003f * scale * grow;
            for (int i = 0; i < _icon.Count; i++)
            {
                var lr = _icon[i];
                var local = _iconLocal[i];
                for (int p = 0; p < local.Length; p++) lr.SetPosition(p, t.TransformPoint(local[p]));
                lr.startWidth = iconWidth;
                lr.endWidth = iconWidth;
            }
            float minWidth = 0.002f * scale;
            float camWidth = Mathf.Max(minWidth, Vector3.Distance(head, _camPos) * 0.0015f);
            for (int i = 0; i < 4; i++)
            {
                _edges[i].startWidth = camWidth;
                _edges[i].endWidth = Mathf.Max(minWidth, Vector3.Distance(head, _ends[i]) * 0.0015f);
            }
            if (_hasOutline)
            {
                float outlineWidth = 0f;
                foreach (var p in _outlinePts) outlineWidth += Mathf.Max(minWidth, Vector3.Distance(head, p) * 0.0015f);
                outlineWidth /= _outlinePts.Length;
                _outline.startWidth = outlineWidth;
                _outline.endWidth = outlineWidth;
            }
        }

        public void Destroy()
        {
            if (_root) Object.Destroy(_root);
            if (_worldRoot) Object.Destroy(_worldRoot);
        }

        private static Vector3[] Rect(float hx, float hy, float z)
        {
            return new[] { new Vector3(-hx, -hy, z), new Vector3(hx, -hy, z), new Vector3(hx, hy, z), new Vector3(-hx, hy, z) };
        }

        private static Vector3[] Circle(Vector3 c, float r)
        {
            var pts = new Vector3[16];
            for (int i = 0; i < pts.Length; i++)
            {
                float a = i * Mathf.PI * 2f / pts.Length;
                pts[i] = c + new Vector3(0f, Mathf.Sin(a) * r, Mathf.Cos(a) * r);
            }
            return pts;
        }

        private void Loop(Vector3[] pts)
        {
            var lr = NewLine(_root, true, pts.Length);
            lr.loop = true;
            _icon.Add(lr);
            _iconLocal.Add(pts);
        }

        private void Line(Vector3 a, Vector3 b)
        {
            var lr = NewLine(_root, true, 2);
            _icon.Add(lr);
            _iconLocal.Add(new[] { a, b });
        }

        private static LineRenderer NewLine(GameObject parent, bool world, int count)
        {
            var go = new GameObject("Line");
            go.transform.SetParent(parent.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = VRHands.OverlayMaterial;
            lr.useWorldSpace = world;
            lr.positionCount = count;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.startColor = Green;
            lr.endColor = Green;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }
    }
}
