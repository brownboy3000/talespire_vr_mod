using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace TaleSpireVR
{
    /// <summary>
    /// Post-processing adjustments for the headset: bloom is toned down to reduce glare (light intensities are untouched)
    /// and depth of field is off. Reflections and anti-aliasing follow TaleSpire's settings unless overridden in the config
    /// (TAA flickers in the headset). Re-applied periodically because TaleSpire's settings and atmosphere system write
    /// these values too.
    /// </summary>
    internal class VRGraphics : MonoBehaviour
    {
        private float _next;
        private readonly Dictionary<Bloom, float[]> _bloom = new Dictionary<Bloom, float[]>();   // {original, lastWritten}
        private readonly HashSet<PostProcessProfile> _logged = new HashSet<PostProcessProfile>();
        private PostProcessLayer.Antialiasing? _lastAa;

        private float _nextLights;
        private readonly Dictionary<PostProcessEffectSettings, bool[]> _switchedOff = new Dictionary<PostProcessEffectSettings, bool[]>();   // {overrideState, value}
        private readonly Dictionary<PostProcessLayer, PostProcessLayer.Antialiasing> _aaOriginal = new Dictionary<PostProcessLayer, PostProcessLayer.Antialiasing>();

        /// <summary>The VR mod switches off (Player mode): TaleSpire's own bloom, effects, anti-aliasing and light shadows back.</summary>
        public void Suspend()
        {
            foreach (var kv in _limited) if (kv.Key != null) kv.Key.shadows = kv.Value;
            _limited.Clear();
            foreach (var kv in _bloom)
                if (kv.Key != null && Mathf.Approximately(kv.Key.intensity.value, kv.Value[1])) kv.Key.intensity.Override(kv.Value[0]);
            _bloom.Clear();
            foreach (var kv in _switchedOff)
            {
                if (kv.Key == null) continue;
                kv.Key.enabled.overrideState = kv.Value[0];
                kv.Key.enabled.value = kv.Value[1];
            }
            _switchedOff.Clear();
            foreach (var kv in _aaOriginal) if (kv.Key != null) kv.Key.antialiasingMode = kv.Value;
            _aaOriginal.Clear();
            _lastAa = null;
            _logged.Clear();
        }

        private void LateUpdate()
        {
            if (!VRManager.Active) return;
            if (Time.unscaledTime >= _nextLights)
            {
                _nextLights = Time.unscaledTime + 0.25f;
                try { LimitLightShadows(); }
                catch (System.Exception e) { Plugin.Log.LogWarning("Light shadow limit failed: " + e.Message); _nextLights = Time.unscaledTime + 10f; }
            }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            try { Apply(); }
            catch (System.Exception e) { Plugin.Log.LogWarning("VR graphics tweak failed: " + e.Message); _next = Time.unscaledTime + 10f; }
        }

        // Lights whose shadows we switched off, with the shadow type TaleSpire gave them.
        private readonly Dictionary<Light, LightShadows> _limited = new Dictionary<Light, LightShadows>();
        private readonly List<KeyValuePair<float, Light>> _candidates = new List<KeyValuePair<float, Light>>();
        private readonly List<Light> _stale = new List<Light>();
        public static int ShadowedLights { get; private set; }

        /// <summary>
        /// Point/spot light shadows (creature torches, lamps) are re-rendered six times per light per eye; on busy boards
        /// they cost more than everything else. Keep them only on the few lights nearest to you.
        /// </summary>
        private void LimitLightShadows()
        {
            var rig = VRManager.Instance.Rig;
            int max = VRConfig.MaxShadowedLights.Value;
            bool limit = rig != null && rig.OnBoard && rig.Head != null && max >= 0;

            _candidates.Clear();
            if (limit)
            {
                Vector3 head = rig.Head.position;
                float reach = Mathf.Max(0f, VRConfig.LightShadowDistance.Value) * rig.Scale;
                foreach (var l in Object.FindObjectsOfType<Light>())
                {
                    if (l.type == LightType.Directional || !l.isActiveAndEnabled) continue;
                    bool ours = _limited.TryGetValue(l, out var original);
                    if (ours && l.shadows != LightShadows.None) { _limited.Remove(l); ours = false; }   // TaleSpire re-set it
                    if (!ours && l.shadows == LightShadows.None) continue;                                // never had shadows
                    float d = Mathf.Max(0f, Vector3.Distance(head, l.transform.position) - l.range);
                    _candidates.Add(new KeyValuePair<float, Light>(d, l));
                }
                _candidates.Sort((a, b) => a.Key.CompareTo(b.Key));
            }

            int kept = 0;
            for (int i = 0; i < _candidates.Count; i++)
            {
                var l = _candidates[i].Value;
                bool keep = i < max && _candidates[i].Key <= rig.Scale * VRConfig.LightShadowDistance.Value;
                if (keep)
                {
                    if (_limited.TryGetValue(l, out var original)) { l.shadows = original; _limited.Remove(l); }
                    kept++;
                }
                else if (!_limited.ContainsKey(l))
                {
                    _limited[l] = l.shadows;
                    l.shadows = LightShadows.None;
                }
            }
            ShadowedLights = kept;

            // Restore everything when not limiting; forget destroyed lights.
            _stale.Clear();
            foreach (var kv in _limited)
            {
                if (kv.Key == null) { _stale.Add(kv.Key); continue; }
                if (!limit) { kv.Key.shadows = kv.Value; _stale.Add(kv.Key); }
            }
            foreach (var l in _stale) _limited.Remove(l);
        }

        private static readonly List<Light> _debugLights = new List<Light>();
        private static ShadowQuality? _debugShadows;
        private static readonly Dictionary<Light, LightShadows> _debugShadowed = new Dictionary<Light, LightShadows>();

        /// <summary>Debug cmd "gfx ...": switch individual rendering features to measure their GPU cost.</summary>
        public static void DebugCommand(string[] p)
        {
            string what = p.Length > 1 ? p[1] : "info";
            bool on = p.Length > 2 && p[2] != "0";
            var cam = VRManager.Instance.Rig != null ? VRManager.Instance.Rig.GameCamera : null;
            switch (what)
            {
                case "ao":
                case "ssr":
                case "bloom":
                    foreach (var v in Object.FindObjectsOfType<PostProcessVolume>())
                    {
                        var prof = v.HasInstantiatedProfile() ? v.profile : v.sharedProfile;
                        if (prof == null) continue;
                        PostProcessEffectSettings s = what == "ao" ? (PostProcessEffectSettings)prof.GetSetting<AmbientOcclusion>()
                            : what == "ssr" ? prof.GetSetting<ScreenSpaceReflections>() : prof.GetSetting<Bloom>();
                        if (s != null) s.enabled.Override(on);
                    }
                    break;
                case "shadows":
                    if (!_debugShadows.HasValue) _debugShadows = QualitySettings.shadows;
                    QualitySettings.shadows = on ? _debugShadows.Value : ShadowQuality.Disable;
                    break;
                case "lights":
                    if (!on)
                    {
                        foreach (var l in Object.FindObjectsOfType<Light>())
                            if (l.enabled && l.type != LightType.Directional) { l.enabled = false; _debugLights.Add(l); }
                    }
                    else
                    {
                        foreach (var l in _debugLights) if (l != null) l.enabled = true;
                        _debugLights.Clear();
                    }
                    break;
                case "layer":
                    if (cam != null && p.Length > 3)
                    {
                        int layer = int.Parse(p[2]);
                        if (p[3] != "0") cam.cullingMask |= 1 << layer; else cam.cullingMask &= ~(1 << layer);
                    }
                    break;
                case "far":
                    VRConfig.DrawDistance.Value = float.Parse(p[2]);
                    break;
                case "sdist":   // real meters (0 = TaleSpire's)
                    VRConfig.ShadowDistance.Value = float.Parse(p[2]);
                    break;
                case "lightshadows":   // gfx lightshadows <max count> [meters]
                    VRConfig.MaxShadowedLights.Value = int.Parse(p[2]);
                    if (p.Length > 3) VRConfig.LightShadowDistance.Value = float.Parse(p[3]);
                    break;
                case "uicam":
                    if (UICapture.UICamera != null) UICapture.UICamera.enabled = on;
                    break;
                case "pickcam":
                    PickCamera.DebugDisabled = !on;
                    break;
                case "cascades":
                    QualitySettings.shadowCascades = int.Parse(p[2]);
                    break;
                case "sres":
                    QualitySettings.shadowResolution = (ShadowResolution)int.Parse(p[2]);
                    break;
                case "pointshadows":
                    foreach (var l in Object.FindObjectsOfType<Light>())
                    {
                        if (l.type == LightType.Directional) continue;
                        if (!on && l.shadows != LightShadows.None) { _debugShadowed[l] = l.shadows; l.shadows = LightShadows.None; }
                        else if (on && _debugShadowed.TryGetValue(l, out var s)) l.shadows = s;
                    }
                    if (on) _debugShadowed.Clear();
                    break;
                case "sunshadows":
                    foreach (var l in Object.FindObjectsOfType<Light>())
                    {
                        if (l.type != LightType.Directional) continue;
                        if (!on && l.shadows != LightShadows.None) { _debugShadowed[l] = l.shadows; l.shadows = LightShadows.None; }
                        else if (on && _debugShadowed.TryGetValue(l, out var s)) { l.shadows = s; _debugShadowed.Remove(l); }
                    }
                    break;
                case "rs":
                    UnityEngine.XR.XRSettings.eyeTextureResolutionScale = float.Parse(p[2]);
                    break;
            }
            Plugin.Log.LogInfo("[gfx] " + string.Join(" ", p) + " -> " + Info(cam));
        }

        private static string Info(Camera cam)
        {
            int point = 0, spot = 0, dir = 0, shadowed = 0;
            foreach (var l in Object.FindObjectsOfType<Light>())
            {
                if (!l.isActiveAndEnabled) continue;
                if (l.type == LightType.Point) point++; else if (l.type == LightType.Spot) spot++; else if (l.type == LightType.Directional) dir++;
                if (l.shadows != LightShadows.None) shadowed++;
            }
            string pp = "";
            foreach (var v in Object.FindObjectsOfType<PostProcessVolume>())
            {
                var prof = v.HasInstantiatedProfile() ? v.profile : v.sharedProfile;
                if (prof == null) continue;
                foreach (var s in prof.settings) if (s.enabled.value && s.active) pp += s.GetType().Name + ",";
            }
            var layer = cam != null ? cam.GetComponent<PostProcessLayer>() : null;
            string sun = "";
            foreach (var l in Object.FindObjectsOfType<Light>())
                if (l.type == LightType.Directional && l.isActiveAndEnabled)
                    sun += $"'{l.name}' shadows={l.shadows} res={l.shadowResolution} customRes={l.shadowCustomResolution} strength={l.shadowStrength:F2} intensity={l.intensity:F2}; ";
            string points = "";
            foreach (var l in Object.FindObjectsOfType<Light>())
                if (l.type == LightType.Point && l.isActiveAndEnabled && points.Length < 400)
                    points += $"'{l.name}' r={l.range:F1} sh={l.shadows} res={l.shadowResolution} mode={l.renderMode}; ";
            Plugin.Log.LogInfo("[gfx] sun: " + sun + " shadowProjection=" + QualitySettings.shadowProjection + " shadowmask=" + QualitySettings.shadowmaskMode + " distance=" + QualitySettings.shadowDistance);
            Plugin.Log.LogInfo("[gfx] points: " + points);
            return $"eyeTex={UnityEngine.XR.XRSettings.eyeTextureWidth}x{UnityEngine.XR.XRSettings.eyeTextureHeight} rs={UnityEngine.XR.XRSettings.eyeTextureResolutionScale} " +
                   $"lights point={point} spot={spot} dir={dir} withShadows={shadowed} shadows={QualitySettings.shadows} shadowRes={QualitySettings.shadowResolution} cascades={QualitySettings.shadowCascades} " +
                   $"pixelLights={QualitySettings.pixelLightCount} hdr={(cam != null && cam.allowHDR)} msaa={(cam != null && cam.allowMSAA)} aa={(layer != null ? layer.antialiasingMode.ToString() : "-")} pp=[{pp}] mask=0x{(cam != null ? cam.cullingMask : 0):X}";
        }

        private void Apply()
        {
            foreach (var volume in Object.FindObjectsOfType<PostProcessVolume>())
            {
                var profile = volume.HasInstantiatedProfile() ? volume.profile : volume.sharedProfile;
                if (profile == null) continue;

                if (VRConfig.DisableReflections.Value && profile.TryGetSettings(out ScreenSpaceReflections ssr) && (!ssr.enabled.overrideState || ssr.enabled.value))
                {
                    if (!_switchedOff.ContainsKey(ssr)) _switchedOff[ssr] = new[] { ssr.enabled.overrideState, ssr.enabled.value };
                    ssr.enabled.Override(false);
                }
                // Depth of field can't work in a headset (your eyes do the focusing); it is also off in your TaleSpire settings.
                if (profile.TryGetSettings(out DepthOfField dof) && (!dof.enabled.overrideState || dof.enabled.value))
                {
                    if (!_switchedOff.ContainsKey(dof)) _switchedOff[dof] = new[] { dof.enabled.overrideState, dof.enabled.value };
                    dof.enabled.Override(false);
                }

                if (profile.TryGetSettings(out Bloom bloom))
                {
                    float scale = Mathf.Clamp(VRConfig.BloomScale.Value, 0f, 2f);
                    float current = bloom.intensity.value;
                    if (!_bloom.TryGetValue(bloom, out var rec))
                    {
                        rec = new[] { current, float.NaN };
                        _bloom[bloom] = rec;
                    }
                    else if (!Mathf.Approximately(current, rec[1]))
                    {
                        rec[0] = current;   // TaleSpire changed it (e.g. time of day); scale the new value
                    }
                    float target = rec[0] * scale;
                    if (!Mathf.Approximately(current, target) || !bloom.intensity.overrideState)
                        bloom.intensity.Override(target);
                    rec[1] = target;
                }

                if (_logged.Add(profile))
                    Plugin.Log.LogInfo($"VR graphics: profile '{profile.name}' (volume '{volume.name}', global={volume.isGlobal}) bloom={(profile.TryGetSettings(out Bloom b) ? b.intensity.value.ToString("F2") : "none")}");
            }

            var cam = VRManager.Instance.Rig != null ? VRManager.Instance.Rig.GameCamera : null;
            var layer = cam != null ? cam.GetComponent<PostProcessLayer>() : null;
            if (layer != null)
            {
                PostProcessLayer.Antialiasing? want = null;
                switch ((VRConfig.AntiAliasing.Value ?? "").Trim().ToUpperInvariant())
                {
                    case "TAA": want = PostProcessLayer.Antialiasing.TemporalAntialiasing; break;
                    case "SMAA": want = PostProcessLayer.Antialiasing.SubpixelMorphologicalAntialiasing; break;
                    case "FXAA": want = PostProcessLayer.Antialiasing.FastApproximateAntialiasing; break;
                    case "NONE": want = PostProcessLayer.Antialiasing.None; break;
                }
                if (want.HasValue && layer.antialiasingMode != want.Value)
                {
                    if (!_aaOriginal.ContainsKey(layer)) _aaOriginal[layer] = layer.antialiasingMode;
                    layer.antialiasingMode = want.Value;
                    if (_lastAa != want) Plugin.Log.LogInfo("VR graphics: anti-aliasing = " + want.Value);
                    _lastAa = want;
                }
            }
        }
    }
}
