using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR;
using Valve.VR;

namespace TaleSpireVR
{
    /// <summary>
    /// Starts Unity's OpenVR XR plugin (XRSDKOpenVR.dll) at runtime without the XR Management package.
    /// The engine registers the subsystem descriptors from TaleSpire_Data/UnitySubsystems at startup;
    /// we just create and start the display + input subsystems.
    /// </summary>
    internal static class XRBoot
    {
        public static bool Running { get; private set; }
        public static XRDisplaySubsystem Display { get; private set; }
        public static XRInputSubsystem Input { get; private set; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct UserDefinedSettings
        {
            public ushort stereoRenderingMode;
            public ushort initializationType;
            public ushort mirrorViewMode;
            [MarshalAs(UnmanagedType.LPStr)] public string editorAppKey;
            [MarshalAs(UnmanagedType.LPStr)] public string actionManifestPath;
            [MarshalAs(UnmanagedType.LPStr)] public string applicationName;
        }

        [DllImport("XRSDKOpenVR.dll", CharSet = CharSet.Auto)]
        private static extern void SetUserDefinedSettings(UserDefinedSettings settings);

        [DllImport("XRSDKOpenVR.dll", CharSet = CharSet.Auto)]
        private static extern EVRInitError GetInitializationResult();

        [DllImport("XRSDKOpenVR.dll", CharSet = CharSet.Auto)]
        private static extern void SetMirrorViewMode(ushort mirrorViewMode);

        public static bool Start()
        {
            if (Running) return true;
            try
            {
                var displayDescs = new List<XRDisplaySubsystemDescriptor>();
                SubsystemManager.GetSubsystemDescriptors(displayDescs);
                var inputDescs = new List<XRInputSubsystemDescriptor>();
                SubsystemManager.GetSubsystemDescriptors(inputDescs);
                Plugin.Log.LogInfo($"XR descriptors: displays=[{string.Join(", ", displayDescs.ConvertAll(d => d.id).ToArray())}] inputs=[{string.Join(", ", inputDescs.ConvertAll(d => d.id).ToArray())}]");

                var displayDesc = displayDescs.Find(d => string.Equals(d.id, "OpenVR Display", StringComparison.OrdinalIgnoreCase));
                var inputDesc = inputDescs.Find(d => string.Equals(d.id, "OpenVR Input", StringComparison.OrdinalIgnoreCase));
                if (displayDesc == null || inputDesc == null)
                {
                    Plugin.Log.LogError("OpenVR XR subsystem descriptors not found. Is TaleSpire_Data/UnitySubsystems/XRSDKOpenVR/UnitySubsystemsManifest.json installed?");
                    return false;
                }

                var mirror = VRConfig.MirrorEye.Value;
                SetUserDefinedSettings(new UserDefinedSettings
                {
                    stereoRenderingMode = 0,          // MultiPass (TaleSpire's shaders are not single-pass-instanced aware)
                    initializationType = 1,           // Scene application
                    mirrorViewMode = (ushort)Mathf.Clamp(mirror, 0, 2),
                    editorAppKey = null,
                    actionManifestPath = null,        // we set our own SteamVR Input manifest from C#
                    applicationName = "TaleSpire",
                });

                Display = displayDesc.Create();
                var init = GetInitializationResult();
                if (init != EVRInitError.None || Display == null)
                {
                    Plugin.Log.LogError($"OpenVR failed to initialize: {init}. Is SteamVR running and the headset connected?");
                    if (Display != null) { Display.Destroy(); Display = null; }
                    return false;
                }

                Input = inputDesc.Create();
                Display.Start();
                Input.Start();

                bool floorOk = Input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                Running = true;

                XRSettings.eyeTextureResolutionScale = Mathf.Clamp(VRConfig.RenderScale.Value, 0.3f, 2.5f);
                XRSettings.gameViewRenderMode = mirror == 2 ? GameViewRenderMode.RightEye : (mirror == 0 ? GameViewRenderMode.None : GameViewRenderMode.LeftEye);

                Plugin.Log.LogInfo($"OpenVR started. floorOrigin={floorOk} xrEnabled={XRSettings.enabled} device='{XRSettings.loadedDeviceName}' eye={XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight} renderScale={XRSettings.eyeTextureResolutionScale} refresh={XRDevice.refreshRate}");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Exception while starting OpenVR: " + e);
                return false;
            }
        }

        /// <summary>
        /// Stop drawing to the headset (TaleSpire draws to its window as usual) but keep OpenVR up, so Resume is quick.
        /// </summary>
        public static void Pause()
        {
            if (!Running) return;
            try
            {
                Input?.Stop();
                Display?.Stop();
            }
            catch (Exception e) { Plugin.Log.LogWarning("Error pausing OpenVR: " + e.Message); }
            Running = false;
            Plugin.Log.LogInfo("OpenVR paused (headset display stopped)");
        }

        public static bool Resume()
        {
            if (Running) return true;
            if (Display == null) return Start();
            try
            {
                Display.Start();
                Input?.Start();
                Running = true;
                Plugin.Log.LogInfo("OpenVR resumed");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("OpenVR did not resume: " + e.Message);
                return false;
            }
        }

        public static void Stop()
        {
            if (Display == null && Input == null) return;
            try
            {
                Input?.Stop();
                Display?.Stop();
                Input?.Destroy();
                Display?.Destroy();
            }
            catch (Exception e) { Plugin.Log.LogWarning("Error stopping OpenVR: " + e.Message); }
            Input = null;
            Display = null;
            Running = false;
        }

        /// <summary>Poll SteamVR system events (quit requests etc.).</summary>
        public static void PollEvents()
        {
            if (QuitRequested && Time.unscaledTime - QuitRequestTime > 20f)
            {
                Plugin.Log.LogWarning("TaleSpire did not finish quitting in 20s; forcing quit.");
                QuitRequestTime = float.MaxValue;
                AppStateManager.ForceQuitNoUiNoSync();
            }
            if (!Running) return;
            var system = OpenVR.System;
            if (system == null) return;
            var ev = new VREvent_t();
            uint size = (uint)Marshal.SizeOf(typeof(VREvent_t));
            for (int i = 0; i < 64 && system.PollNextEvent(ref ev, size); i++)
            {
                var type = (EVREventType)ev.eventType;
                if (type == EVREventType.VREvent_Quit && !QuitRequested)
                {
                    Plugin.Log.LogInfo("SteamVR asked the game to quit; quitting TaleSpire (board sync is kept).");
                    QuitRequested = true;
                    QuitRequestTime = Time.unscaledTime;
                    system.AcknowledgeQuit_Exiting();
                    // TaleSpire asks "Do you want to quit?"; Patches.QuitPromptPatch answers Yes automatically.
                    try { AppStateManager.Quit(); } catch { Application.Quit(); }
                }
            }
        }

        public static bool QuitRequested { get; private set; }
        private static float QuitRequestTime;
    }
}
