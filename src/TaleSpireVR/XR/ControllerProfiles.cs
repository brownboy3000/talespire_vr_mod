using System.Text;
using UnityEngine;
using Valve.VR;

namespace TaleSpireVR
{
    /// <summary>
    /// Which controllers are in your hands (SteamVR's controller type for each hand, checked every few seconds), and
    /// what differs between them for the mod. Buttons are not decided here: SteamVR Input maps them (the mod ships
    /// default bindings for each type, and SteamVR's own Controller Bindings screen changes them like in any game).
    /// What is decided here: whether the hands have trackpads (else the grips stand in for the trackpad gestures),
    /// whether SteamVR tracks every finger (Index) or the fingers are animated from the trigger and grip, and how a held
    /// weapon is angled to sit like the controller's handle.
    /// </summary>
    internal static class ControllerProfiles
    {
        public sealed class Profile
        {
            public string Type, Name;
            /// <summary>Has trackpads (the trackpad gestures: tap = ping, hold = turn / raise, both = stream camera / Predator Vision).</summary>
            public bool Trackpads;
            /// <summary>SteamVR tracks every finger (only the Index controllers): the hands follow the real fingers.</summary>
            public bool FingerTracking;
            /// <summary>
            /// How a held weapon leans forward from the hand (degrees): the Index's handle angle (15). Null: measured from
            /// this controller's pointer, so the barrel or blade points where the controller points.
            /// </summary>
            public float? WeaponLean;
            /// <summary>Without a hand skeleton: the wrist this far behind the pointer's tip (m), the fingers this much below it.</summary>
            public float WristBack = 0.15f, FingerDown = 0.3f;
        }

        private static readonly Profile[] Known =
        {
            new Profile { Type = "knuckles", Name = "Valve Index", Trackpads = true, FingerTracking = true, WeaponLean = 15f },
            new Profile { Type = "vive_controller", Name = "HTC Vive wand", Trackpads = true, WristBack = 0.17f, FingerDown = 0.45f },
            new Profile { Type = "holographic_controller", Name = "Windows Mixed Reality", Trackpads = true, WristBack = 0.14f, FingerDown = 0.4f },
            new Profile { Type = "hpmotioncontroller", Name = "HP Reverb G2", WristBack = 0.13f, FingerDown = 0.35f },
            new Profile { Type = "oculus_touch", Name = "Meta Touch", WristBack = 0.12f, FingerDown = 0.35f },
            new Profile { Type = "pico_controller", Name = "Pico", WristBack = 0.12f, FingerDown = 0.35f },
            new Profile { Type = "vive_cosmos_controller", Name = "HTC Vive Cosmos", WristBack = 0.13f, FingerDown = 0.35f },
            new Profile { Type = "vive_focus3_controller", Name = "HTC Vive Focus 3", WristBack = 0.12f, FingerDown = 0.35f },
        };

        /// <summary>An unknown controller: treated like the thumbstick controllers (grips for the trackpad gestures).</summary>
        private static readonly Profile Unknown = new Profile { Type = "?", Name = "unknown controller", WristBack = 0.13f, FingerDown = 0.35f };

        private static readonly Profile[] _hands = new Profile[2];
        private static float _nextCheck;
        private static readonly StringBuilder _sb = new StringBuilder(64);

        /// <summary>Debug ("controller &lt;type&gt;|off"): pretend both controllers are of this SteamVR controller type.</summary>
        public static string DebugType;

        public static Profile Of(Hand h) => DebugType != null ? Find(DebugType) : _hands[(int)h] ?? _hands[1 - (int)h] ?? Known[0];

        /// <summary>The controllers have trackpads (true until known, so an Index behaves as always).</summary>
        public static bool HasTrackpads => Of(Hand.Right).Trackpads;

        /// <summary>Every frame; looks every 3 seconds (controllers can be switched on or swapped at any time).</summary>
        public static void Update()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 3f;
            var sys = OpenVR.System;
            if (sys == null) return;
            for (int h = 0; h < 2; h++)
            {
                uint index = sys.GetTrackedDeviceIndexForControllerRole(h == 0 ? ETrackedControllerRole.LeftHand : ETrackedControllerRole.RightHand);
                if (index == OpenVR.k_unTrackedDeviceIndexInvalid) continue;
                var err = ETrackedPropertyError.TrackedProp_Success;
                _sb.Length = 0;
                sys.GetStringTrackedDeviceProperty(index, ETrackedDeviceProperty.Prop_ControllerType_String, _sb, 64, ref err);
                if (err != ETrackedPropertyError.TrackedProp_Success) continue;
                string type = _sb.ToString();
                var p = Find(type);
                if (_hands[h] == p) continue;
                _hands[h] = p;
                Plugin.Log.LogInfo($"Controller ({(h == 0 ? "left" : "right")}): {p.Name} ('{type}'): trackpads={p.Trackpads}, finger tracking={p.FingerTracking}");
            }
        }

        private static Profile Find(string type)
        {
            foreach (var p in Known) if (p.Type == type) return p;
            if (type.StartsWith("knuckles")) return Known[0];
            return Unknown;
        }
    }
}
