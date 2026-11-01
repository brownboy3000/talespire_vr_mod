using UnityEngine;

namespace TaleSpireVR
{
    internal static class VRLayers
    {
        /// <summary>
        /// Unused layer for the mod's own panels, lasers and buttons. The headset camera skips it and a second
        /// overlay camera draws only it, on top of everything (water, fog, post-processing can't cover it).
        /// </summary>
        public const int Overlay = 6;

        public static void SetRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++) SetRecursive(go.transform.GetChild(i).gameObject, layer);
        }
    }
}
