using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using Valve.VR;

namespace TaleSpireVR
{
    internal enum Hand { Left = 0, Right = 1 }

    /// <summary>
    /// Reads SteamVR Input actions (actions.json next to the plugin) straight through the OpenVR C# bindings.
    /// </summary>
    internal static class SteamVRInput
    {
        public class Button
        {
            public readonly string Path;
            public ulong Handle;
            public bool Held, Down, Up;
            /// <summary>Debug: pretend the button is held.</summary>
            public bool DebugHeld;
            /// <summary>Whether SteamVR reports the action as bound and available (for diagnostics).</summary>
            public bool Active;
            public Button(string path) { Path = path; }
        }

        /// <summary>A button that can be bound on either hand; tracks which hand pressed it (its laser is used).</summary>
        public class HandButton
        {
            public readonly string Path;
            public ulong Handle;
            public readonly bool[] Held = new bool[2], Down = new bool[2], Up = new bool[2];
            /// <summary>
            /// Squeezed and not used yet: set when the button goes down, cleared when it is let go or a gesture has used
            /// it (Disarm). A grip that stays "held" because the hand keeps gripping firmly after a gesture therefore
            /// stops modifying the trigger until it is squeezed again.
            /// </summary>
            public readonly bool[] Armed = new bool[2];
            /// <summary>Debug: pretend the button is held on a hand.</summary>
            public readonly bool[] DebugHeld = new bool[2];
            public HandButton(string path) { Path = path; }
            public void Disarm(Hand h) { Armed[(int)h] = false; }
        }

        /// <summary>An analog value (0..1) bound on either hand, read per hand.</summary>
        public class HandAxis
        {
            public readonly string Path;
            public ulong Handle;
            public readonly float[] Value = new float[2];
            public HandAxis(string path) { Path = path; }
        }

        public class Axis
        {
            public readonly string Path;
            public ulong Handle;
            public Vector2 Value;
            /// <summary>Debug: pretend the stick is held at this value.</summary>
            public Vector2? DebugValue;
            public Axis(string path) { Path = path; }
        }

        public class Pose
        {
            public readonly string Path;
            public ulong Handle;
            public bool Valid;
            public Vector3 Position;      // tracking space (meters, standing universe)
            public Quaternion Rotation;
            public Pose(string path) { Path = path; }
        }

        /// <summary>
        /// SteamVR's hand skeleton for one hand (Index finger tracking; other controllers get SteamVR's estimate from
        /// their buttons): the skeleton root pose (tracking space), a few joint positions relative to it (meters,
        /// Unity axes) and the finger curls (0 open .. 1 fist; thumb, index, middle, ring, pinky).
        /// </summary>
        public class Skeleton
        {
            public readonly string Path;
            public ulong Handle;
            public bool Valid, HasBones;
            public Vector3 RootPosition;
            public Quaternion RootRotation;
            public readonly float[] Curl = new float[5];
            /// <summary>How SteamVR knows these fingers: Full (Index finger tracking), Partial (some touch sensors) or Estimated (from buttons).</summary>
            public EVRSkeletalTrackingLevel Level = EVRSkeletalTrackingLevel.VRSkeletalTracking_Estimated;
            public Vector3 Wrist, IndexKnuckle, MiddleKnuckle, PinkyKnuckle;   // model space (relative to the root)
            /// <summary>All 31 joints of SteamVR's hand skeleton, model space, Unity axes (1 wrist; thumb 2-5; index 6-10; middle 11-15; ring 16-20; pinky 21-25).</summary>
            public readonly Vector3[] Bones = new Vector3[31];
            /// <summary>When the skeleton (pose + joints) last read fine; its last values are kept.</summary>
            public float LastGoodTime = -1f;
            /// <summary>Read fine this frame, or a moment ago (a dropped frame of tracking shouldn't make a hand jump).</summary>
            public bool Usable => (Valid && HasBones) || (LastGoodTime >= 0f && Time.unscaledTime - LastGoodTime < 0.3f);
            public Skeleton(string path) { Path = path; }
        }

