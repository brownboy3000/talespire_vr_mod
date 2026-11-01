using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TaleSpireVR.Patches
{
    /// <summary>
    /// The right-hand floor slider pins (water level, view / cut level, build height, fog) move by the pointer's
    /// movement, and a laser is far twitchier than a mouse. Scale the drag (FloorSliderSpeed) so small level changes
    /// are easy; the pointer data is restored afterwards so nothing else sees the change.
    /// </summary>
    [HarmonyPatch]
    internal static class FloorPinDragPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(UI_FloorPin), nameof(UI_FloorPin.OnDrag));
            yield return AccessTools.Method(typeof(UI_FloorPinAlt), nameof(UI_FloorPinAlt.OnDrag));
        }

        private static void Prefix(PointerEventData eventData, out Vector2 __state)
        {
            __state = eventData.delta;
            if (VRManager.Active) eventData.delta *= Mathf.Clamp(VRConfig.FloorSliderSpeed.Value, 0.05f, 2f);
        }

        private static void Postfix(PointerEventData eventData, Vector2 __state)
        {
            eventData.delta = __state;
        }
    }
}
