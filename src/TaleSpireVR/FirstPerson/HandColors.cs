using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TaleSpireVR
{
    /// <summary>
    /// First-person hand colours chosen per unique creature (radial menu: hands button). Saved locally in
    /// BepInEx/config/TaleSpireVR/TaleSpireVR.HandColors.txt, one line per campaign + creature ("campaign creature colourKey").
    /// TaleSpire's cloud data has no field of its own for this (only a creature's name or link, which every player sees),
    /// so it stays on this computer. A creature that stops being unique loses its colour (Prune).
    /// </summary>
    internal static class HandColors
    {
        public readonly struct Swatch
        {
            public readonly string Key, Name;
            public readonly Color Color;
            public Swatch(string key, string name, string hex)
            {
                Key = key;
                Name = name;
                Color = ColorUtility.TryParseHtmlString("#" + hex, out Color c) ? c : Color.white;
            }
        }

        /// <summary>Seven skin tones (light to dark), then the others.</summary>
        public static readonly Swatch[] Palette =
        {
            new Swatch("porcelain", "Porcelain", "F1D5C3"),
            new Swatch("fair", "Fair", "E5B999"),
            new Swatch("beige", "Beige", "D29E78"),
            new Swatch("tan", "Tan", "B9835C"),
            new Swatch("bronze", "Bronze", "966242"),
            new Swatch("brown", "Brown", "70452C"),
            new Swatch("deep", "Deep Brown", "4A2C1D"),
            new Swatch("palered", "Pale Red", "C46A60"),
            new Swatch("purple", "Purple", "7A4D9C"),
            new Swatch("charcoal", "Light Black", "3A393E"),
            new Swatch("orcgreen", "Orc Green", "6C8C3C"),
            new Swatch("bone", "Bone White", "E6E2D8"),
        };

        /// <summary>The hand colour from the config (HandColor under [FirstPerson]): creatures without a colour of their own.</summary>
        public static Color DefaultColor
        {
            get
            {
                Color c = new Color(0xA8 / 255f, 0x72 / 255f, 0x56 / 255f);
                if (ColorUtility.TryParseHtmlString("#" + VRConfig.FPHandColor.Value.Trim().TrimStart('#'), out Color parsed)) c = parsed;
                return c;
            }
        }

        private static readonly Dictionary<string, string> _saved = new Dictionary<string, string>();   // "campaign creature" -> key
        private static bool _loaded;
        private static float _nextPrune;
        private static string FilePath => ModFiles.HandColors;

        /// <summary>Debug ("uniques fake n"): treat this one creature as unique, this session only (testing on boards without unique creatures).</summary>
        public static CreatureGuid? DebugUnique;

        /// <summary>Can this creature have its own hand colour (it is a unique creature of this campaign)?</summary>
        public static bool CanColor(CreatureBoardAsset c)
        {
            try { return c != null && (c.IsUnique || (DebugUnique.HasValue && c.CreatureId == DebugUnique.Value)); }
            catch { return false; }
        }

        /// <summary>This creature's own colour, if it has one (only unique creatures do).</summary>
        public static bool TryGet(CreatureBoardAsset c, out Swatch swatch)
        {
            swatch = default;
            if (!CanColor(c)) return false;
            Load();
            if (!_saved.TryGetValue(KeyOf(c), out string key)) return false;
            foreach (var s in Palette)
                if (s.Key == key) { swatch = s; return true; }
            return false;
        }

        /// <summary>The colour of this creature's hands in first person.</summary>
        public static Color ColorFor(CreatureBoardAsset c) => TryGet(c, out var s) ? s.Color : DefaultColor;

        /// <summary>Give a unique creature its own colour (key from Palette), or back to the default (null).</summary>
        public static void Set(CreatureBoardAsset c, string key)
        {
            if (!CanColor(c)) return;
            Load();
            string k = KeyOf(c);
            if (key == null) _saved.Remove(k); else _saved[k] = key;
            Save();
            Plugin.Log.LogInfo($"Hand colour for '{c.Name}': {key ?? "default"}");
        }

        /// <summary>Every few seconds: creatures on this board that have a colour but are no longer unique lose it.</summary>
        public static void Prune()
        {
            if (Time.unscaledTime < _nextPrune) return;
            _nextPrune = Time.unscaledTime + 3f;
            Load();
            if (_saved.Count == 0) return;
            bool changed = false;
            string campaign = Campaign();
            foreach (var c in Object.FindObjectsOfType<CreatureBoardAsset>())
            {
                string k = campaign + " " + c.CreatureId;
                if (!_saved.ContainsKey(k)) continue;
                bool unique;
                try { unique = CanColor(c); } catch { continue; }
                if (unique) continue;
                _saved.Remove(k);
                changed = true;
                Plugin.Log.LogInfo($"Hand colour for '{c.Name}' cleared: no longer a unique creature");
            }
            if (changed) Save();
        }

        private static string Campaign()
        {
            try { return CampaignSessionManager.Id.ToString(); } catch { return "-"; }
        }

        private static string KeyOf(CreatureBoardAsset c) => Campaign() + " " + c.CreatureId;

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var p = line.Trim().Split(' ');
                    if (p.Length == 3 && !line.StartsWith("#")) _saved[p[0] + " " + p[1]] = p[2];
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Hand colours could not be read: " + e.Message); }
        }

        private static void Save()
        {
            try
            {
                var lines = new List<string> { "# TaleSpire VR: first-person hand colours of unique creatures (campaign creature colour)" };
                foreach (var kv in _saved) lines.Add(kv.Key + " " + kv.Value);
                File.WriteAllLines(FilePath, lines);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Hand colours could not be saved: " + e.Message); }
        }
    }
}
