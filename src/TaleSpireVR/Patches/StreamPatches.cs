using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace TaleSpireVR.Patches
{
    /// <summary>
    /// The held stream camera's board pass. TaleSpire culls tiles into shared GPU buffers and queues their draws for
    /// one camera, so the held camera is culled and rendered here, just before TaleSpire does the same for the
    /// headset (which then overwrites the buffers for its own view). See StreamView.
    /// </summary>
    [HarmonyPatch(typeof(BoardPresentation), nameof(BoardPresentation.CullAndRender))]
    internal static class HeldViewRenderPatch
    {
        private static void Prefix(BoardPresentation __instance, Camera camera, Bounds isolationBounds, bool useIsolationBounds)
        {
            if (StreamView.RenderingHeld || CutsceneScreen.Rendering) return;
            var sv = StreamView.Instance;
            if (sv != null && sv.Held) sv.RenderHeld(__instance, camera, isolationBounds, useIsolationBounds);
            // (The same for a player's cutscene screen.)
            var cs = CutsceneScreen.Instance;
            if (cs != null && cs.isActiveAndEnabled && cs.Showing) cs.Render(__instance, camera, isolationBounds, useIsolationBounds);
        }
    }

    /// <summary>No object picking from the held camera's (or cutscene screen's) pass: picking belongs to the laser (PickRenderPatch).</summary>
    [HarmonyPatch(typeof(PixelPickingManager), nameof(PixelPickingManager.UpdateAndSetupForRender))]
    internal static class HeldViewNoPickingPatch
    {
        private static bool Prefix(ref bool __result, out CommandBuffer commandBuffer, out Material writeIdsStandardMaterial, out Material writeIdsIndirectMaterial)
        {
            commandBuffer = null;
            writeIdsStandardMaterial = null;
            writeIdsIndirectMaterial = null;
            if (!StreamView.RenderingHeld && !CutsceneScreen.Rendering) return true;
            __result = false;
            return false;
        }
    }
}
