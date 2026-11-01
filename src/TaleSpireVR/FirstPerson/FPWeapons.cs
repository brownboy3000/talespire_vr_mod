using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// First-person weapons (WeaponLoadouts): the weapon the creature you are holds is in your right hand.
    ///  - Shotgun: held by its pistol grip; its pointer comes out of the barrel. The trigger fires a blast of fire from
    ///    the barrel with a bang; with the pointer showing (grip squeezed) on another creature, that creature is attacked
    ///    (TaleSpire's Red Laser attack, as from the radial menu).
    ///  - Longsword: its pointer starts at the crossguard, along the blade, but is only drawn from the tip on. Holding
    ///    the trigger while the blade enters another creature strikes it once (TaleSpire's Hit attack, its run-up
    ///    skipped here so it lands at once, and with only our sword hit sound).
    ///  - Flame: nothing in the hand (fingers tracked, pointer as usual). Holding the trigger lights a line of fire on the
    ///    floor in the direction your right palm faces, growing over 2 s to 7 squares long and half a square wide, with a
    ///    fiery sigil turning in front of the palm and a roaring fire sound (FlameSound); it dies down quickly when you
    ///    let go. Each fire piece stays where it
    ///    lit (sweep the palm and new ones light beside it while the old ones die down). Creatures it catches burn
    ///    (TaleSpire's Burning look) for 5 s, on this computer only - nothing is changed on the board.
    /// A gun or blade replaces your right hand: the hand isn't drawn, the weapon sits where it would be held in it, and
    /// the pointer follows the weapon. Nothing or the flame: the hand as usual.
    /// The models are rebuilt from weapons.bin (exported once from the Unity Asset Store packs with a newer editor).
    /// </summary>
    internal sealed class FPWeapons
    {
        public static FPWeapons Instance { get; private set; }

        /// <summary>How a model sits in the right hand, in its own (model) coordinates.</summary>
        internal sealed class Grip
        {
            public float ModelScale;              // real metres per model unit
            public Vector3 Point;                 // on the handle's axis, level with the middle finger
            public Vector3 Side, Fwd;             // the hand's index->pinky and wrist->knuckles directions (orthonormal)
            public float KnuckleUp, KnuckleFwd;   // the middle knuckle from Point: out of the back of the hand, along the fingers (real metres)
            public Vector3 Tip, TipDir;           // the barrel / blade tip, and the way it points
            public Vector3 RayFrom;               // where the pointer starts (inside the model), along TipDir; it's drawn from Tip on
            public Vector3 BladeFrom;             // melee: the blade runs from here to Tip
            public Vector3[] Strikes;             // melee: the parts that strike, as line segments (from, to, from, to...); null: BladeFrom -> Tip

            public Vector3[] Segments => Strikes ?? new[] { BladeFrom, Tip };
        }

        /// <summary>
        /// A gun held by its grip: grip = its axis from top to bottom (the hand's index -> pinky), point = on it level
        /// with the middle finger, muzzle and barrel = where the shot comes out and its direction (model units).
        /// </summary>
        private static Grip Gun(float scale, Vector3 point, Vector3 grip, Vector3 muzzle, Vector3 barrel)
        {
            grip.Normalize();
            barrel.Normalize();
            return new Grip
            {
                ModelScale = scale, Point = point, Side = grip,
                Fwd = (barrel - grip * Vector3.Dot(barrel, grip)).normalized,   // (square to the grip, towards the muzzle)
                KnuckleUp = 0.042f, KnuckleFwd = 0.02f,
                Tip = muzzle, TipDir = barrel, RayFrom = muzzle,
            };
        }

        /// <summary>
        /// A melee weapon held by its handle like the longsword: axis = handle towards the head / tip, edge = the side
        /// that strikes (it faces where your knuckles do), the handle running across the palm 20 degrees diagonally.
        /// strikes: line segments (pairs) over the parts that hit.
        /// </summary>
        private static Grip Melee(float scale, Vector3 point, Vector3 axis, Vector3 edge, float knuckleUp, Vector3 tip, Vector3 rayFrom, params Vector3[] strikes)
        {
            const float s = 0.342f, c = 0.940f;   // sin, cos 20 degrees
            axis.Normalize();
            edge.Normalize();
            return new Grip
            {
                ModelScale = scale, Point = point,
                Side = s * edge - c * axis, Fwd = c * edge + s * axis,
                KnuckleUp = knuckleUp, KnuckleFwd = 0.018f,
                Tip = tip, TipDir = axis, RayFrom = rayFrom, BladeFrom = rayFrom,
                Strikes = strikes.Length >= 2 ? strikes : null,
            };
        }

        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        /// <summary>A magic weapon held like a melee one (the fire staff): it shoots from tip the way your hand points.</summary>
        private static Grip Caster(Grip g, Vector3 tip)
        {
            g.Tip = g.RayFrom = tip;
            g.TipDir = g.Fwd;
            return g;
        }

        private static readonly Dictionary<string, Grip> Grips = new Dictionary<string, Grip>
        {
            // Measured from side views with a grid (tools/weapons: export-weapons.ps1 -Mode Side). Guns: barrel along
            // -z (the musket -x, the injector +z), grips slanting back by the angle each model has.
            ["pistol"] = Gun(0.92f, V(0f, -0.041f, 0.050f), V(0f, -0.977f, 0.215f), V(0f, 0.011f, -0.150f), V(0f, 0f, -1f)),
            ["uzi"] = Gun(1f, V(0f, -0.036f, 0.024f), V(0f, -1f, 0f), V(0f, 0.025f, -0.219f), V(0f, 0f, -1f)),
            ["rifle"] = Gun(1f, V(0f, -0.032f, 0.153f), V(0f, -0.883f, 0.470f), V(0f, 0.059f, -0.339f), V(0f, 0f, -1f)),
            ["m249"] = Gun(1f, V(0f, -0.037f, 0.160f), V(0f, -0.940f, 0.342f), V(0f, 0.064f, -0.426f), V(0f, 0f, -1f)),
            ["m107"] = Gun(1f, V(0f, -0.089f, 0.197f), V(0f, -0.921f, 0.391f), V(0f, 0.025f, -0.700f), V(0f, 0f, -1f)),
            ["rpg"] = Gun(1f, V(0f, -0.124f, -0.104f), V(0f, -1f, 0f), V(0f, 0.005f, -0.741f), V(0f, 0f, -1f)),   // (the rocket's tip)
            ["musket"] = Gun(1f, V(0.186f, -0.016f, 0f), V(0.60f, -0.80f, 0f), V(-0.420f, 0.024f, 0f), V(-1f, 0f, 0f)),   // (the stock's wrist)
            ["injector"] = Gun(1f, V(0f, -0.047f, -0.059f), V(0f, -0.976f, -0.218f), V(0f, 0.079f, 0.396f), V(0f, 0f, 1f)),
            // Melee: handle axis, striking edge, then the striking parts (shaft and head; axes' blades, hammer faces).
            ["battleaxe"] = Melee(1.1f, V(0f, -0.015f, 0f), V(0f, 1f, 0f), V(1f, 0f, 0f), 0.04f, V(0f, 0.44f, 0f), V(0f, 0.06f, 0f),
                V(0f, 0.06f, 0f), V(0f, 0.44f, 0f), V(0f, 0.35f, 0f), V(0.15f, 0.35f, 0f), V(0f, 0.35f, 0f), V(-0.15f, 0.35f, 0f)),
            ["fireaxe"] = Melee(0.54f, V(0f, -0.812f, 0f), V(0f, 1f, 0f), V(0f, 0f, 1f), 0.042f, V(0f, 0.517f, 0f), V(0f, -0.70f, 0f),
                V(0f, -0.6f, 0f), V(0f, 0.517f, 0f), V(0f, 0.36f, 0f), V(0f, 0.36f, 0.22f), V(0f, 0.36f, 0f), V(0f, 0.30f, -0.27f)),
            ["warhammer"] = Melee(0.7f, V(0f, -0.394f, 0f), V(0f, 1f, 0f), V(1f, 0f, 0f), 0.035f, V(0f, 0.557f, 0f), V(0f, -0.30f, 0f),
                V(0f, -0.25f, 0f), V(0f, 0.557f, 0f), V(-0.22f, 0.36f, 0f), V(0.22f, 0.36f, 0f)),
            ["bat"] = Melee(0.55f, V(0f, 0.13f, 0f), V(0f, 1f, 0f), V(0f, 0f, 1f), 0.035f, V(0f, 1.57f, 0f), V(0f, 0.25f, 0f),
                V(0f, 0.3f, 0f), V(0f, 1.57f, 0f)),
            ["dagger"] = Melee(0.63f, V(0f, -0.011f, 0f), V(0f, -1f, 0f), V(1f, 0f, 0f), 0.038f, V(0f, -0.483f, 0f), V(0f, -0.10f, 0f),
                V(0f, -0.15f, 0f), V(0f, -0.483f, 0f)),
            ["crowbar"] = Melee(0.45f, V(0f, -0.621f, 0f), V(0f, 1f, 0f), V(0f, 0f, 1f), 0.032f, V(0f, 0.837f, 0f), V(0f, -0.50f, 0f),
                V(0f, -0.45f, 0f), V(0f, 0.837f, 0f), V(0f, 0.78f, 0.02f), V(0f, 0.80f, 0.26f)),
            // Round shield: front face +z, a strap across its back along y. The fist holds the strap, knuckles towards
            // the face, so the shield stands upright in front of the hand facing forward; the pointer comes out of the
            // middle of its face; it strikes with its face (two diameters).
            ["bow"] = Gun(1f, V(0f, -0.01f, 0.018f), V(0f, -1f, 0f), V(0.03f, 0.085f, 0.018f), V(1f, 0f, 0f)),   // (riser; the arrow's way, from the string)
            ["combatshotgun"] = Gun(1f, V(0f, -0.045f, -0.060f), V(0f, -0.984f, -0.176f), V(0f, 0.044f, 0.80f), V(0f, 0f, 1f)),
            ["wand"] = Gun(0.5f, V(0f, -0.05f, 0f), V(-1f, 0f, 0f), V(0.045f, 0.52f, 0f), V(0f, 1f, 0f)),   // (pointed along the hand, the gem out front)
            // Fire staff: held upright like a melee weapon; it shoots out of its crystal the way your hand points.
            ["firestaff"] = Caster(Melee(0.75f, V(0f, 0f, 0f), V(0f, 1f, 0f), V(1f, 0f, 0f), 0.035f, V(0f, 0.87f, 0f), V(0f, 0.75f, 0f)), V(0f, 0.75f, 0f)),
            ["spear"] = Melee(1f, V(0f, -0.2f, 0f), V(0f, 1f, 0f), V(1f, 0f, 0f), 0.035f, V(0f, 0.633f, 0f), V(0f, -0.1f, 0f),
                V(0f, -0.05f, 0f), V(0f, 0.633f, 0f)),
            ["halberd"] = Melee(0.45f, V(0f, 1.0f, 0f), V(0f, -1f, 0f), V(1f, 0f, 0f), 0.035f, V(0f, -2.19f, 0f), V(0f, 0.7f, 0f),
                V(0f, 0.6f, 0f), V(0f, -2.19f, 0f), V(0f, -1.4f, 0f), V(0.40f, -1.45f, 0f), V(0f, -1.4f, 0f), V(-0.38f, -1.35f, 0f)),
            ["glaive"] = Melee(1f, V(0f, -0.15f, 0f), V(0f, 1f, 0f), V(1f, 0f, 0f), 0.035f, V(0f, 1.17f, 0f), V(0f, -0.05f, 0f),
                V(0f, 0f, 0f), V(0f, 0.6f, 0f), V(0f, 0.6f, 0f), V(0.05f, 1.17f, 0f), V(0.08f, 0.65f, 0f), V(0.19f, 1.15f, 0f)),
            ["scythe"] = Melee(0.9f, V(0f, 0.39f, 0f), V(0f, 1f, 0f), V(1f, 0f, 0f), 0.035f, V(0f, 1.16f, 0f), V(0f, 0.5f, 0f),
                V(0f, 0.5f, 0f), V(0f, 1.0f, 0f), V(0f, 1.0f, 0f), V(0.6f, 1.05f, 0f), V(0.6f, 1.05f, 0f), V(0.5f, 0.72f, 0f)),
            ["morningstar"] = Melee(0.85f, V(0f, -0.04f, 0f), V(0f, 1f, 0f), V(0f, 0f, 1f), 0.035f, V(0f, 0.61f, 0f), V(0f, 0f, 0f),
                V(0f, 0.05f, 0f), V(0f, 0.55f, 0f), V(-0.12f, 0.43f, 0f), V(0.12f, 0.43f, 0f)),
            ["scimitar"] = Melee(0.85f, V(0f, -0.37f, 0f), V(0f, 1f, 0f), V(-1f, 0f, 0f), 0.035f, V(0f, 0.588f, 0f), V(0f, -0.2f, 0f),
                V(0f, -0.1f, 0f), V(0f, 0.45f, 0f), V(0f, 0.45f, 0f), V(-0.09f, 0.588f, 0f)),
            ["handaxe"] = Melee(0.45f, V(-0.314f, 0f, -0.296f), V(0.869f, 0f, 0.495f), V(-0.495f, 0f, 0.869f), 0.035f, V(0.40f, 0f, 0.11f), V(-0.2f, 0f, -0.23f),
                V(-0.2f, 0f, -0.23f), V(0.37f, 0f, 0.10f), V(0.35f, 0f, 0.15f), V(0.29f, 0f, 0.37f)),
            ["machete"] = Melee(0.55f, V(0.31f, -0.13f, 0f), V(-1f, 0f, 0f), V(0f, 1f, 0f), 0.038f, V(-0.575f, 0.02f, 0f), V(0.10f, -0.10f, 0f),
                V(0.04f, 0.02f, 0f), V(-0.575f, 0.02f, 0f), V(0.04f, 0.17f, 0f), V(-0.575f, 0.17f, 0f)),
            ["knife"] = Melee(0.55f, V(0f, 0f, 0.02f), V(0f, 0f, 1f), V(0f, -1f, 0f), 0.03f, V(0f, -0.005f, 0.511f), V(0f, 0f, 0.10f),
                V(0f, -0.01f, 0.15f), V(0f, -0.005f, 0.511f)),
            ["sledgehammer"] = Melee(0.42f, V(0f, 0.15f, 0f), V(0f, 1f, 0f), V(0f, 0f, 1f), 0.035f, V(0f, 2.12f, 0f), V(0f, 0.4f, 0f),
                V(0f, 0.4f, 0f), V(0f, 1.95f, 0f), V(0f, 2.02f, -0.23f), V(0f, 2.02f, 0.23f)),
            // Kite shield: front face -z (convex), its flat top at -y; held behind the middle of its face, as the round one.
            ["kiteshield"] = new Grip
            {
                ModelScale = 0.45f,
                Point = V(0f, -0.1f, -0.05f),
                Side = V(0f, 1f, 0f), Fwd = V(0f, 0f, -1f),
                KnuckleUp = 0.04f, KnuckleFwd = 0.018f,
                Tip = V(0f, -0.1f, -0.14f), TipDir = V(0f, 0f, -1f), RayFrom = V(0f, -0.1f, -0.05f),
                Strikes = new[] { V(0f, -0.85f, -0.12f), V(0f, 0.85f, -0.12f), V(-0.6f, -0.1f, -0.02f), V(0.6f, -0.1f, -0.02f) },
            },
            ["shield"] = new Grip
            {
                ModelScale = 0.85f,
                Point = V(0f, 0f, -0.145f),
                Side = V(0f, -1f, 0f), Fwd = V(0f, 0f, 1f),
                KnuckleUp = 0.04f, KnuckleFwd = 0.018f,
                Tip = V(0f, 0f, 0.012f), TipDir = V(0f, 0f, 1f), RayFrom = V(0f, 0f, -0.12f),
                Strikes = new[] { V(0f, -0.33f, 0.01f), V(0f, 0.33f, 0.01f), V(-0.31f, 0f, 0.01f), V(0.31f, 0f, 0.01f) },
            },
            // Benelli M4: barrel along -z, pistol grip slanting down and back behind the trigger; the index finger
            // rests on the trigger, the others wrap the grip.
            ["shotgun"] = new Grip
            {
                ModelScale = 1f,
                Point = new Vector3(0f, -0.080f, 0.181f),
                Side = new Vector3(0f, -0.905f, 0.426f),
                Fwd = new Vector3(0f, -0.426f, -0.905f),
                KnuckleUp = 0.042f, KnuckleFwd = 0.02f,
                Tip = new Vector3(0f, 0f, -0.478f), TipDir = new Vector3(0f, 0f, -1f),
                RayFrom = new Vector3(0f, 0f, -0.478f),
            },
            // Longsword: blade along +y, edges along x; the handle runs across the palm a little diagonally (blade
            // leaning forward), the hand a little below the guard.
            ["sword"] = new Grip
            {
                ModelScale = 0.85f,
                Point = new Vector3(0f, -0.38f, 0f),
                Side = new Vector3(0.342f, -0.940f, 0f),
                Fwd = new Vector3(0.940f, 0.342f, 0f),
                KnuckleUp = 0.04f, KnuckleFwd = 0.018f,
                Tip = new Vector3(0f, 0.595f, 0f), TipDir = new Vector3(0f, 1f, 0f),
                RayFrom = new Vector3(0f, -0.26f, 0f),   // the crossguard: menus close to you are easy to reach
                BladeFrom = new Vector3(0f, -0.22f, 0f),
            },
        };

        private sealed class Model { public Mesh Mesh; public Material[] Materials; }
        private Dictionary<string, Model> _models;
        private bool _modelsTried;
        private GameObject _go;
        private MeshFilter _mf;
        private MeshRenderer _mr;
        private string _shown;
        private WeaponDef _held;
        private bool _armed;   // this trigger press is the weapon's (it didn't start on a menu)

        public FPWeapons() { Instance = this; }

        /// <summary>A hand power (flame, ice) is being cast with the right hand now (its fingers open).</summary>
        public static bool PowerCasting => FirstPersonMode.Active && Instance != null && Instance._flameHeld;

        /// <summary>The weapon in your right hand (first person only).</summary>
        public static WeaponDef Held => FirstPersonMode.Active && Instance != null ? Instance._held : null;

        private static Grip HeldGrip
        {
            get
            {
                var w = Held;
                return w != null && w.Mesh != null && Grips.TryGetValue(w.Mesh, out var g) ? g : null;
            }
        }

        /// <summary>A gun or blade is held: the right hand isn't drawn, only the weapon (nothing or the flame: the hand is).</summary>
        public static bool HidesHand => HeldGrip != null;

        public void SetHeld(WeaponDef w)
        {
            if (w == _held) return;
            Plugin.Log.LogInfo($"First person: holding {(w != null ? w.Name : "nothing")}");
            _held = w;
            _armed = false;
            _flameHeld = false;
            _struck.Clear();
            _struckAt.Clear();
        }

        // ---- Where the weapon is ---------------------------------------------------------------------------------

        /// <summary>
        /// Every held weapon leans this much further forward (degrees) than its grip above: turned about the back of
        /// the hand's axis through the grip point, its top / tip away from you, never left or right - so its handle
        /// sits at the angle of an Index controller's in your hand. ("wpn tune sword lean degrees" to try others.)
        /// </summary>
        private static float Lean = 15f;

        private static float _autoLean = 15f;

        /// <summary>
        /// The lean for the controller in your right hand: the Index's tuned 15 degrees, or - for other controllers, whose
        /// handles sit at other angles in the hand - whatever makes the barrel / blade point where the controller points
        /// (its pointer direction, seen from the side of the hand), smoothed and kept within -10 .. 50 degrees.
        /// </summary>
        private static float LeanFor(Vector3 f, Vector3 u)
        {
            if (ControllerProfiles.Of(Hand.Right).WeaponLean.HasValue) return Lean;
            var hs = VRManager.Instance != null && VRManager.Instance.Hands != null ? VRManager.Instance.Hands.Get(Hand.Right) : null;
            if (hs == null || !hs.Valid) return _autoLean;
            Vector3 d = hs.Rotation * Vector3.forward;
            d -= u * Vector3.Dot(d, u);
            if (d.sqrMagnitude < 1e-4f) return _autoLean;
            float want = Mathf.Clamp(Vector3.SignedAngle(f, d.normalized, u), -10f, 50f);
            _autoLean = Mathf.Lerp(_autoLean, want, 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.2f));
            return _autoLean;
        }

        /// <summary>The weapon model's pose and scale this frame (world), from the right hand's frame.</summary>
        private static bool TryPose(Grip g, out Vector3 pos, out Quaternion rot, out float scale)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;
            scale = 1f;
            var rig = VRManager.Instance != null ? VRManager.Instance.Rig : null;
            if (g == null || rig == null || !FPHands.TryFrame(Hand.Right, out Vector3 w, out Vector3 f, out Vector3 s, out float len)) return false;
            f.Normalize();
            s = (s - f * Vector3.Dot(s, f)).normalized;
            Vector3 u = Vector3.Cross(f, s);   // out of the back of a right hand
            rot = Quaternion.LookRotation(f, u) * Quaternion.Inverse(Quaternion.LookRotation(g.Fwd, Vector3.Cross(g.Fwd, g.Side)));
            rot = Quaternion.AngleAxis(LeanFor(f, u), u) * rot;   // (turns the index side, the weapon's top, towards the fingers)
            float rs = rig.Scale;
            scale = g.ModelScale * rs;
            Vector3 knuckle = w + f * len;
            Vector3 p = knuckle - u * (g.KnuckleUp * rs) - f * (g.KnuckleFwd * rs);
            pos = p - rot * (g.Point * scale);   // (so it turns about the grip point)
            return true;
        }

        /// <summary>
        /// The right pointer while a gun or blade is held: out of the barrel / from the sword's crossguard, along it - and
        /// how much of its start is inside the weapon (world units), not drawn: it shows from the barrel / blade tip on.
        /// </summary>
        public static bool TryTipRay(out Ray ray, out float hidden)
        {
            ray = default;
            hidden = 0f;
            var g = HeldGrip;
            if (g == null || !TryPose(g, out Vector3 pos, out Quaternion rot, out float scale)) return false;
            ray = new Ray(pos + rot * (g.RayFrom * scale), (rot * g.TipDir).normalized);
            hidden = Mathf.Max(0f, Vector3.Dot(g.Tip - g.RayFrom, g.TipDir.normalized)) * scale;
            return true;
        }

        /// <summary>The barrel / blade tip and its direction (where the shotgun's blast comes out).</summary>
        private static bool TryMuzzle(out Ray ray)
        {
            ray = default;
            var g = HeldGrip;
            if (g == null || !TryPose(g, out Vector3 pos, out Quaternion rot, out float scale)) return false;
            ray = new Ray(pos + rot * (g.Tip * scale), (rot * g.TipDir).normalized);
            return true;
        }

        /// <summary>After the hands are posed: the model in the hand, the effects, the burning creatures.</summary>
        public void LateUpdate(bool handsShown)
        {
            var g = HeldGrip;
            bool show = g != null && handsShown && EnsureModel(_held.Mesh);
            if (show && TryPose(g, out Vector3 pos, out Quaternion rot, out float scale))
            {
                _go.transform.SetPositionAndRotation(pos, rot);
                _go.transform.localScale = Vector3.one * scale;
            }
            else show = false;
            if (_go != null && _go.activeSelf != show) _go.SetActive(show);

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            UpdateBlasts(dt);
            UpdateFlame(dt);
            UpdateBurning();
            if (!FirstPersonMode.Active) _armed = _flameHeld = false;
        }

        // ---- The trigger -----------------------------------------------------------------------------------------

        /// <summary>
        /// Each frame from VRPointer: the right trigger uses the weapon unless the press began with that laser on a menu
        /// (a radial option, our buttons, the menu panel...).
        /// </summary>
        public static void Trigger(bool rightOnMenu)
        {
            var me = Instance;
            if (me == null) return;
            var w = Held;
            var t = SteamVRInput.TriggerRight;
            if (w == null || FirstPersonMode.Transitioning) { me._armed = me._flameHeld = false; return; }
            if (t.Down) me._armed = !rightOnMenu;
            if (!t.Held) me._armed = false;
            switch (w.Kind)
            {
                case WeaponKind.Ranged:
                    if (t.Down && me._armed) me.FireGun();
                    break;
                case WeaponKind.Melee:
                    me.SwordContact(me._armed);   // (also unarmed: the blade leaving a creature it struck is tracked)
                    break;
                case WeaponKind.Power:
                    me._flameHeld = me._armed;
                    break;
            }
        }

        // ---- Guns ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Any gun, one shot per trigger pull: the shotgun's blast of fire out of its own muzzle and its bang, and with the
        /// pointer on another creature, the gun's TaleSpire attack on it (WeaponDef.Attack: green for the sci-fi
        /// injector; else ShotgunAttack in the config, red by default).
        /// </summary>
        private void FireGun(CreatureBoardAsset target = null)
        {
            var rig = VRManager.Instance.Rig;
            if (!TryMuzzle(out Ray r)) { Plugin.Log.LogInfo("[wpn] no muzzle (no gun in hand?)"); return; }
            float s = rig.Scale;
            switch (_held != null ? _held.Shot : ShotStyle.Gun)
            {
                case ShotStyle.Bow:
                    // No flash: the string's twang.
                    WeaponFx.PlayClip(WeaponSounds.Twang, r.origin, 0.9f);
                    SteamVRInput.Haptic(Hand.Right, 0.05f, 160f, 0.6f);
                    break;
                case ShotStyle.Magic:
                    WeaponSounds.Flash(r.origin, new Color(0.75f, 0.45f, 1f), 0.9f * s);
                    WeaponFx.PlayClip(WeaponSounds.Zap, r.origin, 0.8f);
                    SteamVRInput.Haptic(Hand.Right, 0.05f, 220f, 0.5f);
                    break;
                case ShotStyle.FireBolt:
                    // A small burst of fire out of the crystal.
                    Blast(r, s, new[] { 0.04f, 0.16f, 0.30f, 0.46f }, new[] { 0.09f, 0.14f, 0.19f, 0.24f });
                    WeaponFx.PlayClip(FlameSound.Whoosh, r.origin, 0.8f, 1.15f);
                    SteamVRInput.Haptic(Hand.Right, 0.06f, 90f, 0.7f);
                    break;
                default:
                    // A quick cone of fire out of the barrel: small at the muzzle, wider further out, each puff a moment later.
                    Blast(r, s, new[] { 0.02f, 0.10f, 0.21f, 0.35f, 0.50f, 0.68f }, new[] { 0.09f, 0.14f, 0.20f, 0.27f, 0.35f, 0.42f });
                    WeaponFx.PlayClip(Clip("ts_sfx_magic_missile_hit", "PieceHitBoardHard"), r.origin, 1f, 0.85f, 0.1f);
                    SteamVRInput.Haptic(Hand.Right, 0.09f, 60f, 1f);
                    break;
            }

            // With the pointer showing on another creature: TaleSpire's ranged attack on it.
            var me = FirstPersonMode.Creature;
            if (me == null || (target == null && !FirstPersonMode.PointerVisible(Hand.Right))) return;
            var victim = target ?? CreatureOnRay(r, rig, me);
            if (victim != null) Attack(me, victim, _held?.Attack ?? VRConfig.WeaponRangedAttack.Value, CreatureBoardAsset.AttackEmotes.TLA_LaserRed);
        }

        /// <summary>Puffs of fire along a ray (dist / width in real metres), each a moment after the last.</summary>
        private void Blast(Ray r, float s, float[] dist, float[] width)
        {
            for (int i = 0; i < dist.Length; i++)
            {
                var f = NewFlame(i == 0);
                if (f == null) break;
                f.Dir = r.direction;
                f.Base = r.origin + r.direction * dist[i] * s;
                f.Size = width[i] / FireWidth * s;
                f.Delay = i * 0.014f;
                f.Speed = (0.5f + i * 0.15f) * s;
                f.Age = 0f;
                f.Spin = Random.Range(0f, 360f);
                if (f.Lights != null) foreach (var l in f.Lights) { l.range = 1.6f * s; l.intensity = 4f; }
                _blasts.Add(f);
            }
        }

        /// <summary>Debug ("wpn shoot name"): the gun in hand fires, its attack going to the nearest creature of that name.</summary>
        public static void DebugShoot(string name)
        {
            var me = FirstPersonMode.Creature;
            CreatureBoardAsset best = null;
            foreach (var c in Object.FindObjectsOfType<CreatureBoardAsset>())
                if (c != me && c.Name == name && (best == null || me == null
                    || Vector3.Distance(c.transform.position, me.transform.position) < Vector3.Distance(best.transform.position, me.transform.position))) best = c;
            if (me == null || best == null || Instance == null) { Plugin.Log.LogInfo("[wpn] shoot: not in first person, or no such creature"); return; }
            Instance.FireGun(best);
        }

        /// <summary>The nearest other creature the ray passes through, in front of whatever else it hits.</summary>
        private static CreatureBoardAsset CreatureOnRay(Ray r, VRRig rig, CreatureBoardAsset me)
        {
            float wall = VRPointer.WorldHitDistance(r, rig, false);
            CreatureBoardAsset best = null;
            float bestD = float.MaxValue;
            foreach (var c in Creatures())
            {
                if (c == null || c == me || !c.isActiveAndEnabled) continue;
                Cylinder(c, out Vector3 basePos, out float radius, out float height);
                var b = new Bounds(basePos + Vector3.up * height * 0.5f, new Vector3(radius * 2f, height, radius * 2f));
                if (!b.IntersectRay(r, out float d) || d > wall + radius || d > bestD) continue;
                best = c;
                bestD = d;
            }
            return best;
        }

        // ---- Longsword -------------------------------------------------------------------------------------------

        /// <summary>Creatures struck whose model the blade has not left yet: no second strike until it has.</summary>
        private readonly HashSet<CreatureBoardAsset> _struck = new HashSet<CreatureBoardAsset>();
        private readonly List<CreatureBoardAsset> _cleared = new List<CreatureBoardAsset>();
        private const int BladeSamples = 30;
        private const int StrikeSamples = 3;   // a tenth of the blade
        /// <summary>When each creature was last struck: no new strike on it within StrikeGap (a blade grazing its edge).</summary>
        private readonly Dictionary<CreatureBoardAsset, float> _struckAt = new Dictionary<CreatureBoardAsset, float>();
        private const float StrikeGap = 0.4f;

        /// <summary>
        /// Every frame while the sword is held: with the trigger held, a creature with a tenth of the blade or more inside
        /// it is struck once. It can't be struck again until the blade has left it completely (a new, separate blow);
        /// holding the blade in it does nothing more.
        /// </summary>
        private void SwordContact(bool armed)
        {
            var g = HeldGrip;
            var me = FirstPersonMode.Creature;
            if (g == null || me == null || !TryPose(g, out Vector3 pos, out Quaternion rot, out float scale)) { _struck.Clear(); return; }
            var segs = g.Segments;
            _cleared.Clear();
            foreach (var c in _struck) if (c == null || c == me) _cleared.Add(c);
            foreach (var c in Creatures())
            {
                if (c == null || c == me || !c.isActiveAndEnabled) continue;
                Cylinder(c, out Vector3 basePos, out float radius, out float height);
                // Each striking part (a line segment) is sampled on its own: a tenth of any one of them inside strikes.
                int inside = 0, most = 0;
                Vector3 hitAt = Vector3.zero;
                for (int s = 0; s + 1 < segs.Length; s += 2)
                {
                    Vector3 a = pos + rot * (segs[s] * scale), b = pos + rot * (segs[s + 1] * scale);
                    int n = 0;
                    for (int i = 0; i < BladeSamples; i++)
                    {
                        Vector3 p = Vector3.Lerp(a, b, (i + 0.5f) / BladeSamples);
                        Vector3 d = p - basePos;
                        if (d.y < 0f || d.y > height || new Vector2(d.x, d.z).sqrMagnitude > radius * radius) continue;
                        n++;
                        hitAt = p;
                    }
                    inside += n;
                    most = Mathf.Max(most, n);
                }
                if (inside == 0) { if (_struck.Contains(c)) _cleared.Add(c); continue; }   // the weapon is out of it
                if (!armed || most < StrikeSamples || _struck.Contains(c)) continue;
                if (_struckAt.TryGetValue(c, out float last) && Time.unscaledTime - last < StrikeGap) continue;
                _struck.Add(c);
                _struckAt[c] = Time.unscaledTime;
                Strike(me, c, hitAt);
            }
            foreach (var c in _cleared) _struck.Remove(c);
        }

        private static void Strike(CreatureBoardAsset me, CreatureBoardAsset victim, Vector3 at)
        {
            WeaponFx.PlayClip(Clip("sfx-SwordBigHit", "sfx-ArrowHit"), at, 0.9f);
            SteamVRInput.Haptic(Hand.Right, 0.06f, 120f, 0.9f);
            Attack(me, victim, VRConfig.WeaponMeleeAttack.Value, CreatureBoardAsset.AttackEmotes.Hit);
        }

        /// <summary>Debug ("wpn strike name"): a sword strike on the nearest creature of that name, as if the blade went in.</summary>
        public static void DebugStrike(string name)
        {
            var me = FirstPersonMode.Creature;
            CreatureBoardAsset best = null;
            foreach (var c in Object.FindObjectsOfType<CreatureBoardAsset>())
                if (c != me && c.Name == name && (best == null || me == null
                    || Vector3.Distance(c.transform.position, me.transform.position) < Vector3.Distance(best.transform.position, me.transform.position))) best = c;
            if (me == null || best == null) { Plugin.Log.LogInfo("[wpn] strike: not in first person, or no such creature"); return; }
            Cylinder(best, out Vector3 basePos, out _, out float height);
            Strike(me, best, basePos + Vector3.up * height * 0.5f);
        }

        /// <summary>
        /// TaleSpire's own attack (the radial menu's Attacks), sent through TaleSpire: everyone sees it, with or without
        /// the mod (a player's attack goes to the Game Master's TaleSpire first, as from the radial menu). Only when the
        /// creature you are may act now (Roles.CanAct: its turn in turn-based mode, no cutscene).
        /// </summary>
        private static void Attack(CreatureBoardAsset me, CreatureBoardAsset victim, string kind, CreatureBoardAsset.AttackEmotes fallback)
        {
            if (!System.Enum.TryParse(kind, true, out CreatureBoardAsset.AttackEmotes emote)) emote = fallback;
            if (!Roles.CanAct(me))
            {
                TurnBanner.Note(Roles.Cutscene && !Roles.IsGm ? "A cutscene is playing" : $"Not {me.Name}'s turn");
                Plugin.Log.LogInfo($"First person: '{me.Name}' can't attack now (not its turn, or a cutscene)");
                return;
            }
            try
            {
                _hitsSent[me] = _strikeAt = Time.unscaledTime;
                CreatureManager.AttackCreature(emote, me.CreatureId, me.transform.position, victim.CreatureId, victim.transform.position);
                Plugin.Log.LogInfo($"First person: '{me.Name}' attacks '{victim.Name}' ({emote})");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("First person: attack failed: " + e.Message); }
        }

        // ---- TaleSpire's Hit, without its run-up -----------------------------------------------------------------

        private static readonly FieldInfo AttackPlayableField = AccessTools.Field(typeof(CreatureBoardAsset), "_attackPlayable");
        /// <summary>Our own attacks sent, by attacker, waiting for TaleSpire to play them (when sent).</summary>
        private static readonly Dictionary<CreatureBoardAsset, float> _hitsSent = new Dictionary<CreatureBoardAsset, float>();
        private static float _strikeAt = -1f;
        /// <summary>Debug ("wpn skip 0|1"): TaleSpire's Hit plays in full (0), to compare.</summary>
        public static bool DebugNoSkip;

        /// <summary>
        /// TaleSpire starts playing an attack (Harmony, CreatureBoardAsset.Attack): when it is one of our own sword Hits,
        /// its timeline jumps to just before the impact. TaleSpire's Hit is the attacker's whole run-up animation (your
        /// own creature, hidden in first person) and the target only reacts at its end. Only on this computer: other
        /// players see TaleSpire's usual Hit.
        /// </summary>
        internal static void AttackPlayed(CreatureBoardAsset attacker, CreatureBoardAsset.AttackEmotes kind)
        {
            if (attacker == null || !_hitsSent.TryGetValue(attacker, out float sent)) return;
            _hitsSent.Remove(attacker);
            float now = Time.unscaledTime;
            if (now - sent > 3f) return;
            string skipped = DebugNoSkip ? ", not skipped" : ", " + SkipToImpact(attacker, kind);
            Plugin.Log.LogInfo($"[wpn] {kind} plays {(now - sent) * 1000f:F0} ms after the strike{skipped}");
        }

        /// <summary>
        /// Hit: to just before its impact (and TaleSpire's swing and thud stay quiet - your weapon made its own sound).
        /// Lasers (guns): to just before the laser leaves - TaleSpire's laser timelines wait 0.43 s before launching it
        /// (their ControlTransitionTrack, which spawns the laser), and then it flies for 1/12 s.
        /// </summary>
        private static string SkipToImpact(CreatureBoardAsset attacker, CreatureBoardAsset.AttackEmotes kind)
        {
            bool hit = kind == CreatureBoardAsset.AttackEmotes.Hit;
            // (As TaleSpire's CreatureBoardAsset.Attack: a Hit plays the creature's own attack, anything else the timeline of that name.)
            var prefab = hit ? AttackPlayableField?.GetValue(attacker) as ActionTimeline : null;
            var source = hit ? (prefab != null ? ActionTimelineDatabase.GetActionPrefab(prefab.name) ?? prefab : null) : ActionTimelineDatabase.GetActionPrefab(kind.ToString());
            var tl = source != null ? Timeline(attacker, source) : null;
            var dir = tl != null ? tl.Director : null;
            var ta = dir != null ? dir.playableAsset as UnityEngine.Timeline.TimelineAsset : null;
            if (ta == null) return "no timeline";
            double impact = double.MaxValue;
            foreach (var track in ta.GetOutputTracks())
                foreach (var clip in track.GetClips())
                    if ((hit ? clip.asset is TM_CharAttackEventClip : track is ControlTransitionTrack) && clip.start < impact) impact = clip.start;
            if (impact == double.MaxValue) return hit ? "no impact in it" : "no launch in it";
            double to = System.Math.Max(0.0, impact - 0.02);
            if (dir.time >= to) return "already there";
            dir.time = to;
            if (!hit) return $"from {to:F2} s, the laser leaves at {impact:F2} s";
            // Your sword already made its hit sound: TaleSpire's swing and thud in it stay quiet.
            _quietDirector = dir;
            _quietAttacker = attacker;
            _quietUntil = Time.unscaledTime + 2.5f;
            return $"from {to:F2} s, impact at {impact:F2} s";
        }

        private static UnityEngine.Playables.PlayableDirector _quietDirector;
        private static CreatureBoardAsset _quietAttacker;
        private static float _quietUntil;
        /// <summary>Our skipped Hit is landing (TaleSpire's AttackTargetCreature is running): its thud is not played.</summary>
        internal static bool QuietImpact { get; private set; }

        /// <summary>A sound of a timeline (Harmony, AudioEventClipBehaviour.ProcessFrame): TaleSpire's swing in our skipped Hit?</summary>
        internal static bool QuietSwing(UnityEngine.Playables.Playable playable)
        {
            if (_quietDirector == null || Time.unscaledTime > _quietUntil) return false;
            if (UnityEngine.Playables.PlayableExtensions.GetGraph(playable).GetResolver() as UnityEngine.Playables.PlayableDirector != _quietDirector) return false;
            _swingQuieted = true;
            return true;
        }

        private static bool _swingQuieted;

        /// <summary>An attack lands (Harmony, CreatureBoardAsset.AttackTargetCreature; ImpactDone after it).</summary>
        internal static void AttackImpact(CreatureBoardAsset attacker)
        {
            QuietImpact = attacker != null && attacker == _quietAttacker && Time.unscaledTime <= _quietUntil;
            if (_strikeAt < 0f || attacker != FirstPersonMode.Creature) return;
            Plugin.Log.LogInfo($"[wpn] it lands {(Time.unscaledTime - _strikeAt) * 1000f:F0} ms after the strike"
                + (QuietImpact ? $" (TaleSpire's thud quiet, its swing {(_swingQuieted ? "quiet" : "not found")})" : ""));
            _strikeAt = -1f;
            _swingQuieted = false;
        }

        internal static void ImpactDone() => QuietImpact = false;

        // ---- Flame -------------------------------------------------------------------------------------------------

        private bool _flameHeld, _wasHeld;
        private float _power, _rumble, _glow, _lightReach;
        private readonly FlameSound _flameSound = new FlameSound();
        private bool _lineAimed;
        private Vector3 _lineOrigin, _lineDir = Vector3.forward;
        /// <summary>
        /// A hand power's look and effect: the model it lays on the floor (from a TaleSpire status effect), the status it
        /// gives creatures in its strip, its light. Flame: the Burning status's fire; Ice: the Frozen status's ice.
        /// </summary>
        private sealed class PowerStyle
        {
            public string Name;            // (logs)
            public string Status;          // TaleSpire status for creatures in the strip (on this computer only)
            public string Piece;           // the model in that status's prefab laid on the floor
            public float Full;             // that model's _Transition when fully there (it dissolves in from 0)
            public float Width;            // its width at scale 1 (board units)
            public Quaternion Upright;     // turns it upright
            public Color Light;
            public bool Ice;
        }
        private static readonly PowerStyle FlameStyle = new PowerStyle
        {
            Name = "flame", Status = "TLA_Burning", Piece = "vfx_fireBunch", Full = 0.5f, Width = 0.25f,
            Upright = Quaternion.Euler(270f, 0f, 0f), Light = new Color(1f, 0.58f, 0.15f),
        };
        private static readonly PowerStyle IceStyle = new PowerStyle
        {
            Name = "ice", Status = "TLA_Frozen", Piece = "mesh_iceBase", Full = 1f, Width = 0.9f,
            Upright = Quaternion.identity, Light = new Color(0.55f, 0.8f, 1f), Ice = true,
        };
        /// <summary>The power last used (set as the trigger is pressed; the strip and its glow keep it while they die down).</summary>
        private PowerStyle _style = FlameStyle;
        private readonly FlameSound _iceSound = new FlameSound(true);

        /// <summary>The fire / ice pieces on the floor, each staying where it appeared.</summary>
        private readonly List<Flame> _line = new List<Flame>();
        private readonly Light[] _lineLights = new Light[3];
        private const int MaxLineFlames = 64;
        private const float LineLength = 7f, LineWidth = 0.5f, LineGrowSeconds = 2f;
        /// <summary>No new fire piece within this of another (board units): the line is about two pieces per square.</summary>
        private const float FlameSpacing = 0.32f;

        /// <summary>
        /// The burning area is a strip from below your hand the way your palm faces, 7 x power long and half a square
        /// wide. Fire pieces never move: where the strip covers floor without fire, a new piece lights and grows; a piece
        /// the strip has left dies down where it is (sweep the palm and the fire spreads beside the old, which goes out).
        /// </summary>
        private void UpdateFlame(float dt)
        {
            var rig = VRManager.Instance != null ? VRManager.Instance.Rig : null;
            bool held = _flameHeld && FirstPersonMode.Active && rig != null;
            if (held && !_wasHeld)
            {
                // A new press: the strip grows again from your hand, in the power held now.
                _power = 0f;
                _lineAimed = false;
                _style = _held != null && _held.Id == "ice" ? IceStyle : FlameStyle;
            }
            _wasHeld = held;
            if (held)
            {
                _power = Mathf.Min(1f, _power + dt / LineGrowSeconds);
                AimLine(dt);
                _rumble -= dt;
                if (_rumble <= 0f) { _rumble = 0.1f; SteamVRInput.Haptic(Hand.Right, 0.03f, 70f, 0.15f + 0.25f * _power); }
            }
            UpdateSigil(held, rig, dt);
            Vector3 hand = FPHands.TryFrame(Hand.Right, out Vector3 hw, out Vector3 hf, out _, out float hl) ? hw + hf.normalized * hl * 0.6f
                : rig != null && rig.Head != null ? rig.Head.position : Vector3.zero;
            _flameSound.Update(held && !_style.Ice, _power, hand, dt);
            _iceSound.Update(held && _style.Ice, _power, hand, dt);
            if (!held && _line.Count == 0 && _glow <= 0f) { RemoveLights(); return; }

            float lit = held && _lineAimed ? LineLength * _power : 0f;
            if (lit > 0f) LightAlongLine(lit);
            for (int i = _line.Count - 1; i >= 0; i--)
            {
                var f = _line[i];
                if (f.Go == null) { _line.RemoveAt(i); continue; }
                bool inside = lit > 0f && f.Style == _style && InStrip(f.T.position, lit, 0.1f);
                float full = f.Style.Full;
                float target = inside ? full : 0f;
                // Up in about a quarter of a second; down faster once the strip has left it (or you let go).
                f.Transition = Mathf.MoveTowards(f.Transition, target, dt * full * (target > f.Transition ? 4f : 6f));
                f.T.localScale = Vector3.one * f.Size * (0.4f + 0.6f * f.Transition / full);
                SetTransition(f, f.Transition);
                if (!inside && f.Transition <= 0f)
                {
                    Object.Destroy(f.Go);
                    _line.RemoveAt(i);
                }
            }

            // A few lights along the burning strip (they follow it; they are not seen themselves).
            _glow = Mathf.MoveTowards(_glow, lit > 0f ? Mathf.Clamp01(_power * 3f) : 0f, dt / (lit > 0f ? 0.2f : 0.3f));
            if (lit > 0f) _lightReach = lit;
            if (_glow > 0f)
            {
                EnsureLights();
                for (int k = 0; k < _lineLights.Length; k++)
                {
                    var l = _lineLights[k];
                    l.transform.position = _lineOrigin + _lineDir * (_lightReach * (k + 0.5f) / _lineLights.Length) + Vector3.up * 0.35f;
                    l.intensity = (_style.Ice ? 1.4f : 1.9f) * _glow;
                    l.color = _style.Light;
                }
            }
            else RemoveLights();
            if (lit > 0.05f) CatchInLine(lit);
        }

        /// <summary>Is this point in the burning strip (with a margin, board units)?</summary>
        private bool InStrip(Vector3 p, float length, float margin)
        {
            Vector3 d = p - _lineOrigin;
            d.y = 0f;
            float along = Vector3.Dot(d, _lineDir);
            if (along < -margin || along > length + margin) return false;
            return (d - _lineDir * along).magnitude <= LineWidth * 0.5f + margin;
        }

        /// <summary>New fire pieces along the strip wherever there is no piece close by (each placed for good).</summary>
        private void LightAlongLine(float lit)
        {
            Vector3 perp = Vector3.Cross(Vector3.up, _lineDir).normalized;
            for (float along = 0.12f; along <= lit; along += FlameSpacing * 0.7f)
            {
                if (_line.Count >= MaxLineFlames) return;
                Vector3 p = _lineOrigin + _lineDir * along;
                bool covered = false;
                foreach (var other in _line)
                {
                    Vector3 d = other.T.position - p;
                    d.y = 0f;
                    if (d.sqrMagnitude < FlameSpacing * FlameSpacing) { covered = true; break; }
                }
                if (covered) continue;
                var f = NewPiece(_style, false);
                if (f == null) return;
                p += perp * Random.Range(-0.08f, 0.08f) + _lineDir * Random.Range(-0.05f, 0.05f);
                if (FirstPersonMode.Instance != null && FirstPersonMode.Instance.FloorAt(p + Vector3.up * 1f, 2.5f, out float y)) p.y = y;
                f.T.position = p;
                f.Spin = Random.Range(0f, 360f);
                f.T.rotation = Quaternion.AngleAxis(f.Spin, Vector3.up) * _style.Upright;
                // Mixed sizes, about 0.4 - 0.65 squares across (ice crystals a little smaller than the flames).
                f.Size = (1.5f + Random.Range(0f, 1.1f)) * (along < 0.5f ? 0.7f : 1f) * FireWidth / _style.Width * (_style.Ice ? 0.8f : 1f);
                f.Transition = 0f;
                f.T.localScale = Vector3.one * f.Size * 0.4f;
                _line.Add(f);
            }
        }

        private void EnsureLights()
        {
            for (int k = 0; k < _lineLights.Length; k++)
            {
                if (_lineLights[k] != null) continue;
                var go = new GameObject("TaleSpireVR_FlameLight" + k);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = _style.Light;
                l.range = 2.6f;
                l.shadows = LightShadows.None;
                l.intensity = 0f;
                _lineLights[k] = l;
            }
        }

        private void RemoveLights()
        {
            for (int k = 0; k < _lineLights.Length; k++)
            {
                if (_lineLights[k] != null) Object.Destroy(_lineLights[k].gameObject);
                _lineLights[k] = null;
            }
        }

        // ---- The flame's sigil (FX Magic Plane II's fiery circles) ------------------------------------------------

        private GameObject _sigil;
        private Transform _sigilDisc, _sigilRing;
        private Material _sigilDiscMat, _sigilRingMat;
        private Material _sigilDiscBody, _sigilRingBody;   // under the glow: its colours, solid over bright backgrounds
        private Light _sigilLight;
        private bool _sigilTried;
        private float _sigilAmount, _sigilFade, _sigilSpin;
        private Texture2D _discFire, _ringFire, _discIce, _ringIce;
        private bool _sigilIce;

        /// <summary>
        /// A bright sigil in front of your right palm while the power is on - fiery for the flame, icy blue for ice -
        /// facing the way the palm does and turning slowly. It pops up at once already palm-sized and glowing, then grows
        /// (to about the hand's length) and moves a little out - most of that early in the power's ramp - and lights the
        /// hand its colour; it shrinks away quickly when you let go. It follows the palm exactly.
        /// </summary>
        private void UpdateSigil(bool held, VRRig rig, float dt)
        {
            if (held) _sigilAmount = _power;
            _sigilFade = Mathf.MoveTowards(_sigilFade, held ? 1f : 0f, dt / (held ? 0.12f : 0.15f));
            bool show = _sigilFade > 0.001f && rig != null && FirstPersonMode.Active && EnsureSigil();
            if (show && FPHands.TryFrame(Hand.Right, out Vector3 w, out Vector3 f, out Vector3 s, out float len))
            {
                f.Normalize();
                Vector3 palm = -Vector3.Cross(f, (s - f * Vector3.Dot(s, f)).normalized);
                float rs = rig.Scale;
                float a = 1f - (1f - _sigilAmount) * (1f - _sigilAmount);   // fast at first, settling at full strength
                float size = Mathf.Lerp(0.075f, 0.16f, a) * _sigilFade * rs;
                float off = (0.018f + Mathf.Lerp(0.02f, 0.06f, a)) * rs;   // (the palm's skin is about 1.8 cm below the knuckle line)
                _sigil.transform.SetPositionAndRotation(w + f * len * 0.55f + palm * off, Quaternion.LookRotation(palm, f));
                _sigilSpin += dt * 25f;
                _sigilDisc.localRotation = Quaternion.AngleAxis(_sigilSpin, Vector3.forward);
                _sigilDisc.localScale = Vector3.one * size;
                _sigilRing.localRotation = Quaternion.AngleAxis(-_sigilSpin * 1.6f, Vector3.forward);
                _sigilRing.localScale = Vector3.one * size * 1.3f;
                bool ice = _style.Ice;
                if (ice != _sigilIce)
                {
                    _sigilIce = ice;
                    _sigilDiscMat.mainTexture = ice ? _discIce : _discFire;
                    _sigilRingMat.mainTexture = ice ? _ringIce : _ringFire;
                    if (_sigilDiscBody != null) _sigilDiscBody.mainTexture = _sigilDiscMat.mainTexture;
                    if (_sigilRingBody != null) _sigilRingBody.mainTexture = _sigilRingMat.mainTexture;
                    _sigilLight.color = ice ? new Color(0.55f, 0.8f, 1f) : new Color(1f, 0.55f, 0.15f);
                }
                float glow = Mathf.Lerp(1.15f, 1.6f, a) * (0.5f + 0.5f * _sigilFade);
                SetSigilColor(_sigilDiscMat, glow, ice);
                SetSigilColor(_sigilRingMat, glow, ice);
                SetBodyOpacity(_sigilDiscBody, Mathf.Lerp(0.75f, 0.95f, a) * _sigilFade);
                SetBodyOpacity(_sigilRingBody, Mathf.Lerp(0.75f, 0.95f, a) * _sigilFade);
                _sigilLight.range = 0.5f * rs;
                _sigilLight.intensity = Mathf.Lerp(0.4f, 0.9f, a) * _sigilFade;
            }
            else show = false;
            if (_sigil != null && _sigil.activeSelf != show) _sigil.SetActive(show);
        }

        private static void SetSigilColor(Material m, float glow, bool ice)
        {
            var c = ice ? new Color(0.85f * glow, 1.15f * glow, 1.6f * glow, 1f) : new Color(1.6f * glow, 1.25f * glow, 1f * glow, 1f);
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", c * 0.5f);   // (the particle shaders double it)
            if (m.HasProperty("_Color")) m.color = c;
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c);
        }

        private static void SetBodyOpacity(Material m, float opacity)
        {
            if (m != null) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f * opacity));   // (doubled by the shader)
        }

        private bool EnsureSigil()
        {
            if (_sigil != null) return true;
            if (_sigilTried) return false;
            _sigilTried = true;
            try
            {
                var disc = _discFire = LoadGlowTexture("sigil_disc.png", false);
                var ring = _ringFire = LoadGlowTexture("sigil_ring.png", false);
                _discIce = LoadGlowTexture("sigil_disc.png", true);
                _ringIce = LoadGlowTexture("sigil_ring.png", true);
                if (disc == null || ring == null || _discIce == null || _ringIce == null) return false;
                _sigil = new GameObject("TaleSpireVR_FlameSigil");
                Object.DontDestroyOnLoad(_sigil);
                _sigilDiscMat = GlowMaterial(disc, 3100);
                _sigilRingMat = GlowMaterial(ring, 3101);
                _sigilDisc = Quad("Disc", _sigil.transform, _sigilDiscMat);
                _sigilRing = Quad("Ring", _sigil.transform, _sigilRingMat);
                // Additive glow alone fades out over a bright sky or floor: its colours drawn first, solid, beneath it.
                _sigilDiscBody = BodyMaterial(disc, 3098);
                _sigilRingBody = BodyMaterial(ring, 3099);
                if (_sigilDiscBody != null)
                {
                    Quad("Body", _sigilDisc, _sigilDiscBody);
                    Quad("Body", _sigilRing, _sigilRingBody);
                }
                if (_sigilDiscMat.shader.name == "Standard")
                {
                    // (Standard draws front faces only: a turned-round copy for the far side.)
                    Quad("Back", _sigilDisc, _sigilDiscMat).localRotation = Quaternion.Euler(0f, 180f, 0f);
                    Quad("Back", _sigilRing, _sigilRingMat).localRotation = Quaternion.Euler(0f, 180f, 0f);
                }
                // Its fire lights the hand (and whatever is close).
                _sigilLight = new GameObject("Light").AddComponent<Light>();
                _sigilLight.transform.SetParent(_sigil.transform, false);
                _sigilLight.type = LightType.Point;
                _sigilLight.color = new Color(1f, 0.55f, 0.15f);
                _sigilLight.shadows = LightShadows.None;
                _sigil.SetActive(false);
                return true;
            }
            catch (System.Exception e) { Plugin.Log.LogError("Flame sigil failed to load: " + e); return false; }
        }

        private static Transform Quad(string name, Transform parent, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        /// <summary>
        /// The pack's circles are glowing colours on black, made for additive blending. Additive if TaleSpire has a
        /// particle shader for it; otherwise the black becomes transparent (alpha = brightest channel) for a fading one.
        /// ice: recoloured from fire to ice - its hottest (yellow-white) parts white, the rest a cold blue.
        /// </summary>
        private static Texture2D LoadGlowTexture(string file, bool ice)
        {
            string path = Path.Combine(Plugin.PluginDir, file);
            if (!File.Exists(path)) { Plugin.Log.LogWarning("Flame sigil: " + path + " not found"); return null; }
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "TaleSpireVR_" + (ice ? "ice_" : "") + file, wrapMode = TextureWrapMode.Clamp };
            if (!t.LoadImage(File.ReadAllBytes(path))) return null;
            var px = t.GetPixels32();
            var cold = new Color(0.32f, 0.62f, 1f);
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                byte m = (byte)Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                if (m == 0) { px[i] = new Color32(0, 0, 0, 0); continue; }
                if (ice)
                {
                    float white = (float)c.g / m;
                    Color k = Color.Lerp(cold, Color.white, white * white);
                    px[i] = new Color32((byte)(k.r * 255f), (byte)(k.g * 255f), (byte)(k.b * 255f), m);
                    continue;
                }
                px[i] = new Color32((byte)(c.r * 255 / m), (byte)(c.g * 255 / m), (byte)(c.b * 255 / m), m);
            }
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        /// <summary>The circle's own colours, alpha blended (null without TaleSpire's particle shaders).</summary>
        private static Material BodyMaterial(Texture2D tex, int queue)
        {
            foreach (var name in new[] { "Legacy Shaders/Particles/Alpha Blended", "Particles/Alpha Blended" })
            {
                var sh = Shader.Find(name);
                if (sh != null) return new Material(sh) { name = "TaleSpireVR_FlameSigilBody", mainTexture = tex, renderQueue = queue };
            }
            return null;
        }

        private static Material GlowMaterial(Texture2D tex, int queue)
        {
            foreach (var name in new[] { "Legacy Shaders/Particles/Additive", "Particles/Additive", "Mobile/Particles/Additive" })
            {
                var sh = Shader.Find(name);
                if (sh == null) continue;
                Plugin.Log.LogInfo("Flame sigil: shader '" + name + "'");
                return new Material(sh) { name = "TaleSpireVR_FlameSigil", mainTexture = tex, renderQueue = queue };
            }
            // Standard, fading: black albedo, the circle as emission, its alpha as opacity.
            var m = new Material(Shader.Find("Standard")) { name = "TaleSpireVR_FlameSigil", mainTexture = tex, color = new Color(0f, 0f, 0f, 1f) };
            m.SetFloat("_Mode", 2f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.EnableKeyword("_EMISSION");
            m.SetTexture("_EmissionMap", tex);
            m.SetFloat("_Glossiness", 0f);
            m.SetFloat("_Metallic", 0f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.renderQueue = queue;
            Plugin.Log.LogInfo("Flame sigil: shader 'Standard' (fade)");
            return m;
        }

        /// <summary>The line starts on the floor just ahead of your right hand and runs the way its palm faces.</summary>
        private void AimLine(float dt)
        {
            if (!FPHands.TryFrame(Hand.Right, out Vector3 w, out Vector3 f, out Vector3 s, out float len)) return;
            Vector3 palm = -Vector3.Cross(f, s).normalized;   // (the cross product points out of the back of a right hand)
            Vector3 dir = new Vector3(palm.x, 0f, palm.z);
            if (dir.magnitude < 0.25f) dir = new Vector3(f.x, 0f, f.z);   // palm flat down: along the fingers
            if (dir.sqrMagnitude < 1e-6f) return;
            dir.Normalize();
            _lineDir = !_lineAimed ? dir : Vector3.Slerp(_lineDir, dir, 1f - Mathf.Exp(-dt / 0.08f)).normalized;
            _lineAimed = true;
            var me = FirstPersonMode.Creature;
            Vector3 hand = w + f * len * 0.5f;
            float floor = me != null ? me.transform.position.y : hand.y;
            if (FirstPersonMode.Instance != null && FirstPersonMode.Instance.FloorAt(new Vector3(hand.x, floor + 1f, hand.z), 2.5f, out float y)) floor = y;
            _lineOrigin = new Vector3(hand.x, floor, hand.z) + _lineDir * 0.1f;
        }

        private void ClearLine()
        {
            foreach (var f in _line) if (f.Go != null) Object.Destroy(f.Go);
            _line.Clear();
            RemoveLights();
            _glow = 0f;
            _power = 0f;
            _sigilAmount = _sigilFade = 0f;
            if (_sigil != null) _sigil.SetActive(false);
        }

        /// <summary>Creatures standing in the burning strip catch fire.</summary>
        private void CatchInLine(float reach)
        {
            var me = FirstPersonMode.Creature;
            Vector3 dir = _lineDir;
            foreach (var c in Creatures())
            {
                if (c == null || c == me || !c.isActiveAndEnabled) continue;
                Cylinder(c, out Vector3 basePos, out float radius, out float height);
                Vector3 d = basePos - _lineOrigin;
                if (Mathf.Abs(d.y) > 1.5f) continue;
                d.y = 0f;
                float along = Vector3.Dot(d, dir);
                if (along < -radius || along > reach + radius) continue;
                float side = (d - dir * along).magnitude;
                if (side > LineWidth * 0.5f + radius) continue;
                Afflict(c, _style);
            }
        }

        // ---- Burning / frozen (TaleSpire's Burning or Frozen status look, on this computer only) ------------------------

        private static readonly MethodInfo GetTimeline = AccessTools.Method(typeof(EmotingMovableBoardAsset), "GetActionTimeline");
        private static readonly FieldInfo DataField = AccessTools.Field(typeof(CreatureBoardAsset), "_mostRecentCreatureData");
        /// <summary>Creatures we gave a status (by creature and status), until when.</summary>
        private readonly Dictionary<(CreatureBoardAsset c, ActionTimeline status), float> _burning = new Dictionary<(CreatureBoardAsset, ActionTimeline), float>();
        private readonly List<(CreatureBoardAsset c, ActionTimeline status)> _expired = new List<(CreatureBoardAsset, ActionTimeline)>();
        private const float BurnSeconds = 5f;

        /// <summary>A creature in the strip burns / freezes for 5 s from its last time in it (not if it has that status on the board).</summary>
        private void Afflict(CreatureBoardAsset c, PowerStyle style)
        {
            var prefab = StatusPrefab(style.Status);
            if (prefab == null) return;
            var key = (c, prefab);
            if (!_burning.ContainsKey(key))
            {
                if (HasStatus(c, prefab)) return;   // already burning / frozen on the board: leave it be
                var tl = Timeline(c, prefab);
                if (tl == null) return;
                tl.JumpToState(0);
                Plugin.Log.LogInfo($"First person: '{c.Name}' {(style.Ice ? "freezes" : "catches fire")} (this computer only, {BurnSeconds:F0} s)");
            }
            _burning[key] = Time.unscaledTime + BurnSeconds;
        }

        private void UpdateBurning()
        {
            if (_burning.Count == 0) return;
            float now = Time.unscaledTime;
            _expired.Clear();
            foreach (var kv in _burning) if (kv.Key.c == null || now >= kv.Value) _expired.Add(kv.Key);
            foreach (var k in _expired)
            {
                _burning.Remove(k);
                StopStatus(k.c, k.status);
            }
        }

        private static void StopStatus(CreatureBoardAsset c, ActionTimeline prefab)
        {
            if (c == null || prefab == null) return;
            try
            {
                if (HasStatus(c, prefab)) return;   // set on the board meanwhile: it stays
                Timeline(c, prefab)?.JumpToState(2);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("First person: ending a status failed: " + e.Message); }
        }

        /// <summary>Put out every fire (and thaw every frozen creature) of ours at once (leaving first person, leaving the board).</summary>
        public void StopAll()
        {
            foreach (var k in _burning.Keys) StopStatus(k.c, k.status);
            _burning.Clear();
            ClearLine();
            _flameSound.Stop();
            _iceSound.Stop();
            foreach (var f in _blasts) if (f.Go != null) Object.Destroy(f.Go);
            _blasts.Clear();
            _struck.Clear();
            _struckAt.Clear();
            _armed = _flameHeld = false;
        }

        private static ActionTimeline Timeline(CreatureBoardAsset c, ActionTimeline prefab)
        {
            try { return GetTimeline?.Invoke(c, new object[] { prefab }) as ActionTimeline; }
            catch { return null; }
        }

        private static bool HasStatus(CreatureBoardAsset c, ActionTimeline prefab)
        {
            try
            {
                var data = (CreatureDataV4)DataField.GetValue(c);
                return data.ActiveEmoteIds.Contains(prefab.ActionTimelineId);
            }
            catch { return false; }
        }

        private static readonly Dictionary<string, ActionTimeline> _statusPrefabs = new Dictionary<string, ActionTimeline>();
        private static ActionTimeline StatusPrefab(string name)
        {
            if (!_statusPrefabs.TryGetValue(name, out var prefab) || prefab == null)
            {
                try { prefab = ActionTimelineDatabase.GetActionPrefab(name); } catch { prefab = null; }
                if (prefab != null) _statusPrefabs[name] = prefab;
            }
            return prefab;
        }

        private static ActionTimeline BurningPrefab() => StatusPrefab(FlameStyle.Status);

        // ---- Fire and ice pieces (the flames of TaleSpire's Burning status, the ice of its Frozen one) ------------------

        private sealed class Flame
        {
            public GameObject Go;
            public Transform T;
            public Renderer R;
            public Light[] Lights;
            public PowerStyle Style;
            public float Transition, Size, Spin;
            // A blast puff:
            public Vector3 Base, Dir;
            public float Age, Delay, Speed;
        }

        private static readonly Dictionary<PowerStyle, GameObject> _pieceTemplates = new Dictionary<PowerStyle, GameObject>();
        private static readonly int TransitionId = Shader.PropertyToID("_Transition");
        private static readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        /// <summary>The fire model's width at scale 1 (board units).</summary>
        private const float FireWidth = 0.25f;
        /// <summary>The fire's _Transition when fully up (it dissolves in from 0 and looks whole from about 0.3).</summary>
        private const float FullFire = 0.5f;
        /// <summary>The fire model's flames rise along its +z: this turns them upright.</summary>
        private static Quaternion Upright => FlameStyle.Upright;
        private readonly List<Flame> _blasts = new List<Flame>();

        private static GameObject PieceTemplate(PowerStyle style)
        {
            if (_pieceTemplates.TryGetValue(style, out var t) && t != null) return t;
            var prefab = StatusPrefab(style.Status);
            if (prefab == null) return null;
            foreach (var tr in prefab.GetComponentsInChildren<Transform>(true))
                if (tr.name == style.Piece) { t = tr.gameObject; break; }
            if (t == null) Plugin.Log.LogWarning($"Weapons: the {style.Status} effect's '{style.Piece}' model was not found");
            else _pieceTemplates[style] = t;
            return t;
        }

        private static Flame NewFlame(bool lights) => NewPiece(FlameStyle, lights);

        /// <summary>
        /// A copy of a status effect's model - the Burning fire (with its flickering lights only if asked) or the Frozen
        /// ice - hidden until shown.
        /// </summary>
        private static Flame NewPiece(PowerStyle style, bool lights)
        {
            var template = PieceTemplate(style);
            if (template == null) return null;
            var go = Object.Instantiate(template);
            go.name = style.Ice ? "TaleSpireVR_Ice" : "TaleSpireVR_Fire";
            go.SetActive(true);
            var f = new Flame { Go = go, T = go.transform, R = go.GetComponent<Renderer>(), Style = style };
            if (f.R != null) f.R.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var box = go.transform.Find("LightBox");
            if (box != null)
            {
                if (lights)
                {
                    f.Lights = box.GetComponentsInChildren<Light>(true);
                    foreach (var l in f.Lights) l.shadows = LightShadows.None;
                }
                else Object.Destroy(box.gameObject);
            }
            SetTransition(f, 0f);
            return f;
        }

        private static void SetTransition(Flame f, float v)
        {
            if (f.R == null) return;
            f.R.GetPropertyBlock(_block);
            _block.SetFloat(TransitionId, v);
            f.R.SetPropertyBlock(_block);
            bool on = v > 0.001f;
            if (f.R.enabled != on) f.R.enabled = on;
        }

        /// <summary>Debug ("wpn slow f"): the shotgun's blast plays this much slower (to catch it in a snapshot).</summary>
        public static float DebugSlow = 1f;

        /// <summary>The shotgun's puffs: out of the barrel and away, growing and dying down within half a second.</summary>
        private void UpdateBlasts(float dt)
        {
            dt /= Mathf.Max(0.01f, DebugSlow);
            for (int i = _blasts.Count - 1; i >= 0; i--)
            {
                var f = _blasts[i];
                f.Age += dt;
                float a = f.Age - f.Delay;
                if (f.Go == null || a > 0.42f)
                {
                    if (f.Go != null) Object.Destroy(f.Go);
                    _blasts.RemoveAt(i);
                    continue;
                }
                float grow = Mathf.Clamp01(a / 0.06f);
                float fade = Mathf.Clamp01((a - 0.06f) / 0.34f);
                float tr = a < 0f ? 0f : FullFire * grow * (1f - fade * fade * (3f - 2f * fade));
                f.T.position = f.Base + f.Dir * Mathf.Max(0f, a) * f.Speed;
                f.T.rotation = Quaternion.LookRotation(f.Dir, Mathf.Abs(f.Dir.y) > 0.9f ? Vector3.forward : Vector3.up) * Quaternion.AngleAxis(f.Spin, Vector3.forward);
                f.T.localScale = Vector3.one * f.Size * (0.35f + 0.65f * grow + 0.3f * fade);
                SetTransition(f, tr);
                if (f.Lights != null) foreach (var l in f.Lights) l.intensity = 4f * (1f - Mathf.Clamp01(a / 0.15f));
            }
        }

        // ---- Shared ------------------------------------------------------------------------------------------------

        private static CreatureBoardAsset[] _creatures = new CreatureBoardAsset[0];
        private static float _creaturesAt = -10f;

        /// <summary>
        /// The creatures weapons and powers can reach: only those shown to you (not hidden by the Game Master - even when
        /// you are the Game Master - and visible by TaleSpire's line-of-sight / fog rules for players).
        /// </summary>
        private static CreatureBoardAsset[] Creatures()
        {
            if (Time.unscaledTime - _creaturesAt > 0.5f)
            {
                _creaturesAt = Time.unscaledTime;
                _creatures = System.Array.FindAll(Object.FindObjectsOfType<CreatureBoardAsset>(), Roles.CanSee);
            }
            return _creatures;
        }

        /// <summary>A creature as an upright cylinder on its base (board units).</summary>
        private static void Cylinder(CreatureBoardAsset c, out Vector3 basePos, out float radius, out float height)
        {
            basePos = c.transform.position;
            Bounds b;
            try { b = c.CreatureBounds; } catch { b = CreatureBoardAsset.DEFAULT_CREATURE_BOUNDS; }
            radius = Mathf.Max(0.15f, Mathf.Max(b.extents.x, b.extents.z) * 0.8f);
            height = Mathf.Max(0.3f, b.max.y);
        }

        private static readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();

        /// <summary>One of TaleSpire's sound clips by name (or the fallback's).</summary>
        private static AudioClip Clip(string name, string fallback)
        {
            if (_clips.TryGetValue(name, out var c) && c != null) return c;
            AudioClip alt = null;
            foreach (var a in Resources.FindObjectsOfTypeAll<AudioClip>())
            {
                if (a == null) continue;
                if (a.name == name) { c = a; break; }
                if (a.name == fallback) alt = a;
            }
            c = c != null ? c : alt;
            if (c != null) _clips[name] = c;
            return c;
        }

        // ---- The models ----------------------------------------------------------------------------------------------

        /// <summary>Where each model starts in weapons.bin (read once); a model itself is read the first time it is held.</summary>
        private static Dictionary<string, long> _offsets;
        private static bool _iconsInFile;

        private void EnsureLoaded()
        {
            if (_modelsTried) return;
            _modelsTried = true;
            _models = new Dictionary<string, Model>();
            try { _offsets = Index(Path.Combine(Plugin.PluginDir, "weapons.bin")); }
            catch (System.Exception e) { Plugin.Log.LogError("Weapons failed to load: " + e); }
        }

        /// <summary>weapons.bin's table of contents: each model's id and where it starts (skipping its data).</summary>
        private static Dictionary<string, long> Index(string path)
        {
            if (!File.Exists(path)) { Plugin.Log.LogWarning("Weapons: " + path + " not found"); return null; }
            var at = new Dictionary<string, long>();
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                string format = r.ReadString();
                if (format != "TSVRWPN1" && format != "TSVRWPN2") { Plugin.Log.LogWarning("Weapons: unknown file format"); return null; }
                _iconsInFile = format == "TSVRWPN2";
                int count = r.ReadInt32();
                for (int w = 0; w < count; w++)
                {
                    long start = r.BaseStream.Position;
                    string id = r.ReadString();
                    at[id] = start;
                    SkipModel(r);
                }
            }
            Plugin.Log.LogInfo($"Weapons: {at.Count} models in weapons.bin (each read when first held)");
            return at;
        }

        /// <summary>Past one model's data (after its id), and its picture.</summary>
        private static void SkipModel(BinaryReader r)
        {
            int n = r.ReadInt32();
            r.BaseStream.Seek((long)n * (12 + 12 + 16 + 8), SeekOrigin.Current);   // vertices, normals, tangents, uvs
            int subs = r.ReadInt32();
            for (int s = 0; s < subs; s++)
            {
                r.BaseStream.Seek((long)r.ReadInt32() * 4 + 16 + 16 + 12, SeekOrigin.Current);   // triangles, two colours, three floats
                for (int t = 0; t < 5; t++) r.BaseStream.Seek(r.ReadInt32(), SeekOrigin.Current);   // textures
            }
            if (_iconsInFile) r.BaseStream.Seek(r.ReadInt32(), SeekOrigin.Current);
        }

        private static Dictionary<string, Texture2D> _icons;

        /// <summary>
        /// A weapon model's picture for the Weapon Catalogue (null: none). Read on its own from weapons.bin, skipping the
        /// models (their textures take a while to decode; they load when a weapon is first held).
        /// </summary>
        public static Texture2D IconOf(string mesh)
        {
            if (string.IsNullOrEmpty(mesh)) return null;
            if (_icons == null)
            {
                _icons = new Dictionary<string, Texture2D>();
                try { ReadIcons(Path.Combine(Plugin.PluginDir, "weapons.bin"), _icons); }
                catch (System.Exception e) { Plugin.Log.LogWarning("Weapon icons failed to load: " + e.Message); }
            }
            return _icons.TryGetValue(mesh, out var t) ? t : null;
        }

        private static void ReadIcons(string path, Dictionary<string, Texture2D> icons)
        {
            if (!File.Exists(path)) return;
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                if (r.ReadString() != "TSVRWPN2") return;   // (older files have none)
                int count = r.ReadInt32();
                for (int w = 0; w < count; w++)
                {
                    string id = r.ReadString();
                    int n = r.ReadInt32();
                    r.BaseStream.Seek((long)n * (12 + 12 + 16 + 8), SeekOrigin.Current);   // vertices, normals, tangents, uvs
                    int subs = r.ReadInt32();
                    for (int s = 0; s < subs; s++)
                    {
                        r.BaseStream.Seek((long)r.ReadInt32() * 4 + 16 + 16 + 12, SeekOrigin.Current);   // triangles, two colours, three floats
                        for (int t = 0; t < 5; t++) r.BaseStream.Seek(r.ReadInt32(), SeekOrigin.Current);   // textures
                    }
                    var png = r.ReadBytes(r.ReadInt32());   // (see SkipModel)
                    var icon = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "TaleSpireVR_" + id + "_icon", wrapMode = TextureWrapMode.Clamp };
                    if (png.Length > 0 && icon.LoadImage(png)) icons[id] = icon;
                }
            }
        }

        private bool EnsureModel(string id)
        {
            EnsureLoaded();
            if (_models == null) return false;
            if (!_models.TryGetValue(id, out var m))
            {
                // Read now, the first time this one is held (decoding every model's textures at once would stall VR).
                if (_offsets == null || !_offsets.TryGetValue(id, out long at)) return false;
                try { m = LoadOne(Path.Combine(Plugin.PluginDir, "weapons.bin"), at); }
                catch (System.Exception e) { Plugin.Log.LogError($"Weapon model '{id}' failed to load: " + e); m = null; }
                _models[id] = m;
            }
            if (m == null) return false;
            if (_go == null)
            {
                _go = new GameObject("TaleSpireVR_Weapon");
                Object.DontDestroyOnLoad(_go);
                _mf = _go.AddComponent<MeshFilter>();
                _mr = _go.AddComponent<MeshRenderer>();
                _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _mr.receiveShadows = true;
                _go.SetActive(false);
            }
            if (_shown != id)
            {
                _mf.sharedMesh = m.Mesh;
                _mr.sharedMaterials = m.Materials;
                _shown = id;
            }
            return true;
        }

        /// <summary>One model, from where it starts in weapons.bin (Index).</summary>
        private static Model LoadOne(string path, long start)
        {
            var standard = Shader.Find("Standard");
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                r.BaseStream.Seek(start, SeekOrigin.Begin);
                {
                    string id = r.ReadString();
                    int n = r.ReadInt32();
                    var verts = new Vector3[n];
                    var normals = new Vector3[n];
                    var tangents = new Vector4[n];
                    var uvs = new Vector2[n];
                    for (int i = 0; i < n; i++) verts[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    for (int i = 0; i < n; i++) normals[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    for (int i = 0; i < n; i++) tangents[i] = new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    for (int i = 0; i < n; i++) uvs[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                    var mesh = new Mesh { name = "TaleSpireVR_" + id };
                    if (n > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    mesh.vertices = verts;
                    mesh.normals = normals;
                    mesh.tangents = tangents;
                    mesh.uv = uvs;
                    int subs = r.ReadInt32();
                    mesh.subMeshCount = subs;
                    var mats = new Material[subs];
                    for (int s = 0; s < subs; s++)
                    {
                        var tri = new int[r.ReadInt32()];
                        for (int i = 0; i < tri.Length; i++) tri[i] = r.ReadInt32();
                        mesh.SetTriangles(tri, s);
                        Color color = C(r), emission = C(r);
                        float gloss = r.ReadSingle(), glossScale = r.ReadSingle(), metallic = r.ReadSingle();
                        var main = Tex(r, false, id + "_albedo");
                        var bump = Tex(r, true, id + "_normal");
                        var metal = Tex(r, true, id + "_metallic");
                        var occl = Tex(r, true, id + "_occlusion");
                        var emis = Tex(r, false, id + "_emission");
                        var m = new Material(standard) { name = "TaleSpireVR_" + id, color = color };
                        if (main != null) m.mainTexture = main;
                        if (bump != null) { m.SetTexture("_BumpMap", bump); m.EnableKeyword("_NORMALMAP"); }
                        if (metal != null) { m.SetTexture("_MetallicGlossMap", metal); m.EnableKeyword("_METALLICGLOSSMAP"); m.SetFloat("_GlossMapScale", glossScale); }
                        else { m.SetFloat("_Metallic", metallic); m.SetFloat("_Glossiness", gloss); }
                        if (occl != null) m.SetTexture("_OcclusionMap", occl);
                        // A little of their own light (like the hands), so they don't vanish in dark dungeons.
                        m.EnableKeyword("_EMISSION");
                        m.SetTexture("_EmissionMap", emis != null ? emis : main);
                        m.SetColor("_EmissionColor", emis != null ? emission : new Color(0.12f, 0.12f, 0.12f));
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                        mats[s] = m;
                    }
                    mesh.RecalculateBounds();
                    Plugin.Log.LogInfo($"Weapon model '{id}': {n} vertices, {subs} parts, bounds {mesh.bounds.center:F3}/{mesh.bounds.size:F3}");
                    return new Model { Mesh = mesh, Materials = mats };
                }
            }
        }

        private static Color C(BinaryReader r) => new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        private static readonly Dictionary<string, Texture2D> _texCache = new Dictionary<string, Texture2D>();

        private static Texture2D Tex(BinaryReader r, bool linear, string name)
        {
            int len = r.ReadInt32();
            if (len <= 0) return null;
            var bytes = r.ReadBytes(len);
            // (The shotgun's parts share one texture.)
            string key = name + "/" + len;
            if (_texCache.TryGetValue(key, out var cached)) return cached;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear) { name = "TaleSpireVR_" + name, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            if (!t.LoadImage(bytes, true)) return null;
            _texCache[key] = t;
            return t;
        }

        // ---- Debug -------------------------------------------------------------------------------------------------

        /// <summary>
        /// Debug ("wpn tune shotgun|sword u|f|scale|px|py|pz|tilt value"): adjust where a weapon sits in the hand, live
        /// ("... lean degrees": every weapon's forward lean).
        /// </summary>
        public static void Tune(string mesh, string what, float[] v)
        {
            if (what == "lean" && v.Length > 0) { Lean = v[0]; Plugin.Log.LogInfo($"[wpn] lean {Lean:F0}"); return; }
            if (!Grips.TryGetValue(mesh, out var g) || v.Length == 0) return;
            switch (what)
            {
                case "u": g.KnuckleUp = v[0]; break;
                case "f": g.KnuckleFwd = v[0]; break;
                case "scale": g.ModelScale = v[0]; break;
                case "px": g.Point.x = v[0]; break;
                case "py": g.Point.y = v[0]; break;
                case "pz": g.Point.z = v[0]; break;
                case "tilt":   // the sword's handle angle across the palm (degrees from the knuckle line)
                {
                    float t = v[0] * Mathf.Deg2Rad;
                    g.Side = new Vector3(Mathf.Sin(t), -Mathf.Cos(t), 0f);
                    g.Fwd = new Vector3(Mathf.Cos(t), Mathf.Sin(t), 0f);
                    break;
                }
            }
            Plugin.Log.LogInfo($"[wpn] {mesh}: point {g.Point:F3} u={g.KnuckleUp:F3} f={g.KnuckleFwd:F3} scale={g.ModelScale:F2}");
        }

        /// <summary>Debug ("wpn firetest"): a row of the Burning fire at transitions 0.1 .. 1 in front of you for 8 s.</summary>
        public static void FireTest(bool ice = false)
        {
            var rig = VRManager.Instance.Rig;
            if (rig == null || rig.Head == null) return;
            var style = ice ? IceStyle : FlameStyle;
            Vector3 fwd = rig.Head.forward;
            fwd.y = 0f;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float s = rig.Scale;
            float[] tr = ice ? new[] { 0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f } : new[] { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f, 1f };
            for (int i = 0; i < tr.Length; i++)
            {
                var f = NewPiece(style, false);
                if (f == null) return;
                f.T.position = rig.Head.position + fwd * 1.2f * s + right * (i - 2.5f) * 0.25f * s - Vector3.up * 0.4f * s;
                f.T.rotation = style.Upright;
                f.T.localScale = Vector3.one * 0.2f / style.Width * s;
                SetTransition(f, tr[i]);
                Object.Destroy(f.Go, 8f);
            }
            Plugin.Log.LogInfo($"[wpn] {style.Name} test row placed (transitions {string.Join(", ", tr)})");
        }

        /// <summary>
        /// Debug ("wpn aimat name"): turn the debug right hand ("fp handpose") so the held weapon's pointer - or, for the
        /// flame, the fingers - point at that creature's middle.
        /// </summary>
        public static void AimAt(string name, bool level = false)
        {
            CreatureBoardAsset target = null;
            foreach (var c in Object.FindObjectsOfType<CreatureBoardAsset>()) if (c.Name == name) target = c;
            if (target == null || !FPHands.DebugTurn.HasValue) { Plugin.Log.LogInfo("[wpn] aimat: no such creature, or no 'fp handpose'"); return; }
            Cylinder(target, out Vector3 basePos, out float radius, out float height);
            Vector3 aim = basePos + Vector3.up * height * 0.5f;
            for (int i = 0; i < 6; i++)
            {
                Ray r;
                if (!TryTipRay(out r, out _))
                {
                    if (!FPHands.TryFrame(Hand.Right, out Vector3 w, out Vector3 f, out _, out _)) return;
                    r = new Ray(w, f);
                }
                Vector3 want = (aim - r.origin).normalized;
                float yawErr = Mathf.DeltaAngle(Mathf.Atan2(r.direction.x, r.direction.z) * Mathf.Rad2Deg, Mathf.Atan2(want.x, want.z) * Mathf.Rad2Deg);
                float pitchErr = (Mathf.Asin(Mathf.Clamp(want.y, -1f, 1f)) - Mathf.Asin(Mathf.Clamp(r.direction.y, -1f, 1f))) * Mathf.Rad2Deg;
                var t = FPHands.DebugTurn.Value;
                FPHands.DebugTurn = new Vector2(t.x + yawErr, t.y);
                FPHands.DebugPitch -= pitchErr;
            }
            if (level) FPHands.DebugPitch = 0f;   // (only turned towards it: a palm-down hand's flame line runs that way)
            Plugin.Log.LogInfo($"[wpn] aiming at '{name}' {aim:F2}: turn {FPHands.DebugTurn.Value:F1} pitch {FPHands.DebugPitch:F1}");
        }

        public string Status()
        {
            string ray = TryTipRay(out Ray r, out float hidden) ? $"{r.origin:F2}->{r.direction:F2} drawn from {hidden:F2}" : "-";
            return $"weapon={(_held != null ? _held.Id : "none")} armed={_armed} model={(_go != null && _go.activeSelf ? _shown : "-")} tipRay={ray} flame(held={_flameHeld} power={_power:F2} pieces={_line.Count} lit={_line.FindAll(x => x.Transition > 0.3f * x.Style.Full).Count} style={_style.Name} sigil={_sigilFade:F2}/{_sigilAmount:F2} from={_lineOrigin:F2} dir={_lineDir:F2}) blasts={_blasts.Count} burning={_burning.Count}";
        }
    }
}
