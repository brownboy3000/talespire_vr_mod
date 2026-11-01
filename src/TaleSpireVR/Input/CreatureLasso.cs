using System.Collections.Generic;
using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// Lasso = hold the Lasso grip (default: right) and pull that hand's trigger while pointing at the board, then sweep
    /// the laser around creatures; release the trigger to select every creature inside the loop (TaleSpire's
    /// multi-select). Then grab any of them with the trigger to carry the whole group. The loop is drawn on top of the
    /// board while you sweep.
    /// </summary>
    internal static class CreatureLasso
    {
        public static bool Active { get; private set; }
        public static Hand ActiveHand { get; private set; }

        private const int MaxPoints = 600;
        private static readonly List<Vector3> _points = new List<Vector3>();
        private static LineRenderer _line;
        private static float _hideAt;

        /// <param name="onBoard">Per hand: is its laser on the board (not on a menu)?</param>
        public static void Update(VRRig rig, VRHands hands, bool[] onBoard)
        {
            if (_line != null && !Active && _hideAt > 0f && Time.unscaledTime > _hideAt)
            {
                _line.enabled = false;
                _hideAt = 0f;
            }
            if (rig == null || !rig.OnBoard)
            {
                if (Active) Cancel();
                return;
            }

            if (!Active)
            {
                for (int h = 0; h < 2; h++)
                {
                    if (SteamVRInput.Lasso.Armed[h] && SteamVRInput.Trigger((Hand)h).Down && onBoard[h]
                        && !MeasureTool.Active && !CreatureHold.IsHolding)
                    {
                        Begin((Hand)h, hands);
                        break;
                    }
                }
                return;
            }

            AddPoint(rig);
            Draw(rig, closed: false);
            if (!SteamVRInput.Trigger(ActiveHand).Held) Finish(rig);
        }

        private static void Begin(Hand hand, VRHands hands)
        {
            Active = true;
            ActiveHand = hand;
            hands.SetActive(hand);
            SteamVRInput.Lasso.Disarm(hand);   // one lasso per squeeze of the grip
            _points.Clear();
            SteamVRInput.Haptic(hand, 0.015f, 160f, 0.3f);
        }

        private static void Cancel()
        {
            Active = false;
            _points.Clear();
            if (_line != null) _line.enabled = false;
        }

        private static void AddPoint(VRRig rig)
        {
            if (_points.Count >= MaxPoints) return;
            var ray = VRPointer.WorldRay;
            Vector3 p = ray.GetPoint(Mathf.Max(0f, VRPointer.WorldHitDistance(ray, rig, true)));
            if (_points.Count == 0 || Vector3.Distance(_points[_points.Count - 1], p) > 0.004f * rig.Scale)
                _points.Add(p);
        }

        private static void Finish(VRRig rig)
        {
            Active = false;
            Draw(rig, closed: true);
            _hideAt = Time.unscaledTime + 0.6f;
            if (_points.Count < 3) { Plugin.Log.LogInfo("[lasso] too short, nothing selected"); return; }

            var inside = new List<CreatureBoardAsset>();
            foreach (var c in Object.FindObjectsOfType<CreatureBoardAsset>())
            {
                if (c == null || !c.isActiveAndEnabled) continue;
                if (Contains(_points, c.transform.position) && LocalClient.CanControlCreature(c.CreatureId)) inside.Add(c);
            }
            Select(inside);
            SteamVRInput.Haptic(ActiveHand, 0.03f, 120f, inside.Count > 0 ? 0.5f : 0.15f);
            Plugin.Log.LogInfo($"[lasso] {_points.Count} points, selected {inside.Count} creature(s)");
        }

        /// <summary>Replace TaleSpire's selection with these creatures (one = normal selection, more = group).</summary>
        public static void Select(List<CreatureBoardAsset> creatures)
        {
            LocalClient.ClearLassoedCreatures();
            LocalClient.SetSelectedCreatureId(default(CreatureGuid));
            foreach (var c in creatures)
            {
                if (LocalClient.LassoIsFull) break;
                LocalClient.ToggleLassoMembership(c.CreatureId);
            }
        }

        /// <summary>Point-in-polygon on the board plane (x/z).</summary>
        private static bool Contains(List<Vector3> poly, Vector3 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vector3 a = poly[i], b = poly[j];
                if ((a.z > p.z) != (b.z > p.z) && p.x < (b.x - a.x) * (p.z - a.z) / (b.z - a.z) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        private static void Draw(VRRig rig, bool closed)
        {
            if (_line == null)
            {
                var go = new GameObject("TaleSpireVR_Lasso");
                Object.DontDestroyOnLoad(go);
                _line = go.AddComponent<LineRenderer>();
                _line.sharedMaterial = VRHands.OverlayMaterial;
                _line.useWorldSpace = true;
                _line.numCapVertices = 2;
                _line.numCornerVertices = 2;
                _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _line.receiveShadows = false;
                VRLayers.SetRecursive(go, VRLayers.Overlay);
            }
            _line.enabled = _points.Count > 1;
            _line.loop = closed;
            _line.widthMultiplier = 0.004f * rig.Scale;
            var color = new Color(0.85f, 0.45f, 1f, 0.95f);
            _line.startColor = color;
            _line.endColor = color;
            _line.positionCount = _points.Count;
            Vector3 lift = Vector3.up * 0.005f * rig.Scale;
            for (int i = 0; i < _points.Count; i++) _line.SetPosition(i, _points[i] + lift);
        }

        /// <summary>Debug: lasso a circle of the given radius (board units) around a point.</summary>
        public static void DebugCircle(VRRig rig, Vector3 center, float radius)
        {
            _points.Clear();
            for (int i = 0; i < 48; i++)
            {
                float a = i / 48f * Mathf.PI * 2f;
                _points.Add(center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
            }
            Active = true;
            Finish(rig);
        }
    }
}
