using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TaleSpireVR.Patches
{
    /// <summary>
    /// Slides sized for TaleSpire's normal panels, made longer by SidePanelScale for the panels enlarged in VR:
    ///  - the side panels themselves (left buttons + menus, top-right buttons + menus, floor slider) slide off screen by
    ///    their normal width when the toolbars collapse (Space); enlarged, a third of them stayed on screen;
    ///  - when a left-hand menu opens, the player portraits slide right by the menu's width.
    /// </summary>
    [HarmonyPatch(typeof(RectTransformTweener), "TweenPosition")]
    internal static class SlideAsideOfEnlargedPanelsPatch
    {
        private static readonly string[] Followers =
        {
            "GUIManager/Ts/#PANEL_ClientList",
            "GUIManager/Ts/PANEL_STRUCTURE/MiddlePanel/MiddlePanel/LeftPanel",
            "GUIManager/Ts/PANEL_STRUCTURE/MiddlePanel/MiddlePanel/RightPanel",
            "GUIManager/Ts/PANEL_STRUCTURE/MiddlePanel/#PANEL_UI_FloorControls",
        };
        private static readonly FieldInfo StartField = AccessTools.Field(typeof(RectTransformTweener), "_position_Start");
        private static readonly FieldInfo RectField = AccessTools.Field(typeof(RectTransformTweener), "_rect");
        private static readonly Dictionary<int, bool> _isFollower = new Dictionary<int, bool>();

        private static void Postfix(RectTransformTweener __instance)
        {
            if (!VRManager.Active || StartField == null || RectField == null) return;
            int id = __instance.GetInstanceID();
            if (!_isFollower.TryGetValue(id, out bool follower))
            {
                string path = Diagnostics.PathOf(__instance.transform);
                follower = false;
                foreach (var f in Followers) if (path.EndsWith(f)) follower = true;
                _isFollower[id] = follower;
            }
            if (!follower) return;
            float s = Mathf.Clamp(VRConfig.SidePanelScale.Value, 0.5f, 3f);
            var rect = RectField.GetValue(__instance) as RectTransform;
            if (rect == null) return;
            Vector2 start = (Vector2)StartField.GetValue(__instance);
            Vector2 p = rect.anchoredPosition;
            p.x = start.x + (p.x - start.x) * s;
            rect.anchoredPosition = p;
        }
    }

    /// <summary>
    /// TaleSpire hides some panels while its toolbars are expanded (Space). In VR the B button always expands them, so
    /// keep these panels visible whenever they would show with the toolbars collapsed: the right-hand floor sliders
    /// (water level, view / cut level, fog level).
    /// </summary>
    [HarmonyPatch(typeof(GUIDisplayItem), nameof(GUIDisplayItem.ShouldShow))]
    internal static class KeepPanelsWhenExpandedPatch
    {
        private static readonly string[] KeepVisible = { "#PANEL_UI_FloorControls" };
        private static readonly Dictionary<int, bool> _isTarget = new Dictionary<int, bool>();
        private static bool _inner;

        private static void Postfix(GUIDisplayItem __instance, ref bool __result,
            GUIDisplayItemBase.Roles role, bool isLoading, GUIDisplayItemBase.Modes mode, bool inBoard, bool building, bool expanded,
            bool leftExpanded, bool rightExpanded, bool canGM, bool GMOverlay, bool UIOverride, bool isPhotoMode, bool isSlabPublishMode)
        {
            if (__result || _inner || !expanded || !VRManager.Active || !IsTarget(__instance)) return;
            _inner = true;
            try
            {
                __result = __instance.ShouldShow(role, isLoading, mode, inBoard, building, false, leftExpanded, rightExpanded,
                    canGM, GMOverlay, UIOverride, isPhotoMode, isSlabPublishMode);
            }
            finally { _inner = false; }
        }

        private static bool IsTarget(GUIDisplayItem item)
        {
            int id = item.GetInstanceID();
            if (!_isTarget.TryGetValue(id, out bool target))
            {
                string path = Diagnostics.PathOf(item.transform);
                target = false;
                foreach (var k in KeepVisible) if (path.Contains(k)) target = true;
                _isTarget[id] = target;
                if (target) Plugin.Log.LogInfo($"Keeping '{path}' visible with the toolbars expanded");
            }
            return target;
        }
    }
}
