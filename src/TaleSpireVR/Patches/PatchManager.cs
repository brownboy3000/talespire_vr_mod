using System;
using System.Reflection;
using HarmonyLib;

namespace TaleSpireVR.Patches
{
    /// <summary>A patch that stays on in normal (non-VR) mode too. Only the development setup's Steam relaunch guard is one.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class AlwaysOnAttribute : Attribute { }

    internal static class PatchManager
    {
        /// <summary>Are the VR mode patches in place (everything but the AlwaysOn ones)?</summary>
        public static bool VRPatched { get; private set; }

        /// <summary>At startup, in either mode: only the AlwaysOn patches.</summary>
        public static void ApplyAlwaysOn(Harmony harmony) => Apply(harmony, alwaysOn: true);

        /// <summary>
        /// VR mode: every other [HarmonyPatch] class, each on its own so one broken patch (e.g. after a TaleSpire update)
        /// does not stop the rest of the mod from loading.
        /// </summary>
        public static void ApplyVR(Harmony harmony)
        {
            if (VRPatched) return;
            Apply(harmony, alwaysOn: false);
            VRPatched = true;
        }

        /// <summary>Normal mode again: all our patches off, then the AlwaysOn ones back, so TaleSpire runs as without the mod.</summary>
        public static void RemoveVR(Harmony harmony)
        {
            if (!VRPatched) return;
            harmony.UnpatchSelf();
            VRPatched = false;
            Apply(harmony, alwaysOn: true);
            Plugin.Log.LogInfo("Harmony patches removed (normal mode)");
        }

        private static void Apply(Harmony harmony, bool alwaysOn)
        {
            int ok = 0, failed = 0;
            foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
                if ((type.GetCustomAttributes(typeof(AlwaysOnAttribute), false).Length > 0) != alwaysOn) continue;
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    ok++;
                }
                catch (Exception e)
                {
                    failed++;
                    Plugin.Log.LogError($"Patch {type.Name} failed: {e.GetBaseException().Message}");
                }
            }
            Plugin.Log.LogInfo($"Harmony patches applied ({(alwaysOn ? "always on" : "VR mode")}): {ok} ok, {failed} failed");
        }
    }
}
