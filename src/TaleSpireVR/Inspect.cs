using System.Reflection;
using System.Text;
using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// Debug look-ups for development (debug channel): "findtext <words>|all" lists on-screen texts, "audio [filter]"
    /// TaleSpire's loaded sound clips, "playsound <name>" plays one at the head, "prefabdump <status index>" the
    /// hierarchy of a status effect (particle systems with their settings).
    /// </summary>
    internal static class Inspect
    {
        public static void FindText(string[] p)
        {
            string filter = p.Length > 1 ? string.Join(" ", p, 1, p.Length - 1).ToLowerInvariant() : "all";
            foreach (var t in Resources.FindObjectsOfTypeAll<TMPro.TMP_Text>())
            {
                if (t == null || t.gameObject.scene.name == null) continue;
                string s = t.text ?? "";
                if (filter != "all" && !s.ToLowerInvariant().Contains(filter)) continue;
                var rt = t.rectTransform;
                Plugin.Log.LogInfo($"[findtext] {Diagnostics.PathOf(t.transform)} active={t.gameObject.activeInHierarchy} size={t.fontSize} auto={t.enableAutoSizing}({t.fontSizeMin}-{t.fontSizeMax}) rect={rt.rect.size} anchored={rt.anchoredPosition} alpha={t.alpha} '{Short(s)}'");
            }
            foreach (var t in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Text>())
            {
                if (t == null || t.gameObject.scene.name == null) continue;
                string s = t.text ?? "";
                if (filter != "all" && !s.ToLowerInvariant().Contains(filter)) continue;
                Plugin.Log.LogInfo($"[findtext] (legacy) {Diagnostics.PathOf(t.transform)} active={t.gameObject.activeInHierarchy} size={t.fontSize} '{Short(s)}'");
            }
        }

        private static string Short(string s)
        {
            s = s.Replace("\n", "\\n");
            return s.Length > 300 ? s.Substring(0, 300) + "..." : s;
        }

        public static void Audio(string[] p)
        {
            string filter = p.Length > 1 ? p[1].ToLowerInvariant() : null;
            var sb = new StringBuilder();
            int n = 0;
            foreach (var c in Resources.FindObjectsOfTypeAll<AudioClip>())
            {
                if (c == null || (filter != null && !c.name.ToLowerInvariant().Contains(filter))) continue;
                sb.Append($"{c.name} ({c.length:F2}s) | ");
                if (++n % 12 == 0) { Plugin.Log.LogInfo("[audio] " + sb); sb.Length = 0; }
            }
            if (sb.Length > 0) Plugin.Log.LogInfo("[audio] " + sb);
            Plugin.Log.LogInfo($"[audio] {n} clips");
        }

        /// <summary>"audiodata [filter]": each clip's loudness envelope (peak, time to -20 dB) and noisiness, to find a bang.</summary>
        public static void AudioData(string[] p)
        {
            string filter = p.Length > 1 ? p[1].ToLowerInvariant() : null;
            foreach (var c in Resources.FindObjectsOfTypeAll<AudioClip>())
            {
                if (c == null || c.length > 8f || (filter != null && !c.name.ToLowerInvariant().Contains(filter))) continue;
                try
                {
                    int n = c.samples * c.channels;
                    var d = new float[n];
                    if (!c.GetData(d, 0)) { Plugin.Log.LogInfo($"[audiodata] {c.name}: no data ({c.loadType})"); continue; }
                    float peak = 0f; int peakAt = 0;
                    for (int i = 0; i < n; i++) if (Mathf.Abs(d[i]) > peak) { peak = Mathf.Abs(d[i]); peakAt = i; }
                    // 10 ms windows: RMS envelope
                    int win = Mathf.Max(1, c.frequency * c.channels / 100);
                    var env = new StringBuilder();
                    float firstLoud = -1f, decay = -1f, maxRms = 0f;
                    int crossings = 0;
                    for (int i = 1; i < n; i++) if ((d[i] >= 0f) != (d[i - 1] >= 0f)) crossings++;
                    for (int w = 0; w * win < n; w++)
                    {
                        double s = 0; int cnt = 0;
                        for (int i = w * win; i < Mathf.Min(n, (w + 1) * win); i++) { s += d[i] * d[i]; cnt++; }
                        float rms = (float)System.Math.Sqrt(s / Mathf.Max(1, cnt));
                        maxRms = Mathf.Max(maxRms, rms);
                        if (w < 40 && w % 2 == 0) env.Append((rms * 100f).ToString("F0")).Append(' ');
                    }
                    for (int w = 0; w * win < n; w++)
                    {
                        double s = 0; int cnt = 0;
                        for (int i = w * win; i < Mathf.Min(n, (w + 1) * win); i++) { s += d[i] * d[i]; cnt++; }
                        float rms = (float)System.Math.Sqrt(s / Mathf.Max(1, cnt));
                        if (firstLoud < 0f && rms > maxRms * 0.5f) firstLoud = w * 0.01f;
                        if (firstLoud >= 0f && decay < 0f && rms < maxRms * 0.1f) decay = w * 0.01f - firstLoud;
                    }
                    float zcr = crossings / Mathf.Max(0.01f, c.length) / c.channels;
                    Plugin.Log.LogInfo($"[audiodata] {c.name} len={c.length:F2}s peak={peak:F2}@{(float)peakAt / c.channels / c.frequency:F3}s attack={firstLoud:F2}s decay-20dB={decay:F2}s zcr={zcr:F0}/s env(20ms)={env}");
                }
                catch (System.Exception e) { Plugin.Log.LogInfo($"[audiodata] {c.name}: {e.Message}"); }
            }
        }

        public static void PlaySound(string[] p)
        {
            if (p.Length < 2) return;
            foreach (var c in Resources.FindObjectsOfTypeAll<AudioClip>())
            {
                if (c == null || c.name != p[1]) continue;
                var rig = VRManager.Instance.Rig;
                Vector3 at = rig != null && rig.Head != null ? rig.Head.position : Vector3.zero;
                float vol = p.Length > 2 ? float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture) : 1f;
                WeaponFx.PlayClip(c, at, vol);
                Plugin.Log.LogInfo($"[playsound] {c.name} at {at}");
                return;
            }
            Plugin.Log.LogInfo("[playsound] not found: " + p[1]);
        }

        /// <summary>A status effect prefab's hierarchy, with each particle system's main settings.</summary>
        public static void PrefabDump(string[] p)
        {
            try
            {
                ActionTimeline prefab;
                if (p.Length > 1 && p[1] == "attack")
                {
                    // The creature you are in first person: its own attack (TaleSpire's "Hit").
                    var c = FirstPersonMode.Creature;
                    prefab = c != null ? HarmonyLib.AccessTools.Field(typeof(CreatureBoardAsset), "_attackPlayable").GetValue(c) as ActionTimeline : null;
                }
                else if (p.Length > 1 && int.TryParse(p[1], out int i))
                {
                    var statuses = WeaponFx.Statuses();
                    if (statuses == null) { Plugin.Log.LogInfo("[prefabdump] no status list"); return; }
                    prefab = statuses[i];
                }
                else prefab = ActionTimelineDatabase.GetActionPrefab(p.Length > 1 ? p[1] : "TLA_Burning");
                if (prefab == null) { Plugin.Log.LogInfo("[prefabdump] not found"); return; }
                Plugin.Log.LogInfo($"[prefabdump] '{prefab.DisplayName}' ({prefab.name})");
                Dump(prefab.transform, 0);
                DumpTimeline(prefab);
            }
            catch (System.Exception e) { Plugin.Log.LogInfo("[prefabdump] failed: " + e); }
        }

        /// <summary>Its timeline: length, and each track's clips (start, length, kind) and markers.</summary>
        private static void DumpTimeline(ActionTimeline prefab)
        {
            var dir = prefab.GetComponent<UnityEngine.Playables.PlayableDirector>();
            var ta = dir != null ? dir.playableAsset as UnityEngine.Timeline.TimelineAsset : null;
            if (ta == null) return;
            Plugin.Log.LogInfo($"[prefabdump] timeline '{ta.name}' {ta.duration:F2} s, wrap {dir.extrapolationMode}");
            foreach (var track in ta.GetOutputTracks())
            {
                var sb = new StringBuilder($"[prefabdump]   track '{track.name}' {track.GetType().Name}{(track.muted ? " muted" : "")}:");
                foreach (var clip in track.GetClips())
                {
                    string extra = clip.asset is UnityEngine.Timeline.AudioPlayableAsset ap && ap.clip != null ? " sound '" + ap.clip.name + "'" : "";
                    sb.Append($" ['{clip.displayName}' {clip.start:F2}+{clip.duration:F2} {clip.asset?.GetType().Name}{extra}]");
                }
                foreach (var m in track.GetMarkers()) sb.Append($" <{m.GetType().Name} {m.time:F2}>");
                Plugin.Log.LogInfo(sb.ToString());
            }
        }

        private static void Dump(Transform t, int depth)
        {
            var sb = new StringBuilder();
            sb.Append(new string(' ', depth * 2)).Append(t.name).Append($" active={t.gameObject.activeSelf} pos={t.localPosition:F3} rot={t.localEulerAngles:F0} scale={t.localScale:F2} [");
            foreach (var c in t.GetComponents<Component>()) if (c != null && !(c is Transform)) sb.Append(c.GetType().Name).Append(' ');
            sb.Append(']');
            var ps = t.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                var m = ps.main;
                var sh = ps.shape;
                var em = ps.emission;
                var r = t.GetComponent<ParticleSystemRenderer>();
                sb.Append($" PS life={m.startLifetime.constantMin:F2}-{m.startLifetime.constantMax:F2} size={m.startSize.constantMin:F2}-{m.startSize.constantMax:F2} speed={m.startSpeed.constantMin:F2}-{m.startSpeed.constantMax:F2} max={m.maxParticles} space={m.simulationSpace} scaling={m.scalingMode} loop={m.loop} dur={m.duration:F1} rate={em.rateOverTime.constantMin:F1}-{em.rateOverTime.constantMax:F1} bursts={em.burstCount} shape={(sh.enabled ? sh.shapeType.ToString() : "off")} r={sh.radius:F2} box={sh.scale:F2} grav={m.gravityModifier.constantMax:F2} color={m.startColor.color} mode={r?.renderMode} mat={(r != null && r.sharedMaterial != null ? r.sharedMaterial.name + "/" + r.sharedMaterial.shader.name : "-")}");
            }
            var mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) sb.Append($" MESH '{mf.sharedMesh.name}' verts={mf.sharedMesh.vertexCount} bounds={mf.sharedMesh.bounds.center:F3}/{mf.sharedMesh.bounds.size:F3} readable={mf.sharedMesh.isReadable}");
            var mr = t.GetComponent<MeshRenderer>();
            if (mr != null)
                foreach (var mat in mr.sharedMaterials)
                {
                    if (mat == null) continue;
                    sb.Append($" MAT '{mat.name}' shader='{mat.shader.name}' queue={mat.renderQueue} kw=[{string.Join(",", mat.shaderKeywords)}] props:");
                    for (int i = 0; i < mat.shader.GetPropertyCount(); i++)
                    {
                        string pn = mat.shader.GetPropertyName(i);
                        var pt = mat.shader.GetPropertyType(i);
                        string v = pt == UnityEngine.Rendering.ShaderPropertyType.Color ? mat.GetColor(pn).ToString()
                            : pt == UnityEngine.Rendering.ShaderPropertyType.Vector ? mat.GetVector(pn).ToString()
                            : pt == UnityEngine.Rendering.ShaderPropertyType.Texture ? (mat.GetTexture(pn) != null ? mat.GetTexture(pn).name : "-")
                            : mat.GetFloat(pn).ToString("F3");
                        sb.Append($" {pn}={v}");
                    }
                }
            foreach (var c in t.GetComponents<MonoBehaviour>())
            {
                if (c == null) continue;
                string tn = c.GetType().Name;
                if (tn != "CreatureEmoteBaseItem" && tn != "LightFlicker" && tn != "CreatureEmoteOnVisibilityChange") continue;
                sb.Append($" {tn}{{");
                foreach (var f in c.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    object v = f.GetValue(c);
                    sb.Append($"{f.Name}={(v is Object o ? (o != null ? o.name : "null") : v)}; ");
                }
                sb.Append('}');
            }
            var light = t.GetComponent<Light>();
            if (light != null) sb.Append($" LIGHT {light.type} range={light.range:F2} int={light.intensity:F2} color={light.color}");
            var audio = t.GetComponent<AudioSource>();
            if (audio != null) sb.Append($" AUDIO clip={(audio.clip != null ? audio.clip.name : "-")} vol={audio.volume:F2} loop={audio.loop} mixer={(audio.outputAudioMixerGroup != null ? audio.outputAudioMixerGroup.name : "-")} spatial={audio.spatialBlend:F1}");
            Plugin.Log.LogInfo("[prefabdump] " + sb);
            for (int c = 0; c < t.childCount; c++) Dump(t.GetChild(c), depth + 1);
        }
    }
}
