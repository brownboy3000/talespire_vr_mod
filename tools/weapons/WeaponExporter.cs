using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Renders previews of the weapon candidates and exports the chosen ones into weapons.bin, which the TaleSpire VR mod
// rebuilds at runtime (TaleSpire is Unity 2021.3 and can't load this editor's asset bundles).
public static class WeaponExporter
{
    const string LowPoly = "Assets/Low Poly Weapons VOL.1/Prefabs/";
    const string Blink = "Assets/Blink/Art/Weapons/Stylized/";
    const string Melee = "Assets/Donthrukat/Simple low poly melee weapon pack/Prefabs/";
    const string SciFi = "Assets/gun/SSG_Guns_Pack_01_Demo/Prefabs/";
    const string Rpg = "Assets/Blink/Art/Weapons/LowPoly/FreeRPGWeapons/_PREFABS/";
    const string Aged = "Assets/Aged Medieval Weapons/Prefabs/";
    const string Shotguns = "Assets/Low Poly ShotGun Weapon Pack 2/Prefabs/Weapons/";

    static readonly Dictionary<string, string> Candidates = new Dictionary<string, string>
    {
        { "shotgun", LowPoly + "Bennelli_M4.prefab" },
        { "sword1", Blink + "Swords/_PrefabsSwords/Sword1_1_3.prefab" },
        { "sword2", Blink + "Swords/_PrefabsSwords/Sword2_3_3.prefab" },
        { "sword3", Blink + "Swords/_PrefabsSwords/Sword3_1_3.prefab" },
        { "sword5", Blink + "Swords/_PrefabsSwords/Sword5_3_2.prefab" },
        // Firearms
        { "m1911", LowPoly + "M1911.prefab" }, { "uzi", LowPoly + "Uzi.prefab" }, { "ak74", LowPoly + "AK74.prefab" },
        { "m4", LowPoly + "M4_8.prefab" }, { "m249", LowPoly + "M249.prefab" }, { "m107", LowPoly + "M107.prefab" },
        { "rpg7", LowPoly + "RPG7.prefab" }, { "musket", Blink + "Musket/_Prefabs_Musket/Musket1_2_1.prefab" },
        { "ssg01", SciFi + "SSG_Guns_Pack_01.prefab" }, { "ssg06", SciFi + "SSG_Guns_Pack_06.prefab" }, { "ssg07", SciFi + "SSG_Guns_Pack_07.prefab" },
        // Melee
        { "axeb1", Blink + "Axes/PrefabsAxes/AxeBasic1_2.prefab" }, { "axeb2", Blink + "Axes/PrefabsAxes/AxeBasic2_1.prefab" },
        { "axeev3", Blink + "Axes/PrefabsAxes/AxeEvolving3_3_2.prefab" },
        { "dagger1", Blink + "Daggers/_PrefabsDaggers/Dagger1_3_5.prefab" }, { "dagger4", Blink + "Daggers/_PrefabsDaggers/Dagger4_1_3.prefab" },
        { "warhammer", Blink + "Hammers/_PrefabsHammers/Hammer1_1_3.prefab" },
        { "shield2", Blink + "Shields/_PrefabsShields/Shield2_1_2.prefab" }, { "shield3", Blink + "Shields/_PrefabsShields/Shield3_1_1.prefab" },
        { "polearm", Blink + "Polearms/_Prefabs_Polearms/Polearm2_2_2.prefab" }, { "scythe", Blink + "Scythes/_Prefabs_Scythes/Scythe1_3_2.prefab" },
        { "axe1", Melee + "Axes/axe_1.prefab" }, { "axe2", Melee + "Axes/axe_2.prefab" }, { "axe3", Melee + "Axes/axe_3.prefab" },
        { "axe4", Melee + "Axes/axe_4.prefab" }, { "axe5", Melee + "Axes/axe_5.prefab" }, { "axe6", Melee + "Axes/axe_6.prefab" },
        { "axe7", Melee + "Axes/axe_7.prefab" },
        { "bat1", Melee + "Baseballbats/baseballbat_1.prefab" }, { "bat2", Melee + "Baseballbats/baseballbat_2.prefab" },
        { "bat3", Melee + "Baseballbats/baseballbat_3.prefab" },
        { "crowbar1", Melee + "Crowbars/crowbar_1.prefab" }, { "crowbar2", Melee + "Crowbars/crowbar_2.prefab" },
        { "hammer1", Melee + "Hammers/hammer_1.prefab" }, { "hammer2", Melee + "Hammers/hammer_2.prefab" },
        { "hammer3", Melee + "Hammers/hammer_3.prefab" }, { "hammer4", Melee + "Hammers/hammer_4.prefab" },
        { "knife1", Melee + "Knives/knife_1.prefab" }, { "knife2", Melee + "Knives/knife_2.prefab" }, { "knife3", Melee + "Knives/knife_3.prefab" },
        { "knife4", Melee + "Knives/knife_4.prefab" }, { "knife5", Melee + "Knives/knife_5.prefab" }, { "knife6", Melee + "Knives/knife_6.prefab" },
        { "knife7", Melee + "Knives/knife_7.prefab" }, { "knife8", Melee + "Knives/knife_8.prefab" },
        { "sledge1", Melee + "Sledgehammers/sledgehammer_1.prefab" }, { "sledge2", Melee + "Sledgehammers/sledgehammer_2.prefab" },
        { "sledge3", Melee + "Sledgehammers/sledgehammer_3.prefab" },
        // Blink "FREE - RPG Weapons" (bows, spears, staffs, wands, maces, axes, shields: basic / medium / epic)
        { "bowb", Rpg + "Bow_Basic.prefab" }, { "bowm", Rpg + "Bow_Medium.prefab" }, { "bowe", Rpg + "Bow_Epic.prefab" },
        { "spear1e", Rpg + "Spear1H_Epic.prefab" }, { "spear1e2", Rpg + "Spear1H_Epic2.prefab" }, { "spear2e", Rpg + "Spear2H_Epic.prefab" },
        { "staffm", Rpg + "Staff_Medium.prefab" }, { "staffe", Rpg + "Staff_Epic.prefab" },
        { "wandm", Rpg + "Wand_Medium.prefab" }, { "wande", Rpg + "Wand_Epic.prefab" },
        { "mace1e", Rpg + "Mace1H_Epic.prefab" }, { "mace2e", Rpg + "Mace2H_Epic.prefab" }, { "axe2e", Rpg + "Axe2H_Epic.prefab" },
        { "shielde", Rpg + "Shield_Epic.prefab" },
        // Alvarolopz98 "Aged Medieval Weapons - Free"
        { "halberd", Aged + "Halberd Prefab.prefab" }, { "amace", Aged + "Mace Prefab.prefab" }, { "machete", Aged + "Machete Prefab.prefab" },
        { "ashield", Aged + "Shield Prefab.prefab" }, { "shortsword", Aged + "Short Sword Prefab.prefab" }, { "asword", Aged + "Sword Prefab.prefab" },
        { "throwaxe", Aged + "Throwing Axe.prefab" }, { "abattleaxe", Aged + "Battle Axe Prefab.prefab" },
        // CASTLE BRAVO "Low Poly ShotGun Weapon Pack 2"
        { "sgf", Shotguns + "ShotGun_F.prefab" }, { "sgg", Shotguns + "ShotGun_G.prefab" }, { "sgh", Shotguns + "ShotGun_H.prefab" },
        { "sgi", Shotguns + "ShotGun_I.prefab" }, { "sgj", Shotguns + "ShotGun_J.prefab" },
    };

