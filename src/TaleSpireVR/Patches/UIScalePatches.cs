using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TaleSpireVR.Patches
{
    /// <summary>Screen size as TaleSpire's UI code should see it in VR (the supersampled UI texture size).</summary>
    internal static class VRScreen
    {
        public static int Width => VRManager.Active && UICapture.Ready ? UICapture.Width : Screen.width;
        public static int Height => VRManager.Active && UICapture.Ready ? UICapture.Height : Screen.height;
    }

    /// <summary>TaleSpire's menus are rendered at supersampled resolution in VR; scale the UI by the same factor.</summary>
    [HarmonyPatch(typeof(TaleSpire.Settings.SettingsManager), nameof(TaleSpire.Settings.SettingsManager.UIScale), MethodType.Getter)]
    internal static class UiScalePatch
    {
        private static void Postfix(ref float __result)
        {
            if (VRManager.Active && UICapture.Ready) __result *= UICapture.UiScaleMultiplier * Mathf.Clamp(VRConfig.MenuScale.Value, 0.5f, 2f);
        }
    }

    /// <summary>
    /// The handful of TaleSpire UI scripts that read Screen.width/height (menu centring, tooltip/bubble placement,
    /// cursor text) get the UI texture size instead, so they agree with the supersampled canvases.
    /// </summary>
    [HarmonyPatch]
    internal static class ScreenSizePatch
    {
        private static readonly string[] Targets =
        {
            "MapMenuManager:Awake",
            "MenuBoardTool:CallUpdate",
            "MenuInternalTool:CallUpdate",
            "MouseManager:SetWorldPosition",
            "MouseManager:Update",
            "MouseManager:UpdateText",
            "RectTransformTweener:TweenPosition",
            "TextBubbleManager:MoveBubbleInsideFrame",
            "TextBubbleManager:RendererToScreenSpaceRect",
            "TextBubbleTail:Update",
            "UI_DescriptionBox:GetScreenNormalizedPosition",
            "UI_GamePopupFrame:GetScreenNormalizedPosition",
        };

        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var t in Targets)
            {
                var parts = t.Split(':');
                var type = AccessTools.TypeByName(parts[0]);
                if (type == null) continue;
                foreach (var m in AccessTools.GetDeclaredMethods(type))
                    if (m.Name == parts[1] && !m.IsAbstract) yield return m;
            }
        }

        private static readonly MethodInfo ScreenW = AccessTools.PropertyGetter(typeof(Screen), nameof(Screen.width));
        private static readonly MethodInfo ScreenH = AccessTools.PropertyGetter(typeof(Screen), nameof(Screen.height));
        private static readonly MethodInfo VrW = AccessTools.PropertyGetter(typeof(VRScreen), nameof(VRScreen.Width));
        private static readonly MethodInfo VrH = AccessTools.PropertyGetter(typeof(VRScreen), nameof(VRScreen.Height));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var ins in instructions)
            {
                if (ins.Calls(ScreenW)) yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call, VrW).MoveLabelsFrom(ins).MoveBlocksFrom(ins);
                else if (ins.Calls(ScreenH)) yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call, VrH).MoveLabelsFrom(ins).MoveBlocksFrom(ins);
                else yield return ins;
            }
        }
    }
}
