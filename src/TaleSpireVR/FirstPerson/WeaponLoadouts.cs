using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TaleSpireVR
{
    internal enum WeaponKind { Ranged, Melee, Power }

    /// <summary>What a ranged weapon shows and sounds like when it fires (the attack itself is WeaponDef.Attack).</summary>
    internal enum ShotStyle { Gun, Bow, Magic, FireBolt }

    /// <summary>One entry of the Weapon Catalogue.</summary>
    internal sealed class WeaponDef
    {
        public string Id, Name;
        public WeaponKind Kind;
        public string Mesh;     // its model in weapons.bin (null: nothing in the hand)
        public string Attack;   // the TaleSpire attack it plays (null: ShotgunAttack / SwordAttack in the config)
        public ShotStyle Shot;  // ranged: a gun's blast and bang; a bow's twang (no flash); a wand's sparkle; a fire staff's bolt
    }

    /// <summary>
    /// First-person weapons per unique creature (radial menu: weapons button, like hand colours): the weapons added to
    /// it from the Weapon Catalogue and the one it holds. Saved locally in BepInEx/config/TaleSpireVR/TaleSpireVR.Weapons.txt,
    /// one line per campaign + creature ("campaign creature equipped|- id,id,..."); never shared with other players.
    /// </summary>
    internal static class WeaponLoadouts
    {
        public static readonly WeaponDef[] Catalogue =
        {
            // Firearms (one shot per pull, like the shotgun; the sci-fi injector's attack is green).
            new WeaponDef { Id = "shotgun", Name = "Shotgun", Kind = WeaponKind.Ranged, Mesh = "shotgun" },
            new WeaponDef { Id = "pistol", Name = "Pistol", Kind = WeaponKind.Ranged, Mesh = "pistol" },
            new WeaponDef { Id = "uzi", Name = "Uzi", Kind = WeaponKind.Ranged, Mesh = "uzi" },
            new WeaponDef { Id = "rifle", Name = "Assault Rifle", Kind = WeaponKind.Ranged, Mesh = "rifle" },
            new WeaponDef { Id = "m249", Name = "Machine Gun", Kind = WeaponKind.Ranged, Mesh = "m249" },
            new WeaponDef { Id = "m107", Name = "Sniper Rifle", Kind = WeaponKind.Ranged, Mesh = "m107" },
            new WeaponDef { Id = "rpg", Name = "Rocket Launcher", Kind = WeaponKind.Ranged, Mesh = "rpg" },
            new WeaponDef { Id = "musket", Name = "Musket", Kind = WeaponKind.Ranged, Mesh = "musket" },
            new WeaponDef { Id = "injector", Name = "Injector", Kind = WeaponKind.Ranged, Mesh = "injector", Attack = "TLA_LaserGreen" },
            new WeaponDef { Id = "combatshotgun", Name = "Combat Shotgun", Kind = WeaponKind.Ranged, Mesh = "combatshotgun" },
            // Bow (green attack, a twang, no flash) and magic (TaleSpire's magic missile).
            new WeaponDef { Id = "bow", Name = "Bow", Kind = WeaponKind.Ranged, Mesh = "bow", Attack = "TLA_LaserGreen", Shot = ShotStyle.Bow },
            new WeaponDef { Id = "wand", Name = "Magic Wand", Kind = WeaponKind.Ranged, Mesh = "wand", Attack = "TLA_MagicMissileAttack", Shot = ShotStyle.Magic },
            new WeaponDef { Id = "firestaff", Name = "Fire Staff", Kind = WeaponKind.Ranged, Mesh = "firestaff", Attack = "TLA_MagicMissileAttack", Shot = ShotStyle.FireBolt },
            // Melee (a strike when it enters another creature with the trigger held).
            new WeaponDef { Id = "sword", Name = "Longsword", Kind = WeaponKind.Melee, Mesh = "sword" },
            new WeaponDef { Id = "battleaxe", Name = "Battle Axe", Kind = WeaponKind.Melee, Mesh = "battleaxe" },
            new WeaponDef { Id = "fireaxe", Name = "Fire Axe", Kind = WeaponKind.Melee, Mesh = "fireaxe" },
            new WeaponDef { Id = "warhammer", Name = "War Hammer", Kind = WeaponKind.Melee, Mesh = "warhammer" },
            new WeaponDef { Id = "bat", Name = "Spiked Bat", Kind = WeaponKind.Melee, Mesh = "bat" },
            new WeaponDef { Id = "dagger", Name = "Dagger", Kind = WeaponKind.Melee, Mesh = "dagger" },
            new WeaponDef { Id = "crowbar", Name = "Crowbar", Kind = WeaponKind.Melee, Mesh = "crowbar" },
            new WeaponDef { Id = "shield", Name = "Shield", Kind = WeaponKind.Melee, Mesh = "shield" },
            new WeaponDef { Id = "spear", Name = "Spear", Kind = WeaponKind.Melee, Mesh = "spear" },
            new WeaponDef { Id = "halberd", Name = "Halberd", Kind = WeaponKind.Melee, Mesh = "halberd" },
            new WeaponDef { Id = "glaive", Name = "Glaive", Kind = WeaponKind.Melee, Mesh = "glaive" },
            new WeaponDef { Id = "scythe", Name = "Scythe", Kind = WeaponKind.Melee, Mesh = "scythe" },
            new WeaponDef { Id = "morningstar", Name = "Morning Star", Kind = WeaponKind.Melee, Mesh = "morningstar" },
            new WeaponDef { Id = "scimitar", Name = "Scimitar", Kind = WeaponKind.Melee, Mesh = "scimitar" },
            new WeaponDef { Id = "handaxe", Name = "Hand Axe", Kind = WeaponKind.Melee, Mesh = "handaxe" },
            new WeaponDef { Id = "machete", Name = "Machete", Kind = WeaponKind.Melee, Mesh = "machete" },
            new WeaponDef { Id = "knife", Name = "Knife", Kind = WeaponKind.Melee, Mesh = "knife" },
            new WeaponDef { Id = "sledgehammer", Name = "Sledgehammer", Kind = WeaponKind.Melee, Mesh = "sledgehammer" },
            new WeaponDef { Id = "kiteshield", Name = "Kite Shield", Kind = WeaponKind.Melee, Mesh = "kiteshield" },
            // Hand powers (nothing in the hand).
            new WeaponDef { Id = "flame", Name = "Flame", Kind = WeaponKind.Power, Mesh = null },
            new WeaponDef { Id = "ice", Name = "Ice", Kind = WeaponKind.Power, Mesh = null },
        };

        public static WeaponDef Find(string id)
        {
            foreach (var w in Catalogue) if (w.Id == id) return w;
            return null;
        }

        private sealed class Loadout
        {
            public string Equipped;
            public readonly List<string> Weapons = new List<string>();
        }

        private static readonly Dictionary<string, Loadout> _saved = new Dictionary<string, Loadout>();   // "campaign creature"
        private static bool _loaded;
        private static float _nextPrune;
        private static string FilePath => ModFiles.Weapons;
        private static readonly List<WeaponDef> _none = new List<WeaponDef>();

        /// <summary>Only unique creatures have weapons (the same rule as hand colours).</summary>
        public static bool CanArm(CreatureBoardAsset c) => HandColors.CanColor(c);

        /// <summary>The weapons added to this creature, in the order they were added.</summary>
        public static List<WeaponDef> WeaponsOf(CreatureBoardAsset c)
        {
            if (!CanArm(c)) return _none;
            Load();
            var list = new List<WeaponDef>();
            if (_saved.TryGetValue(KeyOf(c), out var l))
                foreach (var id in l.Weapons) { var w = Find(id); if (w != null) list.Add(w); }
            return list;
        }

        public static bool Has(CreatureBoardAsset c, string id)
        {
            if (!CanArm(c)) return false;
            Load();
            return _saved.TryGetValue(KeyOf(c), out var l) && l.Weapons.Contains(id);
        }

        /// <summary>The weapon this creature holds in its right hand (null: unarmed).</summary>
        public static WeaponDef EquippedOf(CreatureBoardAsset c)
        {
            if (!CanArm(c)) return null;
            Load();
            return _saved.TryGetValue(KeyOf(c), out var l) && l.Equipped != null ? Find(l.Equipped) : null;
        }

        /// <summary>Add a catalogue weapon to this creature (or take it away again: remove).</summary>
        public static void SetAdded(CreatureBoardAsset c, string id, bool added)
        {
            if (!CanArm(c) || Find(id) == null) return;
            Load();
            string k = KeyOf(c);
            if (!_saved.TryGetValue(k, out var l)) _saved[k] = l = new Loadout();
            if (added && !l.Weapons.Contains(id)) l.Weapons.Add(id);
            if (!added)
            {
                l.Weapons.Remove(id);
                if (l.Equipped == id) l.Equipped = null;
            }
            if (l.Weapons.Count == 0 && l.Equipped == null) _saved.Remove(k);
            Save();
            Plugin.Log.LogInfo($"Weapons of '{c.Name}': {(added ? "added" : "removed")} {id}");
        }

        /// <summary>Hold one of its weapons (null: unarmed).</summary>
        public static void Equip(CreatureBoardAsset c, string id)
        {
            if (!CanArm(c)) return;
            Load();
            string k = KeyOf(c);
            if (!_saved.TryGetValue(k, out var l))
            {
                if (id == null) return;
                _saved[k] = l = new Loadout();
            }
            if (id != null && !l.Weapons.Contains(id)) l.Weapons.Add(id);
            l.Equipped = id;
            if (l.Weapons.Count == 0 && l.Equipped == null) _saved.Remove(k);
            Save();
            Plugin.Log.LogInfo($"Weapons of '{c.Name}': holding {id ?? "nothing"}");
        }

        /// <summary>Every few seconds: creatures on this board that have weapons but are no longer unique lose them.</summary>
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
                try { unique = CanArm(c); } catch { continue; }
                if (unique) continue;
                _saved.Remove(k);
                changed = true;
                Plugin.Log.LogInfo($"Weapons of '{c.Name}' cleared: no longer a unique creature");
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
                    if (line.StartsWith("#")) continue;
                    var p = line.Trim().Split(' ');
                    if (p.Length < 3) continue;
                    var l = new Loadout { Equipped = p[2] == "-" ? null : p[2] };
                    if (p.Length > 3)
                        foreach (var id in p[3].Split(','))
                            if (Find(id) != null && !l.Weapons.Contains(id)) l.Weapons.Add(id);
                    _saved[p[0] + " " + p[1]] = l;
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Weapons could not be read: " + e.Message); }
        }

        private static void Save()
        {
            try
            {
                var lines = new List<string> { "# TaleSpire VR: first-person weapons of unique creatures (campaign creature held|- weapon,weapon,...)" };
                foreach (var kv in _saved) lines.Add(kv.Key + " " + (kv.Value.Equipped ?? "-") + " " + string.Join(",", kv.Value.Weapons));
                File.WriteAllLines(FilePath, lines);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Weapons could not be saved: " + e.Message); }
        }
    }
}