    /// <summary>WEAPONS_KEYS = "a,b,c": only these candidates (Preview / Side); empty = all.</summary>
    static IEnumerable<string> Keys()
    {
        string only = System.Environment.GetEnvironmentVariable("WEAPONS_KEYS");
        if (string.IsNullOrEmpty(only)) return Candidates.Keys;
        return only.Split(',');
    }

    public static void Preview()
    {
        string outDir = System.Environment.GetEnvironmentVariable("WEAPONS_PREVIEW");
        Directory.CreateDirectory(outDir);
        var rt = new RenderTexture(768, 768, 24, RenderTextureFormat.ARGB32);
        var camGo = new GameObject("cam");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.6f, 0.65f, 1f);
        cam.targetTexture = rt;
        var lightGo = new GameObject("light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;

        foreach (var key in Keys())
        {
            var kv = new KeyValuePair<string, string>(key, Candidates[key]);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kv.Value);
            if (prefab == null) { Debug.LogError("WEAPON: missing " + kv.Value); continue; }
            var go = Object.Instantiate(prefab);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var b = Bounds(go);
            Describe(kv.Key, go, b);
            float ext = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            var axes = Axes(ext * 0.35f);
            var views = new (string, Vector3, Vector3)[] { ("px", Vector3.right, Vector3.up), ("py", Vector3.up, Vector3.forward), ("pz", Vector3.forward, Vector3.up), ("persp", new Vector3(1f, 0.6f, -0.8f).normalized, Vector3.up) };
            foreach (var (name, dir, up) in views)
            {
                cam.transform.position = b.center + dir * ext * 3f;
                cam.transform.rotation = Quaternion.LookRotation(-dir, up);
                cam.orthographicSize = ext * 0.6f;
                cam.nearClipPlane = 0.001f;
                cam.farClipPlane = ext * 10f;
                lightGo.transform.rotation = Quaternion.LookRotation(-dir + Vector3.down * 0.5f + Vector3.left * 0.3f);
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, $"{kv.Key}_{name}.png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            Object.DestroyImmediate(axes);
            Object.DestroyImmediate(go);
        }
        Debug.Log("WEAPON: previews written to " + outDir);
        EditorApplication.Exit(0);
    }

