using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;

namespace TaleSpireVR
{
    /// <summary>Shared pieces for the first-person weapons' effects: TaleSpire's status effects and sounds.</summary>
    internal static class WeaponFx
    {
        private static readonly FieldInfo StatusField = AccessTools.Field(typeof(CreatureMenuBoardTool), "_statusEmotes");
        private static AudioMixerGroup _sfxGroup;
        private static bool _groupSearched;

        /// <summary>TaleSpire's status effects (the creature menu's Status list).</summary>
        public static ActionTimelineCollection Statuses()
        {
            if (StatusField == null || !SingletonBehaviour<BoardToolManager>.HasInstance) return null;
            var tool = SingletonBehaviour<BoardToolManager>.Instance.GetTool<CreatureMenuBoardTool>();
            return tool != null ? StatusField.GetValue(tool) as ActionTimelineCollection : null;
        }

        /// <summary>Play a clip once at a point (from startAt seconds in), through TaleSpire's sound effects mixer (its volume setting applies).</summary>
        public static void PlayClip(AudioClip clip, Vector3 at, float volume = 1f, float pitch = 1f, float startAt = 0f)
        {
            if (clip == null) return;
            var go = new GameObject("TaleSpireVR_Sound_" + clip.name);
            go.transform.position = at;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = volume;
            src.pitch = pitch;
            src.spatialBlend = 0.6f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1000f;   // board units can be tiny in first person: never fade with distance
            src.maxDistance = 2000f;
            var group = SfxGroup();
            if (group != null) src.outputAudioMixerGroup = group;
            src.Play();
            if (startAt > 0f && startAt < clip.length) src.time = startAt;
            Object.Destroy(go, (clip.length - startAt) / Mathf.Max(0.1f, pitch) + 0.2f);
        }

        internal static AudioMixerGroup SfxGroup()
        {
            if (_groupSearched) return _sfxGroup;
            _groupSearched = true;
            AudioMixerGroup any = null;
            foreach (var s in Resources.FindObjectsOfTypeAll<AudioSource>())
            {
                var g = s != null ? s.outputAudioMixerGroup : null;
                if (g == null) continue;
                string n = g.name.ToLowerInvariant();
                if (n.Contains("sfx") || n.Contains("effect") || n.Contains("sound")) { _sfxGroup = g; break; }
                if (any == null && !n.Contains("music") && !n.Contains("ambien")) any = g;
            }
            if (_sfxGroup == null) _sfxGroup = any;
            Plugin.Log.LogInfo("Weapons: sound mixer group '" + (_sfxGroup != null ? _sfxGroup.name : "-") + "'");
            return _sfxGroup;
        }
    }
}
