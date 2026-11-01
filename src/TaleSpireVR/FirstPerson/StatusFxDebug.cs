using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// Debug ("fx list | play i | stop i | dump"): play TaleSpire's status effects on a creature on this computer only
    /// (nothing is sent to the board or other players), to look at how they show in first person. The creature is the
    /// one you are in first person, else the nearest to your head.
    /// </summary>
    internal static class StatusFxDebug
    {
        private static readonly FieldInfo StatusField = AccessTools.Field(typeof(CreatureMenuBoardTool), "_statusEmotes");
        private static readonly MethodInfo GetTimeline = AccessTools.Method(typeof(EmotingMovableBoardAsset), "GetActionTimeline");

        public static void Command(string[] p)
        {
            string what = p.Length > 1 ? p[1] : "list";
            var statuses = Statuses();
            var creature = Target();
            if (statuses == null || creature == null) { Plugin.Log.LogInfo("[fx] no status list or creature"); return; }
            switch (what)
            {
                case "list":
                    for (int i = 0; i < statuses.Count; i++) Plugin.Log.LogInfo($"[fx] {i}: {statuses[i].DisplayName} ({statuses[i].name})");
                    break;
                case "play":
                case "stop":
                {
                    int i = p.Length > 2 ? int.Parse(p[2]) : 0;
                    var timeline = GetTimeline.Invoke(creature, new object[] { statuses[i] }) as ActionTimeline;
                    if (timeline == null) { Plugin.Log.LogInfo("[fx] no timeline"); return; }
                    timeline.JumpToState(what == "play" ? 0 : 2);   // as TaleSpire does when a status is added / removed
                    Plugin.Log.LogInfo($"[fx] {what} '{statuses[i].DisplayName}' on '{creature.Name}' (locally)");
                    break;
                }
                case "dump":
                    foreach (var tl in creature.GetComponentsInChildren<ActionTimeline>(true))
                    {
                        Plugin.Log.LogInfo($"[fx] timeline '{tl.name}' active={tl.gameObject.activeInHierarchy} playing={tl.IsActive} state={tl.GetCurrentStateIndex()}");
                        foreach (var r in tl.GetComponentsInChildren<Renderer>(true))
                        {
                            if (!r.gameObject.activeInHierarchy) continue;
                            var b = r.bounds;
                            string extra = r is ParticleSystemRenderer ? $" particles={r.GetComponent<ParticleSystem>()?.particleCount} space={r.GetComponent<ParticleSystem>()?.main.simulationSpace}" : "";
                            Plugin.Log.LogInfo($"[fx]   {r.GetType().Name} '{r.name}' enabled={r.enabled} off={r.forceRenderingOff} centre-feet={b.center - creature.transform.position:F2} size={b.size:F2} shader={(r.sharedMaterial ? r.sharedMaterial.shader.name : "-")}{extra}");
                        }
                    }
                    break;
            }
        }

        private static ActionTimelineCollection Statuses()
        {
            if (StatusField == null || !SingletonBehaviour<BoardToolManager>.HasInstance) return null;
            var tool = SingletonBehaviour<BoardToolManager>.Instance.GetTool<CreatureMenuBoardTool>();
            return tool != null ? StatusField.GetValue(tool) as ActionTimelineCollection : null;
        }

        private static CreatureBoardAsset Target()
        {
            if (FirstPersonMode.Creature != null) return FirstPersonMode.Creature;
            var rig = VRManager.Instance.Rig;
            CreatureBoardAsset best = null;
            float bestD = float.MaxValue;
            foreach (var c in Object.FindObjectsOfType<CreatureBoardAsset>())
            {
                float d = Vector3.Distance(c.transform.position, rig.Head.position);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }
    }
}
