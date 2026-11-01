using System.Collections.Generic;
using System.IO;
using Bouncyrock.Physics;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// First-person mode: see the board through a creature's eyes, at its size, with your own hands.
    ///
    /// Entered from the creature's radial menu (RadialExtraButton) - only as a creature TaleSpire lets you control - and
    /// left from any radial menu while in it. The world scale is set so your real eye height is the creature's eye
    /// height (from its model's height). The creature is hidden on your screen but goes everywhere with you, and stays
    /// the selected creature, so its attacks and emotes start where you stand. Everyone else sees it walk with you
    /// (FirstPersonSync: TaleSpire's own live move), put down whenever you stop and when you leave, facing where you look;
    /// your table view comes back shifted by how far it moved. Not its turn: you can be it, but not move it.
    ///
    /// Walking is our own: a cylinder the size of the creature against tiles and props (never creatures), climbing
    /// ledges up to StepHeight with a short climb, falling with gravity, jumping, crouching, flying for flying
    /// creatures. Your real steps move the body too.
    /// </summary>
    [DefaultExecutionOrder(30035)]
    internal partial class FirstPersonMode : MonoBehaviour
    {
        public static FirstPersonMode Instance { get; private set; }
        /// <summary>In first person (from the dark moment of the blink in to the dark moment of the blink out).</summary>
        public static bool Active => Instance != null && Instance._active;
        /// <summary>A blink in or out is running (inputs that would start something are ignored).</summary>
        public static bool Transitioning => Instance != null && Instance._blink.Running;
        public static float BlinkClosure => Instance != null ? Instance._blink.Closure : 0f;

        /// <summary>A hand's grip is squeezed.</summary>
        public static bool GripHeld(Hand h) => SteamVRInput.Measure.Held[(int)h] || SteamVRInput.Lasso.Held[(int)h];

        /// <summary>
        /// Is this hand's laser drawn? In first person the lasers work exactly as always, but are only drawn while that
        /// hand's grip is squeezed, or while a radial menu or the ESC menu (B) is open (to aim at its options).
        /// </summary>
        public static bool PointerVisible(Hand h) => !Active || GripHeld(h) || RadialOpen || UIPanels.EscOnly;

        private static bool RadialOpen => (SingletonBehaviour<MapMenuManager>.HasInstance && MapMenuManager.IsOpen) || RadialExtraButton.OwnMenuOpen;

        /// <summary>The creature you are (null outside first person).</summary>
        public static CreatureBoardAsset Creature => Active ? Instance._creature : null;

        private const float Skin = 0.02f;
        private const float SmallStep = 0.25f;   // bumps you walk over without climbing (floor seams, rugs, thin tiles)
        private const float SnapDown = 0.6f;     // walk down steps up to this high; bigger drops are falls
        private const float MaxFall = 40f;

        private readonly FPBlink _blink = new FPBlink();
        private readonly FPRuler _ruler = new FPRuler();
        private readonly FPVignette _vignette = new FPVignette();
        private FPHands _hands;
        private bool _handsTried;
        private readonly FPWeapons _weapons = new FPWeapons();

        private bool _active;
        private CreatureBoardAsset _creature;
        private CreatureGuid _creatureId;
        private string _creatureName = "";
        private Vector3 _startPos;
        private Quaternion _startRot;
        private Vector3 _feet, _origin;
        private float _yaw, _scale, _height, _eye, _radius, _viewY, _viewVel;
        private bool _flying, _airborne, _crouched;
        private float _vy, _crouchT, _jumpWait, _landTimer, _dip, _turn, _flyInput;
        private bool _upReady = true, _downReady = true, _snapReady = true;
        private bool _homeOnExit, _toolbarsWereOpen;
        // A climb onto a ledge: up first, then over the edge, so your eyes never pass through the raised floor.
        private bool _vaulting;
        private Vector3 _vaultFrom, _vaultDir;
        private float _vaultRise, _vaultDist, _vaultT, _vaultDur, _vaultFwd;
        private CollisionFilter _filter;
        private bool _filterReady;
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private readonly List<BounceCollider> _disabledColliders = new List<BounceCollider>();
        private float _nextHideScan;
        private bool _fxErrorLogged;

        private void Awake() { Instance = this; }
        private void OnEnable() { Application.onBeforeRender += OnBeforeRender; }
        private void OnDisable() { Application.onBeforeRender -= OnBeforeRender; }
        private void OnBeforeRender() { _blink.Relevel(); }

        // ---- Entering and leaving ------------------------------------------------------------------------------

        public void Enter(CreatureBoardAsset creature, bool homeOnExit = false)
        {
            var rig = VRManager.Instance != null ? VRManager.Instance.Rig : null;
            if (_active || _blink.Running || creature == null || rig == null || !rig.OnBoard) return;
            // Only as a creature TaleSpire lets you control (a Game Master: any; a player: their own).
            if (!Roles.CanControl(creature)) { Plugin.Log.LogInfo($"First person: not entering as '{creature.Name}' (you don't control it)"); return; }
            Plugin.Log.LogInfo($"First person: entering as '{creature.Name}'");
            _homeOnExit = homeOnExit;
            _blink.Start(() => DoEnter(creature));
        }

        public void Exit(bool home = false)
        {
            if (!_active || _blink.Running) return;
            _homeOnExit |= home;
            Plugin.Log.LogInfo("First person: leaving");
            _blink.Start(DoExit);
        }

        private void DoEnter(CreatureBoardAsset c)
        {
            var mgr = VRManager.Instance;
            var rig = mgr.Rig;
            if (c == null || rig == null || !rig.OnBoard) return;
            EnsureHands();
            _hands?.SetColor(HandColors.ColorFor(c));   // a unique creature's own colour, else the config's
            _weapons.SetHeld(WeaponLoadouts.EquippedOf(c));
            EnsureFilter();
            RadialExtraButton.CloseTaleSpireMenu();
            RadialExtraButton.CloseOwn();
            if (BoardBuilder.InBuilderMode) VirtualInput.Tap(UnityEngine.InputSystem.Key.B);   // no Build Mode in first person
            if (mgr.Panels != null) mgr.Panels.HideMain();
            // No toolbars (dice, hotbar) while you are a creature; they come back when you leave.
            _toolbarsWereOpen = BoardToolManager.IsUIExpanded;
            CollapseToolbars();

            _creature = c;
            _creatureId = c.CreatureId;
            _creatureName = c.Name;
            _startPos = c.transform.position;
            _startRot = c.Rotator.localRotation;
            Bounds b = c.CreatureBounds;
            _height = Mathf.Max(0.25f, b.max.y);
            _eye = _height * Mathf.Clamp(VRConfig.FPEyeHeight.Value, 0.5f, 1f);
            _radius = VRConfig.FPBodyRadius.Value > 0f ? VRConfig.FPBodyRadius.Value : Mathf.Clamp(c.ScaledBaseRadius * 0.6f, 0.1f, 3f);
            _flying = c.Flying;
            // Your real eye height becomes the creature's (fixed while you are it, so real crouching still works).
            float hmdY = Mathf.Clamp(rig.HeadLocalPosition.y + DebugLift, 0.8f, 2.2f);
            _scale = _eye / hmdY;
            // Face the way the creature faces.
            Vector3 f = -c.Rotator.right;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            Vector3 hf = rig.HeadLocalRotation * Vector3.forward;
            _yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg - Mathf.Atan2(hf.x, hf.z) * Mathf.Rad2Deg;

            _feet = _startPos;
            if (!_flying && FloorBelow(_feet + Vector3.up * 0.5f, 1.5f, out float floor)) _feet.y = floor;
            _viewY = _feet.y;
            _viewVel = 0f;
            _airborne = _crouched = _vaulting = false;
            _vy = _crouchT = _jumpWait = _landTimer = _dip = _turn = _flyInput = 0f;
            _upReady = _downReady = _snapReady = true;
            _ruler.LineVisible = false;   // hidden until you click the left stick
            _ruler.Reset();

            Quaternion yawRot = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 hl = rig.HeadLocalPosition;
            _origin = _feet - yawRot * new Vector3(hl.x, 0f, hl.z) * _scale;
            _origin.y = _feet.y;
            rig.BeginFirstPerson();
            rig.SetFirstPersonPose(_origin, _yaw, _scale);

            HideCreature(true);
            DisableColliders();
            SelectCreature();
            BeginSync();
            SteamVRInput.WantSkeletons = true;
            if (StreamView.Instance != null) StreamView.Instance.SetFirstPerson(true);
            _active = true;
            Plugin.Log.LogInfo($"First person as '{_creatureName}' at {_feet}: model height {_height:F2} (bounds {b.min:F2}..{b.max:F2}), eye {_eye:F2}, scale {_scale:F3} units/m (real eye {hmdY:F2} m), body radius {_radius:F2} (base {c.ScaledBaseRadius:F2}), flying={_flying}, toolbars were {(_toolbarsWereOpen ? "open" : "closed")}");
        }

        private void DoExit()
        {
            var rig = VRManager.Instance.Rig;
            Vector3 pos = _feet;
            Vector3 look = rig != null && rig.Head != null ? rig.Head.forward : Vector3.forward;
            look.y = 0f;
            HideCreature(false);
            Vector3 shift = Vector3.zero;
            if (_creature != null)
            {
                Quaternion rot = RotationFacing(_creature.Rotator, look);
                bool moved = (pos - _placedPos).sqrMagnitude > 0.02f * 0.02f;
                bool turned = Quaternion.Angle(rot, _placedRot) > 1f;
                // Not allowed to move it now (not its turn, a cutscene, or a test): it stays where the board has it.
                bool may = !_homeOnExit && Roles.CanAct(_creature);
                if (!may || (!moved && !turned)) PutBack();
                else
                {
                    try
                    {
                        // A normal (networked) move to here; the drop turns its colliders back on.
                        CreaturePresenter.TeleportCreature(_creature, pos, rot);
                        _disabledColliders.Clear();
                        _placedPos = pos;
                        _placedRot = rot;
                        Plugin.Log.LogInfo($"First person: '{_creatureName}' dropped at {pos} facing {Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg:F0} deg (moved {(pos - _startPos).magnitude:F2})");
                    }
                    catch (System.Exception e) { Plugin.Log.LogError("First person: moving the creature failed: " + e); PutBack(); }
                }
                // Your table view comes back shifted by how far the creature went (in steps put down on the way too).
                shift = _placedPos - _startPos;
                shift.y = 0f;
            }
            DetachSync();
            Finish(rig, shift);
        }

        /// <summary>The creature back exactly where the board has it (where you entered, or where it was last put down).</summary>
        private void PutBack()
        {
            if (_creature == null) return;
            _creature.JumpTo(_placedPos);
            _creature.JumpRotation(_placedRot);
            _creature.CorrectPos = _placedPos;
            _creature.CorrectRotation = _placedRot;
            RestoreColliders();
        }

        /// <summary>The VR mod switches off (Player mode): leave at once, the creature back where it was, nothing of ours left showing.</summary>
        public void Suspend()
        {
            _blink.Cancel();
            if (_active) Abort("the VR mod switched off (Player mode)");
            _weapons.SetHeld(null);
            _weapons.StopAll();
            FPHands.RightHidden = false;
            if (_hands != null) _hands.Visible = false;
            _ruler.Hide();
            _vignette.Set(0f, null);
        }

        /// <summary>Leave at once (board changed, creature deleted): no blink, the creature stays where it was.</summary>
        private void Abort(string why)
        {
            Plugin.Log.LogInfo("First person ended: " + why);
            HideCreature(false);
            if (_creature != null) PutBack();
            DetachSync();
            Finish(VRManager.Instance != null ? VRManager.Instance.Rig : null, Vector3.zero);
        }

        private void Finish(VRRig rig, Vector3 shift)
        {
            _active = false;
            _homeOnExit = false;
            _vaulting = false;
            if (rig != null && rig.FirstPerson) rig.EndFirstPerson(shift);
            if (StreamView.Instance != null) StreamView.Instance.SetFirstPerson(false);
            SteamVRInput.WantSkeletons = false;
            RadialExtraButton.CloseOwn();
            if (_hands != null) _hands.Visible = false;
            _weapons.SetHeld(null);
            _weapons.StopAll();
            FPHands.RightHidden = false;
            _ruler.Hide();
            _vignette.Set(0f, null);
            _disabledColliders.Clear();
            if (_toolbarsWereOpen)
            {
                try { if (SingletonBehaviour<BoardToolManager>.HasInstance) SingletonBehaviour<BoardToolManager>.Instance.ExtendUI(); }
                catch { }
            }
            _creature = null;
        }

        /// <summary>
        /// The creature's rotator turned to face a direction, the way TaleSpire turns it (rotation about its own z,
        /// with the facing being -right).
        /// </summary>
        private static Quaternion RotationFacing(Transform rotator, Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-6f) return rotator.localRotation;
            dir.Normalize();
            Vector3 cur = -rotator.right;
            cur.y = 0f;
            float angle = Vector3.Angle(cur, dir);
            float side = Vector3.Dot(-rotator.up, dir);
            Quaternion q = rotator.localRotation * Quaternion.Euler(0f, 0f, side < 0f ? -angle : angle);
            Vector3 e = q.eulerAngles;
            e.x = 0f;
            e.y = 0f;
            return Quaternion.Euler(e);
        }

        /// <summary>
        /// Hide the creature's model, base and indicators, but not its effects (status effects, attacks and emotes:
        /// ActionTimelines under it), which stay visible around you (see CutOutEffects). forceRenderingOff is used (not
        /// enabled), so TaleSpire's own switching of renderers on and off can't make it flash back, and is left as
        /// TaleSpire wants it afterwards.
        /// </summary>
        private void HideCreature(bool hide)
        {
            if (hide)
            {
                _hidden.Clear();
                _fxRenderers.Clear();
                _fxSystems.Clear();
                if (_creature == null) return;
                ScanCreature();
                _nextHideScan = Time.unscaledTime + 0.5f;
            }
            else
            {
                foreach (var r in _hidden) if (r != null) r.forceRenderingOff = false;
                _hidden.Clear();
                foreach (var r in _fxCut) if (r != null) r.forceRenderingOff = false;
                RestoreParticles();
                _fxRenderers.Clear();
                _fxSystems.Clear();
            }
        }

        /// <summary>A morph that loads later, or a status effect switched on, brings new renderers.</summary>
        private void KeepHidden()
        {
            if (Time.unscaledTime < _nextHideScan || _creature == null) return;
            _nextHideScan = Time.unscaledTime + 0.5f;
            ScanCreature();
        }

        private void ScanCreature()
        {
            foreach (var r in _creature.GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetComponentInParent<ActionTimeline>(true) != null)
                {
                    if (!_fxRenderers.Contains(r)) _fxRenderers.Add(r);
                    continue;
                }
                if (!r.forceRenderingOff) { _hidden.Add(r); r.forceRenderingOff = true; }
            }
            foreach (var ps in _creature.GetComponentsInChildren<ParticleSystem>(true))
                if (!_fxSystems.Contains(ps) && ps.GetComponentInParent<ActionTimeline>(true) != null) _fxSystems.Add(ps);
        }

        // ---- The creature's effects around you, with a hole for your eyes --------------------------------------------

        private readonly List<Renderer> _fxRenderers = new List<Renderer>();
        private readonly List<ParticleSystem> _fxSystems = new List<ParticleSystem>();
        private readonly HashSet<Renderer> _fxCut = new HashSet<Renderer>();
        private readonly Dictionary<long, Vector3> _shrunk = new Dictionary<long, Vector3>();   // particle (system, seed) -> its size
        private ParticleSystem.Particle[] _particles = new ParticleSystem.Particle[256];

        /// <summary>Effect pieces closer than this to your eyes are not drawn (board units).</summary>
        private float CutoutRadius => Mathf.Max(0.15f, 0.45f * _height);

        /// <summary>
        /// Status effects (frozen, burning, auras...) play on the hidden creature that walks with you, so you see them
        /// around your feet and body when you look around. What would sit right in front of your eyes is cut out: effect
        /// particles within CutoutRadius of your eyes aren't drawn (each particle, while it is there), and effect meshes
        /// that wrap around your head aren't drawn while they do. The effects themselves are not changed.
        /// </summary>
        private void CutOutEffects(VRRig rig)
        {
            if (rig.Head == null || (_fxRenderers.Count == 0 && _fxSystems.Count == 0)) return;
            Vector3 eye = rig.Head.position;
            float r2 = CutoutRadius * CutoutRadius;
            foreach (var r in _fxRenderers)
            {
                if (r == null || r is ParticleSystemRenderer) continue;
                bool wraps = r.enabled && r.gameObject.activeInHierarchy && r.bounds.SqrDistance(eye) < 1e-6f;
                if (wraps == _fxCut.Contains(r)) continue;
                r.forceRenderingOff = wraps;
                if (wraps) _fxCut.Add(r); else _fxCut.Remove(r);
            }
            foreach (var ps in _fxSystems)
            {
                if (ps == null || !ps.gameObject.activeInHierarchy) continue;
                int n = ps.particleCount;
                if (n == 0) continue;
                if (_particles.Length < n) _particles = new ParticleSystem.Particle[n * 2];
                n = ps.GetParticles(_particles, n);
                bool local = ps.main.simulationSpace == ParticleSystemSimulationSpace.Local;
                Transform t = ps.transform;
                long id = (long)ps.GetInstanceID() << 32;
                bool changed = false;
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = local ? t.TransformPoint(_particles[i].position) : _particles[i].position;
                    long key = id | _particles[i].randomSeed;
                    bool near = (p - eye).sqrMagnitude < r2;
                    if (near && !_shrunk.ContainsKey(key))
                    {
                        _shrunk[key] = _particles[i].startSize3D;
                        _particles[i].startSize3D = Vector3.zero;
                        changed = true;
                    }
                    else if (!near && _shrunk.TryGetValue(key, out Vector3 size))
                    {
                        _particles[i].startSize3D = size;
                        _shrunk.Remove(key);
                        changed = true;
                    }
                }
                if (changed) ps.SetParticles(_particles, n);
            }
            if (_shrunk.Count > 4000) _shrunk.Clear();   // particles that died while cut out
        }

        /// <summary>Give cut-out particles their size back (leaving first person).</summary>
        private void RestoreParticles()
        {
            foreach (var ps in _fxSystems)
            {
                if (ps == null || _shrunk.Count == 0) continue;
                int n = ps.particleCount;
                if (n == 0) continue;
                if (_particles.Length < n) _particles = new ParticleSystem.Particle[n * 2];
                n = ps.GetParticles(_particles, n);
                long id = (long)ps.GetInstanceID() << 32;
                bool changed = false;
                for (int i = 0; i < n; i++)
                    if (_shrunk.TryGetValue(id | _particles[i].randomSeed, out Vector3 size)) { _particles[i].startSize3D = size; changed = true; }
                if (changed) ps.SetParticles(_particles, n);
            }
            _shrunk.Clear();
            _fxCut.Clear();
        }

        /// <summary>Its colliders off while it walks with you (like TaleSpire does while carrying), so your own lasers and picking pass through it.</summary>
        private void DisableColliders()
        {
            _disabledColliders.Clear();
            if (_creature == null) return;
            foreach (var col in _creature.GetComponents<BounceCollider>())
                if (col.enabled) { col.enabled = false; _disabledColliders.Add(col); }
        }

        private void RestoreColliders()
        {
            foreach (var col in _disabledColliders) if (col != null) col.enabled = true;
            _disabledColliders.Clear();
        }

        /// <summary>You are the selected creature (TaleSpire's attacks start from the selected one); nothing else can be.</summary>
        private void SelectCreature()
        {
            try
            {
                if (LocalClient.HasLassoedCreatures) LocalClient.ClearLassoedCreatures();
                if (LocalClient.SelectedCreatureId != _creatureId) LocalClient.SetSelectedCreatureId(_creatureId);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("First person: selecting the creature failed: " + e.Message); }
        }

        private static void CollapseToolbars()
        {
            try
            {
                if (BoardToolManager.IsUIExpanded && SingletonBehaviour<BoardToolManager>.HasInstance)
                    SingletonBehaviour<BoardToolManager>.Instance.CollapseUI();
            }
            catch { }
        }

        /// <summary>The creature goes everywhere with you, hidden, facing where you look (only on this computer until you leave).</summary>
        private void FollowCreature(VRRig rig)
        {
            if (_creature == null) return;
            Vector3 look = rig.Head != null ? rig.Head.forward : Vector3.zero;
            look.y = 0f;
            Quaternion rot = RotationFacing(_creature.Rotator, look);
            _creature.JumpTo(_feet);
            _creature.JumpRotation(rot);
            _creature.CorrectPos = _feet;
            _creature.CorrectRotation = rot;
        }

        /// <summary>A hand colour was just chosen for this creature: if you are it, your hands change at once.</summary>
        public void HandColorChanged(CreatureBoardAsset c)
        {
            if (_active && _creature != null && c != null && c.CreatureId == _creatureId) _hands?.SetColor(HandColors.ColorFor(c));
        }

        /// <summary>This creature's weapons changed: if you are it, the one in your hand changes at once.</summary>
        public void WeaponChanged(CreatureBoardAsset c)
        {
            if (_active && _creature != null && c != null && c.CreatureId == _creatureId) _weapons.SetHeld(WeaponLoadouts.EquippedOf(c));
        }

        /// <summary>The floor (tiles, props, the board) within a distance below a point, for effects on the ground.</summary>
        public bool FloorAt(Vector3 from, float maxDist, out float y)
        {
            y = 0f;
            try
            {
                if (_filterReady && PhysicsEngine.Default.SphereCast(from, 0.05f, Vector3.down, out HitInfo hit, maxDist, _filter, true))
                {
                    y = hit.point.y;
                    return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>Open the radial menu of the creature you are (A when not pointing at another creature or a door).</summary>
        public bool OpenCreatureMenu()
        {
            if (!_active || _creature == null) return false;
            try
            {
                CreatureMenuBoardTool.SwitchToTool(_creature, _height * 0.8f);
                return true;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("First person: opening the creature's menu failed: " + e.Message);
                return false;
            }
        }

        private void EnsureHands()
        {
            if (_handsTried) return;
            _handsTried = true;
            try
            {
                _hands = FPHands.Load(Path.Combine(Plugin.PluginDir, "vrhands.bin"), HandColors.DefaultColor);
            }
            catch (System.Exception e) { Plugin.Log.LogError("First-person hands failed to load: " + e); }
        }

        // ---- Per frame -------------------------------------------------------------------------------------------

        private void Update()
        {
            _blink.Update(Time.unscaledDeltaTime);
            if (!_active) return;
            var rig = VRManager.Instance != null ? VRManager.Instance.Rig : null;
            if (rig == null || !rig.OnBoard || rig.RigTransform == null || !rig.FirstPerson) { Abort("left the board"); return; }
            if (_creature == null) { Abort("the creature is gone"); return; }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            // The body stands under your head: real steps move it (they are not stopped by walls).
            Quaternion yawRot = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 hl = rig.HeadLocalPosition;
            Vector3 head = _origin + yawRot * new Vector3(hl.x, 0f, hl.z) * _scale;
            _feet.x = head.x;
            _feet.z = head.z;

            // Flying can be switched on or off from the creature's menu while you are it.
            bool flying = DebugFlying ?? _creature.Flying;
            if (flying != _flying)
            {
                _flying = flying;
                _vaulting = false;
                _jumpWait = 0f;
                _vy = 0f;
                _airborne = !flying;   // stops in the air, or starts falling
                Plugin.Log.LogInfo("First person: flying " + (flying ? "on" : "off"));
            }

            bool still = _blink.Running;
            Vector2 stick = still || _vaulting ? Vector2.zero : Deadzone(SteamVRInput.Move.Value, 0.15f);
            // Not allowed to move the creature now (not its turn): no walking - say why if you try.
            if (!_mayMove && stick.sqrMagnitude > 0.25f) NoteLocked();
            if (!_mayMove) stick = Vector2.zero;
            Vector2 right = still || CreatureHold.IsHolding ? Vector2.zero : SteamVRInput.TurnHeight.Value;
            RightStick(right, dt);
            if (!_mayMove && _flyInput != 0f) { _flyInput = 0f; NoteLocked(); }

            Vector3 delta = Vector3.zero;
            if (stick != Vector2.zero && rig.Head != null)
            {
                Vector3 f = rig.Head.forward;
                f.y = 0f;
                if (f.sqrMagnitude < 1e-4f) f = yawRot * Vector3.forward;
                f.Normalize();
                Vector3 r = Vector3.Cross(Vector3.up, f);
                float speed = Mathf.Max(0f, VRConfig.FPWalkSpeed.Value) * (_crouched ? 0.6f : 1f);
                delta = (f * stick.y + r * stick.x) * speed * dt;
            }

            Vector3 before = _feet;
            EnsureFilter();
            if (_vaulting) UpdateVault(dt);
            else StepBody(delta, dt);
            _origin.x += _feet.x - before.x;
            _origin.z += _feet.z - before.z;

            // Turn about your head.
            if (_turn != 0f)
            {
                Vector3 pivot = new Vector3(_feet.x, 0f, _feet.z);
                Vector3 o = new Vector3(_origin.x, 0f, _origin.z);
                o = pivot + Quaternion.Euler(0f, _turn, 0f) * (o - pivot);
                _origin.x = o.x;
                _origin.z = o.z;
                _yaw += _turn;
            }

            // Your eyes follow the body smoothly (never a sudden start or stop): steps gently, jumps and climbs closely.
            float smooth = _vaulting ? 0.05f : _airborne ? 0.06f : 0.14f;
            _viewY = Mathf.SmoothDamp(_viewY, _feet.y, ref _viewVel, smooth, Mathf.Infinity, dt);
            // Never lag far behind the body (fast jumps and falls): your eyes must stay above whatever it stands on.
            float lag = (_airborne || _vaulting ? 0.4f : 1f) * _eye;
            _viewY = Mathf.Clamp(_viewY, _feet.y - lag, _feet.y + Mathf.Max(lag, SnapDown));
            if (Mathf.Abs(_viewY - _feet.y) > 3f * _height) { _viewY = _feet.y; _viewVel = 0f; }
            _crouchT = Mathf.MoveTowards(_crouchT, _crouched ? 1f : 0f, dt / 0.3f);
            float dipTarget = _jumpWait > 0f ? 0.06f * _height : _landTimer > 0f ? 0.05f * _height : 0f;
            _landTimer = Mathf.Max(0f, _landTimer - dt);
            _dip = Mathf.Lerp(_dip, dipTarget, 1f - Mathf.Exp(-dt / 0.08f));
            float c = _crouchT * _crouchT * (3f - 2f * _crouchT);
            _origin.y = _viewY - _eye / 3f * c - _dip + DebugLift * _scale;
            rig.SetFirstPersonPose(_origin, _yaw, _scale);

            FollowCreature(rig);
            UpdateSync(dt);
            SelectCreature();
            CollapseToolbars();
        }

        private void LateUpdate()
        {
            var mgr = VRManager.Instance;
            var rig = mgr != null ? mgr.Rig : null;
            _blink.Draw(rig);
            if (!_active || rig == null)
            {
                if (_hands != null && _hands.Visible) _hands.Visible = false;
                _vignette.Set(0f, null);
                FPHands.RightHidden = false;
                UpdateWeapons(false);
                return;
            }
            KeepHidden();
            FollowCreature(rig);   // again: after anything TaleSpire did to it this frame
            try { CutOutEffects(rig); }
            catch (System.Exception e) { if (!_fxErrorLogged) { _fxErrorLogged = true; Plugin.Log.LogError("First person: effect cutout failed: " + e); } }
            FPHands.RightHidden = FPWeapons.HidesHand;   // a gun or blade in the right hand: only the weapon is drawn
            if (_hands != null)
            {
                _hands.Visible = true;
                try { _hands.Update(rig, mgr.Hands); }
                catch (System.Exception e) { Plugin.Log.LogError("First-person hands failed: " + e); _hands.Visible = false; _hands = null; }
            }
            UpdateWeapons(_hands != null && _hands.Visible);
            _ruler.Update(_startPos, _feet, _height, _hands);
            // Crouching ("sneaking"): the edges of your view darken a little.
            _vignette.Set(_crouchT, rig.Head);
        }

        private bool _weaponErrorLogged;

        private void UpdateWeapons(bool handsShown)
        {
            try { _weapons.LateUpdate(handsShown); }
            catch (System.Exception e) { if (!_weaponErrorLogged) { _weaponErrorLogged = true; Plugin.Log.LogError("First person: weapons failed: " + e); } }
        }

        /// <summary>
        /// Right stick: sideways turns exactly like the table view (snap turns, or smooth if SmoothTurn is on); up jumps,
        /// down crouches / stands. Flying creatures: up and down fly up and down instead.
        /// </summary>
        private void RightStick(Vector2 s, float dt)
        {
            bool vertical = Mathf.Abs(s.y) > Mathf.Abs(s.x);
            _flyInput = 0f;
            if (_flying)
            {
                if (vertical && Mathf.Abs(s.y) > 0.25f) _flyInput = Mathf.Sign(s.y) * Mathf.InverseLerp(0.25f, 1f, Mathf.Abs(s.y));
            }
            else
            {
                if (vertical && s.y > 0.7f && _upReady) { _upReady = false; Jump(); }
                if (vertical && s.y < -0.7f && _downReady) { _downReady = false; ToggleCrouch(); }
            }
            if (s.y < 0.3f) _upReady = true;
            if (s.y > -0.3f) _downReady = true;

            float turnInput = !vertical && Mathf.Abs(s.x) > 0.2f ? s.x : 0f;
            _turn = 0f;
            if (VRConfig.SmoothTurn.Value) _turn = turnInput * VRConfig.SmoothTurnSpeed.Value * dt;
            else
            {
                if (Mathf.Abs(turnInput) > 0.7f && _snapReady) { _turn = Mathf.Sign(turnInput) * VRConfig.SnapTurnDegrees.Value; _snapReady = false; }
                if (Mathf.Abs(s.x) < 0.3f) _snapReady = true;
            }

            if (_jumpWait > 0f)
            {
                _jumpWait -= dt;
                if (_jumpWait <= 0f)
                {
                    _airborne = true;
                    _vy = Mathf.Sqrt(2f * Gravity * (Mathf.Max(0f, DebugJump ?? VRConfig.FPJumpHeight.Value) + 0.25f));
                }
            }
        }

        private static float Gravity => Mathf.Max(1f, VRConfig.FPGravity.Value);

        public void Jump()
        {
            if (!_active || _flying || _airborne || _vaulting || _jumpWait > 0f) return;
            if (!_mayMove) { NoteLocked(); return; }
            _jumpWait = 0.2f / 3f;   // a brief crouch before the leap
        }

        public void ToggleCrouch()
        {
            if (!_active) return;
            if (_crouched)
            {
                // Stand up only if there is room above.
                float crouchTop = _height * (2f / 3f), standTop = _height;
                if (CastBody(_feet, 0.05f, crouchTop, Vector3.up, standTop - crouchTop + Skin, out HitInfo above) && above.normal.y < -0.2f)
                {
                    SteamVRInput.Haptic(Hand.Right, 0.03f, 80f, 0.4f);
                    return;
                }
            }
            _crouched = !_crouched;
        }

        public void ToggleRuler()
        {
            if (!_active) return;
            _ruler.LineVisible = !_ruler.LineVisible;
            Plugin.Log.LogInfo("First person ruler " + (_ruler.LineVisible ? "shown" : "hidden"));
        }

        // ---- Walking -------------------------------------------------------------------------------------------

        /// <summary>Collision height above the feet (lower while crouching).</summary>
        private float BodyTop() { return _height * (1f - _crouchT / 3f); }

        private float StepHeight => Mathf.Max(0f, DebugStep ?? VRConfig.FPStepHeight.Value);

        private void StepBody(Vector3 delta, float dt)
        {
            float top = BodyTop();
            if (_flying)
            {
                _feet += MoveHorizontal(_feet, delta, 0.05f, top);
                float dy = _flyInput * Mathf.Max(0.5f, VRConfig.FPWalkSpeed.Value) * dt;
                if (dy > 0f && CastBody(_feet, 0.05f, top, Vector3.up, dy + Skin, out HitInfo hu) && hu.normal.y < -0.2f) dy = Mathf.Max(0f, hu.distance - Skin);
                if (dy < 0f && FloorBelow(_feet + Vector3.up * 0.05f, -dy + 0.05f, out float fy)) dy = Mathf.Max(dy, fy - _feet.y);
                _feet.y += dy;
                return;
            }
            if (!_airborne)
            {
                // Across at floor level (stepping over small bumps). If a ledge stops you and you can climb it, climb.
                // Floor level first, so doorways and arches (wall above, opening below) let you through.
                float maxLift = StepHeight;
                bool upHit = CastBody(_feet, 0.05f, top, Vector3.up, maxLift + Skin, out HitInfo up) && up.normal.y < -0.2f;   // a ceiling, not a wall
                if (upHit) maxLift = Mathf.Max(0f, up.distance - Skin);
                float lift = Mathf.Min(Mathf.Min(SmallStep, 0.4f * _height), maxLift);
                Vector3 m = Vector3.zero;
                if (delta.sqrMagnitude > 0f)
                {
                    m = MoveHorizontal(_feet, delta, lift + 0.02f, top);
                    if (m.sqrMagnitude < delta.sqrMagnitude * 0.9f && maxLift > lift + 0.01f && TryStartVault(delta.normalized, maxLift)) return;
                }
                Vector3 p = _feet + m;
                bool floorHit = FloorBelow(p + Vector3.up * lift, lift + SnapDown, out float floor);
                if (DebugTrace > 0 && delta.sqrMagnitude > 0f)
                {
                    DebugTrace--;
                    Plugin.Log.LogInfo($"[fp] feet {_feet:F2} delta {delta:F3} upHit={upHit} maxLift {maxLift:F2} -> p {p:F2} (moved {(p - _feet).magnitude:F3}) floor={floorHit}@{floor:F2} {_lastCast}");
                }
                if (floorHit) _feet = new Vector3(p.x, floor, p.z);
                else
                {
                    _feet = p;          // walked off an edge
                    _airborne = true;
                    _vy = 0f;
                }
                return;
            }
            // In the air: across (a little above the feet, so a ledge's edge doesn't snag), then up or down. Running into
            // a ledge you can reach (jumping at it) climbs onto it.
            Vector3 air = MoveHorizontal(_feet + Vector3.up * 0.2f, delta, 0.05f, top);
            if (delta.sqrMagnitude > 0f && air.sqrMagnitude < delta.sqrMagnitude * 0.9f && TryStartVault(delta.normalized, StepHeight)) return;
            _feet += air;
            _vy = Mathf.Max(_vy - Gravity * dt, -MaxFall);
            float step = _vy * dt;
            if (step > 0f)
            {
                if (CastBody(_feet, 0.05f, top, Vector3.up, step + Skin, out HitInfo hu) && hu.normal.y < -0.2f)
                {
                    step = Mathf.Max(0f, hu.distance - Skin);
                    _vy = 0f;
                }
                _feet.y += step;
            }
            else if (FloorBelow(_feet + Vector3.up * 0.2f, -step + 0.2f, out float floor) && floor >= _feet.y + step - 0.001f)
            {
                _feet.y = floor;
                _airborne = false;
                _vy = 0f;
                _landTimer = 0.12f;
            }
            else _feet.y += step;
        }

        /// <summary>
        /// Something stops you: if it is a ledge you can climb (its top within reach, room to stand on it), start
        /// climbing it. The climb rises first and moves over the edge at the end, so your eyes are above the ledge
        /// before they cross its edge.
        /// </summary>
        private bool TryStartVault(Vector3 dir, float reach)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f || reach <= SmallStep) return false;
            dir.Normalize();
            float over = _radius + 0.15f;   // your centre this far onto the ledge
            Vector3 ahead = _feet + dir * over;
            if (!FloorBelow(ahead + Vector3.up * reach, reach, out float ledge)) return false;
            float rise = ledge - _feet.y;
            if (rise < 0.05f || rise > reach + 0.01f) return false;
            float top = BodyTop();
            // Room up there: nothing in the body's way at the ledge (a wall on top of it, a ceiling).
            Vector3 onLedge = new Vector3(ahead.x, ledge, ahead.z);
            if (CastBody(onLedge, 0.05f, top, Vector3.up, 0.05f, out HitInfo blocked) && blocked.distance < 0.01f) return false;
            if (CastBody(_feet + Vector3.up * rise, 0.05f, top, dir, over, out HitInfo path) && path.distance < over - Skin) return false;
            _vaulting = true;
            _vaultFrom = _feet;
            _vaultDir = dir;
            _vaultDist = over;
            _vaultRise = rise;
            _vaultT = 0f;
            _vaultFwd = 0f;
            _vaultDur = 0.35f + 0.18f * rise;
            _airborne = false;
            _vy = 0f;
            _jumpWait = 0f;
            Plugin.Log.LogInfo($"First person: climbing {rise:F2} onto a ledge ({_vaultDur:F2} s)");
            return true;
        }

        private void UpdateVault(float dt)
        {
            _vaultT += dt;
            float x = Mathf.Clamp01(_vaultT / Mathf.Max(0.05f, _vaultDur));
            float up = Ease(Mathf.Clamp01(x / 0.8f));        // up during the first 80%
            float fwd = Ease(Mathf.InverseLerp(0.35f, 1f, x));  // over the edge once mostly up
            Vector3 step = _vaultDir * ((fwd - _vaultFwd) * _vaultDist);
            _vaultFwd = fwd;
            _feet.x += step.x;
            _feet.z += step.z;
            _feet.y = _vaultFrom.y + _vaultRise * up;
            if (x >= 1f)
            {
                _vaulting = false;
                _feet.y = _vaultFrom.y + _vaultRise;
                _landTimer = 0.08f;
            }
        }

        private static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>Move the body sideways, stopping at walls and sliding along them. Returns how far it moved.</summary>
        private Vector3 MoveHorizontal(Vector3 feet, Vector3 delta, float bottom, float top)
        {
            Vector3 moved = Vector3.zero;
            delta.y = 0f;
            for (int i = 0; i < 3 && delta.sqrMagnitude > 1e-10f; i++)
            {
                float dist = delta.magnitude;
                Vector3 dir = delta / dist;
                bool any = CastBody(feet + moved, bottom, top, dir, dist + Skin, out HitInfo hit);
                if (DebugTrace > 0) _lastCast = any ? $"wall@{hit.distance:F3} n={(Vector3)hit.normal:F2} p={(Vector3)hit.point:F2}" : "no wall";
                if (any && !(hit.distance < 1e-3f && Vector3.Dot(dir, (Vector3)hit.normal) >= 0f))   // already touching, moving away
                {
                    float go = Mathf.Clamp(hit.distance - Skin, 0f, dist);
                    moved += dir * go;
                    Vector3 n = hit.normal;
                    n.y = 0f;
                    if (n.sqrMagnitude < 1e-6f) break;
                    n.Normalize();
                    Vector3 rest = dir * (dist - go);
                    delta = rest - n * Vector3.Dot(rest, n);   // slide along the wall
                }
                else
                {
                    moved += delta;
                    break;
                }
            }
            return moved;
        }

        /// <summary>
        /// Sweep the body (an upright cylinder from feet+bottom to feet+top) along a direction. Straight up or down it is
        /// a little thinner, so a wall the body is already touching doesn't count as a ceiling or floor.
        /// </summary>
        private bool CastBody(Vector3 feet, float bottom, float top, Vector3 dir, float dist, out HitInfo hit)
        {
            hit = default;
            if (!_filterReady) return false;
            float h = Mathf.Max(0.05f, top - bottom);
            Vector3 centre = feet + Vector3.up * (bottom + h * 0.5f);
            float r = Mathf.Abs(dir.y) > 0.9f ? _radius * 0.8f : _radius;
            try
            {
                return PhysicsEngine.Default.CylinderCast(centre, new float3(0f, -h * 0.5f, 0f), new float3(0f, h * 0.5f, 0f), r, dir, out hit, dist, _filter);
            }
            catch { return false; }
        }

        /// <summary>The highest floor under the body within a distance below a point (the ground plane counts at level 0).</summary>
        private bool FloorBelow(Vector3 from, float maxDist, out float y)
        {
            y = 0f;
            float r = _radius * 0.5f;
            try
            {
                if (_filterReady && PhysicsEngine.Default.SphereCast(from + Vector3.up * r, r, Vector3.down, out HitInfo hit, maxDist + r, _filter, true))
                {
                    y = hit.point.y;
                    return true;
                }
            }
            catch { }
            if (from.y >= 0f && from.y - maxDist <= 0f) { y = 0f; return true; }
            return false;
        }

        private void EnsureFilter()
        {
            if (_filterReady) return;
            try
            {
                // What TaleSpire drops creatures onto: tiles, props and the board's ground. Never creatures.
                _filter = BouncePhysicsLayers.GetQueryFilterByLayerNames("Tile", "Board Mat", "Props");
                _filterReady = _filter.CollidesWith != 0;
                Plugin.Log.LogInfo($"First person collision filter ready={_filterReady} (collides {_filter.CollidesWith:X})");
            }
            catch (System.Exception e) { Plugin.Log.LogError("First person: no collision filter: " + e.Message); }
        }

        private static Vector2 Deadzone(Vector2 v, float dz)
        {
            float m = v.magnitude;
            if (m < dz) return Vector2.zero;
            return v * ((m - dz) / (1f - dz) / m);
        }

        // ---- Debug ---------------------------------------------------------------------------------------------

        /// <summary>Debug ("fp enter n"): the n-th nearest creature to your head.</summary>
        public void DebugEnterNearest(int index, bool home)
        {
            var rig = VRManager.Instance.Rig;
            var all = UnityEngine.Object.FindObjectsOfType<CreatureBoardAsset>();
            if (all.Length == 0 || rig.Head == null) { Plugin.Log.LogInfo("[fp] no creatures"); return; }
            System.Array.Sort(all, (a, b) => Vector3.Distance(a.transform.position, rig.Head.position).CompareTo(Vector3.Distance(b.transform.position, rig.Head.position)));
            Enter(all[Mathf.Clamp(index, 0, all.Length - 1)], home);
        }

        public static void DebugHandView(float? curl) { FPHands.DebugCurl = curl; }

        /// <summary>
        /// Debug ("fp facetest"): check the exit rotation maths on the current creature without moving it: for a few
        /// directions, the rotation we would drop it with must make it face that way.
        /// </summary>
        public void DebugFaceTest()
        {
            if (_creature == null) { Plugin.Log.LogInfo("[fp] facetest: not in first person"); return; }
            var rot = _creature.Rotator;
            var probe = new GameObject("TaleSpireVR_FaceProbe").transform;
            probe.SetParent(rot.parent, false);
            Vector3 cur = -rot.right;
            cur.y = 0f;
            foreach (float a in new[] { 0f, 45f, 90f, 180f, -90f, -135f })
            {
                Vector3 want = Quaternion.Euler(0f, a, 0f) * cur.normalized;
                probe.localRotation = RotationFacing(rot, want);
                Vector3 got = -probe.right;
                got.y = 0f;
                Plugin.Log.LogInfo($"[fp] facetest turn {a,5:F0}: want {want:F2} got {got.normalized:F2} error {Vector3.Angle(want, got):F1} deg");
            }
            Destroy(probe.gameObject);
        }

        /// <summary>Debug ("fp lift m"): pretend the headset is this many meters higher (testing with it lying on a desk).</summary>
        public static float DebugLift;
        /// <summary>Debug ("fp trace n"): log the next n walking steps' collision queries.</summary>
        public static int DebugTrace;
        /// <summary>Debug ("fp step h|off"): a step height for testing (not saved to the config).</summary>
        public static float? DebugStep;
        /// <summary>Debug ("fp fly 1|0|off"): treat the creature as flying or not without changing it on the board.</summary>
        public static bool? DebugFlying;
        /// <summary>Debug ("fp jumph h|off"): a jump height for testing (not saved to the config).</summary>
        public static float? DebugJump;
        private string _lastCast = "";

        public string Status()
        {
            string sel = "-";
            try { sel = LocalClient.SelectedCreatureId == _creatureId && _active ? "self" : LocalClient.SelectedCreatureId.ToString(); } catch { }
            string cpos = _creature != null ? _creature.transform.position.ToString("F2") : "-";
            return $"fp active={_active} blink={_blink.Closure:F2} creature='{_creatureName}' at {cpos} selected={sel} yaw={_yaw:F0} feet={_feet:F2} view={_viewY:F2} start={_startPos:F2} height={_height:F2} eye={_eye:F2} scale={_scale:F3} r={_radius:F2} airborne={_airborne} vy={_vy:F1} vaulting={_vaulting} crouch={_crouchT:F2} flying={_flying} ruler='{_ruler.Text}' line={_ruler.LineVisible} pointers L={PointerVisible(Hand.Left)} R={PointerVisible(Hand.Right)} toolbars={(BoardToolManager.IsUIExpanded ? "open" : "closed")} {RadialExtraButton.Status()}\n"
                   + (_hands != null ? _hands.Status() : "hands not loaded") + "\n" + _weapons.Status();
        }

        private void OnDestroy()
        {
            if (_active) { HideCreature(false); PutBack(); }
            _blink.Destroy();
            _ruler.Destroy();
            _vignette.Destroy();
            if (_hands != null) _hands.Destroy();
        }
    }
}
