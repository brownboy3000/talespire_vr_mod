using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// Controller visuals and laser pointers. The "active" hand (see VRPointer.ArbitrateHands) drives the pointer.
    /// </summary>
    [DefaultExecutionOrder(30030)]
    internal class VRHands : MonoBehaviour
    {
        public class HandState
        {
            public Hand Hand;
            public GameObject Root;
            public LineRenderer Laser;
            public GameObject Tip;
            public GameObject Dot;
            public bool Valid;
            public Vector3 Position;       // world
            public Quaternion Rotation;    // world
            public Ray Ray;                // world pointer ray
            public float Hidden;           // how much of the ray's start isn't drawn (inside a held weapon), world units
            public float Length = -1f;     // laser length to whatever it hits (world units), set by VRPointer
            public Color Color = new Color(0.3f, 0.8f, 1f, 0.9f);
            public Color DotColor = Color.white;   // end dots; tinted by VRPointer while a grip mode is armed
            public Renderer TipRenderer, DotRenderer;
        }

        public readonly HandState Left = new HandState { Hand = Hand.Left };
        public readonly HandState Right = new HandState { Hand = Hand.Right };
        /// <summary>The hand in control of TaleSpire's cursor on the board (chosen by VRPointer from button presses).</summary>
        public Hand ActiveHand { get; private set; } = Hand.Right;
        public HandState Active => ActiveHand == Hand.Left ? Left : Right;
        /// <summary>The hand drawn with the bolder laser: the one driving the cursor right now (may be hovering a menu).</summary>
        public Hand HighlightHand { get; set; } = Hand.Right;


        private static Material _mat;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private readonly MaterialPropertyBlock _dotBlock = new MaterialPropertyBlock();

        private void Awake()
        {
            Build(Left);
            Build(Right);
        }

        private static Material Mat()
        {
            if (_mat != null) return _mat;
            var shader = Shader.Find("Hidden/Internal-Colored");
            _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _mat.SetInt("_ZWrite", 0);
            _mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            _mat.renderQueue = 4000;
            return _mat;
        }

        internal static Material OverlayMaterial => Mat();

        private void Build(HandState h)
        {
            h.Root = new GameObject("TaleSpireVR_Hand_" + h.Hand);
            DontDestroyOnLoad(h.Root);

            h.Laser = h.Root.AddComponent<LineRenderer>();
            h.Laser.sharedMaterial = Mat();
            h.Laser.useWorldSpace = true;
            h.Laser.positionCount = 2;
            h.Laser.numCapVertices = 2;
            h.Laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            h.Laser.receiveShadows = false;

            h.Tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(h.Tip.GetComponent<Collider>());
            h.Tip.name = "Tip";
            h.Tip.transform.SetParent(h.Root.transform, false);
            var r = h.Tip.GetComponent<Renderer>();
            r.sharedMaterial = Mat();
            h.TipRenderer = r;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            // Marker where the laser touches a panel or the board.
            h.Dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(h.Dot.GetComponent<Collider>());
            h.Dot.name = "Dot";
            h.Dot.transform.SetParent(h.Root.transform, false);
            var dr = h.Dot.GetComponent<Renderer>();
            dr.sharedMaterial = Mat();
            h.DotRenderer = dr;
            dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dr.receiveShadows = false;
            VRLayers.SetRecursive(h.Root, VRLayers.Overlay);
        }

        /// <summary>Early in the frame: compute world poses/rays so TaleSpire's LateUpdate sees this frame's laser.</summary>
        public void UpdatePoses()
        {
            ComputePose(Left, SteamVRInput.PoseLeft);
            ComputePose(Right, SteamVRInput.PoseRight);
        }

        /// <summary>Recompute world poses after the rig moved this frame (called by VRPointer before raycasting).</summary>
        public void RefreshPoses()
        {
            ComputePose(Left, SteamVRInput.PoseLeft);
            ComputePose(Right, SteamVRInput.PoseRight);
        }

        public HandState Get(Hand h) { return h == Hand.Left ? Left : Right; }

        public void SetActive(Hand h) { ActiveHand = h; }

        private void LateUpdate()
        {
            DrawHand(Left);
            DrawHand(Right);
        }

        /// <summary>Debug: fake both hands below the head, pointing forward and down (to see lasers without controllers).</summary>
        public static bool DebugFakeHands;
        /// <summary>Debug ("handaim"): a fixed laser for one hand (index = Hand).</summary>
        public static readonly Ray?[] DebugHandRays = new Ray?[2];

        private void ComputePose(HandState h, SteamVRInput.Pose pose)
        {
            var vrRig = VRManager.Instance != null ? VRManager.Instance.Rig : null;
            h.Hidden = 0f;
            var aimed = DebugHandRays[(int)h.Hand];
            if (aimed.HasValue)
            {
                h.Valid = true;
                h.Position = aimed.Value.origin;
                h.Rotation = Quaternion.LookRotation(aimed.Value.direction);
                h.Ray = aimed.Value;
                return;
            }
            if (DebugFakeHands && vrRig != null && vrRig.Head != null)
            {
                var head = vrRig.Head;
                float s = vrRig.Scale;
                h.Valid = true;
                h.Position = head.position + head.right * (h.Hand == Hand.Left ? -0.2f : 0.2f) * s - head.up * 0.3f * s + head.forward * 0.25f * s;
                h.Rotation = Quaternion.LookRotation(head.forward - head.up * 0.6f + head.right * (h.Hand == Hand.Left ? 0.15f : -0.15f), head.up);
                h.Ray = new Ray(h.Position, h.Rotation * Vector3.forward);
                return;
            }
            var rig = vrRig != null ? vrRig.RigTransform : null;
            h.Valid = pose.Valid && rig != null;
            if (!h.Valid) return;
            h.Position = rig.TransformPoint(pose.Position);
            h.Rotation = rig.rotation * pose.Rotation * Quaternion.Euler(VRConfig.PointerPitchOffset.Value, 0f, 0f);
            h.Ray = new Ray(h.Position, h.Rotation * Vector3.forward);
            // First person with a gun or a blade in the right hand: its pointer comes out of the barrel / starts at the
            // sword's crossguard, in line with it, and is drawn from the barrel / blade tip on.
            if (h.Hand == Hand.Right && FPWeapons.TryTipRay(out Ray tip, out float hidden)) { h.Ray = tip; h.Hidden = hidden; }
        }

        private void DrawHand(HandState h)
        {
            // First person: lasers are only drawn (white) while that hand's grip is squeezed or a radial menu is open;
            // they point and click the same either way.
            bool show = h.Valid && FirstPersonMode.PointerVisible(h.Hand) && !(h.Hand == Hand.Left && DiceForearm.Wanted);
            if (h.Root.activeSelf != show) h.Root.SetActive(show);
            if (!show) return;
            float scale = VRManager.Instance.Rig.Scale;

            bool active = h.Hand == HighlightHand;
            // Dice in hand (held by the hand in control, DiceHand): its pointer is short, ending at them.
            Vector3 dicePoint = default;
            bool dice = h.Hand == ActiveHand && DiceHand.TryHoldPoint(out dicePoint);
            bool hits = h.Length > 0f && !dice;
            float len = dice ? Vector3.Distance(h.Ray.origin, dicePoint) : hits ? h.Length : 0.3f * scale;
            float width = (active ? 0.0035f : 0.0025f) * scale;
            h.Laser.startWidth = width;
            h.Laser.endWidth = width * 0.6f;
            var c = h.Color;
            if (!active) c.a *= 0.55f;
            if (!hits) c.a *= 0.5f;
            h.Laser.startColor = c;
            h.Laser.endColor = c;
            // (From the ray's start: the controller, or in first person the weapon's barrel / blade tip - the part of
            // the ray inside the weapon isn't drawn over it; nothing of the line if it ends there.)
            Vector3 from = h.Ray.origin;
            bool line = len > h.Hidden;
            if (h.Laser.enabled != line) h.Laser.enabled = line;
            if (h.Tip.activeSelf != line) h.Tip.SetActive(line);
            if (line)
            {
                Vector3 start = from + h.Ray.direction * h.Hidden;
                h.Laser.SetPosition(0, start);
                h.Laser.SetPosition(1, from + h.Ray.direction * len);
                h.Tip.transform.position = start;
                h.Tip.transform.localScale = Vector3.one * 0.015f * scale;
            }

            // Both end dots share one material; colour them per hand (white, or the grip mode's colour).
            _dotBlock.SetColor(ColorId, h.DotColor);
            h.TipRenderer.SetPropertyBlock(_dotBlock);
            h.DotRenderer.SetPropertyBlock(_dotBlock);

            bool showDot = hits;
            if (h.Dot.activeSelf != showDot) h.Dot.SetActive(showDot);
            if (showDot)
            {
                h.Dot.transform.position = from + h.Ray.direction * len;
                h.Dot.transform.localScale = Vector3.one * Mathf.Max(0.006f * scale, len * 0.01f);
            }
        }

        private void OnDestroy()
        {
            if (Left.Root) Destroy(Left.Root);
            if (Right.Root) Destroy(Right.Root);
        }
    }
}