        public static readonly Skeleton SkeletonLeft = new Skeleton("/actions/talespire/in/SkeletonLeft");
        public static readonly Skeleton SkeletonRight = new Skeleton("/actions/talespire/in/SkeletonRight");
        public static Skeleton SkeletonOf(Hand h) { return h == Hand.Left ? SkeletonLeft : SkeletonRight; }
        private static readonly Skeleton[] Skeletons = { SkeletonLeft, SkeletonRight };
        private static readonly VRBoneTransform_t[] _bones = new VRBoneTransform_t[31];
        /// <summary>Only read hand skeletons while something shows hands (first-person mode).</summary>
        public static bool WantSkeletons;

        public static bool Ready { get; private set; }

        public static readonly Pose PoseLeft = new Pose("/actions/talespire/in/PoseLeft");
        public static readonly Pose PoseRight = new Pose("/actions/talespire/in/PoseRight");
        public static readonly Button TriggerLeft = new Button("/actions/talespire/in/TriggerLeft");
        public static readonly Button TriggerRight = new Button("/actions/talespire/in/TriggerRight");
        public static readonly Button RightClick = new Button("/actions/talespire/in/RightClick");
        public static readonly Button MenuToggle = new Button("/actions/talespire/in/MenuToggle");
        public static readonly Button ZoomIn = new Button("/actions/talespire/in/ZoomIn");
        public static readonly Button ZoomOut = new Button("/actions/talespire/in/ZoomOut");
        public static readonly Axis Move = new Axis("/actions/talespire/in/Move");
        public static readonly Axis TurnHeight = new Axis("/actions/talespire/in/TurnHeight");
        public static readonly Button MiddleClick = new Button("/actions/talespire/in/MiddleClick");
        public static readonly Button BuildModeToggle = new Button("/actions/talespire/in/BuildModeToggle");
        public static readonly Button RotateModifier = new Button("/actions/talespire/in/RotateModifier");
        public static readonly Button ElevateModifier = new Button("/actions/talespire/in/ElevateModifier");

        public static readonly HandButton Measure = new HandButton("/actions/talespire/in/Measure");
        public static readonly HandButton Lasso = new HandButton("/actions/talespire/in/Lasso");
        /// <summary>The thumb resting on a button, stick or thumb rest (touch sensors), per hand: first-person thumbs without finger tracking.</summary>
        public static readonly HandButton ThumbRest = new HandButton("/actions/talespire/in/ThumbRest");
        /// <summary>How far each trigger / grip is pulled (0..1), per hand: first-person fingers without finger tracking.</summary>
        public static readonly HandAxis TriggerValue = new HandAxis("/actions/talespire/in/TriggerValue");
        public static readonly HandAxis GripValue = new HandAxis("/actions/talespire/in/GripValue");

        private static readonly Button[] Buttons = { TriggerLeft, TriggerRight, RightClick, MenuToggle, ZoomIn, ZoomOut, MiddleClick, BuildModeToggle, RotateModifier, ElevateModifier };
        private static readonly HandButton[] HandButtons = { Measure, Lasso, ThumbRest };
        private static readonly HandAxis[] HandAxes = { TriggerValue, GripValue };
        private static readonly ulong[] _handSources = new ulong[2];
        private static readonly Axis[] Axes = { Move, TurnHeight };
        private static readonly Pose[] Poses = { PoseLeft, PoseRight };

        private static ulong _actionSet;
        private static ulong _haptic;
        private static VRActiveActionSet_t[] _activeSets;
        private static uint _activeSetSize, _digitalSize, _analogSize, _poseSize;

        public static Button Trigger(Hand h) { return h == Hand.Left ? TriggerLeft : TriggerRight; }
        public static Pose PoseOf(Hand h) { return h == Hand.Left ? PoseLeft : PoseRight; }

