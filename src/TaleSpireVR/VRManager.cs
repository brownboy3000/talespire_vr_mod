using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TaleSpireVR
{
    /// <summary>
    /// Root behaviour of VR mode: boots OpenVR, pumps input, and owns the camera rig, hands, UI panels and pointer.
    /// Added by ModeSwitch when VR mode starts; switched off (SwitchOff) and on again (SwitchOn) by the main menu's VR
    /// button. It works for Game Masters and players alike (Roles says what each may do); everything it changes in
    /// the world goes through TaleSpire's own actions, so others need no mod.
    /// </summary>
    [DefaultExecutionOrder(-30000)]
    internal class VRManager : MonoBehaviour
    {
        public static VRManager Instance { get; private set; }
        public static bool Active => XRBoot.Running;
        /// <summary>Switched off by the main menu's VR button (normal mode).</summary>
        public static bool Suspended { get; private set; }
        /// <summary>Set once VR has started (or failed to): ModeSwitch reads it.</summary>
        public bool StartFinished { get; private set; }
        public bool StartFailed { get; private set; }

        public VRRig Rig { get; private set; }
        public VRHands Hands { get; private set; }
        public UIPanels Panels { get; private set; }
        public VRPointer Pointer { get; private set; }
        public PickCamera Picker { get; private set; }

        private float _nextDiag;
        private int _diagCount;
        private bool _flatRunInBackground;
        private UnityEngine.InputSystem.InputSettings.BackgroundBehavior _flatBackground;

        private void Awake()
        {
            Instance = this;
            SceneManager.sceneLoaded += (s, m) =>
            {
                Plugin.Log.LogInfo($"Scene loaded: '{s.name}' (mode {m}) frame {Time.frameCount}");
                _nextDiag = Time.unscaledTime + 2f;
            };
        }

        private IEnumerator Start()
        {
            // Give Unity a couple of frames so graphics and the first scene are fully up.
            yield return null;
            yield return null;
            if (!XRBoot.Start())
            {
                Plugin.Log.LogError("VR did not start (is SteamVR running?); TaleSpire continues as normal.");
                StartFailed = StartFinished = true;
                yield break;
            }
            SteamVRInput.Init();
            VirtualInput.Create();
            // In VR the desktop window is rarely focused; keep the game and its input running regardless.
            _flatRunInBackground = Application.runInBackground;
            _flatBackground = UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior;
            RunInBackground(true);

            Rig = gameObject.AddComponent<VRRig>();
            Hands = gameObject.AddComponent<VRHands>();
            gameObject.AddComponent<UICapture>();
            Panels = gameObject.AddComponent<UIPanels>();
            Picker = gameObject.AddComponent<PickCamera>();
            Pointer = gameObject.AddComponent<VRPointer>();
            gameObject.AddComponent<VRGraphics>();
            if (VRConfig.StreamViewEnabled.Value) gameObject.AddComponent<StreamView>();   // (TaleSpireVR.StreamCamera.ini)
            gameObject.AddComponent<RadialExtraButton>();
            gameObject.AddComponent<FirstPersonMode>();
            gameObject.AddComponent<PredatorVision>();
            if (VRConfig.Diagnostics.Value) gameObject.AddComponent<FrameProfiler>();
            gameObject.AddComponent<TurnBanner>();
            gameObject.AddComponent<CutsceneScreen>();
            _nextDiag = Time.unscaledTime + 3f;
            StartFinished = true;
        }

        private void RunInBackground(bool vr)
        {
            Application.runInBackground = vr || _flatRunInBackground;
            UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = vr ? UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus : _flatBackground;
        }

        private void Update()
        {
            if (Suspended) return;
            if (!Active) return;
            FrameProfiler.MarkFrameStart();
            XRBoot.PollEvents();
            ControllerProfiles.Update();
            SteamVRInput.Update();
            // Before TaleSpire's scripts read Mouse.current this frame.
            VirtualInput.MakeCurrent();
            if (Hands != null)
            {
                Hands.UpdatePoses();
                VRPointer.EarlyUpdate(Hands);
                if (Picker != null) Picker.UpdatePose();
            }
            MainMenuText.Update();

            if (VRConfig.Diagnostics.Value)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.f10Key.wasPressedThisFrame)
                    Diagnostics.Dump("F10");
                if (_nextDiag > 0 && Time.unscaledTime > _nextDiag)
                {
                    _diagCount++;
                    Diagnostics.Dump("auto#" + _diagCount);
                    _nextDiag = _diagCount < 8 ? Time.unscaledTime + 20f : -1f;
                }
            }
        }

        private void LateUpdate()
        {
            if (Active) DebugSnapshot.MaybeCapture();
        }

        // ---- Switching VR off and on (the main menu's VR button) ----------------------------------------------------

        private readonly List<GameObject> _hiddenOwn = new List<GameObject>();
        private Behaviour[] Parts => new Behaviour[]
        {
            Rig, Hands, GetComponent<UICapture>(), Panels, Picker, Pointer, GetComponent<VRGraphics>(), GetComponent<StreamView>(),
            GetComponent<RadialExtraButton>(), GetComponent<FirstPersonMode>(), GetComponent<PredatorVision>(), GetComponent<FrameProfiler>(),
            GetComponent<TurnBanner>(), GetComponent<CutsceneScreen>(),
        };

        /// <summary>Normal mode: everything of ours off, everything we changed in TaleSpire put back, the headset stopped.</summary>
        public void SwitchOff()
        {
            if (Suspended) return;
            Plugin.Log.LogInfo("Normal mode: the VR mod switches off; TaleSpire runs on the monitor as usual");
            Suspended = true;
            Safe("first person", () => GetComponent<FirstPersonMode>()?.Suspend());
            Safe("radial buttons", RadialExtraButton.Suspend);
            Safe("stream view", () => GetComponent<StreamView>()?.Suspend());
            Safe("ui capture", () => GetComponent<UICapture>()?.Suspend());
            Safe("graphics", () => GetComponent<VRGraphics>()?.Suspend());
            Safe("rig", () => Rig?.Suspend());
            Safe("labels", Patches.InGameTextPatch.Restore);
            Safe("viewport", Patches.GameViewportPatch.Restore);
            Safe("main menu text", MainMenuText.Restore);
            foreach (var b in Parts) if (b != null) b.enabled = false;
            Safe("own objects", HideOwn);
            Safe("input", () =>
            {
                VirtualInput.Remove();
                RunInBackground(false);
            });
            XRBoot.Pause();
            Safe("ui scale", UICapture.SendUiScaleChanged);
        }

        /// <summary>VR mode again: the mod as it was. False if the headset could not start (SteamVR not running).</summary>
        public bool SwitchOn()
        {
            if (!Suspended) return true;
            if (!XRBoot.Resume()) return false;
            Plugin.Log.LogInfo("VR mode: the VR mod is back on");
            Suspended = false;
            Safe("input", () =>
            {
                VirtualInput.Create();
                RunInBackground(true);
            });
            Safe("own objects", ShowOwn);
            foreach (var b in Parts) if (b != null) b.enabled = true;
            Safe("ui capture", () => GetComponent<UICapture>()?.Resume());
            Safe("stream view", () => GetComponent<StreamView>()?.Resume());
            return true;
        }

        /// <summary>Our own scene objects (panels, lasers, labels, hands...), all named TaleSpireVR_..., hidden.</summary>
        private void HideOwn()
        {
            _hiddenOwn.Clear();
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || go == gameObject || go.transform.parent != null || !go.activeSelf || !go.scene.IsValid()
                    || !go.name.StartsWith("TaleSpireVR_")) continue;
                go.SetActive(false);
                _hiddenOwn.Add(go);
            }
            Plugin.Log.LogInfo($"Hid {_hiddenOwn.Count} of our objects");
        }

        private void ShowOwn()
        {
            foreach (var go in _hiddenOwn) if (go != null) go.SetActive(true);
            _hiddenOwn.Clear();
        }

        private static void Safe(string what, System.Action a)
        {
            try { a(); }
            catch (System.Exception e) { Plugin.Log.LogError($"Switching the VR mod {(Suspended ? "off" : "on")}: {what} failed: {e}"); }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnApplicationQuit()
        {
            VirtualInput.Remove();
            XRBoot.Stop();
        }
    }
}
