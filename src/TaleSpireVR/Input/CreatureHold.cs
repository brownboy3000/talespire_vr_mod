using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// Extra controls while a creature is held with the trigger:
    ///  - right thumbstick left/right turns it (like Alt + mouse in the flat game),
    ///  - zoom buttons (left B / A) raise / lower it (like Ctrl + mouse), via TaleSpire's own elevate mode.
    /// </summary>
    internal static class CreatureHold
    {
        public static bool IsHolding => VRManager.Active && TryGetHeld(out _);

        private static readonly System.Reflection.FieldInfo GroupPickup = HarmonyLib.AccessTools.Field(typeof(CreatureGroupMoveBoardTool), "_pickupObject");

        /// <summary>
        /// The creature in hand: the single held creature, or for a lassoed group the one you grabbed (the group keeps
        /// its formation around it, so turning it turns the whole group around that creature).
        /// </summary>
        public static bool TryGetHeld(out CreatureBoardAsset asset)
        {
            if (CreatureMoveBoardTool.TryGetHeldCreatureBoardAsset(out asset) && asset != null) return true;
            asset = BoardToolManager.CurrentTool is CreatureGroupMoveBoardTool g && GroupPickup != null ? GroupPickup.GetValue(g) as CreatureBoardAsset : null;
            return asset != null;
        }

        /// <summary>True while a zoom button is raising/lowering the held creature (holds virtual Ctrl).</summary>
        public static bool ElevatingWithButtons { get; private set; }

        /// <summary>Height fed to TaleSpire's elevate mode while the buttons are held (see SampleOnVerticalPlane patch).</summary>
        public static float ElevY;

        private static CreatureBoardAsset _held;

        public static void Update(VRRig rig)
        {
            if (!VRManager.Active || rig == null || !TryGetHeld(out var asset))
            {
                ElevatingWithButtons = false;
                _held = null;
                return;
            }
            if (_held != asset)
            {
                _held = asset;
                asset.ForceFixRotation();   // stand it up straight if it was left leaning
                Plugin.Log.LogInfo($"[hold] picked up '{asset.name}' with {BoardToolManager.CurrentTool?.GetType().Name} (group of {(LocalClient.HasLassoedCreatures ? LocalClient.LassoedCount : 1)}); TaleSpire's Alt={ControllerManager.Modifiers.BtnRotationMod.IsDown} Ctrl={ControllerManager.Modifiers.BtnVerticalMod.IsDown}");
            }
            float dt = Time.unscaledDeltaTime;

            // Turn with the right stick.
            Vector2 s = SteamVRInput.TurnHeight.Value;
            if (Mathf.Abs(s.x) > 0.2f && Mathf.Abs(s.x) > Mathf.Abs(s.y) && asset.Rotator != null)
            {
                float amount = Mathf.Sign(s.x) * Mathf.InverseLerp(0.2f, 1f, Mathf.Abs(s.x));
                // Turn only around the mini's own upright axis (its local z, like TaleSpire does). A carried mini
                // wobbles, so turning around the world's vertical would bake a lean into it that stays after the drop.
                float sign = Vector3.Dot(asset.Rotator.forward, Vector3.up) >= 0f ? 1f : -1f;
                asset.Rotator.Rotate(0f, 0f, sign * amount * VRConfig.CreatureRotateSpeed.Value * dt, Space.Self);
            }

            // Raise / lower with the zoom buttons.
            float dir = (SteamVRInput.ZoomIn.Held ? 1f : 0f) - (SteamVRInput.ZoomOut.Held ? 1f : 0f);
            if (dir != 0f)
            {
                if (!ElevatingWithButtons)
                {
                    ElevatingWithButtons = true;
                    ElevY = asset.transform.position.y;
                }
                ElevY += dir * VRConfig.CreatureElevateSpeed.Value * rig.Scale * dt;
            }
            else
            {
                ElevatingWithButtons = false;
            }

            // Move / turn / raise modes follow the controllers (not TaleSpire's Alt / Ctrl events).
            Patches.CarryModes.Sync(BoardToolManager.CurrentTool);
        }
    }
}