        public static void Init()
        {
            var input = OpenVR.Input;
            if (input == null)
            {
                Plugin.Log.LogError("OpenVR.Input is not available; controllers will not work.");
                return;
            }
            string manifest = Path.Combine(Path.Combine(Plugin.PluginDir, "SteamVR"), "actions.json");
            var err = input.SetActionManifestPath(manifest);
            Plugin.Log.LogInfo($"SetActionManifestPath('{manifest}') -> {err}");
            if (err != EVRInputError.None && err != EVRInputError.MismatchedActionManifest)
            {
                Plugin.Log.LogError("SteamVR rejected the action manifest; controllers will not work.");
            }

            input.GetActionSetHandle("/actions/talespire", ref _actionSet);
            foreach (var b in Buttons) Check(b.Path, input.GetActionHandle(b.Path, ref b.Handle));
            foreach (var b in HandButtons) Check(b.Path, input.GetActionHandle(b.Path, ref b.Handle));
            foreach (var x in HandAxes) Check(x.Path, input.GetActionHandle(x.Path, ref x.Handle));
            input.GetInputSourceHandle("/user/hand/left", ref _handSources[0]);
            input.GetInputSourceHandle("/user/hand/right", ref _handSources[1]);
            foreach (var a in Axes) Check(a.Path, input.GetActionHandle(a.Path, ref a.Handle));
            foreach (var p in Poses) Check(p.Path, input.GetActionHandle(p.Path, ref p.Handle));
            foreach (var s in Skeletons) Check(s.Path, input.GetActionHandle(s.Path, ref s.Handle));
            input.GetActionHandle("/actions/talespire/out/Haptic", ref _haptic);

            _activeSets = new[] { new VRActiveActionSet_t { ulActionSet = _actionSet, ulRestrictedToDevice = OpenVR.k_ulInvalidInputValueHandle, nPriority = 0 } };
            _activeSetSize = (uint)Marshal.SizeOf(typeof(VRActiveActionSet_t));
            _digitalSize = (uint)Marshal.SizeOf(typeof(InputDigitalActionData_t));
            _analogSize = (uint)Marshal.SizeOf(typeof(InputAnalogActionData_t));
            _poseSize = (uint)Marshal.SizeOf(typeof(InputPoseActionData_t));
            Ready = _actionSet != 0;
            Plugin.Log.LogInfo($"SteamVR Input ready={Ready} actionSet={_actionSet}");
        }

        private static void Check(string path, EVRInputError err)
        {
            if (err != EVRInputError.None) Plugin.Log.LogWarning($"GetActionHandle({path}) -> {err}");
        }

        public static void Update()
        {
            if (!Ready) return;
            var input = OpenVR.Input;
            if (input == null) return;
            input.UpdateActionState(_activeSets, _activeSetSize);

            var d = new InputDigitalActionData_t();
            foreach (var b in Buttons)
            {
                bool held = b.DebugHeld;
                b.Active = false;
                if (input.GetDigitalActionData(b.Handle, ref d, _digitalSize, OpenVR.k_ulInvalidInputValueHandle) == EVRInputError.None)
                {
                    b.Active = d.bActive;
                    held |= d.bActive && d.bState;
                }
                b.Down = held && !b.Held;
                b.Up = !held && b.Held;
                b.Held = held;
            }
            foreach (var b in HandButtons)
            {
                for (int h = 0; h < 2; h++)
                {
                    bool held = b.DebugHeld[h];
                    if (!held && _handSources[h] != 0 && input.GetDigitalActionData(b.Handle, ref d, _digitalSize, _handSources[h]) == EVRInputError.None)
                        held = d.bActive && d.bState;
                    b.Down[h] = held && !b.Held[h];
                    b.Up[h] = !held && b.Held[h];
                    b.Held[h] = held;
                    if (b.Down[h]) b.Armed[h] = true;
                    if (!held) b.Armed[h] = false;
                }
            }

            var a = new InputAnalogActionData_t();
            foreach (var x in HandAxes)
                for (int h = 0; h < 2; h++)
                {
                    x.Value[h] = 0f;
                    if (_handSources[h] != 0 && input.GetAnalogActionData(x.Handle, ref a, _analogSize, _handSources[h]) == EVRInputError.None && a.bActive)
                        x.Value[h] = Mathf.Clamp01(a.x);
                }
            foreach (var ax in Axes)
            {
                ax.Value = ax.DebugValue ?? Vector2.zero;
                if (!ax.DebugValue.HasValue && input.GetAnalogActionData(ax.Handle, ref a, _analogSize, OpenVR.k_ulInvalidInputValueHandle) == EVRInputError.None && a.bActive)
                    ax.Value = new Vector2(a.x, a.y);
            }

            var p = new InputPoseActionData_t();
            foreach (var pose in Poses)
            {
                pose.Valid = false;
                if (input.GetPoseActionDataForNextFrame(pose.Handle, ETrackingUniverseOrigin.TrackingUniverseStanding, ref p, _poseSize, OpenVR.k_ulInvalidInputValueHandle) == EVRInputError.None
                    && p.bActive && p.pose.bPoseIsValid)
                {
                    ToUnity(p.pose.mDeviceToAbsoluteTracking, out pose.Position, out pose.Rotation);
                    pose.Valid = true;
                }
            }

            if (WantSkeletons) UpdateSkeletons(input, ref p);
        }

