using Bouncyrock.Physics;
using Unity.Mathematics;
using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// TaleSpire's ping (T, then click: three animated arrows everyone sees) on a quick tap of either trackpad: the ping
    /// lands where that hand's laser points on the board (on a creature it follows the creature). A tap is a press
    /// shorter than TapSeconds while nothing is carried, and not part of the two-trackpad stream-view combo - held
    /// trackpads keep their rotate / elevate jobs.
    /// </summary>
    internal static class PingTap
    {
        private const float TapSeconds = 0.35f;
        private static readonly float[] _downTime = new float[2];
        private static readonly bool[] _spoiled = new bool[2];

        private static SteamVRInput.Button Pad(Hand h) => h == Hand.Right ? SteamVRInput.RotateModifier : SteamVRInput.ElevateModifier;

        /// <param name="onBoard">Per hand: is its laser on the board (not on a menu)?</param>
        public static void Update(VRHands hands, bool[] onBoard, bool pickFreshFor0, bool pickFreshFor1)
        {
            for (int i = 0; i < 2; i++)
            {
                var h = (Hand)i;
                var pad = Pad(h);
                var other = Pad(i == 0 ? Hand.Right : Hand.Left);
                if (pad.Down)
                {
                    _downTime[i] = Time.unscaledTime;
                    _spoiled[i] = false;
                }
                // Both trackpads at once is the stream-view combo; carrying uses the pads as modifiers.
                if (pad.Held && (other.Held || CreatureHold.IsHolding)) _spoiled[i] = true;
                if (pad.Up && !_spoiled[i] && Time.unscaledTime - _downTime[i] < TapSeconds && onBoard[i])
                    Ping(hands.Get(h), i == 0 ? pickFreshFor0 : pickFreshFor1);
            }
        }

        private static void Ping(VRHands.HandState hand, bool pickIsThisHand)
        {
            try
            {
                var rc = CameraController.Raycast;
                if (rc == null || !rc.GetRayPoint(hand.Ray, out HitInfo hit, BouncePhysicsLayers.QueryAll, 0.01f)) return;
                TempClientColorManager.TryGetColor(LocalClient.Id, out Color color);
                // Pointing at a creature (TaleSpire's picking follows the hand in control): pin the ping to it.
                if (pickIsThisHand && PixelPickingManager.TryGetPicked(out float3 pos, out _, out CreatureBoardAsset creature, out _, out _) == PixelPickingManager.PickedKind.CreatureBoardAsset
                    && creature != null)
                {
                    Transform head = creature.HookHead;
                    GestureManager.PingCreatureAtLocation(pos, hit.normal, creature.CreatureId,
                        head.InverseTransformPoint(pos), head.InverseTransformDirection(hit.normal), color);
                    Plugin.Log.LogInfo($"[ping] on creature '{creature.name}' ({hand.Hand} trackpad tap)");
                }
                else
                {
                    GestureManager.PingAtLocation(hit.point, hit.normal, color);
                    Plugin.Log.LogInfo($"[ping] at {(Vector3)hit.point} ({hand.Hand} trackpad tap)");
                }
                SteamVRInput.Haptic(hand.Hand, 0.02f, 160f, 0.4f);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[ping] failed: " + e.Message); }
        }
    }
}
