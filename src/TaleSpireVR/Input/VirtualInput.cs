using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace TaleSpireVR
{
    /// <summary>
    /// A virtual mouse + keyboard registered with Unity's Input System. TaleSpire's own input actions
    /// (bound to &lt;Mouse&gt;/leftButton, &lt;Keyboard&gt;/b, ...) and the UI EventSystem react to them exactly
    /// as they do to real hardware, so every game feature keeps its own logic.
    /// </summary>
    internal static class VirtualInput
    {
        public static Mouse Mouse { get; private set; }
        public static Keyboard Keyboard { get; private set; }

        public static Vector2 Position;
        public static bool Left, Right, Middle;
        public static Vector2 Scroll;

        private static Vector2 _lastSentPosition;
        private static readonly HashSet<Key> HeldKeys = new HashSet<Key>();
        private static readonly List<Key> TapKeys = new List<Key>();
        private static readonly List<Key> ReleaseNext = new List<Key>();

        public static void Create()
        {
            if (Mouse != null) return;
            Mouse = InputSystem.AddDevice<Mouse>("TaleSpireVR Mouse");
            Keyboard = InputSystem.AddDevice<Keyboard>("TaleSpireVR Keyboard");
            Position = new Vector2(-100, -100);
            Plugin.Log.LogInfo($"Virtual input devices created: {Mouse.name} (id {Mouse.deviceId}), {Keyboard.name} (id {Keyboard.deviceId})");

            // The OpenVR XR plugin also registers the headset/controllers with the Input System. Unity's UI module
            // treats those as "tracked pointers" that constantly take over from the mouse, so clicks get ignored.
            // We read the controllers through SteamVR Input instead, so switch those devices off.
            foreach (var d in InputSystem.devices) DisableIfXR(d);
            if (_watching) return;
            _watching = true;
            InputSystem.onDeviceChange += (d, change) =>
            {
                if (Mouse != null && (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected || change == InputDeviceChange.Enabled))
                    DisableIfXR(d);
            };
        }

        private static bool _watching;

        private static void DisableIfXR(InputDevice d)
        {
            if (d == null || !d.enabled) return;
            if (d is UnityEngine.InputSystem.XR.XRController || d is UnityEngine.InputSystem.XR.XRHMD || d is UnityEngine.InputSystem.TrackedDevice)
            {
                InputSystem.DisableDevice(d);
                Plugin.Log.LogInfo($"Disabled XR input-system device '{d.name}' (controllers are read via SteamVR Input)");
            }
        }

        /// <summary>Keep our mouse as Mouse.current so TaleSpire's MouseManager reads it.</summary>
        public static void MakeCurrent()
        {
            if (Mouse != null && Mouse.current != Mouse) Mouse.MakeCurrent();
        }

        public static void SetKey(Key key, bool held)
        {
            if (held) HeldKeys.Add(key); else HeldKeys.Remove(key);
        }

        /// <summary>Press a key (or chord) for one frame.</summary>
        public static void Tap(params Key[] keys)
        {
            TapKeys.AddRange(keys);
        }

        // Chords (e.g. Ctrl+Z) are played over three frames: modifier down, modifier+key down, release.
        // TaleSpire's shortcut bindings only fire when the modifier is pressed before the key, like a real hand.
        private static readonly Queue<Key[]> ChordFrames = new Queue<Key[]>();

        public static void Chord(Key modifier, Key key)
        {
            ChordFrames.Enqueue(new[] { modifier });
            ChordFrames.Enqueue(new[] { modifier, key });
            ChordFrames.Enqueue(new[] { modifier, key });
            ChordFrames.Enqueue(new Key[0]);
        }

        /// <summary>Queue this frame's state; Unity processes it at the start of the next frame.</summary>
        public static void Flush()
        {
            if (Mouse == null) return;
            // Added while the game window wasn't focused (before input was set to ignore focus): Unity switched them off.
            if (!Mouse.enabled) InputSystem.EnableDevice(Mouse);
            if (!Keyboard.enabled) InputSystem.EnableDevice(Keyboard);

            var ms = new MouseState
            {
                position = Position,
                delta = Position - _lastSentPosition,
                scroll = Scroll,
            };
            ms = ms.WithButton(MouseButton.Left, Left).WithButton(MouseButton.Right, Right).WithButton(MouseButton.Middle, Middle);
            InputSystem.QueueStateEvent(Mouse, ms);
            _lastSentPosition = Position;
            Scroll = Vector2.zero;

            var ks = new KeyboardState();
            foreach (var k in HeldKeys) ks.Set(k, true);
            foreach (var k in ReleaseNext) if (!HeldKeys.Contains(k)) ks.Set(k, false);
            ReleaseNext.Clear();
            foreach (var k in TapKeys) { ks.Set(k, true); ReleaseNext.Add(k); }
            TapKeys.Clear();
            if (ChordFrames.Count > 0)
                foreach (var k in ChordFrames.Dequeue()) ks.Set(k, true);
            InputSystem.QueueStateEvent(Keyboard, ks);
        }

        public static void Remove()
        {
            if (Mouse != null) InputSystem.RemoveDevice(Mouse);
            if (Keyboard != null) InputSystem.RemoveDevice(Keyboard);
            Mouse = null;
            Keyboard = null;
            HeldKeys.Clear();
            TapKeys.Clear();
            ReleaseNext.Clear();
            ChordFrames.Clear();
            Left = Right = Middle = false;
            // The real mouse and keyboard are TaleSpire's again.
            foreach (var d in InputSystem.devices)
            {
                if (d is Mouse m && m.enabled && UnityEngine.InputSystem.Mouse.current != m) { m.MakeCurrent(); break; }
            }
            foreach (var d in InputSystem.devices)
            {
                if (d is Keyboard k && k.enabled && UnityEngine.InputSystem.Keyboard.current != k) { k.MakeCurrent(); break; }
            }
        }
    }
}