        private static void UpdateSkeletons(CVRInput input, ref InputPoseActionData_t p)
        {
            foreach (var s in Skeletons)
            {
                s.Valid = s.HasBones = false;
                if (s.Handle == 0) continue;
                // A skeleton action is also a pose action: its pose is the skeleton's root.
                if (input.GetPoseActionDataForNextFrame(s.Handle, ETrackingUniverseOrigin.TrackingUniverseStanding, ref p, _poseSize, OpenVR.k_ulInvalidInputValueHandle) != EVRInputError.None
                    || !p.bActive || !p.pose.bPoseIsValid) continue;
                ToUnity(p.pose.mDeviceToAbsoluteTracking, out s.RootPosition, out s.RootRotation);
                var sum = new VRSkeletalSummaryData_t();
                var level = EVRSkeletalTrackingLevel.VRSkeletalTracking_Estimated;
                if (input.GetSkeletalTrackingLevel(s.Handle, ref level) == EVRInputError.None) s.Level = level;
                if (input.GetSkeletalSummaryData(s.Handle, EVRSummaryType.FromDevice, ref sum) == EVRInputError.None)
                {
                    s.Curl[0] = sum.flFingerCurl0; s.Curl[1] = sum.flFingerCurl1; s.Curl[2] = sum.flFingerCurl2;
                    s.Curl[3] = sum.flFingerCurl3; s.Curl[4] = sum.flFingerCurl4;
                    s.Valid = true;
                }
                if (input.GetSkeletalBoneData(s.Handle, EVRSkeletalTransformSpace.Model, EVRSkeletalMotionRange.WithController, _bones) == EVRInputError.None)
                {
                    // OpenVR hand skeleton: 1 wrist, 7 index / 12 middle / 22 pinky proximal joints (the knuckles).
                    for (int i = 0; i < 31; i++) s.Bones[i] = BonePos(i);
                    s.Wrist = s.Bones[1]; s.IndexKnuckle = s.Bones[7]; s.MiddleKnuckle = s.Bones[12]; s.PinkyKnuckle = s.Bones[22];
                    s.HasBones = (s.MiddleKnuckle - s.Wrist).sqrMagnitude > 1e-6f;
                }
                if (s.Valid && s.HasBones) s.LastGoodTime = Time.unscaledTime;
            }
        }

        private static Vector3 BonePos(int i) { var v = _bones[i].position; return new Vector3(v.v0, v.v1, -v.v2); }

        public static void Haptic(Hand hand, float duration = 0.02f, float frequency = 150f, float amplitude = 0.4f)
        {
            if (!Ready || _haptic == 0) return;
            var input = OpenVR.Input;
            if (input == null) return;
            ulong device = 0;
            input.GetInputSourceHandle(hand == Hand.Left ? "/user/hand/left" : "/user/hand/right", ref device);
            input.TriggerHapticVibrationAction(_haptic, 0f, duration, frequency, amplitude, device);
        }

        private static void ToUnity(HmdMatrix34_t m, out Vector3 pos, out Quaternion rot)
        {
            var mat = Matrix4x4.identity;
            mat[0, 0] = m.m0; mat[0, 1] = m.m1; mat[0, 2] = -m.m2; mat[0, 3] = m.m3;
            mat[1, 0] = m.m4; mat[1, 1] = m.m5; mat[1, 2] = -m.m6; mat[1, 3] = m.m7;
            mat[2, 0] = -m.m8; mat[2, 1] = -m.m9; mat[2, 2] = m.m10; mat[2, 3] = -m.m11;
            pos = mat.GetColumn(3);
            rot = Quaternion.LookRotation(mat.GetColumn(2), mat.GetColumn(1));
        }
    }
}
