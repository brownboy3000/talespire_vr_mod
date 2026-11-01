using System.Reflection;
using HarmonyLib;
using Unity.Mathematics;
using UnityEngine;

namespace TaleSpireVR.Patches
{
    /// <summary>
    /// TaleSpire pins small labels to 3D points by projecting them onto the flat screen: ruler distances, flying
    /// heights, move distances. In VR that lands them on the menu panel (or nowhere), so show each one as a small
    /// sign in the world next to its point instead, facing you and drawn on top so it is always readable.
    /// </summary>
    [HarmonyPatch(typeof(UIInGameText.HiredText), nameof(UIInGameText.HiredText.LateUpdate))]
    internal static class InGameTextPatch
    {
        private static readonly FieldInfo ItemField = AccessTools.Field(typeof(UIInGameText.HiredText), "_hiredText");
        private static readonly FieldInfo PosField = AccessTools.Field(typeof(UIInGameText.HiredText), "_worldPosition");
        private static Canvas _canvas;
        /// <summary>Labels moved to our signs: where TaleSpire had them (parent, layer).</summary>
        private static readonly System.Collections.Generic.Dictionary<UIIngameTextItem, (Transform parent, int layer)> _moved
            = new System.Collections.Generic.Dictionary<UIIngameTextItem, (Transform, int)>();

        /// <summary>The VR mod switches off (Player mode): the labels back on TaleSpire's screen, where it draws them.</summary>
        public static void Restore()
        {
            foreach (var kv in _moved)
            {
                var item = kv.Key;
                if (item == null || kv.Value.parent == null) continue;
                item.transform.SetParent(kv.Value.parent, false);
                VRLayers.SetRecursive(item.gameObject, kv.Value.layer);
                item.transform.localScale = Vector3.one;
                item.transform.localRotation = Quaternion.identity;
            }
            _moved.Clear();
        }

        private static void Postfix(UIInGameText.HiredText __instance)
        {
            if (!VRManager.Active || ItemField == null || PosField == null) return;
            var rig = VRManager.Instance.Rig;
            if (rig == null || !rig.OnBoard || rig.Head == null) return;
            if (!(ItemField.GetValue(__instance) is UIIngameTextItem item) || item == null) return;
            Vector3 world = (float3)PosField.GetValue(__instance);

            EnsureCanvas();
            var t = item.transform;
            if (t.parent != _canvas.transform)
            {
                if (!_moved.ContainsKey(item)) _moved[item] = (t.parent, item.gameObject.layer);
                t.SetParent(_canvas.transform, false);
                VRLayers.SetRecursive(item.gameObject, VRLayers.Overlay);
            }
            Vector3 head = rig.Head.position;
            float dist = Mathf.Max(0.01f, Vector3.Distance(head, world));
            // Constant apparent size (about 1.4 degrees for TaleSpire's label font), lifted a little above the point.
            float s = dist * 0.0012f;
            t.position = world + Vector3.up * (0.02f * rig.Scale);
            t.rotation = Quaternion.LookRotation(t.position - head, Vector3.up);
            t.localScale = new Vector3(s, s, s);
        }

        private static void EnsureCanvas()
        {
            if (_canvas != null) return;
            var go = new GameObject("TaleSpireVR_WorldLabels", typeof(RectTransform));
            Object.DontDestroyOnLoad(go);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 50;
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            VRLayers.SetRecursive(go, VRLayers.Overlay);
        }
    }
}