    // Side views with a grid in model coordinates, to place grips, muzzles, blades and shield handles by eye: looking along
    // the model's thinnest axis, its longest axis to the right. Minor lines every `step`, every fifth darker (blue), the
    // zero lines red. The log gives each picture's mapping (centre, axes, model units per pixel).
    public static void Side()
    {
        string outDir = System.Environment.GetEnvironmentVariable("WEAPONS_PREVIEW");
        Directory.CreateDirectory(outDir);
        const int W = 1600, H = 1000;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
        var cam = new GameObject("cam").AddComponent<Camera>();
        cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.93f, 0.93f, 0.93f, 1f); cam.targetTexture = rt;
        var light = new GameObject("light").AddComponent<Light>(); light.type = LightType.Directional;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f);
        var sh = Shader.Find("Unlit/Color");
        var minorMat = new Material(sh) { color = new Color(0.75f, 0.75f, 0.82f) };
        var majorMat = new Material(sh) { color = new Color(0.2f, 0.25f, 0.9f) };
        var zeroMat = new Material(sh) { color = new Color(0.95f, 0.1f, 0.1f) };
        foreach (var key in Keys())
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Candidates[key]));
            go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            var b = Bounds(go);
            Describe(key, go, b);
            Vector3 s = b.size;
            int thin = s.x <= s.y && s.x <= s.z ? 0 : (s.y <= s.z ? 1 : 2);
            int a = (thin + 1) % 3, c = (thin + 2) % 3;
            if (s[a] < s[c]) { int t = a; a = c; c = t; }
            Vector3 right = Vector3.zero, up = Vector3.zero; right[a] = 1f; up[c] = 1f;
            Vector3 fwd = Vector3.Cross(right, up);
            float halfH = Mathf.Max(s[c], s[a] / ((float)W / H)) * 0.56f;
            float step = 0.005f;
            foreach (var st in new[] { 0.005f, 0.01f, 0.02f, 0.05f, 0.1f }) { step = st; if (halfH * 2f / st <= 60f) break; }
            var grid = new GameObject("grid");
            float halfW = halfH * W / H;
            Vector3 behind = b.center + fwd * (s[thin] * 0.5f + 0.01f);
            int i0 = Mathf.FloorToInt((Vector3.Dot(b.center, right) - halfW) / step), i1 = Mathf.CeilToInt((Vector3.Dot(b.center, right) + halfW) / step);
            int j0 = Mathf.FloorToInt((Vector3.Dot(b.center, up) - halfH) / step), j1 = Mathf.CeilToInt((Vector3.Dot(b.center, up) + halfH) / step);
            float th = halfH * 2f / H * 1.5f;
            for (int i = i0; i <= i1; i++)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.transform.SetParent(grid.transform);
                g.transform.position = behind - right * Vector3.Dot(behind, right) + right * (i * step) - up * Vector3.Dot(behind, up) + up * Vector3.Dot(b.center, up);
                g.transform.rotation = Quaternion.LookRotation(fwd, up);
                g.transform.localScale = new Vector3(i == 0 ? th * 2f : th, halfH * 2.2f, 0.0001f);
                g.GetComponent<Renderer>().sharedMaterial = i == 0 ? zeroMat : i % 5 == 0 ? majorMat : minorMat;
            }
            for (int j = j0; j <= j1; j++)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.transform.SetParent(grid.transform);
                g.transform.position = behind - up * Vector3.Dot(behind, up) + up * (j * step) - right * Vector3.Dot(behind, right) + right * Vector3.Dot(b.center, right);
                g.transform.rotation = Quaternion.LookRotation(fwd, up);
                g.transform.localScale = new Vector3(halfW * 2.2f, j == 0 ? th * 2f : th, 0.0001f);
                g.GetComponent<Renderer>().sharedMaterial = j == 0 ? zeroMat : j % 5 == 0 ? majorMat : minorMat;
            }
            cam.transform.position = b.center - fwd * (s.magnitude * 3f + 1f);
            cam.transform.rotation = Quaternion.LookRotation(fwd, up);
            cam.orthographicSize = halfH; cam.nearClipPlane = 0.01f; cam.farClipPlane = s.magnitude * 6f + 3f;
            light.transform.rotation = Quaternion.LookRotation(fwd + up * -0.5f + right * 0.3f);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(outDir, $"{key}_side.png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            string axis(int k) => "xyz"[k].ToString();
            Debug.Log($"WEAPON: side {key}: right = +{axis(a)}, up = +{axis(c)}, looking along +{axis(thin)} (model {axis(thin)} {b.min[thin]:F4}..{b.max[thin]:F4}); picture centre ({b.center[a]:F4}, {b.center[c]:F4}), {halfH * 2f / H:F6} units/pixel, grid every {step} (blue every {step * 5f})");
            Object.DestroyImmediate(grid);
            Object.DestroyImmediate(go);
        }
        EditorApplication.Exit(0);
    }

    // Side view (from +X) of the shotgun's grip area with a 1 cm grid (every 5 cm darker), to measure the pistol grip.
    public static void Zoom()
    {
        string outDir = System.Environment.GetEnvironmentVariable("WEAPONS_PREVIEW");
        var rt = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32);
        var cam = new GameObject("cam").AddComponent<Camera>();
        cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.9f, 0.9f, 0.9f, 1f); cam.targetTexture = rt;
        var light = new GameObject("light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.LookRotation(new Vector3(-1f, -0.5f, 0.3f));
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f);
        var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Candidates["shotgun"]));
        go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity;
        var sh = Shader.Find("Unlit/Color");
        var grid = new GameObject("grid");
        Vector3 c = new Vector3(0f, -0.08f, 0.17f); float half = 0.12f;
        for (int i = -12; i <= 12; i++)
        {
            float t = i * 0.01f; bool major = i % 5 == 0;
            var col = major ? new Color(0.2f, 0.2f, 0.9f) : new Color(0.7f, 0.7f, 0.8f);
            float th = major ? 0.0008f : 0.0004f;
            var h = GameObject.CreatePrimitive(PrimitiveType.Cube); h.transform.SetParent(grid.transform); h.transform.position = new Vector3(-0.05f, c.y + t, c.z); h.transform.localScale = new Vector3(0.0001f, th, half * 2f); h.GetComponent<Renderer>().sharedMaterial = new Material(sh) { color = col };
            var v = GameObject.CreatePrimitive(PrimitiveType.Cube); v.transform.SetParent(grid.transform); v.transform.position = new Vector3(-0.05f, c.y, c.z + t); v.transform.localScale = new Vector3(0.0001f, half * 2f, th); v.GetComponent<Renderer>().sharedMaterial = new Material(sh) { color = col };
        }
        cam.transform.position = c + Vector3.right * 1f; cam.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
        cam.orthographicSize = half; cam.nearClipPlane = 0.01f; cam.farClipPlane = 3f;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(outDir, "shotgun_grip.png"), tex.EncodeToPNG());
        Debug.Log($"WEAPON: zoom centre {c} half {half} (screen right = +z, up = +y)");
        EditorApplication.Exit(0);
    }

    // Cross-section profiles, to place the grips and tips.
    public static void Analyze()
    {
        foreach (var key in new[] { "shotgun", "sword1" })
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Candidates[key]));
            go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            var pts = new List<Vector3>();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                foreach (var v in mf.sharedMesh.vertices) pts.Add(mf.transform.TransformPoint(v));
            bool gun = key == "shotgun";
            // Slices along the long axis (z for the gun, y for the sword).
            float lo = gun ? -0.5f : -0.6f, hi = gun ? 0.5f : 0.6f;
            for (float s = lo; s < hi; s += 0.02f)
            {
                float a0 = float.MaxValue, a1 = float.MinValue, b0 = float.MaxValue, b1 = float.MinValue; int n = 0;
                foreach (var p in pts)
                {
                    float along = gun ? p.z : p.y;
                    if (along < s || along >= s + 0.02f) continue;
                    float a = gun ? p.y : p.x, b = gun ? p.x : p.z;
                    a0 = Mathf.Min(a0, a); a1 = Mathf.Max(a1, a); b0 = Mathf.Min(b0, b); b1 = Mathf.Max(b1, b); n++;
                }
                if (n > 0) Debug.Log($"WEAPON: {key} slice {s:F2} n={n} {(gun ? "y" : "x")} {a0:F4}..{a1:F4} {(gun ? "x" : "z")} {b0:F4}..{b1:F4}");
            }
            if (gun)
            {
                // Pistol grip: below the receiver; y slices of its z range.
                for (float y = -0.16f; y < 0.04f; y += 0.01f)
                {
                    float z0 = float.MaxValue, z1 = float.MinValue; int n = 0;
                    foreach (var p in pts) { if (p.y < y || p.y >= y + 0.01f || p.z < 0.05f) continue; z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z); n++; }
                    if (n > 0) Debug.Log($"WEAPON: shotgun yslice {y:F2} n={n} z {z0:F4}..{z1:F4}");
                }
                float minZ = float.MaxValue; foreach (var p in pts) minZ = Mathf.Min(minZ, p.z);
                Vector3 c = Vector3.zero; int m = 0; float y0 = float.MaxValue, y1 = float.MinValue;
                foreach (var p in pts) if (p.z < minZ + 0.004f) { c += p; m++; y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y); }
                Debug.Log($"WEAPON: shotgun muzzle centroid {(c / Mathf.Max(1, m)):F4} n={m} y {y0:F4}..{y1:F4}");
            }
            Object.DestroyImmediate(go);
        }
        EditorApplication.Exit(0);
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static void Describe(string key, GameObject go, Bounds b)
    {
        Debug.Log($"WEAPON: {key} bounds center {b.center:F4} size {b.size:F4}");
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            var m = mf.sharedMesh;
            var mr = mf.GetComponent<MeshRenderer>();
            Debug.Log($"WEAPON: {key}   part '{mf.name}' mesh '{m.name}' verts {m.vertexCount} subs {m.subMeshCount} local pos {mf.transform.position:F4} rot {mf.transform.rotation.eulerAngles:F1} scale {mf.transform.lossyScale:F4} meshBounds {m.bounds.center:F4}/{m.bounds.size:F4}");
            foreach (var mat in mr.sharedMaterials)
            {
                if (mat == null) continue;
                string T(string p) => Tex(mat, p) != null ? AssetDatabase.GetAssetPath(Tex(mat, p)) : "-";
                Debug.Log($"WEAPON: {key}     mat '{mat.name}' shader {mat.shader.name} main {T("_MainTex")} bump {T("_BumpMap")} metal {T("_MetallicGlossMap")} occl {T("_OcclusionMap")} emis {T("_EmissionMap")} color {MainColor(mat)}");
            }
        }
    }

    static GameObject Axes(float len)
    {
        var root = new GameObject("axes");
        var sh = Shader.Find("Unlit/Color");
        void Bar(Vector3 dir, Color c)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.transform.SetParent(root.transform, false);
            g.transform.localPosition = dir * len * 0.5f;
            g.transform.localScale = new Vector3(dir.x != 0 ? len : len * 0.02f, dir.y != 0 ? len : len * 0.02f, dir.z != 0 ? len : len * 0.02f);
            g.GetComponent<Renderer>().sharedMaterial = new Material(sh) { color = c };
        }
        Bar(Vector3.right, Color.red);
        Bar(Vector3.up, Color.green);
        Bar(Vector3.forward, Color.blue);
        return root;
    }

    // ---- Export ----

    // WEAPONS_EXPORT = "id=prefabKey;id=prefabKey" (keys from Candidates).
    public static void Export()
    {
        string outPath = System.Environment.GetEnvironmentVariable("WEAPONS_OUT");
        string list = System.Environment.GetEnvironmentVariable("WEAPONS_EXPORT");
        int texSize = 1024;
        using (var w = new BinaryWriter(File.Create(outPath)))
        {
            w.Write("TSVRWPN2");   // (2: each model is followed by its catalogue icon)
            var entries = list.Split(';');
            w.Write(entries.Length);
            foreach (var e in entries)
            {
                var parts = e.Split('=');
                string id = parts[0], key = parts[1];
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Candidates[key]);
                var go = Object.Instantiate(prefab);
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                w.Write(id);

                // All parts' meshes, baked into the prefab root's space; one submesh per (part, material).
                var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tans = new List<Vector4>(); var uvs = new List<Vector2>();
                var subs = new List<int[]>(); var subMats = new List<Material>();
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                {
                    var m = mf.sharedMesh;
                    var mats = mf.GetComponent<MeshRenderer>().sharedMaterials;
                    var l2w = mf.transform.localToWorldMatrix;
                    int baseV = verts.Count;
                    var v = m.vertices; var n = m.normals; var t = m.tangents; var uv = m.uv;
                    for (int i = 0; i < v.Length; i++)
                    {
                        verts.Add(l2w.MultiplyPoint3x4(v[i]));
                        norms.Add(n.Length == v.Length ? l2w.MultiplyVector(n[i]).normalized : Vector3.up);
                        if (t.Length == v.Length) { var tv = l2w.MultiplyVector(new Vector3(t[i].x, t[i].y, t[i].z)).normalized; tans.Add(new Vector4(tv.x, tv.y, tv.z, t[i].w)); }
                        else tans.Add(new Vector4(1, 0, 0, 1));
                        uvs.Add(uv.Length == v.Length ? uv[i] : Vector2.zero);
                    }
                    // A mirrored part flips its triangles.
                    bool flip = l2w.determinant < 0f;
                    for (int s = 0; s < m.subMeshCount; s++)
                    {
                        var tri = m.GetTriangles(s);
                        for (int i = 0; i < tri.Length; i++) tri[i] += baseV;
                        if (flip) for (int i = 0; i < tri.Length; i += 3) { int a = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = a; }
                        subs.Add(tri);
                        subMats.Add(mats[Mathf.Min(s, mats.Length - 1)]);
                    }
                }
                w.Write(verts.Count);
                foreach (var p in verts) V3(w, p);
                foreach (var p in norms) V3(w, p);
                foreach (var p in tans) { w.Write(p.x); w.Write(p.y); w.Write(p.z); w.Write(p.w); }
                foreach (var p in uvs) { w.Write(p.x); w.Write(p.y); }
                w.Write(subs.Count);
                for (int s = 0; s < subs.Count; s++)
                {
                    w.Write(subs[s].Length);
                    foreach (var i in subs[s]) w.Write(i);
                    var mat = subMats[s];
                    C(w, MainColor(mat));
                    C(w, Tex(mat, "_EmissionMap") != null || (mat.HasProperty("_EmissionColor") && mat.IsKeywordEnabled("_EMISSION")) ? SavedColor(mat, "_EmissionColor", Color.black) : Color.black);
                    w.Write(SavedFloat(mat, "_Glossiness", SavedFloat(mat, "_Smoothness", 0.4f)));
                    w.Write(SavedFloat(mat, "_GlossMapScale", 1f));
                    w.Write(SavedFloat(mat, "_Metallic", 0f));
                    foreach (var prop in new[] { "_MainTex", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap" })
                    {
                        var tex = Tex(mat, prop);
                        byte[] png = tex != null ? Png(AssetDatabase.GetAssetPath(tex), texSize) : new byte[0];
                        w.Write(png.Length);
                        w.Write(png);
                        Debug.Log($"WEAPON: {id} sub {s} {prop} {(tex != null ? AssetDatabase.GetAssetPath(tex) : "-")} {png.Length} bytes");
                    }
                }
                byte[] icon = IconPng(go, 160);
                w.Write(icon.Length);
                w.Write(icon);
                string iconDir = System.Environment.GetEnvironmentVariable("WEAPONS_PREVIEW");
                if (!string.IsNullOrEmpty(iconDir)) { Directory.CreateDirectory(iconDir); File.WriteAllBytes(Path.Combine(iconDir, $"icon_{id}.png"), icon); }
                Debug.Log($"WEAPON: exported {id} ({key}): {verts.Count} verts, {subs.Count} submeshes, icon {icon.Length} bytes");
                Object.DestroyImmediate(go);
            }
        }
        Debug.Log("WEAPON: wrote " + outPath);
        EditorApplication.Exit(0);
    }

    // The Weapon Catalogue's picture of a model: seen from its broad side, its longest axis along the diagonal, lit from
    // the front, on a transparent background (rendered over black and over white: alpha from the difference).
    static byte[] IconPng(GameObject go, int size)
    {
        // Materials whose shader is missing here (URP): a Standard stand-in with the same textures.
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || mats[i].shader.name != "Hidden/InternalErrorShader") continue;
                var std = new Material(Shader.Find("Standard")) { mainTexture = Tex(mats[i], "_MainTex"), color = MainColor(mats[i]) };
                var em = Tex(mats[i], "_EmissionMap");
                if (em != null) { std.EnableKeyword("_EMISSION"); std.SetTexture("_EmissionMap", em); std.SetColor("_EmissionColor", Color.white); }
                mats[i] = std;
            }
            r.sharedMaterials = mats;
        }
        var b = Bounds(go);
        Vector3 s = b.size;
        int thin = s.x <= s.y && s.x <= s.z ? 0 : (s.y <= s.z ? 1 : 2);
        int a = (thin + 1) % 3, c = (thin + 2) % 3;
        if (s[a] < s[c]) { int t = a; a = c; c = t; }
        Vector3 right = Vector3.zero, up = Vector3.zero; right[a] = 1f; up[c] = 1f;
        Vector3 fwd = Vector3.Cross(right, up);
        Vector3 camUp = Quaternion.AngleAxis(-40f, fwd) * up;   // the long axis rises to the right
        Vector3 camRight = Vector3.Cross(camUp, fwd);
        float half = 0f;
        for (int i = 0; i < 8; i++)
        {
            Vector3 p = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z) - b.center;
            half = Mathf.Max(half, Mathf.Abs(Vector3.Dot(p, camRight)), Mathf.Abs(Vector3.Dot(p, camUp)));
        }
        var camGo = new GameObject("iconcam");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = half * 1.08f;
        cam.transform.position = b.center - fwd * (s.magnitude * 3f + 1f);
        cam.transform.rotation = Quaternion.LookRotation(fwd, camUp);
        cam.nearClipPlane = 0.01f; cam.farClipPlane = s.magnitude * 6f + 3f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        var lightGo = new GameObject("iconlight");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.25f;
        lightGo.transform.rotation = Quaternion.LookRotation(fwd - camUp * 0.6f + camRight * 0.4f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.55f);
        var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        Color[] Shot(Color bg)
        {
            cam.backgroundColor = bg;
            cam.Render();
            RenderTexture.active = rt;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, size, size), 0, 0); t.Apply();
            RenderTexture.active = null;
            var px = t.GetPixels();
            Object.DestroyImmediate(t);
            return px;
        }
        var onBlack = Shot(Color.black);
        var onWhite = Shot(Color.white);
        var outPx = new Color[onBlack.Length];
        for (int i = 0; i < outPx.Length; i++)
        {
            Color k = onBlack[i], wh = onWhite[i];
            float alpha = Mathf.Clamp01(1f - ((wh.r - k.r) + (wh.g - k.g) + (wh.b - k.b)) / 3f);
            outPx[i] = alpha > 0.002f ? new Color(Mathf.Clamp01(k.r / alpha), Mathf.Clamp01(k.g / alpha), Mathf.Clamp01(k.b / alpha), alpha) : new Color(0f, 0f, 0f, 0f);
        }
        var icon = new Texture2D(size, size, TextureFormat.RGBA32, false);
        icon.SetPixels(outPx); icon.Apply();
        byte[] png = icon.EncodeToPNG();
        Object.DestroyImmediate(icon);
        cam.targetTexture = null;
        rt.Release();
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(lightGo);
        return png;
    }

    // The source image as PNG, scaled down (linear, no colour conversion) to at most max pixels.
    static byte[] Png(string assetPath, int max)
    {
        var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        src.LoadImage(File.ReadAllBytes(assetPath));
        if (src.width <= max && src.height <= max) return src.EncodeToPNG();
        int wpx = Mathf.Min(max, src.width), hpx = Mathf.Min(max, src.height);
        var rt = RenderTexture.GetTemporary(wpx, hpx, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        src.filterMode = FilterMode.Bilinear;
        Graphics.Blit(src, rt);
        RenderTexture.active = rt;
        var dst = new Texture2D(wpx, hpx, TextureFormat.RGBA32, false, true);
        dst.ReadPixels(new Rect(0, 0, wpx, hpx), 0, 0);
        dst.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        return dst.EncodeToPNG();
    }

    // Materials made for URP (the Sci-Fi pack) keep their values under URP's names (_BaseMap, _BaseColor) and their shader
    // is missing in this project: read the saved values by name, whatever the shader.
    static readonly Dictionary<string, string[]> TexAliases = new Dictionary<string, string[]>
    {
        { "_MainTex", new[] { "_MainTex", "_BaseMap", "_BaseColorMap", "_Albedo" } },
        { "_BumpMap", new[] { "_BumpMap", "_NormalMap" } },
        { "_MetallicGlossMap", new[] { "_MetallicGlossMap" } },
        { "_OcclusionMap", new[] { "_OcclusionMap" } },
        { "_EmissionMap", new[] { "_EmissionMap", "_EmissiveColorMap" } },
    };

    static Texture Tex(Material mat, string prop)
    {
        var so = new SerializedObject(mat);
        var envs = so.FindProperty("m_SavedProperties.m_TexEnvs");
        foreach (var name in TexAliases[prop])
        {
            if (mat.HasProperty(name) && mat.GetTexture(name) != null) return mat.GetTexture(name);
            for (int i = 0; envs != null && i < envs.arraySize; i++)
            {
                var e = envs.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("first").stringValue != name) continue;
                var t = e.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture;
                if (t != null) return t;
            }
        }
        return null;
    }

    static Color SavedColor(Material mat, string name, Color fallback)
    {
        if (mat.HasProperty(name)) return mat.GetColor(name);
        var cols = new SerializedObject(mat).FindProperty("m_SavedProperties.m_Colors");
        for (int i = 0; cols != null && i < cols.arraySize; i++)
        {
            var e = cols.GetArrayElementAtIndex(i);
            if (e.FindPropertyRelative("first").stringValue == name) return e.FindPropertyRelative("second").colorValue;
        }
        return fallback;
    }

    static float SavedFloat(Material mat, string name, float fallback)
    {
        if (mat.HasProperty(name)) return mat.GetFloat(name);
        var fl = new SerializedObject(mat).FindProperty("m_SavedProperties.m_Floats");
        for (int i = 0; fl != null && i < fl.arraySize; i++)
        {
            var e = fl.GetArrayElementAtIndex(i);
            if (e.FindPropertyRelative("first").stringValue == name) return e.FindPropertyRelative("second").floatValue;
        }
        return fallback;
    }

    static Color MainColor(Material mat) => mat.HasProperty("_Color") ? mat.color : SavedColor(mat, "_BaseColor", Color.white);

    static void V3(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
    static void C(BinaryWriter w, Color c) { w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a); }
}
