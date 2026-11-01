using HarmonyLib;
using UnityEngine;
using UnityEngine.Playables;

namespace TaleSpireVR.Patches
{
    /// <summary>TaleSpire starts playing an attack: our own first-person melee Hits and gun lasers skip the run-up (FPWeapons.AttackPlayed).</summary>
    [HarmonyPatch(typeof(CreatureBoardAsset), nameof(CreatureBoardAsset.Attack))]
    internal static class CreatureAttackPatch
    {
        private static void Postfix(CreatureBoardAsset __instance, CreatureBoardAsset.AttackEmotes opAttackKind)
        {
            if (VRManager.Active) FPWeapons.AttackPlayed(__instance, opAttackKind);
        }
    }

    /// <summary>An attack lands on its target (FPWeapons.AttackImpact): for our skipped Hits, without TaleSpire's thud.</summary>
    [HarmonyPatch(typeof(CreatureBoardAsset), nameof(CreatureBoardAsset.AttackTargetCreature), new[] { typeof(Vector3) })]
    internal static class CreatureAttackImpactPatch
    {
        private static void Prefix(CreatureBoardAsset __instance)
        {
            if (VRManager.Active) FPWeapons.AttackImpact(__instance);
        }

        private static void Finalizer() => FPWeapons.ImpactDone();
    }

    /// <summary>The thud of our skipped Hit landing is not played (see above).</summary>
    [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.Play))]
    internal static class QuietImpactSoundPatch
    {
        private static bool Prefix() => !FPWeapons.QuietImpact;
    }

    /// <summary>TaleSpire's swing sound in our skipped Hit is not played: your sword already made its sound.</summary>
    [HarmonyPatch(typeof(Bounce.ActionTracks.AudioEventClipBehaviour), nameof(Bounce.ActionTracks.AudioEventClipBehaviour.ProcessFrame))]
    internal static class QuietSwingSoundPatch
    {
        private static bool Prefix(Playable playable, ref bool ___hasPlayed)
        {
            if (!VRManager.Active || !FPWeapons.QuietSwing(playable)) return true;
            ___hasPlayed = true;
            return false;
        }
    }
}
