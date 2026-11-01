using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// First person, as others see it: your creature walks with you on every screen, through TaleSpire's own
    /// networking only - nothing of the mod's goes over the network, and players without the mod see the same.
    ///
    /// While you may move it (Roles.CanAct: you control it, it is its turn in turn-based mode, no cutscene holds you),
    /// the creature is attached to your client's MovableHandle - the object TaleSpire itself uses to show a mini being
    /// dragged to everyone, which streams its position and facing several times a second while attached. When you stop
    /// walking for a second, it is dropped there for real (TaleSpire's ordinary drop: saved on the board, as when you
    /// put a mini down), and again when you leave first person. Not allowed to move (not its turn): you can still be it
    /// and look around, but walking and jumping do nothing and the creature stays where it is.
    /// </summary>
    internal partial class FirstPersonMode
    {
        private const float SettleSeconds = 1f;
        private static readonly FieldInfo HandleAssets = AccessTools.Field(typeof(MovableHandle), "_assets");
        private static readonly FieldInfo HandleSearching = AccessTools.Field(typeof(MovableHandle), "_idsOfAssetsWeAreStillSearchingFor");
        private static readonly MethodInfo SwitchStateMethod = AccessTools.Method(typeof(MovableBoardAsset), "SwitchState");

        /// <summary>This frame: you may move the creature (see the class summary).</summary>
        private bool _mayMove;
        /// <summary>Attached to the MovableHandle by us (its position streams to everyone).</summary>
        private bool _attached;
        /// <summary>Where TaleSpire's board has the creature: where you entered, or where it was last dropped.</summary>
        private Vector3 _placedPos;
        private Quaternion _placedRot;
        private Vector3 _lastFeet;
        private float _stillFor, _nextAttachCheck, _nextNote;

        /// <summary>You may move the creature you are now (walking is locked otherwise).</summary>
        public static bool MayMove => Active && Instance._mayMove;

        private void BeginSync()
        {
            _placedPos = _startPos;
            _placedRot = _startRot;
            _attached = false;
            _stillFor = 0f;
            _lastFeet = _feet;
            _nextAttachCheck = 0f;
            _mayMove = !_homeOnExit && Roles.CanAct(_creature);
            if (!_mayMove && !_homeOnExit) Plugin.Log.LogInfo($"First person: '{_creatureName}' can't move now (not its turn, or a cutscene) - looking around only");
        }

        /// <summary>Each frame, after the creature has followed you.</summary>
        private void UpdateSync(float dt)
        {
            if (_creature == null) return;
            bool may = !_homeOnExit && Roles.CanAct(_creature);
            if (may != _mayMove)
            {
                if (!may)
                {
                    // Its turn ended (or a cutscene began): put it down where it is and stop.
                    Commit("its turn ended");
                    DetachSync();
                    Plugin.Log.LogInfo("First person: moving locked (not its turn, or a cutscene)");
                }
                else Plugin.Log.LogInfo("First person: moving allowed again (its turn)");
                _mayMove = may;
            }
            if (!_mayMove) { _lastFeet = _feet; return; }

            if (Time.unscaledTime >= _nextAttachCheck)
            {
                _nextAttachCheck = Time.unscaledTime + 0.25f;
                EnsureAttached();
            }
            bool moving = (_feet - _lastFeet).sqrMagnitude > 1e-6f;
            _lastFeet = _feet;
            _stillFor = moving ? 0f : _stillFor + dt;
            if (_stillFor >= SettleSeconds && Moved(_feet, _creature.Rotator.localRotation)) Commit("stopped walking");
        }

        private bool Moved(Vector3 pos, Quaternion rot) => (pos - _placedPos).sqrMagnitude > 0.02f * 0.02f || Quaternion.Angle(rot, _placedRot) > 3f;

        /// <summary>Walking or jumping was tried while it isn't allowed: say why, now and then.</summary>
        private void NoteLocked()
        {
            if (_homeOnExit || Time.unscaledTime < _nextNote) return;
            _nextNote = Time.unscaledTime + 4f;
            TurnBanner.Note(Roles.Cutscene && !Roles.IsGm ? "A cutscene is playing" : $"Not {_creatureName}'s turn");
            SteamVRInput.Haptic(Hand.Left, 0.04f, 60f, 0.3f);
        }

        private static MovableHandle Handle
        {
            get
            {
                try { return SingletonBehaviour<BoardToolManager>.HasInstance ? SingletonBehaviour<BoardToolManager>.Instance.MovableHandle : null; }
                catch { return null; }
            }
        }

        /// <summary>One of TaleSpire's carry tools is using the handle (you are carrying another mini with a laser).</summary>
        private static bool HandleBusy
        {
            get
            {
                var t = BoardToolManager.CurrentTool;
                return t is CreatureMoveBoardTool || t is CreatureGroupMoveBoardTool || t is CreatureKeyMoveBoardTool;
            }
        }

        private bool OnHandle(MovableHandle h)
        {
            if (h == null || _creature == null) return false;
            if (HandleAssets?.GetValue(h) is IList assets) foreach (var a in assets) if ((Object)a == _creature) return true;
            if (HandleSearching?.GetValue(h) is IEnumerable ids) foreach (var id in ids) if (id is CreatureGuid g && g == _creatureId) return true;
            return false;
        }

        /// <summary>On the handle (again, after a carry tool used it and let go of everything).</summary>
        private void EnsureAttached()
        {
            var h = Handle;
            if (h == null || HandleBusy) return;
            if (OnHandle(h)) return;
            try
            {
                h.Attach(_creature, showRuler: false);
                if (!_attached) Plugin.Log.LogInfo($"First person: '{_creatureName}' walks with you on everyone's board (TaleSpire's live move)");
                _attached = true;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("First person: live move failed: " + e.Message); }
        }

        private void DetachSync()
        {
            if (!_attached) return;
            _attached = false;
            var h = Handle;
            // (Only if nothing else of TaleSpire's is on it: a carry tool detaches its own.)
            if (h == null || HandleBusy || !OnHandle(h)) return;
            try { h.Detach(); }
            catch (System.Exception e) { Plugin.Log.LogWarning("First person: ending the live move failed: " + e.Message); }
        }

        /// <summary>
        /// Put the creature down where you are, for real: TaleSpire's own drop (saved on the board, sent to everyone).
        /// On your screen it stays hidden and keeps following you; its local drop (sound, little fall) is skipped.
        /// </summary>
        private void Commit(string why)
        {
            if (_creature == null || !Moved(_feet, _creature.Rotator.localRotation)) return;
            Vector3 pos = _feet;
            Quaternion rot = _creature.Rotator.localRotation;
            try
            {
                _creature.Drop(pos, pos.y);
                SwitchStateMethod?.Invoke(_creature, new object[] { System.Enum.Parse(SwitchStateMethod.GetParameters()[0].ParameterType, "Idle") });
                DisableColliders();   // (the drop switched them back on)
                _placedPos = pos;
                _placedRot = rot;
                Plugin.Log.LogInfo($"First person: '{_creatureName}' put down at {pos} ({why})");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("First person: putting the creature down failed: " + e.Message); }
        }
    }
}
