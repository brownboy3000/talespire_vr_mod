using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// First-person hands: PolyOne's free rigged VR hands, rebuilt from vrhands.bin (exported once from the Unity
    /// package with a newer editor; TaleSpire's Unity 2021 can't load its asset bundles) and posed every frame from
    /// SteamVR's hand skeleton: Valve Index finger tracking, or SteamVR's estimate for other controllers. Without a
    /// skeleton the controller pose is used, with the trigger curling index + thumb and the grip the other fingers.
    /// </summary>
    internal class FPHands
    {
        private class HandRig
        {
            public Hand Hand;
            public Transform Wrist;
            public Vector3 WristScale;
            public Vector3 HiddenScale = Vector3.one;
            public Vector3 WristRestPos;          // the wrist's rest pose under its forearm bone
            public Quaternion WristRestRot;
            public Vector3 FwdLocal, SideLocal;   // wrist-local: wrist -> middle knuckle, index knuckle -> pinky knuckle
            public float ModelLength;             // wrist -> middle knuckle, model units at root scale 1
            public Quaternion FrameInv;           // inverse of the wrist-local hand frame
            public readonly Transform[,] Joints = new Transform[5, 3];
            public readonly Transform[] Tips = new Transform[5];   // the fingertip ends (each finger's 4th joint)
            public readonly Quaternion[,] Base = new Quaternion[5, 3];
            public readonly Vector3[,] Axis = new Vector3[5, 3];
            public readonly float[] Curl = new float[5];
            public int ThumbFirstBone = 2;
            public bool Shown = true;
            // Last pose (world), for the ruler label on the back of the left hand.
            public bool Posed;
            public Vector3 WristPos, Fwd, Dorsal;
            public float Length;
        }

        /// <summary>Bend per joint (base, middle, tip) at a full curl, degrees: thumb, index, middle, ring, pinky.</summary>
        private static readonly float[,] MaxBend =
        {
            { 15f, 25f, 40f },
            { 65f, 90f, 60f },
            { 70f, 95f, 60f },
            { 70f, 95f, 60f },
            { 70f, 90f, 60f },
        };

        public readonly GameObject Root;
        private readonly HandRig[] _hands = new HandRig[2];
        private readonly SkinnedMeshRenderer _smr;
        private bool _visible;

        private FPHands(GameObject root, SkinnedMeshRenderer smr, HandRig left, HandRig right)
        {
            Root = root;
            _smr = smr;
            _hands[0] = left;
            _hands[1] = right;
            Root.SetActive(false);
        }

        public static FPHands Load(string path, Color color)
        {
            if (!File.Exists(path)) { Plugin.Log.LogWarning("First-person hands: " + path + " not found"); return null; }
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                if (r.ReadString() != "TSVRHAND1") { Plugin.Log.LogWarning("First-person hands: unknown file format"); return null; }

                int count = r.ReadInt32();
                var ts = new Transform[count];
                var names = new string[count];
                var parents = new int[count];
                var root = new GameObject("TaleSpireVR_FirstPersonHands");
                UnityEngine.Object.DontDestroyOnLoad(root);
                for (int i = 0; i < count; i++)
                {
                    names[i] = r.ReadString();
                    parents[i] = r.ReadInt32();
                    Vector3 lp = V3(r);
                    var lr = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    Vector3 ls = V3(r);
                    var t = new GameObject(names[i]).transform;
                    t.SetParent(i > 0 && parents[i] >= 0 ? ts[parents[i]] : root.transform, false);
                    t.localPosition = lp;
                    t.localRotation = lr;
                    t.localScale = ls;
                    ts[i] = t;
                }
                // The prefab root sits under ours: our root carries the scale and position.
                ts[0].localPosition = Vector3.zero;
                ts[0].localRotation = Quaternion.identity;

                int smrIndex = r.ReadInt32();
                int rootBone = r.ReadInt32();
                var mesh = new Mesh { name = "TaleSpireVR_Hands" };
                int n = r.ReadInt32();
                var verts = new Vector3[n];
                var normals = new Vector3[n];
                for (int i = 0; i < n; i++) verts[i] = V3(r);
                for (int i = 0; i < n; i++) normals[i] = V3(r);
                int uvCount = r.ReadInt32();
                var uvs = new Vector2[uvCount];
                for (int i = 0; i < uvCount; i++) uvs[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                mesh.vertices = verts;
                mesh.normals = normals;
                if (uvCount == n) mesh.uv = uvs;
                int subs = r.ReadInt32();
                mesh.subMeshCount = subs;
                for (int s = 0; s < subs; s++)
                {
                    var tri = new int[r.ReadInt32()];
                    for (int i = 0; i < tri.Length; i++) tri[i] = r.ReadInt32();
                    mesh.SetTriangles(tri, s);
                }
                var bw = new BoneWeight[r.ReadInt32()];
                for (int i = 0; i < bw.Length; i++)
                {
                    bw[i].boneIndex0 = r.ReadInt32(); bw[i].boneIndex1 = r.ReadInt32(); bw[i].boneIndex2 = r.ReadInt32(); bw[i].boneIndex3 = r.ReadInt32();
                    bw[i].weight0 = r.ReadSingle(); bw[i].weight1 = r.ReadSingle(); bw[i].weight2 = r.ReadSingle(); bw[i].weight3 = r.ReadSingle();
                }
                mesh.boneWeights = bw;
                var bones = new Transform[r.ReadInt32()];
                for (int i = 0; i < bones.Length; i++) bones[i] = ts[r.ReadInt32()];
                var bind = new Matrix4x4[r.ReadInt32()];
                for (int i = 0; i < bind.Length; i++)
                {
                    var m = new Matrix4x4();
                    for (int k = 0; k < 16; k++) m[k] = r.ReadSingle();
                    bind[i] = m;
                }
                mesh.bindposes = bind;
                mesh.RecalculateBounds();

                var hands = new HandRig[2];
                for (int h = 0; h < 2; h++)
                {
                    var hr = new HandRig { Hand = (Hand)h, Wrist = ts[r.ReadInt32()] };
                    hr.WristScale = hr.Wrist.localScale;
                    hr.WristRestPos = hr.Wrist.localPosition;
                    hr.WristRestRot = hr.Wrist.localRotation;
                    hr.FwdLocal = V3(r);
                    hr.SideLocal = V3(r);
                    for (int f = 0; f < 5; f++)
                        for (int j = 0; j < 3; j++)
                        {
                            hr.Joints[f, j] = ts[r.ReadInt32()];
                            hr.Base[f, j] = hr.Joints[f, j].localRotation;
                            hr.Axis[f, j] = V3(r).normalized;
                        }
                    hr.ModelLength = hr.Wrist.TransformVector(hr.FwdLocal).magnitude;
                    Vector3 fwd = hr.FwdLocal.normalized;
                    hr.FrameInv = Quaternion.Inverse(Quaternion.LookRotation(fwd, Vector3.Cross(fwd, hr.SideLocal.normalized)));
                    // Fingertip ends ("...Index3" -> "...Index4") for the finger directions.
                    for (int f = 0; f < 5; f++)
                    {
                        string last = hr.Joints[f, 2].name;
                        string tipName = last.Substring(0, last.Length - 1) + "4";
                        hr.Tips[f] = Array.Find(ts, t => t.name == tipName);
                    }
                    // Without finger tracking the fingers curl by amounts: all four long fingers about the same axis
                    // (along the knuckles), so they close side by side instead of crossing over each other.
                    Vector3 lateral = hr.Wrist.TransformVector(hr.SideLocal).normalized;
                    Vector3 middleBend = hr.Joints[2, 0].TransformDirection(hr.Axis[2, 0]);
                    if (Vector3.Dot(lateral, middleBend) < 0f) lateral = -lateral;
                    for (int f = 1; f < 5; f++)
                        for (int j = 0; j < 3; j++)
                            hr.Axis[f, j] = hr.Joints[f, j].InverseTransformDirection(lateral).normalized;
                    // Does the model's thumb start at its base by the wrist (like SteamVR's, bone 2) or one joint out?
                    float thumbOut = (hr.Joints[0, 0].position - hr.Wrist.position).magnitude / Mathf.Max(1e-5f, (hr.Joints[1, 0].position - hr.Wrist.position).magnitude);
                    hr.ThumbFirstBone = thumbOut < 0.55f ? 2 : 3;
                    hands[h] = hr;
                }
                long rest = r.BaseStream.Length - r.BaseStream.Position;

                var smrGo = ts[smrIndex].gameObject;
                var smr = smrGo.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                smr.bones = bones;
                smr.rootBone = rootBone >= 0 ? ts[rootBone] : ts[0];
                smr.updateWhenOffscreen = true;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                smr.receiveShadows = true;
                var mat = MakeMaterial(color, out string shader);
                var mats = new Material[Mathf.Max(1, subs)];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                smr.sharedMaterials = mats;

                // The package names its hands from the viewer's side in some versions: check which one is anatomically
                // left (fingers curl about an axis that points from the pinky towards the index on a left hand).
                float Handedness(HandRig hr) => Vector3.Dot(hr.Joints[2, 0].TransformDirection(hr.Axis[2, 0]), hr.Wrist.TransformVector(hr.SideLocal).normalized);
                float hl = Handedness(hands[0]), hrt = Handedness(hands[1]);
                if (hl > 0f && hrt < 0f)
                {
                    var tmp = hands[0]; hands[0] = hands[1]; hands[1] = tmp;
                    hands[0].Hand = Hand.Left; hands[1].Hand = Hand.Right;
                    Plugin.Log.LogInfo("First-person hands: model's left/right swapped to match anatomy");
                }
                Plugin.Log.LogInfo($"First-person hands loaded: {count} transforms, {n} vertices, {bones.Length} bones, shader '{shader}', handedness L={hl:F2} R={hrt:F2}, model hand length {hands[0].ModelLength:F4}/{hands[1].ModelLength:F4}, {rest} bytes left over");
                return new FPHands(root, smr, hands[0], hands[1]);
            }
        }

        private static Vector3 V3(BinaryReader r) { return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }

        private static Material MakeMaterial(Color c, out string shaderName)
        {
            foreach (var name in new[] { "Standard", "Legacy Shaders/Diffuse", "Unlit/Color" })
            {
                var sh = Shader.Find(name);
                if (sh == null) continue;
                var m = new Material(sh) { name = "TaleSpireVR_Hands", color = c };
                if (name == "Standard")
                {
                    m.SetFloat("_Glossiness", 0.3f);
                    m.SetFloat("_Metallic", 0f);
                    // A little of their own light, so they don't vanish in dark dungeons.
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", c * 0.3f);
                }
                shaderName = name;
                return m;
            }
            shaderName = "Hidden/Internal-Colored";
            return new Material(Shader.Find("Hidden/Internal-Colored")) { color = c };
        }

        /// <summary>The hands' colour (the creature's own, or the config's: HandColors).</summary>
        public void SetColor(Color c)
        {
            var mat = _smr != null ? _smr.sharedMaterial : null;
            if (mat == null) return;
            mat.color = c;
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", c * 0.3f);
        }

        public bool Visible
        {
            get => _visible;
            set { _visible = value; if (Root.activeSelf != value) Root.SetActive(value); }
        }

        /// <summary>Debug ("fp handview"): hold both hands in front of the face with these curls.</summary>
        public static float? DebugCurl;
        public static float DebugDistance = 0.28f;
        /// <summary>Debug ("fp handpose yaw roll"): the right hand alone in front of the face, turned this much (degrees).</summary>
        public static Vector2? DebugTurn;
        /// <summary>Debug: and tipped down this much (degrees, about your right; "wpn aimat").</summary>
        public static float DebugPitch;

        /// <summary>
        /// A gun or blade in the right hand (FPWeapons): that hand (and its forearm) isn't drawn - only the weapon, which
        /// sits where the hand is. The left hand, and the right one with nothing or a hand power, are drawn as usual.
        /// </summary>
        public static bool RightHidden;

        private enum FrameKind { None, Debug, Skeleton, Controller }

        /// <summary>
        /// Where a hand is this frame (world): wrist, finger direction (wrist to middle knuckle), index to pinky knuckle,
        /// and its length (wrist to middle knuckle) - from SteamVR's hand skeleton, else the controller.
        /// </summary>
        private static FrameKind Frame(int h, VRRig rig, VRHands hands, out Vector3 wrist, out Vector3 fwd, out Vector3 side, out float length)
        {
            wrist = fwd = side = Vector3.zero;
            length = 0f;
            var rt = rig.RigTransform;
            if (DebugCurl.HasValue && rig.Head != null)
            {
                // In front of the eyes, palms down, fingers forward.
                Transform head = rig.Head;
                float s = rig.Scale;
                wrist = head.position + head.forward * DebugDistance * s - head.up * 0.12f * s + head.right * (h == 0 ? -0.09f : 0.09f) * s;
                fwd = (head.forward + head.right * (h == 0 ? 0.25f : -0.25f)).normalized;
                side = head.right * (h == 0 ? -1f : 1f);
                length = 0.09f * s;
                if (DebugTurn.HasValue)
                {
                    // ("fp handpose"): the right hand alone in the middle of the view, turned (yaw about your up, then
                    // rolled about its fingers), to look at it from any side; the left one out of the way.
                    if (h == 0) { wrist = head.position - head.up * 2f * s; return FrameKind.Debug; }
                    Quaternion q = Quaternion.AngleAxis(DebugTurn.Value.x, head.up) * Quaternion.AngleAxis(DebugPitch, head.right);
                    fwd = q * head.forward;
                    side = Quaternion.AngleAxis(DebugTurn.Value.y, fwd) * (q * head.right);
                    wrist = head.position + head.forward * DebugDistance * s - fwd * length * 0.8f;
                }
                return FrameKind.Debug;
            }
            var sk = SteamVRInput.SkeletonOf((Hand)h);
            if (sk.Usable)
            {
                Vector3 W = rt.TransformPoint(sk.RootPosition + sk.RootRotation * sk.Wrist);
                Vector3 I = rt.TransformPoint(sk.RootPosition + sk.RootRotation * sk.IndexKnuckle);
                Vector3 M = rt.TransformPoint(sk.RootPosition + sk.RootRotation * sk.MiddleKnuckle);
                Vector3 P = rt.TransformPoint(sk.RootPosition + sk.RootRotation * sk.PinkyKnuckle);
                wrist = W; fwd = (M - W).normalized; side = (P - I).normalized; length = (M - W).magnitude;
                return FrameKind.Skeleton;
            }
            var hs = hands != null ? hands.Get((Hand)h) : null;
            if (hs != null && hs.Valid)
            {
                // Controller only: the hand wraps the handle behind the tip, knuckles stacked index-over-pinky (how
                // far behind and how steeply depends on the controller: ControllerProfiles).
                float s = rig.Scale;
                var prof = ControllerProfiles.Of((Hand)h);
                Vector3 f = hs.Rotation * Vector3.forward, up = hs.Rotation * Vector3.up;
                wrist = hs.Position - f * prof.WristBack * s - up * 0.02f * s;
                fwd = (f - up * prof.FingerDown).normalized;
                side = -up;
                length = 0.09f * s;
                return FrameKind.Controller;
            }
            return FrameKind.None;
        }

        /// <summary>A hand's frame this frame, as the first-person hands are posed with it (see Frame).</summary>
        public static bool TryFrame(Hand hand, out Vector3 wrist, out Vector3 fwd, out Vector3 side, out float length)
        {
            var mgr = VRManager.Instance;
            var rig = mgr != null ? mgr.Rig : null;
            wrist = fwd = side = Vector3.zero;
            length = 0f;
            if (rig == null || rig.RigTransform == null) return false;
            return Frame((int)hand, rig, mgr.Hands, out wrist, out fwd, out side, out length) != FrameKind.None;
        }

        /// <summary>Pose both hands for this frame (after the rig and the controller poses are final).</summary>
        public void Update(VRRig rig, VRHands hands)
        {
            if (!_visible || rig == null || rig.RigTransform == null) return;
            var rt = rig.RigTransform;
            Root.transform.position = rig.Head != null ? rig.Head.position : rt.position;

            // Scale: the model's hand to the size of SteamVR's hand (or a typical one), in board units.
            float target = 0f;
            int got = 0;
            var pose = _pose;
            var have = _have;
            for (int h = 0; h < 2; h++)
            {
                var hr = _hands[h];
                var sk = SteamVRInput.SkeletonOf((Hand)h);
                have[h] = false;
                _tracked[h] = false;
                _fingerAdjust[h] = Quaternion.identity;
                var kind = Frame(h, rig, hands, out Vector3 w, out Vector3 fwd, out Vector3 side, out float len);
                if (kind == FrameKind.None) continue;
                pose[h, 0] = w; pose[h, 1] = fwd; pose[h, 2] = side; pose[h, 3] = new Vector3(len, 0f, 0f);
                have[h] = true;
                if (h == (int)Hand.Right && RightHidden) { }   // holding a weapon: not drawn (its size still counts)
                else if (kind == FrameKind.Debug)
                {
                    for (int f = 0; f < 5; f++) hr.Curl[f] = Mathf.Max(0f, DebugCurl.Value);
                    // ("fingers ..."): the fingers as animated without finger tracking, from made-up trigger / grip values.
                    if (DebugFingers.HasValue) SmoothCurls(hr, SynthCurls(h));
                    // A negative curl: the real tracked fingers, shown on hands held in front of the face.
                    else if (DebugCurl.Value < 0f && sk.Usable)
                    {
                        _tracked[h] = true;
                        Vector3 sf = (sk.MiddleKnuckle - sk.Wrist).normalized, ss = (sk.PinkyKnuckle - sk.IndexKnuckle).normalized;
                        Quaternion skFrame = rt.rotation * sk.RootRotation * Quaternion.LookRotation(sf, Vector3.Cross(sf, ss));
                        _fingerAdjust[h] = Quaternion.LookRotation(fwd, Vector3.Cross(fwd, side)) * Quaternion.Inverse(skFrame);
                    }
                }
                else if (kind == FrameKind.Skeleton && sk.Level == Valve.VR.EVRSkeletalTrackingLevel.VRSkeletalTracking_Full && ControllerProfiles.Of((Hand)h).FingerTracking)
                {
                    // Every finger tracked (Index): the fingers follow SteamVR's skeleton joint by joint.
                    _tracked[h] = true;
                    SmoothCurls(hr, sk.Curl);
                }
                else SmoothCurls(hr, SynthCurls(h));   // other controllers: from the trigger and grip (the hand still placed by the skeleton if SteamVR has one)
                target += len;
                got++;
            }
            if (got == 0) { Visible = false; return; }
            float modelLen = Mathf.Max(1e-5f, (_hands[0].ModelLength + _hands[1].ModelLength) * 0.5f);
            Root.transform.localScale = Vector3.one * (target / got / modelLen);

            for (int h = 0; h < 2; h++)
            {
                var hr = _hands[h];
                bool show = have[h] && !(h == (int)Hand.Right && RightHidden);
                if (show != hr.Shown)
                {
                    hr.Shown = show;
                    // Collapse that hand (and its forearm) away.
                    var part = hr.Wrist.parent != null && hr.Wrist.parent != Root.transform ? hr.Wrist.parent : hr.Wrist;
                    if (!show) hr.HiddenScale = part.localScale;
                    part.localScale = show ? hr.HiddenScale : Vector3.zero;
                }
                hr.Posed = show;
                if (!show) continue;
                Vector3 fwd = pose[h, 1], side = pose[h, 2];
                Vector3 up = Vector3.Cross(fwd, side);
                if (up.sqrMagnitude < 1e-8f) continue;
                Quaternion rot = Quaternion.LookRotation(fwd, up) * hr.FrameInv;
                // Move the forearm bone with the hand (the wrist keeps its rest pose on it), so the arm runs straight
                // back from the wrist instead of stretching to wherever the model's arm was.
                var arm = hr.Wrist.parent;
                if (arm != null && arm != Root.transform)
                {
                    hr.Wrist.localPosition = hr.WristRestPos;
                    hr.Wrist.localRotation = hr.WristRestRot;
                    arm.rotation = rot * Quaternion.Inverse(hr.WristRestRot);
                    arm.position = pose[h, 0] - arm.TransformVector(hr.WristRestPos);
                }
                else hr.Wrist.SetPositionAndRotation(pose[h, 0], rot);
                if (_tracked[h]) FollowFingers(hr, SteamVRInput.SkeletonOf((Hand)h), rt, _fingerAdjust[h]);
                else
                    for (int f = 0; f < 5; f++)
                        for (int j = 0; j < 3; j++)
                            hr.Joints[f, j].localRotation = hr.Base[f, j] * Quaternion.AngleAxis(hr.Curl[f] * MaxBend[f, j], hr.Axis[f, j]);
                hr.WristPos = pose[h, 0];
                hr.Fwd = fwd;
                // Cross(fingers, index->pinky) points out of the back of a right hand and the palm of a left one.
                hr.Dorsal = (h == 1 ? up : -up).normalized;
                hr.Length = pose[h, 3].x;
            }
        }

        private readonly float[] _fallbackCurl = new float[5];

        /// <summary>Debug ("fingers trigger grip thumbRest | off"): made-up controller values for SynthCurls (shown with "fp handview").</summary>
        public static Vector3? DebugFingers;

        /// <summary>
        /// Fingers without finger tracking (every controller but the Index), as in most VR games: the index finger curls
        /// with the trigger, the other three with the grip. The thumb, which no controller tracks fully, follows what the
        /// hand is doing: relaxed and slightly in for an open hand, down on the button or stick it rests on, along the
        /// middle finger when pointing (grip without trigger), wrapped over the fingers in a fist (grip and trigger) and
        /// half in for a trigger-only pinch. Casting a power (flame / ice) with that hand: open, fingers spread.
        /// </summary>
        private float[] SynthCurls(int h)
        {
            float trig, grip;
            bool rest;
            if (DebugFingers.HasValue) { trig = DebugFingers.Value.x; grip = DebugFingers.Value.y; rest = DebugFingers.Value.z > 0.5f; }
            else
            {
                trig = Mathf.Max(SteamVRInput.TriggerValue.Value[h], SteamVRInput.Trigger((Hand)h).Held ? 1f : 0f);
                grip = Mathf.Max(SteamVRInput.GripValue.Value[h], SteamVRInput.Measure.Held[h] || SteamVRInput.Lasso.Held[h] ? 1f : 0f);
                rest = SteamVRInput.ThumbRest.Held[h];
            }
            var c = _fallbackCurl;
            if (h == (int)Hand.Right && FPWeapons.PowerCasting)
            {
                for (int f = 0; f < 5; f++) c[f] = 0.06f;
                return c;
            }
            c[1] = Mathf.Lerp(0.12f, 1f, trig);
            c[2] = c[3] = c[4] = Mathf.Lerp(0.18f, 1f, grip);
            float thumb = 0.22f;
            if (rest) thumb = Mathf.Max(thumb, 0.55f);
            if (grip > 0.5f) thumb = Mathf.Max(thumb, Mathf.Lerp(0.6f, 0.9f, trig));
            if (trig > 0.5f && grip <= 0.5f) thumb = Mathf.Max(thumb, 0.45f);
            c[0] = thumb;
            return c;
        }
        private readonly bool[] _tracked = new bool[2];   // this frame's fingers come from SteamVR's skeleton joints
        private readonly Quaternion[] _fingerAdjust = { Quaternion.identity, Quaternion.identity };   // debug hand view only

        /// <summary>SteamVR's first joint of each finger segment chain: thumb (or 3, see ThumbFirstBone), index, middle, ring, pinky.</summary>
        private static readonly int[] FirstBone = { 2, 7, 12, 17, 22 };

        /// <summary>
        /// Finger tracking: every finger segment of the model points the way SteamVR's matching segment points (curl and
        /// spread of each finger, the thumb's position), so a closed hand closes like a real one, fingers side by side.
        /// </summary>
        private static void FollowFingers(HandRig hr, SteamVRInput.Skeleton sk, Transform rt, Quaternion adjust)
        {
            for (int f = 0; f < 5; f++)
            {
                int first = f == 0 ? hr.ThumbFirstBone : FirstBone[f];
                int last = f == 0 ? 5 : FirstBone[f] + 3;   // the fingertip's joint
                for (int j = 0; j < 3; j++) hr.Joints[f, j].localRotation = hr.Base[f, j];
                for (int j = 0; j < 3; j++)
                {
                    int a = first + j, b = a + 1;
                    if (b > last) break;
                    Transform joint = hr.Joints[f, j];
                    Transform child = j < 2 ? hr.Joints[f, j + 1] : hr.Tips[f];
                    if (child == null) break;
                    Vector3 cur = child.position - joint.position;
                    Vector3 want = adjust * rt.TransformDirection(sk.RootRotation * (sk.Bones[b] - sk.Bones[a]));
                    if (cur.sqrMagnitude < 1e-12f || want.sqrMagnitude < 1e-12f) continue;
                    joint.rotation = Quaternion.FromToRotation(cur, want) * joint.rotation;
                }
            }
        }
        private readonly Vector3[,] _pose = new Vector3[2, 4];   // per hand: wrist, finger direction, index->pinky, (length, 0, 0)
        private readonly bool[] _have = new bool[2];

        private static void SmoothCurls(HandRig hr, float[] curls)
        {
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.035f);
            for (int f = 0; f < 5; f++) hr.Curl[f] = Mathf.Lerp(hr.Curl[f], Mathf.Clamp01(curls[f]), k);
        }

        /// <summary>The back of a hand this frame: a point just above the skin between wrist and knuckles, the outward normal, the finger direction and the hand's length (world).</summary>
        public bool TryGetBackOfHand(Hand hand, out Vector3 pos, out Vector3 normal, out Vector3 fingers, out float length)
        {
            var hr = _hands[(int)hand];
            pos = normal = fingers = Vector3.zero;
            length = 0f;
            if (!_visible || !hr.Posed) return false;
            normal = hr.Dorsal;
            fingers = hr.Fwd;
            length = hr.Length;
            pos = hr.WristPos + hr.Fwd * (hr.Length * 0.45f) + hr.Dorsal * (hr.Length * 0.22f);
            return true;
        }

        public string Status()
        {
            var l = SteamVRInput.SkeletonLeft;
            var r = SteamVRInput.SkeletonRight;
            string C(float[] c) => $"{c[0]:F2}/{c[1]:F2}/{c[2]:F2}/{c[3]:F2}/{c[4]:F2}";
            return $"hands visible={_visible} skeleton L(valid={l.Valid} bones={l.HasBones} curl={C(l.Curl)}) R(valid={r.Valid} bones={r.HasBones} curl={C(r.Curl)}) scale={Root.transform.localScale.x:F3} bounds={_smr.bounds.center:F2} size {_smr.bounds.size:F3} wrists L={_hands[0].Wrist.position:F2} R={_hands[1].Wrist.position:F2} handLen={_hands[0].Length:F3}";
        }

        public void Destroy()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root);
        }
    }
}
