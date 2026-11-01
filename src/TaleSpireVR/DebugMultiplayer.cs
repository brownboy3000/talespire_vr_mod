using System.Collections.Generic;
using System.Reflection;
using Bounce.Singletons;
using HarmonyLib;
using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// Debug commands for multiplayer features, on this computer only (nothing is sent to TaleSpire's servers, so the
    /// board and other players are untouched): a local play mode (turn-based / cutscene) and a made-up initiative
    /// lineup, to see the turn banner, turn locks and the cutscene screen without running a real session.
    /// </summary>
    internal static class DebugMultiplayer
    {
        private static readonly FieldInfo QueueField = AccessTools.Field(typeof(InitiativeManager), "_turnQueueList");
        private static readonly FieldInfo QueueEditField = AccessTools.Field(typeof(InitiativeManager), "_turnQueueEditList");
        private static readonly FieldInfo CurrentField = AccessTools.Field(typeof(InitiativeManager), "_currentTurnElement");
        private static readonly FieldInfo SwitchEvent = AccessTools.Field(typeof(InitiativeManager), "OnTurnSwitch");
        private static readonly FieldInfo QueueEvent = AccessTools.Field(typeof(InitiativeManager), "OnTurnQueueUpdate");
        private static List<QueueElement> _savedQueue;
        private static QueueElement _savedCurrent;

        /// <summary>"playmode local free|turnbased|cutscene": switch the play mode on this computer only (not pushed).</summary>
        public static void PlayModeLocal(string mode)
        {
            if (!SingletonStateMBehaviour<PlayMode, PlayMode.State>.TryGetInstance(out var pm)) { Plugin.Log.LogInfo("[mp] no play mode"); return; }
            var s = pm.CurrentState;
            if (mode == "turnbased") s.SwitchState<PlayMode.TurnBased>();
            else if (mode == "cutscene") s.SwitchState<PlayMode.Cutscene>();
            else s.SwitchState<PlayMode.FreeRoam>();
            Plugin.Log.LogInfo($"[mp] play mode (local only) now {pm.CurrentState.GetType().Name}");
        }

        /// <summary>
        /// "turnfake n i": a lineup of the n creatures nearest you, turn at index i (on this computer only); "turnfake
        /// next" moves the turn on; "turnfake off" puts TaleSpire's real lineup back.
        /// </summary>
        public static void TurnFake(string[] p)
        {
            var list = QueueField?.GetValue(null) as List<QueueElement>;
            if (list == null) { Plugin.Log.LogInfo("[mp] no initiative data"); return; }
            if (p[1] == "off")
            {
                if (_savedQueue == null) return;
                list.Clear();
                list.AddRange(_savedQueue);
                CurrentField.SetValue(null, _savedCurrent);
                _savedQueue = null;
                Fire(list);
                Plugin.Log.LogInfo("[mp] real lineup back");
                return;
            }
            if (_savedQueue == null) { _savedQueue = new List<QueueElement>(list); _savedCurrent = InitiativeManager.CurrentTurnElement; }
            if (p[1] == "next")
            {
                int at = Mathf.Max(0, list.IndexOf(InitiativeManager.CurrentTurnElement));
                if (list.Count > 0) CurrentField.SetValue(null, list[(at + 1) % list.Count]);
            }
            else
            {
                int n = int.Parse(p[1]), i = p.Length > 2 ? int.Parse(p[2]) : 0;
                var rig = VRManager.Instance.Rig;
                var all = Object.FindObjectsOfType<CreatureBoardAsset>();
                System.Array.Sort(all, (a, b) => Vector3.Distance(a.transform.position, rig.Head.position).CompareTo(Vector3.Distance(b.transform.position, rig.Head.position)));
                list.Clear();
                for (int k = 0; k < Mathf.Min(n, all.Length); k++) list.Add(new QueueElement(QueueElement.ElementType.Creature, all[k].CreatureId));
                if (list.Count > 0) CurrentField.SetValue(null, list[Mathf.Clamp(i, 0, list.Count - 1)]);
            }
            Fire(list);
            var cur = InitiativeManager.CurrentTurnElement;
            string who = CreaturePresenter.TryGetAsset(cur.CreatureGuid, out var c) ? c.Name : "-";
            Plugin.Log.LogInfo($"[mp] lineup (local only): {list.Count} creatures, turn: '{who}'");
        }

        private static void Fire(List<QueueElement> list)
        {
            if (QueueEditField?.GetValue(null) is List<QueueElement> edit) { edit.Clear(); edit.AddRange(list); }
            try { (SwitchEvent?.GetValue(null) as System.Action<QueueElement>)?.Invoke(InitiativeManager.CurrentTurnElement); } catch (System.Exception e) { Plugin.Log.LogInfo("[mp] turn switch event: " + e.Message); }
            try { (QueueEvent?.GetValue(null) as System.Action<List<QueueElement>>)?.Invoke(list); } catch (System.Exception e) { Plugin.Log.LogInfo("[mp] queue event: " + e.Message); }
        }
    }
}
