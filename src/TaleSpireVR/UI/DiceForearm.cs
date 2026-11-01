using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// Where the dice window lives: on the inside of your left forearm (the palm side), as if strapped to the arm. It shows
    /// whenever that wrist is turned so the palm faces up (wherever you are looking); in normal play (palms facing each
    /// other, or down, fingers or knuckles forward) it stays hidden. Everything here is in the rig's tracking space
    /// (meters), so zoom doesn't matter.
    /// </summary>
    internal static class DiceForearm
    {
        // The left controller's pointer ("tip") pose: +z along the laser, +y up, +x out of the left palm (the hand holds
        // the grip with its palm on the controller's +x side). As in FPHands' controller-only hand: the wrist is 0.15 m
        // behind the tip and the fingers point along (0, -0.3, 1), so the forearm runs back along (0, 0.3, -1).
        /// <summary>The middle of the inner forearm, on the skin, from the tip pose: back past the wrist, palm side.</summary>
        private static readonly Vector3 ArmPoint = new Vector3(0.04f, -0.01f, -0.25f);
        private static readonly Vector3 ArmAxis = new Vector3(0f, -0.3f, 1f).normalized;   // towards the hand
        /// <summary>
        /// Palm up: how far the wrist is turned (about the forearm) past sideways (palms facing each other), in degrees -
        /// it shows from 34 and hides again below 17, however the forearm is pitched. Compared as the sine of the turn: the
        /// palm normal against "up" square to the forearm.
        /// </summary>
        private const float ShowTurn = 34f, HideTurn = 17f;
        private static readonly float ShowPalm = Mathf.Sin(ShowTurn * Mathf.Deg2Rad), HidePalm = Mathf.Sin(HideTurn * Mathf.Deg2Rad);
        /// <summary>The palm must stay up this long before it shows (a passing turn of the wrist doesn't flash it).</summary>
        private const float ShowDelay = 0.12f;
        private const float HideDelay = 0.15f, FadeIn = 0.12f;

        /// <summary>The window is out (the gesture is held, or the window is in use).</summary>
        public static bool Wanted { get; private set; }
        /// <summary>Opacity while it fades in.</summary>
        public static float Alpha { get; private set; }
        /// <summary>Debug (status.txt): this frame's palm and gaze cosines.</summary>
        public static float PalmDot, GazeDot;
        /// <summary>Debug ("forearm up|side|off"): a made-up left controller pose in tracking space, for tests without the controller in hand.</summary>
        public static Pose? DebugPose;

        private static float _failedFor, _heldFor;
        private static Vector3 _arm, _palm, _along;

        /// <summary>
        /// Once per frame. allowed: on a board, not first person, no radial menu, no dice in hand. inUse: a laser is on the
        /// window (it stays while you use it, as long as it is allowed).
        /// </summary>
        public static bool Update(VRRig rig, bool allowed, bool inUse)
        {
            float dt = Time.unscaledDeltaTime;
            if (!TryGetArm(rig))
            {
                Wanted = false;
                Alpha = PalmDot = GazeDot = 0f;
                return false;
            }
            Vector3 eye = rig.HeadLocalPosition;
            Vector3 look = rig.HeadLocalRotation * Vector3.forward;
            // The wrist's turn about the forearm: the palm against the "up" square to the forearm (so a forearm pointing
            // down at the table doesn't need a bigger turn). A forearm pointing nearly straight up or down (within ~17
            // degrees of vertical) has no such up.
            Vector3 upAcross = Vector3.up - _along * Vector3.Dot(Vector3.up, _along);
            PalmDot = upAcross.magnitude > 0.3f ? Vector3.Dot(_palm, upAcross.normalized) : 0f;
            GazeDot = Vector3.Dot(look, (_arm - eye).normalized);   // (shown in status.txt only)

            // Not while the left hand is busy (a press, a measure or lasso), so turning that hand never opens it mid-action.
            bool handFree = !SteamVRInput.TriggerLeft.Held && !MeasureTool.Active && !CreatureLasso.Active;
            bool palmUp = Wanted ? PalmDot > HidePalm : handFree && PalmDot > ShowPalm;
            _heldFor = palmUp ? _heldFor + dt : 0f;
            if (!allowed) { Wanted = false; _failedFor = 0f; }
            else if ((palmUp && (Wanted || _heldFor >= ShowDelay)) || (Wanted && inUse)) { Wanted = true; _failedFor = 0f; }
            else if (Wanted && (_failedFor += dt) >= HideDelay) Wanted = false;
            Alpha = Wanted ? Mathf.MoveTowards(Alpha, 1f, dt / FadeIn) : 0f;
            return Wanted;
        }

        /// <summary>
        /// The window's pose (tracking space; it faces along its forward): lying on the forearm, facing out of it, its long
        /// side along the arm and its top towards the top of your view. The dice row (at the bottom of the window) sits on
        /// the arm and the tray rises above it. towardsHand: meters to slide it along the arm from the forearm's middle
        /// towards the wrist.
        /// </summary>
        public static void Place(VRRig rig, float height, float towardsHand, out Vector3 center, out Quaternion rotation)
        {
            Vector3 up = Vector3.Cross(_palm, _along);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
            up.Normalize();
            if (Vector3.Dot(up, rig.HeadLocalRotation * Vector3.up) < 0f) up = -up;
            rotation = Quaternion.LookRotation(-_palm, up);
            Vector3 along = Vector3.ProjectOnPlane(_along, _palm).normalized;
            center = _arm + up * (height * 0.4f) + along * towardsHand;
        }

        private static bool TryGetArm(VRRig rig)
        {
            Vector3 p;
            Quaternion q;
            if (DebugPose.HasValue)
            {
                p = DebugPose.Value.position;
                q = DebugPose.Value.rotation;
            }
            else
            {
                var pose = SteamVRInput.PoseLeft;
                if (!pose.Valid || !rig.HeadTracked) return false;
                p = pose.Position;
                q = pose.Rotation;
            }
            _palm = q * Vector3.right;
            _along = q * ArmAxis;
            _arm = p + q * ArmPoint;
            return true;
        }

        /// <summary>Debug: a left controller pose (tracking space) with the palm turned up, the arm in front of you ("up"), or held as in normal play ("side").</summary>
        public static Pose DebugPoseFor(VRRig rig, bool palmUp)
        {
            Vector3 eye = rig.HeadLocalPosition;
            Quaternion head = rig.HeadLocalRotation;
            Vector3 flatFwd = Vector3.ProjectOnPlane(head * Vector3.forward, Vector3.up).normalized;
            if (flatFwd.sqrMagnitude < 1e-4f) flatFwd = Vector3.forward;
            Vector3 flatRight = Vector3.Cross(Vector3.up, flatFwd);
            Vector3 arm, palm, fingers;
            if (palmUp)
            {
                arm = eye + flatFwd * 0.35f - Vector3.up * 0.3f;
                palm = Vector3.up;
                fingers = flatRight;   // forearm across in front of you, hand to the right
            }
            else
            {
                arm = eye - flatRight * 0.18f - Vector3.up * 0.42f + flatFwd * 0.2f;
                palm = flatRight;                                                // palms facing each other
                fingers = (flatFwd - Vector3.up * 0.5f).normalized;              // pointing forward and down
            }
            Quaternion q = Quaternion.LookRotation(fingers, Vector3.Cross(fingers, palm));
            return new Pose(arm - q * ArmPoint, q);
        }

        /// <summary>
        /// Debug ("forearm turn deg [pitch]"): the normal-play pose with the wrist turned counter-clockwise by deg about the
        /// forearm (palm from sideways towards up), the fingers pitched down by pitch degrees (default 26).
        /// </summary>
        public static Pose DebugPoseTurned(VRRig rig, float deg, float pitch = 26f)
        {
            Vector3 eye = rig.HeadLocalPosition;
            Vector3 flatFwd = Vector3.ProjectOnPlane(rig.HeadLocalRotation * Vector3.forward, Vector3.up).normalized;
            if (flatFwd.sqrMagnitude < 1e-4f) flatFwd = Vector3.forward;
            Vector3 flatRight = Vector3.Cross(Vector3.up, flatFwd);
            Vector3 arm = eye - flatRight * 0.18f - Vector3.up * 0.42f + flatFwd * 0.2f;
            Vector3 fingers = Quaternion.AngleAxis(pitch, flatRight) * flatFwd;
            // The forearm (ArmAxis in the controller's space) is what the wrist turns about.
            Quaternion side = Quaternion.LookRotation(fingers, Vector3.Cross(fingers, flatRight));
            Vector3 forearm = side * ArmAxis;
            Quaternion turned = Quaternion.AngleAxis(deg, forearm) * side;
            if (Vector3.Dot(turned * Vector3.right, Vector3.up) < Vector3.Dot(side * Vector3.right, Vector3.up))
                turned = Quaternion.AngleAxis(-deg, forearm) * side;   // (whichever way turns the palm up)
            return new Pose(arm - turned * ArmPoint, turned);
        }
    }
}
