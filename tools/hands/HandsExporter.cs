using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Exports the PolyOne "Free Pack - VR Hands (Rigged)" prefab into a small binary file the TaleSpire VR mod rebuilds
// at runtime (TaleSpire is Unity 2021.3 and can't load this editor's asset bundles).
public static class HandsExporter
{
    const string Prefab = "Assets/PolyOne/Free VR Hands/Prefabs/Free Pack - VR Hands ( Rigged ).prefab";
    const string Idle = "Assets/PolyOne/Free VR Hands/Animation/VR Hands_Idle.anim";
    static readonly string[] Fingers = { "Thumb", "Index", "Middle", "Ring", "Pinky" };

    public static void Export()
    {
        string outPath = System.Environment.GetEnvironmentVariable("HANDS_OUT");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
        if (prefab == null) { Debug.LogError("HANDS: prefab not found"); EditorApplication.Exit(2); return; }
        var go = Object.Instantiate(prefab);
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Idle);
        if (clip != null) { clip.SampleAnimation(go, 0f); Debug.Log("HANDS: sampled idle pose"); }

        var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
        var mesh = smr.sharedMesh;
        var all = new List<Transform>(go.GetComponentsInChildren<Transform>(true));
        int Index(Transform t) => t == null ? -1 : all.IndexOf(t);
        Transform Find(string n) => all.Find(t => t.name == n);

        using (var w = new BinaryWriter(File.Create(outPath)))
        {
            w.Write("TSVRHAND1");
            // Transforms (index 0 = prefab root), local TRS in the idle pose.
            w.Write(all.Count);
            foreach (var t in all)
            {
                w.Write(t.name);
                w.Write(t == go.transform ? -1 : Index(t.parent));
                V3(w, t.localPosition); Q(w, t.localRotation); V3(w, t.localScale);
            }
            w.Write(Index(smr.transform));
            w.Write(Index(smr.rootBone));
            // Mesh
            var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv;
            w.Write(v.Length);
            foreach (var p in v) V3(w, p);
            foreach (var p in n) V3(w, p);
            w.Write(uv.Length);
            foreach (var p in uv) { w.Write(p.x); w.Write(p.y); }
            w.Write(mesh.subMeshCount);
            for (int s = 0; s < mesh.subMeshCount; s++) { var tri = mesh.GetTriangles(s); w.Write(tri.Length); foreach (var i in tri) w.Write(i); }
            var bw = mesh.boneWeights;
            w.Write(bw.Length);
            foreach (var b in bw)
            {
                w.Write(b.boneIndex0); w.Write(b.boneIndex1); w.Write(b.boneIndex2); w.Write(b.boneIndex3);
                w.Write(b.weight0); w.Write(b.weight1); w.Write(b.weight2); w.Write(b.weight3);
            }
            w.Write(smr.bones.Length);
            foreach (var b in smr.bones) w.Write(Index(b));
            var bp = mesh.bindposes;
            w.Write(bp.Length);
            foreach (var m in bp) for (int i = 0; i < 16; i++) w.Write(m[i]);

            // Per hand: wrist + frame vectors (wrist local), finger joints with their curl axes (joint local).
            foreach (var side in new[] { "Left", "Right" })
            {
                var wrist = Find($"J_{side}_Hand");
                var i1 = Find($"J_{side}_HandIndex1"); var m1 = Find($"J_{side}_HandMiddle1"); var p1 = Find($"J_{side}_HandPinky1");
                w.Write(Index(wrist));
                V3(w, wrist.InverseTransformPoint(m1.position));                         // wrist -> middle knuckle
                V3(w, wrist.InverseTransformVector(p1.position - i1.position));           // knuckle line (index -> pinky)
                // Middle finger's bend axis is the reference sign for the other long fingers.
                Vector3 refAxis = BendAxis(Find($"J_{side}_HandMiddle1"), Find($"J_{side}_HandMiddle2"), Find($"J_{side}_HandMiddle3"), Vector3.zero);
                if (refAxis == Vector3.zero) refAxis = (wrist.TransformVector(Vector3.Cross(m1.position - wrist.position, p1.position - i1.position))).normalized;
                foreach (var f in Fingers)
                {
                    var j1 = Find($"J_{side}_Hand{f}1"); var j2 = Find($"J_{side}_Hand{f}2"); var j3 = Find($"J_{side}_Hand{f}3");
                    Vector3 axis = BendAxis(j1, j2, j3, f == "Thumb" ? Vector3.zero : refAxis);
                    if (axis == Vector3.zero) axis = refAxis;
                    foreach (var j in new[] { j1, j2, j3 })
                    {
                        w.Write(Index(j));
                        V3(w, j.InverseTransformDirection(axis).normalized);
                    }
                    Debug.Log($"HANDS: {side} {f} axis {axis}");
                }
            }
        }
        Debug.Log($"HANDS: wrote {outPath} ({all.Count} transforms, {mesh.vertexCount} vertices, {smr.bones.Length} bones)");
        EditorApplication.Exit(0);
    }

    // Axis about which the finger already bends in the idle pose (cross of consecutive segments), sign-matched to ref.
    static Vector3 BendAxis(Transform j1, Transform j2, Transform j3, Vector3 reference)
    {
        if (j1 == null || j2 == null || j3 == null) return Vector3.zero;
        Vector3 a = Vector3.Cross(j2.position - j1.position, j3.position - j2.position);
        if (a.sqrMagnitude < 1e-10f) return reference;
        a.Normalize();
        if (reference != Vector3.zero && Vector3.Dot(a, reference) < 0f) a = -a;
        return a;
    }

    static void V3(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
    static void Q(BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
}
