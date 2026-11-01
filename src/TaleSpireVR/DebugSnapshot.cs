using System;
using System.IO;
using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// Developer aid: when VRMod/_debug/snap.request exists, saves the headset's left eye and the captured UI
    /// as PNGs (game content only) and a short status text, then deletes the request file.
    /// </summary>
    internal static class DebugSnapshot
    {
        private static string _dir;
        private static float _nextCheck;

        internal static string DebugDir => Dir;

        private static string Dir
        {
            get
            {
                if (_dir == null)
                {
                    // BepInEx/plugins/TaleSpireVR -> game root -> VRMod/_debug
                    var gameRoot = Path.GetFullPath(Path.Combine(Plugin.PluginDir, "..", "..", ".."));
                    _dir = Path.Combine(Path.Combine(gameRoot, "VRMod"), "_debug");
                }
                return _dir;
            }
        }

        /// <summary>Call once per frame (late).</summary>
        public static void MaybeCapture()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.5f;
            try
            {
                string req = Path.Combine(Dir, "snap.request");
                string cropReq = Path.Combine(Dir, "crop.request");
                if (File.Exists(cropReq))
                {
                    File.Delete(cropReq);
                    var d = XRBoot.Display;
                    var e = d != null && d.GetRenderPassCount() > 0 ? d.GetRenderTextureForRenderPass(0) : null;
                    if (e != null) SaveEyeCrop(e, Path.Combine(Dir, "eyecrop.png"), 1400, 1000);
                }
                if (!File.Exists(req)) return;
                File.Delete(req);
                Directory.CreateDirectory(Dir);

                var display = XRBoot.Display;
                if (display != null && display.GetRenderPassCount() > 0)
                {
                    var eye = display.GetRenderTextureForRenderPass(0);
                    if (eye != null) Save(eye, Path.Combine(Dir, "eye.png"), 1280, SystemInfo.graphicsUVStartsAtTop);
                }
                if (UICapture.Texture != null) Save(UICapture.Texture, Path.Combine(Dir, "ui.png"), 1280, false);
                if (StreamView.Instance != null) StreamView.Instance.RequestSnapshot();   // stream.png, as the window shows it
                File.WriteAllText(Path.Combine(Dir, "status.txt"), Status());
                Diagnostics.Dump("snapshot");
                Plugin.Log.LogInfo("Debug snapshot written to " + Dir);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Snapshot failed: " + e.Message);
            }
        }

        private static System.Collections.IEnumerator FlatSnap()
        {
            yield return new WaitForEndOfFrame();
            try
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                Directory.CreateDirectory(Dir);
                File.WriteAllBytes(Path.Combine(Dir, "flat.png"), tex.EncodeToPNG());
                Plugin.Log.LogInfo($"[flatsnap] {tex.width}x{tex.height} written");
                UnityEngine.Object.Destroy(tex);
            }
            catch (Exception e) { Plugin.Log.LogWarning("[flatsnap] failed: " + e.Message); }
        }

        /// <summary>SteamVR compositor timing averaged over the last 45 frames: app GPU time vs. frame interval, dropped/reprojected frames.</summary>
        private static string FrameTiming()
        {
            try
            {
                var comp = Valve.VR.OpenVR.Compositor;
                if (comp == null) return "gpuMs=-";
                var t = new Valve.VR.Compositor_FrameTiming { m_nSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Valve.VR.Compositor_FrameTiming)) };
                float gpu = 0f, interval = 0f, gpuMax = 0f;
                int n = 0, reprojected = 0;
                uint dropped = 0;
                for (uint i = 0; i < 45; i++)
                {
                    if (!comp.GetFrameTiming(ref t, i)) break;
                    gpu += t.m_flTotalRenderGpuMs;
                    gpuMax = Mathf.Max(gpuMax, t.m_flTotalRenderGpuMs);
                    interval += t.m_flClientFrameIntervalMs;
                    dropped += t.m_nNumDroppedFrames;
                    if ((t.m_nReprojectionFlags & 0x3) != 0) reprojected++;   // ReprojectionReason_Cpu | _Gpu (missed frame)
                    n++;
                }
                if (n == 0) return "gpuMs=?";
                return $"gpuMs={gpu / n:F1} (max {gpuMax:F1}) frameIntervalMs={interval / n:F1} reprojected={reprojected}/{n} dropped={dropped}";
            }
            catch (Exception e) { return "gpuMs=err " + e.Message; }
        }

        private static string FieldText()
        {
            if (!VRKeyboard.TryGetField(out var tmp, out var legacy)) return "-";
            return tmp != null ? $"'{tmp.text}'({Diagnostics.PathOf(tmp.transform)})" : $"'{legacy.text}'";
        }

        public static string Status()
        {
            var mgr = VRManager.Instance;
            var rig = mgr != null ? mgr.Rig : null;
            return $"frame={Time.frameCount} time={DateTime.Now:HH:mm:ss} fps={1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f):F1} hmdRefresh={UnityEngine.XR.XRDevice.refreshRate}\n" +
                   $"{FrameTiming()} far={(rig != null && rig.GameCamera != null ? rig.GameCamera.farClipPlane : 0f):F0} shadowDist={QualitySettings.shadowDistance:F0} cascades={QualitySettings.shadowCascades} lightShadows={VRGraphics.ShadowedLights}\n" +
                   FrameProfiler.Status() + "\n" +
                   $"onBoard={(rig != null && rig.OnBoard)} scale={(rig != null ? rig.Scale : 0f)} head={(rig != null && rig.Head != null ? rig.Head.position.ToString() : "-")}\n" +
                   $"pointerTarget={VRPointer.Current} worldMode={VRPointer.WorldMode} ray={VRPointer.WorldRay} vmouse={VirtualInput.Position} L={VirtualInput.Left} R={VirtualInput.Right}\n" +
                   $"triggers L(held={SteamVRInput.TriggerLeft.Held} active={SteamVRInput.TriggerLeft.Active} debug={SteamVRInput.TriggerLeft.DebugHeld}) R(held={SteamVRInput.TriggerRight.Held} active={SteamVRInput.TriggerRight.Active} debug={SteamVRInput.TriggerRight.DebugHeld})\n" +
                   $"mainPanel={(mgr != null && mgr.Panels != null && mgr.Panels.Main.Visible)} radial={(mgr != null && mgr.Panels != null && mgr.Panels.Radial.Visible)} keyboard={(mgr != null && mgr.Panels != null && mgr.Panels.Keyboard.Visible)} dice={(mgr != null && mgr.Panels != null && mgr.Panels.Dice.Visible)} (forearm wanted={DiceForearm.Wanted} palm={DiceForearm.PalmDot:F2} gaze={DiceForearm.GazeDot:F2}) banner={(mgr != null && mgr.Panels != null && mgr.Panels.Banner.Visible)} field={FieldText()}\n" +
                   $"hands L={(mgr != null && mgr.Hands != null && mgr.Hands.Left.Valid)} R={(mgr != null && mgr.Hands != null && mgr.Hands.Right.Valid)} active={(mgr != null && mgr.Hands != null ? mgr.Hands.ActiveHand.ToString() : "-")} {VRPointer.HandSummary} radialOpen={(SingletonBehaviour<MapMenuManager>.HasInstance && MapMenuManager.IsOpen)} tool={BoardToolManager.CurrentTool?.GetType().Name}\n" +
                   $"Mouse.current={(UnityEngine.InputSystem.Mouse.current != null ? UnityEngine.InputSystem.Mouse.current.name : "none")} focus={Application.isFocused}\n" +
                   (StreamView.Instance != null ? StreamView.Instance.Status() + "\n" : "") +
                   (FirstPersonMode.Instance != null ? FirstPersonMode.Instance.Status() + "\n" : "") +
                   RadialExtraButton.Status() + " " + PredatorVision.Status() + "\n";
        }

        /// <summary>
        /// Debug command channel: VRMod/_debug/cmd.txt with lines like "click 960 120" (UI pixels, origin bottom-left)
        /// or "rclick x y". Lets the developer press menu buttons without a headset on.
        /// </summary>
        private static SteamVRInput.Button _tapButton;
        private static int _tapFrames, _secondTapIn, _secondTapLength;
        private static int _sweepFrames;
        private static float _sweepYaw, _sweepPitch;

        public static void PollCommands()
        {
            // "toss": swing the right debug laser (degrees per frame) while its trigger is held, as when throwing dice.
            if (_sweepFrames > 0 && VRHands.DebugHandRays[1].HasValue)
            {
                _sweepFrames--;
                var r = VRHands.DebugHandRays[1].Value;
                Vector3 dir = Vector3.RotateTowards(r.direction, _sweepPitch >= 0f ? Vector3.up : Vector3.down, Mathf.Abs(_sweepPitch) * Mathf.Deg2Rad, 0f);
                VRHands.DebugHandRays[1] = new Ray(r.origin, Quaternion.AngleAxis(_sweepYaw, Vector3.up) * dir);
            }
            if (_tapFrames > 0 && --_tapFrames == 0 && _tapButton != null) _tapButton.DebugHeld = false;
            // "btntap2": the second tap of a quick double press.
            if (_secondTapIn > 0 && --_secondTapIn == 0 && _tapButton != null) { _tapButton.DebugHeld = true; _tapFrames = _secondTapLength; }
            if (_pendingFrames > 0)
            {
                _pendingFrames--;
                VirtualInput.Position = _cmdPos;
                if (_cmdButton == 0) VirtualInput.Left = _pendingFrames > 2 && _pendingFrames < 8;
                else VirtualInput.Right = _pendingFrames > 2 && _pendingFrames < 8;
                LogPointerState();
                return;
            }
            if (Time.unscaledTime < _nextCmdCheck) return;
            _nextCmdCheck = Time.unscaledTime + 0.5f;
            try
            {
                string f = Path.Combine(Dir, "cmd.txt");
                if (!File.Exists(f)) return;
                var parts = File.ReadAllText(f).Trim().Split(' ');
                File.Delete(f);
                if (parts.Length >= 2 && parts[0] == "stream")
                {
                    // stream hold [pitch] | release | toggle | open | close | snap | far <units>
                    var sv = StreamView.Instance;
                    if (sv == null) return;
                    if (parts.Length == 3 && parts[1] == "far") { sv.DebugSetFar(float.Parse(parts[2])); return; }
                    if (parts.Length == 3) StreamView.DebugPitch = float.Parse(parts[2]);
                    switch (parts[1])
                    {
                        case "hold": sv.Hold(); break;
                        case "release": sv.Release("debug"); break;
                        case "toggle": sv.Toggle(); break;
                        case "open": sv.SetWindow(true); break;
                        case "close": sv.SetWindow(false); break;
                        case "snap": sv.RequestSnapshot(); break;
                        case "markersnap": sv.DebugMarkerSnap(); break;
                    }
                    return;
                }
                if (parts.Length >= 2 && parts[0] == "fp")
                {
                    // fp enter [n] [home] | exit [home] | jump | crouch | ruler | radial | handview <curl>|off | aimextra left|right
                    var fpm = FirstPersonMode.Instance;
                    if (fpm == null) return;
                    switch (parts[1])
                    {
                        case "enter": fpm.DebugEnterNearest(parts.Length > 2 && int.TryParse(parts[2], out int n) ? n : 0, Array.IndexOf(parts, "home") > 0); break;
                        case "entername":
                            // fp entername <name> [home] : become the creature with that name
                            foreach (var c in UnityEngine.Object.FindObjectsOfType<CreatureBoardAsset>())
                                if (c.Name == parts[2]) { fpm.Enter(c, Array.IndexOf(parts, "home") > 0); break; }
                            break;
                        case "exit": fpm.Exit(Array.IndexOf(parts, "home") > 0); break;
                        case "jump": fpm.Jump(); break;
                        case "crouch": fpm.ToggleCrouch(); break;
                        case "ruler": fpm.ToggleRuler(); break;
                        case "radial": RadialExtraButton.OpenOwn(); break;
                        case "lift": FirstPersonMode.DebugLift = float.Parse(parts[2]); break;
                        case "trace": FirstPersonMode.DebugTrace = int.Parse(parts[2]); break;
                        case "facetest": fpm.DebugFaceTest(); break;
                        case "jumph": FirstPersonMode.DebugJump = parts[2] == "off" ? (float?)null : float.Parse(parts[2]); break;
                        case "fly": FirstPersonMode.DebugFlying = parts[2] == "off" ? (bool?)null : parts[2] != "0"; break;
                        case "menu": fpm.OpenCreatureMenu(); break;
                        case "step": FirstPersonMode.DebugStep = parts[2] == "off" ? (float?)null : float.Parse(parts[2]); break;
                        case "blink": FPBlink.DebugClosure = parts[2] == "off" ? (float?)null : float.Parse(parts[2]); break;
                        case "handpose":
                            // fp handpose <yaw> <roll> [distance m] | off : the right hand alone in front of the face, turned
                            if (parts[2] == "off") { FPHands.DebugTurn = null; FPHands.DebugPitch = 0f; break; }
                            FPHands.DebugTurn = new Vector2(float.Parse(parts[2]), float.Parse(parts[3]));
                            if (!FPHands.DebugCurl.HasValue) FPHands.DebugCurl = 0f;
                            if (parts.Length > 4) FPHands.DebugDistance = float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "handview":
                            FirstPersonMode.DebugHandView(parts.Length > 2 && parts[2] != "off" ? float.Parse(parts[2]) : (float?)null);
                            if (parts.Length > 3) FPHands.DebugDistance = float.Parse(parts[3]);
                            break;
                        case "aimextra":
                        {
                            // Point a hand's debug laser at the extra radial button.
                            var rig = VRManager.Instance.Rig;
                            int h = parts.Length > 2 && parts[2] == "left" ? 0 : 1;
                            Vector3 target = VRManager.Instance.Panels.Radial.WorldPointForPixel(RadialExtraButton.DebugCenter);
                            Vector3 origin = rig.Head.position + rig.Head.right * (h == 0 ? -0.2f : 0.2f) * rig.Scale - rig.Head.up * 0.3f * rig.Scale;
                            VRHands.DebugHandRays[h] = new Ray(origin, (target - origin).normalized);
                            Plugin.Log.LogInfo($"[fp] aiming {(h == 0 ? "left" : "right")} at the extra button {RadialExtraButton.DebugCenter} -> {target}");
                            break;
                        }
                    }
                    return;
                }
                if (parts.Length >= 1 && parts[0] == "fx")
                {
                    StatusFxDebug.Command(parts);
                    return;
                }
                if (parts.Length == 2 && parts[0] == "clientmode")
                {
                    // clientmode gm|player : switch this GM between Game Master and Player mode, as TaleSpire's own toggle does
                    BoardSessionManager.PushLocalClientMode(parts[1] == "gm" ? ClientMode.GameMaster : ClientMode.Player);
                    Plugin.Log.LogInfo($"[clientmode] now GM={LocalClient.IsInGmMode}");
                    return;
                }
                if (parts.Length == 1 && parts[0] == "vrstate")
                {
                    // The mod on / off (Player mode) and what TaleSpire's window has: camera, menus, input, UI scale.
                    var cam = Camera.main;
                    int overlay = 0, onUiCam = 0;
                    foreach (var cv in UnityEngine.Object.FindObjectsOfType<Canvas>())
                    {
                        if (!cv.isRootCanvas) continue;
                        if (cv.renderMode == RenderMode.ScreenSpaceOverlay) overlay++;
                        else if (cv.worldCamera == UICapture.UICamera) onUiCam++;
                    }
                    Plugin.Log.LogInfo($"[vrstate] suspended={VRManager.Suspended} active={VRManager.Active} gm={LocalClient.IsInGmMode} inCampaign={CampaignSessionManager.InCampaign} inBoard={BoardSessionManager.InBoard} boardScene={Bounce.Singletons.SimpleSingletonBehaviour<CameraController>.HasInstance} " +
                        $"camera='{(cam != null ? cam.name : "-")}' parent='{(cam != null && cam.transform.parent != null ? cam.transform.parent.name : "-")}' near={(cam != null ? cam.nearClipPlane : 0f):F2} far={(cam != null ? cam.farClipPlane : 0f):F0} " +
                        $"canvases overlay={overlay} vr={onUiCam} mouse='{(UnityEngine.InputSystem.Mouse.current != null ? UnityEngine.InputSystem.Mouse.current.name : "-")}' uiScale={TaleSpire.Settings.SettingsManager.UIScale:F2} shadowDist={QualitySettings.shadowDistance:F0} runInBackground={Application.runInBackground} " +
                        $"vmouse={(VirtualInput.Mouse != null ? $"enabled={VirtualInput.Mouse.enabled} added={VirtualInput.Mouse.added} pos={VirtualInput.Mouse.position.ReadValue()}" : "-")} bg={UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior} update={UnityEngine.InputSystem.InputSystem.settings.updateMode} focused={Application.isFocused}");
                    return;
                }
                if (parts.Length == 1 && parts[0] == "flatsnap")
                {
                    // The game window's own picture (what TaleSpire draws on the monitor), as flat.png.
                    ModeSwitch.Instance.StartCoroutine(FlatSnap());
                    return;
                }
                if (parts.Length >= 2 && parts[0] == "wpn")
                {
                    // wpn equip <id>|none (the creature you are, or the n-th nearest unique) | add <id> | remove <id> | tune <mesh> <what> <v> | firetest
                    var c = FirstPersonMode.Creature;
                    switch (parts[1])
                    {
                        case "equip":
                            if (c == null) { Plugin.Log.LogInfo("[wpn] not in first person"); return; }
                            WeaponLoadouts.Equip(c, parts[2] == "none" ? null : parts[2]);
                            FirstPersonMode.Instance.WeaponChanged(c);
                            break;
                        case "add":
                        case "remove":
                            if (c == null) { Plugin.Log.LogInfo("[wpn] not in first person"); return; }
                            WeaponLoadouts.SetAdded(c, parts[2], parts[1] == "add");
                            FirstPersonMode.Instance.WeaponChanged(c);
                            break;
                        case "tune":
                        {
                            var vals = new float[parts.Length - 4];
                            for (int i = 0; i < vals.Length; i++) vals[i] = float.Parse(parts[4 + i], System.Globalization.CultureInfo.InvariantCulture);
                            FPWeapons.Tune(parts[2], parts[3], vals);
                        }
                            break;
                        case "firetest":
                            FPWeapons.FireTest();
                            break;
                        case "icetest":
                            FPWeapons.FireTest(true);
                            break;
                        case "aimat":
                        case "turnto":
                            FPWeapons.AimAt(string.Join(" ", parts, 2, parts.Length - 2), parts[1] == "turnto");
                            break;
                        case "strike":
                            FPWeapons.DebugStrike(string.Join(" ", parts, 2, parts.Length - 2));
                            break;
                        case "shoot":
                            FPWeapons.DebugShoot(string.Join(" ", parts, 2, parts.Length - 2));
                            break;
                        case "skip":
                            FPWeapons.DebugNoSkip = parts[2] == "0";
                            break;
                        case "swordattack":   // (saved in the config file, like changing SwordAttack there)
                            VRConfig.WeaponMeleeAttack.Value = parts[2];
                            break;
                        case "slow":
                            FPWeapons.DebugSlow = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                            break;
                    }
                    return;
                }
                if (parts.Length >= 1 && parts[0] == "findtext") { Inspect.FindText(parts); return; }
                if (parts.Length >= 1 && parts[0] == "audio") { Inspect.Audio(parts); return; }
                if (parts.Length >= 1 && parts[0] == "audiodata") { Inspect.AudioData(parts); return; }
                if (parts.Length >= 2 && parts[0] == "playsound") { Inspect.PlaySound(parts); return; }
                if (parts.Length >= 1 && parts[0] == "prefabdump") { Inspect.PrefabDump(parts); return; }
                if (parts.Length >= 1 && parts[0] == "diceui")
                {
                    DiceUi.LogLayout();
                    return;
                }
                if ((parts.Length == 4 || parts.Length == 6) && parts[0] == "creaturerot")
                {
                    // creaturerot <x y z> [axis degrees] : log the facing of the creature within 0.25 of x y z (its rotator's
                    // local euler angles); with an axis (x|y|z) and degrees, set that angle back with a normal drop where it stands
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    var at = new Vector3(float.Parse(parts[1], inv), float.Parse(parts[2], inv), float.Parse(parts[3], inv));
                    CreatureBoardAsset found = null;
                    foreach (var c in UnityEngine.Object.FindObjectsOfType<CreatureBoardAsset>())
                        if (Vector3.Distance(c.transform.position, at) < 0.25f) found = c;
                    if (found == null) { Plugin.Log.LogInfo("[creaturerot] no creature there"); return; }
                    Vector3 e = found.Rotator.localRotation.eulerAngles;
                    Plugin.Log.LogInfo($"[creaturerot] '{found.Name}' at {found.transform.position.ToString("F4")} rotator euler {e.ToString("F3")}");
                    if (parts.Length == 6)
                    {
                        float deg = float.Parse(parts[5], inv);
                        if (parts[4] == "x") e.x = deg; else if (parts[4] == "y") e.y = deg; else e.z = deg;
                        CreaturePresenter.TeleportCreature(found, found.transform.position, Quaternion.Euler(e));
                        Plugin.Log.LogInfo($"[creaturerot] '{found.Name}' turned to euler {found.Rotator.localRotation.eulerAngles.ToString("F3")}");
                    }
                    return;
                }
                if (parts.Length == 7 && parts[0] == "creaturemove")
                {
                    // creaturemove <from x y z> <to x y z> : put back a creature moved by mistake (a normal networked drop
                    // there, rotation kept). Only a creature within 0.25 of "from" is moved.
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    var from = new Vector3(float.Parse(parts[1], inv), float.Parse(parts[2], inv), float.Parse(parts[3], inv));
                    var to = new Vector3(float.Parse(parts[4], inv), float.Parse(parts[5], inv), float.Parse(parts[6], inv));
                    CreatureBoardAsset best = null;
                    float bestD = 0.25f;
                    foreach (var c in UnityEngine.Object.FindObjectsOfType<CreatureBoardAsset>())
                    {
                        float dist = Vector3.Distance(c.transform.position, from);
                        if (dist < 1.5f) Plugin.Log.LogInfo($"[creaturemove] candidate '{c.Name}' at {c.transform.position.ToString("F4")} ({dist:F3} away)");
                        if (dist < bestD) { bestD = dist; best = c; }
                    }
                    if (best == null) { Plugin.Log.LogInfo("[creaturemove] no creature there"); return; }
                    CreaturePresenter.TeleportCreature(best, to);
                    Plugin.Log.LogInfo($"[creaturemove] '{best.Name}' moved from {from.ToString("F4")} to {to.ToString("F4")}");
                    return;
                }
                if (parts.Length == 1 && parts[0] == "floorpanel") { UICapture.DebugDumpPanels(); return; }
                if (parts.Length >= 1 && parts[0] == "dicein")
                {
                    // dicein [kind] [count] : dice into the tray and out into your hand (as a click on the tray)
                    DiceHand.DebugTakeOut(parts.Length > 1 ? ushort.Parse(parts[1]) : (ushort)1, parts.Length > 2 ? int.Parse(parts[2]) : 2);
                    return;
                }
                if (parts.Length == 2 && parts[0] == "tool")
                {
                    // tool <BoardTool type name> : switch TaleSpire's board tool (e.g. TilesSelectionBoardTool, to test Build Mode)
                    var type = HarmonyLib.AccessTools.TypeByName(parts[1]);
                    var mgr = SingletonBehaviour<BoardToolManager>.Instance;
                    System.Reflection.MethodInfo m = null;
                    foreach (var mi in typeof(BoardToolManager).GetMethods())
                        if (mi.Name == "SwitchToTool" && mi.IsGenericMethodDefinition && mi.GetParameters().Length == 1) m = mi;
                    if (type == null || m == null) { Plugin.Log.LogInfo("[tool] no such tool"); return; }
                    m = m.MakeGenericMethod(type);
                    m.Invoke(mgr, new[] { m.GetParameters()[0].DefaultValue });
                    Plugin.Log.LogInfo($"[tool] now {BoardToolManager.CurrentTool?.GetType().Name}, build mode {BoardBuilder.InBuilderMode}");
                    return;
                }
                if (parts.Length >= 1 && parts[0] == "dicelist")
                {
                    // dicelist [remove <i>] : list the dice on the board; remove = that die's "Remove" option (its whole roll)
                    var dice = UnityEngine.Object.FindObjectsOfType<Dice.Die>();
                    for (int i = 0; i < dice.Length; i++) Plugin.Log.LogInfo($"[dicelist] {i}: '{dice[i].name}' at {dice[i].transform.position} roll={dice[i].RollId}");
                    if (parts.Length == 3 && parts[1] == "remove")
                    {
                        var die = dice[int.Parse(parts[2])];
                        Bounce.Singletons.SimpleSingletonBehaviour<Dice.DiceRollManager>.Instance.RemoveRoll(die.RollId);
                        Plugin.Log.LogInfo($"[dicelist] removed roll {die.RollId}");
                    }
                    return;
                }
                if (parts.Length == 3 && parts[0] == "uniques" && parts[1] == "fake")
                {
                    // uniques fake <n>|off : treat the n-th nearest creature as unique this session (nothing is saved to the board)
                    if (parts[2] == "off") { HandColors.DebugUnique = null; return; }
                    var rig = VRManager.Instance.Rig;
                    var all = UnityEngine.Object.FindObjectsOfType<CreatureBoardAsset>();
                    System.Array.Sort(all, (a, b) => Vector3.Distance(a.transform.position, rig.Head.position).CompareTo(Vector3.Distance(b.transform.position, rig.Head.position)));
                    var c = all[Mathf.Clamp(int.Parse(parts[2]), 0, all.Length - 1)];
                    HandColors.DebugUnique = c.CreatureId;
                    Plugin.Log.LogInfo($"[uniques] treating '{c.Name}' at {c.transform.position.ToString("F2")} as unique (this session only)");
                    return;
                }
                if (parts.Length == 1 && parts[0] == "uniques")
                {
                    // List this board's unique creatures (they can have their own first-person hand colour).
                    foreach (var c in UnityEngine.Object.FindObjectsOfType<CreatureBoardAsset>())
                        if (HandColors.CanColor(c))
                            Plugin.Log.LogInfo($"[uniques] '{c.Name}' at {c.transform.position.ToString("F2")} id {c.CreatureId} unique {c.UniqueId} colour {(HandColors.TryGet(c, out var s) ? s.Name : "default")}");
                    return;
                }
                if (parts.Length == 2 && parts[0] == "predator")
                {
                    // predator on|off|flip : Predator Vision without the trackpads (first person only); flip the grey pass
                    if (parts[1] == "flip") PredatorVision.FlipGrey = !PredatorVision.FlipGrey;
                    else PredatorVision.DebugForce = parts[1] == "on";
                    return;
                }
                if (parts.Length >= 2 && parts[0] == "radialbtn")
                {
                    // radialbtn main|hands|swatch [n] : aim the right debug laser at one of our radial buttons (then "trig right 1/0")
                    var rig = VRManager.Instance.Rig;
                    Vector2 px = RadialExtraButton.DebugPoint(parts[1], parts.Length > 2 ? int.Parse(parts[2]) : 0);
                    Vector3 target = VRManager.Instance.Panels.Radial.WorldPointForPixel(px);
                    Vector3 origin = rig.Head.position + rig.Head.right * 0.2f * rig.Scale - rig.Head.up * 0.3f * rig.Scale;
                    VRHands.DebugHandRays[1] = new Ray(origin, (target - origin).normalized);
                    Plugin.Log.LogInfo($"[radialbtn] {parts[1]} at UI {px} -> {target}");
                    return;
                }
                if (parts.Length == 2 && parts[0] == "carrylog")
                {
                    Patches.DragHelper.DebugFrames = int.Parse(parts[1]);
                    return;
                }
                if (parts.Length == 3 && parts[0] == "nudge")
                {
                    // nudge <yaw deg> <pitch deg, + = up> : turn the right debug laser once (to move something being carried)
                    if (!VRHands.DebugHandRays[1].HasValue) return;
                    var r = VRHands.DebugHandRays[1].Value;
                    float pitch = float.Parse(parts[2]);
                    Vector3 dir = Vector3.RotateTowards(r.direction, pitch >= 0f ? Vector3.up : Vector3.down, Mathf.Abs(pitch) * Mathf.Deg2Rad, 0f);
                    VRHands.DebugHandRays[1] = new Ray(r.origin, Quaternion.AngleAxis(float.Parse(parts[1]), Vector3.up) * dir);
                    return;
                }
                if (parts.Length == 4 && parts[0] == "toss")
                {
                    // toss <yaw deg> <pitch deg, + = up> <frames> : hold the trigger while the right debug laser swings, then let go
                    // (only with dice in hand: on the board the press would pick up whatever is under the laser)
                    if (!DiceHand.InHand) { Plugin.Log.LogInfo("[toss] no dice in hand: ignored"); return; }
                    int frames = Mathf.Max(1, int.Parse(parts[3]));
                    _sweepYaw = float.Parse(parts[1]) / frames;
                    _sweepPitch = float.Parse(parts[2]) / frames;
                    _sweepFrames = frames + 3;   // still moving as TaleSpire sees the release
                    VRPointer.DebugTriggerFrames = frames;
                    return;
                }
                if (parts.Length == 3 && parts[0] == "btntap2")
                {
                    // btntap2 a <gap frames> : two 3-frame A taps, the second <gap> frames after the first
                    _tapButton = SteamVRInput.RightClick;
                    _tapButton.DebugHeld = true;
                    _tapFrames = 3;
                    _secondTapLength = 3;
                    _secondTapIn = int.Parse(parts[2]);
                    return;
                }
                if (parts.Length == 2 && parts[0] == "clicklog")
                {
                    Patches.ClickLogPatch.Remaining = int.Parse(parts[1]);
                    return;
                }
                if (parts.Length == 2 && parts[0] == "roles") { Roles.DebugAsPlayer = parts[1] == "player"; Plugin.Log.LogInfo("[roles] acting as a player: " + Roles.DebugAsPlayer); return; }
                if (parts.Length == 2 && parts[0] == "controller") { ControllerProfiles.DebugType = parts[1] == "off" ? null : parts[1]; Plugin.Log.LogInfo("[controller] pretending: " + (ControllerProfiles.DebugType ?? "off")); return; }
                if (parts.Length >= 2 && parts[0] == "fingers")
                {
                    // fingers <trigger> <grip> <thumbRest 0|1> | off : made-up controller values for the fingers without finger tracking
                    FPHands.DebugFingers = parts[1] == "off" ? (Vector3?)null : new Vector3(float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture));
                    return;
                }
                if (parts.Length == 3 && parts[0] == "playmode" && parts[1] == "local") { DebugMultiplayer.PlayModeLocal(parts[2]); return; }
                if (parts.Length >= 2 && parts[0] == "turnfake") { DebugMultiplayer.TurnFake(parts); return; }
                if (parts.Length == 3 && parts[0] == "cutscene" && parts[1] == "force") { CutsceneScreen.DebugForce = parts[2] == "on"; return; }
                if (parts.Length == 2 && parts[0] == "vrmode")
                {
                    // vrmode on|off : what the main menu's VR button does
                    ModeSwitch.Instance.Switch(parts[1] == "on");
                    return;
                }
                if (parts.Length >= 3 && parts[0] == "forearm" && parts[1] == "turn")
                {
                    // forearm turn <deg> [pitch] : the normal-play pose with the left wrist turned palm-up by deg degrees
                    float pitch = parts.Length > 3 ? float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture) : 26f;
                    DiceForearm.DebugPose = DiceForearm.DebugPoseTurned(VRManager.Instance.Rig, float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture), pitch);
                    return;
                }
                if (parts.Length == 2 && parts[0] == "forearm")
                {
                    // forearm up|side|off : pretend the left controller is turned palm up / held as in normal play
                    DiceForearm.DebugPose = parts[1] == "off" ? (Pose?)null : DiceForearm.DebugPoseFor(VRManager.Instance.Rig, parts[1] == "up");
                    return;
                }
                if (parts.Length >= 2 && parts[0] == "head")
                {
                    // head <height m> <pitch deg, + = down> [yaw deg] | head off : a fixed head pose (headset lying on a desk)
                    if (parts[1] == "off") VRRig.DebugHead = null;
                    else VRRig.DebugHead = new Pose(new Vector3(0f, float.Parse(parts[1]), 0f),
                        Quaternion.Euler(parts.Length > 2 ? float.Parse(parts[2]) : 0f, parts.Length > 3 ? float.Parse(parts[3]) : 0f, 0f));
                    return;
                }
                if (parts.Length == 2 && parts[0] == "cambufs")
                {
                    // List the command buffers on a camera ("game" = the headset camera).
                    var cam = parts[1] == "game" ? VRManager.Instance.Rig.GameCamera : GameObject.Find(parts[1])?.GetComponent<Camera>();
                    if (cam == null) return;
                    foreach (UnityEngine.Rendering.CameraEvent ev in Enum.GetValues(typeof(UnityEngine.Rendering.CameraEvent)))
                        foreach (var cb in cam.GetCommandBuffers(ev))
                            Plugin.Log.LogInfo($"[cambufs] {cam.name} {ev}: '{cb.name}' ({cb.sizeInBytes} bytes)");
                    return;
                }
                if (parts.Length == 3 && parts[0] == "probe")
                {
                    Probe(new Vector2(float.Parse(parts[1]), float.Parse(parts[2])));
                    return;
                }
                if (parts.Length == 3 && parts[0] == "aimui")
                {
                    // Aim the debug laser from the head through the menu panel at UI pixel (x, y).
                    var panels = VRManager.Instance.Panels;
                    var rig = VRManager.Instance.Rig;
                    float x = float.Parse(parts[1]), y = float.Parse(parts[2]);
                    var quad = panels.Radial.Visible ? panels.Radial : panels.Main;
                    Vector3 p = quad.WorldPointForPixel(new Vector2(x, y));
                    Vector3 origin = rig.Head.position - rig.Head.up * 0.25f * rig.Scale + rig.Head.right * 0.15f * rig.Scale;
                    VRPointer.DebugRay = new Ray(origin, (p - origin).normalized);
                    VRPointer.DebugRayUntil = Time.unscaledTime + 20f;
                    quad.Raycast(VRPointer.DebugRay.Value, out float dd, out Vector2 back);
                    Plugin.Log.LogInfo($"[aimui] ({x},{y}) -> world {p} -> raycast back {back} quadPos={quad.T.position} quadScale={quad.T.lossyScale}");
                    VRPointer.TraceFrames = 3;
                    return;
                }
                if (parts.Length == 2 && parts[0] == "rulermode")
                {
                    // Same as picking a ruler type in TaleSpire's ruler menu.
                    SingletonBehaviour<BoardToolManager>.Instance.GetTool<RulerBoardTool>().SetRulerMode(int.Parse(parts[1]));
                    return;
                }
                if (parts.Length == 2 && parts[0] == "hold")
                {
                    VRPointer.DebugTriggerFrames = int.Parse(parts[1]);
                    return;
                }
                if (parts.Length == 1 && parts[0] == "pull")
                {
                    VRPointer.DebugTriggerFrames = 6;
                    return;
                }
                if (parts.Length == 1 && parts[0] == "near")
                {
                    LogNearRenderers();
                    return;
                }
                if (parts.Length == 1 && parts[0] == "big")
                {
                    LogBigGraphics();
                    return;
                }
                if (parts.Length >= 1 && parts[0] == "aimcreature")
                {
                    int index = parts.Length > 1 ? int.Parse(parts[1]) : 0;
                    AimAtCreature(index);
                    return;
                }
                if (parts.Length == 5 && parts[0] == "drive")
                {
                    // drive <stickX> <stickY> <zoom: +1 in / -1 out> <seconds>
                    VRRig.DebugMove = new Vector2(float.Parse(parts[1]), float.Parse(parts[2]));
                    VRRig.DebugZoom = float.Parse(parts[3]);
                    VRRig.DebugDriveUntil = Time.unscaledTime + float.Parse(parts[4]);
                    return;
                }
                if (parts.Length == 4 && parts[0] == "grip")
                {
                    // grip measure|lasso left|right 1|0 : pretend a grip button is held / released
                    var b = parts[1] == "measure" ? SteamVRInput.Measure : SteamVRInput.Lasso;
                    b.DebugHeld[parts[2] == "left" ? 0 : 1] = parts[3] != "0";
                    return;
                }
                if (parts.Length == 4 && parts[0] == "stick")
                {
                    // stick left|right x y : hold a thumbstick (0 0 releases it)
                    var ax = parts[1] == "left" ? SteamVRInput.Move : SteamVRInput.TurnHeight;
                    var v = new Vector2(float.Parse(parts[2]), float.Parse(parts[3]));
                    ax.DebugValue = v == Vector2.zero ? (Vector2?)null : v;
                    return;
                }
                if (parts.Length == 3 && parts[0] == "btn")
                {
                    // btn rotate|elevate|zoomin|zoomout|a|menu 1|0 : hold a button (a = right click, menu = B)
                    SteamVRInput.Button b = parts[1] == "rotate" ? SteamVRInput.RotateModifier : parts[1] == "elevate" ? SteamVRInput.ElevateModifier
                        : parts[1] == "zoomin" ? SteamVRInput.ZoomIn : parts[1] == "a" ? SteamVRInput.RightClick
                        : parts[1] == "menu" ? SteamVRInput.MenuToggle : SteamVRInput.ZoomOut;
                    b.DebugHeld = parts[2] != "0";
                    return;
                }
                if (parts.Length == 1 && parts[0] == "tweens")
                {
                    // List TaleSpire's UI slide tweens (side panels) with their positions and state.
                    var tt = typeof(RectTransformTweener);
                    string F(string n, object o) => HarmonyLib.AccessTools.Field(tt, n)?.GetValue(o)?.ToString() ?? "-";
                    foreach (var tw in UnityEngine.Object.FindObjectsOfType<RectTransformTweener>(true))
                    {
                        var rt = tw.transform as RectTransform;
                        if (rt == null || !tw.isActiveAndEnabled) continue;
                        Plugin.Log.LogInfo($"[tween] {Diagnostics.PathOf(tw.transform)} posX={F("_tweenPositionX", tw)} start={F("_position_Start", tw)} end={F("_position_End", tw)} addW={F("_addScreenWidth", tw)} off={F("_posOffset", tw)} timer={F("_timer", tw)} dir={F("_tweenDirection", tw)} anchored={rt.anchoredPosition} pivot={rt.pivot} anchors={rt.anchorMin}-{rt.anchorMax} scale={rt.localScale.x:F2} width={rt.rect.width:F0} opacity={F("_tweenOpacity", tw)}");
                    }
                    return;
                }
                if (parts.Length == 3 && parts[0] == "btntap")
                {
                    // btntap a|menu|stick|build|... <frames> : a short press (TaleSpire only treats short presses as taps)
                    SteamVRInput.Button b = parts[1] == "a" ? SteamVRInput.RightClick : parts[1] == "menu" ? SteamVRInput.MenuToggle
                        : parts[1] == "stick" ? SteamVRInput.MiddleClick : parts[1] == "build" ? SteamVRInput.BuildModeToggle
                        : parts[1] == "rotate" ? SteamVRInput.RotateModifier
                        : parts[1] == "elevate" ? SteamVRInput.ElevateModifier : SteamVRInput.ZoomOut;
                    b.DebugHeld = true;
                    _tapButton = b;
                    _tapFrames = int.Parse(parts[2]);
                    return;
                }
                if (parts.Length >= 2 && parts[0] == "handaim")
                {
                    // handaim left|right ui <x> <y> | creature <n> | board | off : point one hand's laser (others keep theirs)
                    var rig = VRManager.Instance.Rig;
                    if (parts[1] == "off") { VRHands.DebugHandRays[0] = VRHands.DebugHandRays[1] = null; return; }
                    int h = parts[1] == "left" ? 0 : 1;
                    Vector3 origin = rig.Head.position + rig.Head.right * (h == 0 ? -0.2f : 0.2f) * rig.Scale - rig.Head.up * 0.3f * rig.Scale;
                    Vector3 target;
                    if (parts[2] == "ui") target = VRManager.Instance.Panels.Main.WorldPointForPixel(new Vector2(float.Parse(parts[3]), float.Parse(parts[4])));
                    else if (parts[2] == "radial") target = VRManager.Instance.Panels.Radial.WorldPointForPixel(new Vector2(float.Parse(parts[3]), float.Parse(parts[4])));
                    else if (parts[2] == "dice") target = VRManager.Instance.Panels.Dice.WorldPointForPixel(new Vector2(float.Parse(parts[3]), float.Parse(parts[4])));
                    else if (parts[2] == "creature") target = CreaturePoint(int.Parse(parts[3]));
                    else target = rig.Head.position + rig.Head.forward * 2f * rig.Scale - Vector3.up * 1.5f * rig.Scale;
                    VRHands.DebugHandRays[h] = new Ray(origin, (target - origin).normalized);
                    Plugin.Log.LogInfo($"[handaim] {parts[1]} -> {string.Join(" ", parts, 2, parts.Length - 2)} at {target}");
                    return;
                }
                if (parts.Length == 3 && parts[0] == "trig")
                {
                    // trig left|right 1|0 : pretend a trigger is held / released
                    SteamVRInput.Trigger(parts[1] == "left" ? Hand.Left : Hand.Right).DebugHeld = parts[2] != "0";
                    return;
                }
                if (parts.Length == 2 && parts[0] == "lassocircle")
                {
                    // Lasso a circle around the creature nearest the debug ray (or the board point it hits).
                    var rig = VRManager.Instance.Rig;
                    Vector3 c = VRPointer.WorldRay.GetPoint(VRPointer.WorldHitDistance(VRPointer.WorldRay, rig, false));
                    CreatureLasso.DebugCircle(rig, c, float.Parse(parts[1]));
                    return;
                }
                if (parts.Length >= 2 && parts[0] == "tree")
                {
                    // tree <path> [depth] : dump a UI subtree (active, alpha, rect, components)
                    // The path may contain spaces ("ContentPanel (1)"); a trailing number is the depth.
                    int depth = 4;
                    int last = parts.Length;
                    if (parts.Length > 2 && int.TryParse(parts[parts.Length - 1], out int d)) { depth = d; last--; }
                    string treePath = string.Join(" ", parts, 1, last - 1);
                    var go = GameObject.Find(treePath);
                    if (go == null) { Plugin.Log.LogInfo("[tree] not found: " + treePath); return; }
                    DumpTree(go.transform, 0, depth);
                    return;
                }
                if (parts.Length == 1 && parts[0] == "displayitems")
                {
                    foreach (var item in Resources.FindObjectsOfTypeAll<GUIDisplayItemBase>())
                    {
                        if (item.gameObject.scene.name == null) continue;
                        Plugin.Log.LogInfo($"[displayitems] {Diagnostics.PathOf(item.transform)} type={item.GetType().Name} showing={item.IsShowing()} active={item.gameObject.activeInHierarchy}");
                    }
                    return;
                }
                if (parts.Length == 2 && parts[0] == "fakehands")
                {
                    VRHands.DebugFakeHands = parts[1] != "0";
                    return;
                }
                if (parts.Length == 2 && parts[0] == "aimkey")
                {
                    // Aim the debug laser at a VR keyboard key (then press with "trig right 1/0").
                    var rig = VRManager.Instance.Rig;
                    if (VRManager.Instance.Panels.Keyboard.TryGetKeyCenter(parts[1], out Vector3 p))
                    {
                        Vector3 origin = rig.Head.position - rig.Head.up * 0.25f * rig.Scale + rig.Head.right * 0.15f * rig.Scale;
                        VRPointer.DebugRay = new Ray(origin, (p - origin).normalized);
                        VRPointer.DebugRayUntil = Time.unscaledTime + 20f;
                    }
                    return;
                }
                if (parts.Length == 2 && parts[0] == "kbtype")
                {
                    VRManager.Instance.Panels.Keyboard.DebugType(parts[1]);
                    return;
                }
                if (parts.Length == 2 && parts[0] == "hotkey")
                {
                    VRManager.Instance.Panels.Strip.Press(int.Parse(parts[1]));
                    return;
                }
                if (parts.Length == 3 && parts[0] == "uicamclip")
                {
                    UICapture.UICamera.nearClipPlane = float.Parse(parts[1]);
                    UICapture.UICamera.farClipPlane = float.Parse(parts[2]);
                    return;
                }
                if (parts.Length == 2 && parts[0] == "sidescale")
                {
                    VRConfig.SidePanelScale.Value = float.Parse(parts[1]);
                    return;
                }
                if (parts.Length == 2 && parts[0] == "menuscale")
                {
                    VRConfig.MenuScale.Value = float.Parse(parts[1]);
                    HarmonyLib.AccessTools.Method(typeof(TaleSpire.Settings.SettingsManager), "SendOnUiScaleChangeEvent")?.Invoke(null, null);
                    return;
                }
                if (parts.Length == 2 && parts[0] == "sharp")
                {
                    VRConfig.MenuSharpness.Value = float.Parse(parts[1]);
                    return;
                }
                if (parts.Length >= 1 && parts[0] == "renderers")
                {
                    LogSceneRenderers(parts.Length > 1 ? float.Parse(parts[1]) : 50f);
                    return;
                }
                if (parts.Length == 1 && (parts[0] == "uisnap" || parts[0] == "uidiff"))
                {
                    UiDiff(parts[0] == "uisnap");
                    return;
                }
                if (parts.Length >= 1 && parts[0] == "gfx")
                {
                    VRGraphics.DebugCommand(parts);
                    return;
                }
                if (parts.Length == 2 && parts[0] == "turn")
                {
                    VRRig.DebugTurnDegrees = float.Parse(parts[1]);
                    return;
                }
                if (parts.Length == 1 && parts[0] == "pick")
                {
                    LogPick();
                    return;
                }
                if (parts.Length == 1 && parts[0] == "status")
                {
                    Directory.CreateDirectory(Dir);
                    File.WriteAllText(Path.Combine(Dir, "status.txt"), Status());
                    return;
                }
                if (parts.Length == 1 && parts[0] == "panel")
                {
                    VRManager.Instance.Panels.ToggleMain();
                    return;
                }
                if (parts.Length == 1 && parts[0] == "undo")
                {
                    VirtualInput.Chord(UnityEngine.InputSystem.Key.LeftCtrl, UnityEngine.InputSystem.Key.Z);
                    return;
                }
                if (parts.Length == 1 && parts[0] == "buildmode")
                {
                    VirtualInput.Tap(UnityEngine.InputSystem.Key.B);
                    return;
                }
                if (parts.Length == 1 && parts[0] == "esc")
                {
                    VirtualInput.Tap(UnityEngine.InputSystem.Key.Escape);
                    return;
                }
                if (parts.Length == 2 && parts[0] == "raise")
                {
                    VRManager.Instance.Rig.DebugRaise(float.Parse(parts[1]));
                    return;
                }
                if (parts.Length == 3 && (parts[0] == "click" || parts[0] == "rclick"))
                {
                    _cmdPos = new Vector2(float.Parse(parts[1]), float.Parse(parts[2]));
                    _cmdButton = parts[0] == "click" ? 0 : 1;
                    _pendingFrames = 12;
                    Plugin.Log.LogInfo($"Debug {parts[0]} at {_cmdPos}");
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Debug command failed: " + e.Message); }
        }

        private static void Probe(Vector2 pos)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) { Plugin.Log.LogInfo("[probe] no EventSystem"); return; }
            var ped = new UnityEngine.EventSystems.PointerEventData(es) { position = pos };
            var results = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            es.RaycastAll(ped, results);
            Plugin.Log.LogInfo($"[probe] {pos}: {results.Count} hits");
            foreach (var r in results)
                Plugin.Log.LogInfo($"[probe]   {Diagnostics.PathOf(r.gameObject.transform)} module={r.module?.GetType().Name} dist={r.distance} screen={r.screenPosition}");
            var mod = es.currentInputModule as UnityEngine.InputSystem.UI.InputSystemUIInputModule;
            if (mod != null)
            {
                Plugin.Log.LogInfo($"[probe] module pointerBehavior={mod.pointerBehavior} asset={(mod.actionsAsset ? mod.actionsAsset.name : "null")} assetDevices={(mod.actionsAsset != null && mod.actionsAsset.devices.HasValue ? string.Join(",", System.Linq.Enumerable.Select(mod.actionsAsset.devices.Value, d => d.name)) : "any")}");
                LogAction("point", mod.point);
                LogAction("leftClick", mod.leftClick);
            }
            if (Bounce.Singletons.SimpleSingletonBehaviour<ControllerManager>.HasInstance)
            {
                var lmb = Bounce.Singletons.SimpleSingletonBehaviour<ControllerManager>.Instance.MouseMap.LMB;
                Plugin.Log.LogInfo($"[probe] TaleSpire LMB enabled={lmb.enabled} controls=[{string.Join(",", System.Linq.Enumerable.Select(lmb.controls, c => c.path))}]");
            }
            foreach (var gr in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.GraphicRaycaster>())
            {
                var c = gr.GetComponent<Canvas>();
                Plugin.Log.LogInfo($"[probe] raycaster '{Diagnostics.PathOf(gr.transform)}' enabled={gr.isActiveAndEnabled} cam={(gr.eventCamera ? gr.eventCamera.name : "null")} mode={(c ? c.renderMode.ToString() : "-")} canvasPos={gr.transform.position} lossyScale={gr.transform.lossyScale} pixelRect={(c ? c.pixelRect.ToString() : "-")}");
            }
        }

        /// <summary>A point on the n-th nearest creature (for "handaim ... creature n").</summary>
        private static Vector3 CreaturePoint(int index)
        {
            var rig = VRManager.Instance.Rig;
            var all = UnityEngine.Object.FindObjectsOfType<CreatureBoardAsset>();
            if (all.Length == 0) return rig.Head.position + rig.Head.forward;
            System.Array.Sort(all, (a, b) => Vector3.Distance(a.transform.position, rig.Head.position).CompareTo(Vector3.Distance(b.transform.position, rig.Head.position)));
            var c = all[Mathf.Clamp(index, 0, all.Length - 1)];
            Plugin.Log.LogInfo($"[handaim] creature '{c.name}' at {c.transform.position}");
            return c.transform.position + Vector3.up * 0.4f;
        }

        private static void AimAtCreature(int index)
        {
            var rig = VRManager.Instance.Rig;
            var all = UnityEngine.Object.FindObjectsOfType<CreatureBoardAsset>();
            if (all.Length == 0 || rig.Head == null) { Plugin.Log.LogInfo("[aim] no creatures"); return; }
            System.Array.Sort(all, (a, b) => Vector3.Distance(a.transform.position, rig.Head.position).CompareTo(Vector3.Distance(b.transform.position, rig.Head.position)));
            var c = all[Mathf.Clamp(index, 0, all.Length - 1)];
            Vector3 target = c.transform.position + Vector3.up * 0.4f;
            Vector3 origin = rig.Head.position + rig.Head.right * 0.2f * rig.Scale - Vector3.up * 0.3f * rig.Scale;
            VRPointer.DebugRay = new Ray(origin, (target - origin).normalized);
            VRPointer.DebugRayUntil = Time.unscaledTime + 30f;
            Plugin.Log.LogInfo($"[aim] aiming at creature '{c.name}' at {c.transform.position} (of {all.Length})");
        }

        private static void LogPick()
        {
            try
            {
                var kind = PixelPickingManager.TryGetPicked(out Unity.Mathematics.float3 pos, out DataModel.PlaceableRef placeable, out CreatureBoardAsset creature, out Component comp, out PickedExternalSystemData ext);
                Plugin.Log.LogInfo($"[pick] kind={kind} pos={pos} creature={(creature != null ? creature.name + " rot=" + creature.Rotator.localEulerAngles : "-")} tool={(BoardToolManager.CurrentTool != null ? BoardToolManager.CurrentTool.GetType().Name : "-")} pickCam={(PickCamera.Cam != null ? PickCamera.Cam.transform.position + " en=" + PickCamera.Cam.enabled + " bufs=" + PickCamera.Cam.commandBufferCount : "-")} radialOpen={(SingletonBehaviour<MapMenuManager>.HasInstance && MapMenuManager.IsOpen)}");
            }
            catch (Exception e) { Plugin.Log.LogInfo("[pick] " + e); }
        }

        private static void LogNearRenderers()
        {
            Vector3 anchor = CameraController.ControllerPosition;
            Plugin.Log.LogInfo($"[near] anchor={anchor} camPlane={CameraController.CameraPlaneHeight}");
            var ctrl = Bounce.Singletons.SimpleSingletonBehaviour<CameraController>.HasInstance ? Bounce.Singletons.SimpleSingletonBehaviour<CameraController>.Instance.transform : null;
            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                bool underCam = ctrl != null && r.transform.IsChildOf(ctrl.root);
                float d = Vector3.Distance(r.bounds.center, anchor);
                if (!underCam && d > 4f) continue;
                if (r.name.StartsWith("TaleSpireVR")) continue;
                var mf = r.GetComponent<MeshFilter>();
                Plugin.Log.LogInfo($"[near] {Diagnostics.PathOf(r.transform)} type={r.GetType().Name} layer={LayerMask.LayerToName(r.gameObject.layer)} pos={r.bounds.center} size={r.bounds.size} dist={d:F1} mesh={(mf && mf.sharedMesh ? mf.sharedMesh.name : "-")} shader={(r.sharedMaterial ? r.sharedMaterial.shader.name : "-")} underCamRoot={underCam}");
            }
        }

        /// <summary>Every visible renderer within range of the VR camera (to identify scene objects such as the menu figure).</summary>
        private static void LogSceneRenderers(float range)
        {
            var cam = VRManager.Instance.Rig != null ? VRManager.Instance.Rig.GameCamera : Camera.main;
            if (cam == null) return;
            Vector3 eye = cam.transform.position;
            int n = 0;
            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r.name.StartsWith("TaleSpireVR") || r.transform.root.name.StartsWith("TaleSpireVR")) continue;
                float d = Vector3.Distance(r.bounds.center, eye);
                if (d > range || n++ > 150) continue;
                Plugin.Log.LogInfo($"[renderers] {Diagnostics.PathOf(r.transform)} type={r.GetType().Name} layer={LayerMask.LayerToName(r.gameObject.layer)} scene={r.gameObject.scene.name} center={r.bounds.center} size={r.bounds.size} dist={d:F2} shader={(r.sharedMaterial ? r.sharedMaterial.shader.name : "-")} tex={(r.sharedMaterial && r.sharedMaterial.mainTexture ? r.sharedMaterial.mainTexture.name + " " + r.sharedMaterial.mainTexture.width + "x" + r.sharedMaterial.mainTexture.height : "-")}");
            }
            Plugin.Log.LogInfo($"[renderers] camera at {eye} fwd={cam.transform.forward}, {n} within {range}");
        }

        private static void DumpTree(Transform t, int level, int maxDepth)
        {
            var corners = new Vector3[4];
            string info = "";
            var rt = t as RectTransform;
            if (rt != null)
            {
                rt.GetWorldCorners(corners);
                info += $" rect=({corners[0].x:F0},{corners[0].y:F0})-({corners[2].x:F0},{corners[2].y:F0}) anchored={rt.anchoredPosition} size={rt.rect.size} scale={rt.localScale} anchors={rt.anchorMin}-{rt.anchorMax} pivot={rt.pivot} offsets={rt.offsetMin}/{rt.offsetMax}";
            }
            info += $" layer={LayerMask.LayerToName(t.gameObject.layer)} localZ={t.localPosition.z:F2} worldZ={t.position.z:F2} euler={t.localEulerAngles}";
            var cv = t.GetComponent<Canvas>();
            if (cv != null) info += $" canvas(override={cv.overrideSorting} order={cv.sortingOrder} mode={cv.renderMode} enabled={cv.enabled})";
            var cg = t.GetComponent<CanvasGroup>();
            if (cg != null) info += $" canvasGroup(alpha={cg.alpha:F2} raycasts={cg.blocksRaycasts})";
            var g = t.GetComponent<UnityEngine.UI.Graphic>();
            if (g != null) info += $" graphic({g.GetType().Name} enabled={g.enabled} a={g.color.a:F2} inherited={g.canvasRenderer.GetInheritedAlpha():F2} culled={g.canvasRenderer.cull} hasClip={g.canvasRenderer.hasRectClipping} mat={(g.materialForRendering ? g.materialForRendering.shader.name : "-")} color={g.color})";
            var comps = string.Join(",", System.Linq.Enumerable.Select(t.GetComponents<Component>(), c => c == null ? "null" : c.GetType().Name));
            Plugin.Log.LogInfo($"[tree] {new string(' ', level * 2)}{t.name} active={t.gameObject.activeSelf}{info} [{comps}]");
            if (level >= maxDepth) return;
            for (int i = 0; i < t.childCount; i++) DumpTree(t.GetChild(i), level + 1, maxDepth);
        }

        private static readonly System.Collections.Generic.HashSet<int> _uiBefore = new System.Collections.Generic.HashSet<int>();

        /// <summary>uisnap: remember what UI graphics / renderers are visible; uidiff: log the ones that appeared since.</summary>
        private static void UiDiff(bool snap)
        {
            if (snap) _uiBefore.Clear();
            int n = 0;
            foreach (var g in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Graphic>())
            {
                if (!g.isActiveAndEnabled || g.canvasRenderer.GetInheritedAlpha() <= 0.01f) continue;
                if (snap) { _uiBefore.Add(g.GetInstanceID()); continue; }
                if (_uiBefore.Contains(g.GetInstanceID()) || n++ > 120) continue;
                var c = g.canvas;
                Plugin.Log.LogInfo($"[uidiff] UI {Diagnostics.PathOf(g.transform)} type={g.GetType().Name} layer={LayerMask.LayerToName(g.gameObject.layer)} canvas={(c ? Diagnostics.PathOf(c.rootCanvas.transform) + " mode=" + c.rootCanvas.renderMode + " cam=" + (c.rootCanvas.worldCamera ? c.rootCanvas.worldCamera.name : "none") : "-")} tex={(g.mainTexture ? g.mainTexture.name : "-")}");
            }
            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (snap) { _uiBefore.Add(r.GetInstanceID()); continue; }
                if (_uiBefore.Contains(r.GetInstanceID()) || n++ > 160) continue;
                Plugin.Log.LogInfo($"[uidiff] R {Diagnostics.PathOf(r.transform)} type={r.GetType().Name} layer={LayerMask.LayerToName(r.gameObject.layer)} center={r.bounds.center} shader={(r.sharedMaterial ? r.sharedMaterial.shader.name : "-")}");
            }
            Plugin.Log.LogInfo(snap ? $"[uidiff] snapshot of {_uiBefore.Count} visible items" : $"[uidiff] {n} new items");
        }

        private static void LogBigGraphics()
        {
            var corners = new Vector3[4];
            foreach (var g in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Graphic>())
            {
                if (!g.isActiveAndEnabled || g.color.a <= 0.01f || g.canvasRenderer.GetInheritedAlpha() <= 0.01f) continue;
                g.rectTransform.GetWorldCorners(corners);
                float w = corners[2].x - corners[0].x, h = corners[2].y - corners[0].y;
                if (w * h < Screen.width * Screen.height * 0.5f) continue;
                Plugin.Log.LogInfo($"[big] {Diagnostics.PathOf(g.transform)} type={g.GetType().Name} color={g.color} size={w:F0}x{h:F0} tex={(g.mainTexture ? g.mainTexture.name : "-")} raycast={g.raycastTarget}");
            }
        }

        private static void LogAction(string label, UnityEngine.InputSystem.InputActionReference r)
        {
            if (r == null || r.action == null) { Plugin.Log.LogInfo($"[probe] {label}: null"); return; }
            var a = r.action;
            Plugin.Log.LogInfo($"[probe] {label}: enabled={a.enabled} map={a.actionMap?.name} mapDevices={(a.actionMap != null && a.actionMap.devices.HasValue ? string.Join(",", System.Linq.Enumerable.Select(a.actionMap.devices.Value, d => d.name)) : "any")} bindings=[{string.Join(",", System.Linq.Enumerable.Select(a.bindings, b => b.effectivePath))}] controls=[{string.Join(",", System.Linq.Enumerable.Select(a.controls, c => c.path))}]");
        }

        private static void LogPointerState()
        {
            try
            {
                var m = VirtualInput.Mouse;
                var es = UnityEngine.EventSystems.EventSystem.current;
                var mod = es != null ? es.currentInputModule as UnityEngine.InputSystem.UI.InputSystemUIInputModule : null;
                string hit = "-";
                if (mod != null && m != null)
                {
                    var r = mod.GetLastRaycastResult(m.deviceId);
                    hit = r.gameObject != null ? Diagnostics.PathOf(r.gameObject.transform) : "none";
                }
                Plugin.Log.LogInfo($"[dbg] f={Time.frameCount} vpos={(m != null ? m.position.ReadValue().ToString() : "-")} lmb={(m != null && m.leftButton.isPressed)} current={(UnityEngine.InputSystem.Mouse.current != null ? UnityEngine.InputSystem.Mouse.current.name : "-")} module={(mod != null ? mod.name + " enabled=" + mod.enabled : (es != null && es.currentInputModule != null ? es.currentInputModule.GetType().Name : "none"))} esFocused={(es != null && es.isFocused)} overUI={MouseManager.IsHoveringOverUI} hit={hit} bg={UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior}");
            }
            catch (Exception e) { Plugin.Log.LogInfo("[dbg] " + e.Message); }
        }

        public static bool CommandActive => _pendingFrames > 0;
        private static int _pendingFrames;
        private static Vector2 _cmdPos;
        private static int _cmdButton;
        private static float _nextCmdCheck;

        /// <summary>Full-resolution crop of the left eye around where the menu panel appears (to judge text sharpness).</summary>
        private static void SaveEyeCrop(Texture eye, string path, int cw, int ch)
        {
            var mgr = VRManager.Instance;
            var cam = mgr.Rig != null ? mgr.Rig.GameCamera : null;
            Vector2 uv = new Vector2(0.5f, 0.5f);
            if (cam != null && mgr.Panels != null && mgr.Panels.Main.Visible)
            {
                Vector3 p = mgr.Panels.Main.T.position;
                Vector4 clip = cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left) * cam.GetStereoViewMatrix(Camera.StereoscopicEye.Left) * new Vector4(p.x, p.y, p.z, 1f);
                if (clip.w > 0f) uv = new Vector2(clip.x / clip.w * 0.5f + 0.5f, clip.y / clip.w * 0.5f + 0.5f);
            }
            int w = eye.width, h = eye.height;
            cw = Mathf.Min(cw, w); ch = Mathf.Min(ch, h);
            int x = Mathf.Clamp(Mathf.RoundToInt(uv.x * w - cw / 2f), 0, w - cw);
            int y = Mathf.Clamp(Mathf.RoundToInt(uv.y * h - ch / 2f), 0, h - ch);
            var tmp = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            // Same orientation fix as the full snapshot, then read the region (y from the bottom, like the uv).
            if (SystemInfo.graphicsUVStartsAtTop) Graphics.Blit(eye, tmp, new Vector2(1f, -1f), new Vector2(0f, 1f));
            else Graphics.Blit(eye, tmp);
            RenderTexture.active = tmp;
            var tex = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(x, y, cw, ch), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(tmp);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            Plugin.Log.LogInfo($"Eye crop at uv {uv} ({x},{y}) {cw}x{ch} -> {path}");
        }

        private static void Save(Texture src, string path, int maxWidth, bool flipY)
        {
            int w = Mathf.Min(maxWidth, src.width);
            int h = Mathf.Max(1, src.height * w / src.width);
            var tmp = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            if (flipY) Graphics.Blit(src, tmp, new Vector2(1f, -1f), new Vector2(0f, 1f));
            else Graphics.Blit(src, tmp);
            RenderTexture.active = tmp;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(tmp);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }
    }
}
