using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.ProBuilder;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;

// メニュー「IBProto > キャラを生成（ProBuilder）」
// StarterAssets の人型素体に、ProBuilder で作った角の落ちたアーマーを骨ごとに付けてシーンに保存する。
// 生成されたパーツは普通の ProBuilder オブジェクトなので、あとから ProBuilder で自由に編集できる。
public static class IBProtoCharaBuilder
{
    const string MODEL_PATH = "Assets/StarterAssets/ThirdPersonController/Character/Models/Armature.fbx";
    const string MAT_DIR = "Assets/IBProto/Characters/SF/Materials";

    static Transform root;
    static Animator anim;

    [MenuItem("IBProto/旧・生成ツール（作り直し用）/キャラを生成（ProBuilder）")]
    public static void Build()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(MODEL_PATH);
        if (model == null) { Debug.LogError("[IBProto] 素体が見つかりません: " + MODEL_PATH); return; }

        var old = GameObject.Find("PBCharacter");
        if (old != null) Object.DestroyImmediate(old);

        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = "PBCharacter";
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        root = go.transform;
        anim = go.GetComponent<Animator>();
        if (anim == null || !anim.isHuman) { Debug.LogError("[IBProto] 素体が Humanoid ではありません"); return; }

        // マテリアル（アセットとして保存）
        var armor = Mat("PB_Armor", new Color(0.9f, 0.89f, 0.86f), 0.15f, 0.6f);
        var armor2 = Mat("PB_Armor2", new Color(0.78f, 0.36f, 0.18f), 0.2f, 0.55f);
        var dark = Mat("PB_Dark", new Color(0.09f, 0.09f, 0.1f), 0.85f, 0.55f);
        var visor = Mat("PB_Visor", new Color(0.04f, 0.04f, 0.05f), 0.9f, 0.95f);
        var glow = Mat("PB_Glow", new Color(1f, 0.45f, 0.08f), 0f, 0.5f, new Color(1f, 0.45f, 0.08f) * 3f);
        var suit = Mat("PB_Suit", new Color(0.15f, 0.15f, 0.17f), 0.1f, 0.35f);
        foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            r.sharedMaterials = r.sharedMaterials.Select(_ => suit).ToArray();

        var H = HumanBodyBones.Head; var C = HumanBodyBones.UpperChest; var HP = HumanBodyBones.Hips;

        // ---- ヘルメット（角を落とした箱でSFっぽく）----
        Box("Helmet", H, new Vector3(0, 0.08f, 0.0f), new Vector3(0.215f, 0.235f, 0.25f), Vector3.zero, armor, 0.045f);
        Box("HelmetCrest", H, new Vector3(0, 0.19f, -0.01f), new Vector3(0.05f, 0.04f, 0.2f), Vector3.zero, armor2, 0.012f);
        Box("Visor", H, new Vector3(0, 0.075f, 0.118f), new Vector3(0.18f, 0.065f, 0.035f), new Vector3(-4f, 0, 0), visor, 0.014f);
        Box("Chin", H, new Vector3(0, -0.01f, 0.085f), new Vector3(0.12f, 0.05f, 0.07f), new Vector3(12f, 0, 0), dark, 0.014f);
        Cyl("EarL", H, new Vector3(-0.112f, 0.07f, -0.005f), new Vector3(0.055f, 0.02f, 0.055f), new Vector3(0, 0, 90f), dark);
        Cyl("EarR", H, new Vector3(0.112f, 0.07f, -0.005f), new Vector3(0.055f, 0.02f, 0.055f), new Vector3(0, 0, 90f), dark);
        Box("EarLight", H, new Vector3(0.123f, 0.07f, -0.005f), new Vector3(0.006f, 0.012f, 0.03f), Vector3.zero, glow, 0.002f);

        // ---- 胴 ----
        Box("Collar", C, new Vector3(0, 0.12f, -0.01f), new Vector3(0.22f, 0.045f, 0.17f), Vector3.zero, dark, 0.015f);
        Box("ChestL", C, new Vector3(-0.065f, 0.0f, 0.1f), new Vector3(0.13f, 0.16f, 0.045f), new Vector3(-8f, -12f, 0), armor, 0.02f);
        Box("ChestR", C, new Vector3(0.065f, 0.0f, 0.1f), new Vector3(0.13f, 0.16f, 0.045f), new Vector3(-8f, 12f, 0), armor, 0.02f);
        Box("ChestStripe", C, new Vector3(-0.075f, 0.055f, 0.126f), new Vector3(0.05f, 0.012f, 0.006f), new Vector3(-8f, -12f, 0), glow, 0.002f);
        Box("Abs", C, new Vector3(0, -0.115f, 0.09f), new Vector3(0.15f, 0.05f, 0.04f), Vector3.zero, dark, 0.012f);
        Box("Belt", HP, new Vector3(0, 0.05f, 0), new Vector3(0.28f, 0.045f, 0.19f), Vector3.zero, dark, 0.012f);
        Box("Buckle", HP, new Vector3(0, 0.05f, 0.1f), new Vector3(0.06f, 0.04f, 0.015f), Vector3.zero, armor2, 0.006f);
        Box("Pouch", HP, new Vector3(0.12f, 0.02f, 0.07f), new Vector3(0.06f, 0.07f, 0.04f), Vector3.zero, armor, 0.01f);

        // ---- バックパック（推進器付き）----
        Box("Pack", C, new Vector3(0, -0.02f, -0.13f), new Vector3(0.24f, 0.28f, 0.1f), Vector3.zero, armor, 0.03f);
        Box("PackCore", C, new Vector3(0, 0.0f, -0.185f), new Vector3(0.12f, 0.16f, 0.03f), Vector3.zero, dark, 0.01f);
        Cyl("ThrusterL", C, new Vector3(-0.085f, -0.06f, -0.2f), new Vector3(0.055f, 0.11f, 0.055f), Vector3.zero, dark);
        Cyl("ThrusterR", C, new Vector3(0.085f, -0.06f, -0.2f), new Vector3(0.055f, 0.11f, 0.055f), Vector3.zero, dark);
        Box("PackLight", C, new Vector3(0, 0.1f, -0.2f), new Vector3(0.12f, 0.016f, 0.008f), Vector3.zero, glow, 0.003f);

        // ---- 肩 ----
        Box("ShoulderL", HumanBodyBones.LeftUpperArm, new Vector3(-0.025f, 0.035f, 0), new Vector3(0.13f, 0.075f, 0.15f), new Vector3(0, 0, 28f), armor, 0.025f);
        Box("ShoulderR", HumanBodyBones.RightUpperArm, new Vector3(0.025f, 0.035f, 0), new Vector3(0.13f, 0.075f, 0.15f), new Vector3(0, 0, -28f), armor2, 0.025f);

        // ---- 腕 ----
        Along("ForearmL", HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.55f, new Vector3(0.075f, 0.13f, 0.075f), armor, 0.018f);
        Along("ForearmR", HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.55f, new Vector3(0.075f, 0.13f, 0.075f), armor, 0.018f);
        Along("UpperArmBandL", HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 0.55f, new Vector3(0.085f, 0.04f, 0.085f), dark, 0.01f);
        Along("UpperArmBandR", HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.55f, new Vector3(0.085f, 0.04f, 0.085f), dark, 0.01f);

        // ---- 脚 ----
        Along("Holster", HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.35f, new Vector3(0.05f, 0.1f, 0.085f), dark, 0.012f, new Vector3(0.07f, 0, 0));
        Box("KneeL", HumanBodyBones.LeftLowerLeg, new Vector3(0, 0.0f, 0.05f), new Vector3(0.075f, 0.075f, 0.04f), Vector3.zero, armor, 0.015f);
        Box("KneeR", HumanBodyBones.RightLowerLeg, new Vector3(0, 0.0f, 0.05f), new Vector3(0.075f, 0.075f, 0.04f), Vector3.zero, armor, 0.015f);
        Along("ShinL", HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.5f, new Vector3(0.068f, 0.15f, 0.03f), armor, 0.01f, new Vector3(0, 0, 0.04f));
        Along("ShinR", HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.5f, new Vector3(0.068f, 0.15f, 0.03f), armor, 0.01f, new Vector3(0, 0, 0.04f));

        // 表示用スクリプトを付け替え（キャラクリ画面のある旧スクリプトは外す）
        var holder = GameObject.Find("Chara") ?? new GameObject("Chara");
        foreach (var mb in holder.GetComponents<MonoBehaviour>()) if (mb != null && mb.GetType().Name == "ProtoCharacter") Object.DestroyImmediate(mb);
        if (holder.GetComponent<ProtoCharaViewer>() == null) holder.AddComponent<ProtoCharaViewer>();

        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(go.scene);
        EditorSceneManager.SaveScene(go.scene);
        int tris = 0;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh) tris += mf.sharedMesh.triangles.Length / 3;
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>()) if (smr.sharedMesh) tris += smr.sharedMesh.triangles.Length / 3;
        Debug.Log("[IBProto] キャラを生成しました。合計 " + tris + " tris（素体＋アーマー）");
    }

    // ================= 部品づくり =================
    static Transform Bone(HumanBodyBones b)
    {
        var t = anim.GetBoneTransform(b);
        if (t == null && b == HumanBodyBones.UpperChest) t = anim.GetBoneTransform(HumanBodyBones.Chest);
        return t;
    }

    static Vector3 BasePos(HumanBodyBones b)
    {
        if (b == HumanBodyBones.UpperChest && anim.GetBoneTransform(HumanBodyBones.Neck) != null)
            return anim.GetBoneTransform(HumanBodyBones.Neck).position + root.rotation * new Vector3(0, -0.17f, -0.01f);
        return Bone(b).position;
    }

    static Material Mat(string name, Color c, float metallic, float smooth, Color? emission = null)
    {
        IBProtoSamuraiBuilder.MakeFolder(MAT_DIR);
        string path = MAT_DIR + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
        }
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
        UnityEditor.EditorUtility.SetDirty(m);
        return m;
    }

    // ProBuilder の形を作り、指定サイズに合わせて頂点を変形し、角を落とす
    static ProBuilderMesh Shape(ShapeType type, Vector3 size, float bevel, Material mat, string name)
    {
        var pb = ShapeGenerator.CreateShape(type, PivotLocation.Center);
        pb.gameObject.name = name;
        var verts = pb.GetVertices();
        Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
        foreach (var v in verts) { min = Vector3.Min(min, v.position); max = Vector3.Max(max, v.position); }
        Vector3 c = (min + max) * 0.5f, ext = max - min;
        for (int i = 0; i < verts.Length; i++)
        {
            var p = verts[i].position - c;
            verts[i].position = new Vector3(p.x / ext.x * size.x, p.y / ext.y * size.y, p.z / ext.z * size.z);
        }
        pb.SetVertices(verts);
        pb.ToMesh(); pb.Refresh();
        if (bevel > 0f)
        {
            var edges = pb.faces.SelectMany(f => f.edges).Distinct().ToList();
            Bevel.BevelEdges(pb, edges, bevel);
            pb.ToMesh(); pb.Refresh();
        }
        pb.SetMaterial(pb.faces, mat);
        pb.ToMesh(); pb.Refresh();
        EditorMeshUtility.Optimize(pb);
        var col = pb.GetComponent<Collider>(); if (col) Object.DestroyImmediate(col);
        return pb;
    }

    static void Place(ProBuilderMesh pb, HumanBodyBones bone, Vector3 offset, Vector3 euler)
    {
        var b = Bone(bone); if (b == null) { Object.DestroyImmediate(pb.gameObject); return; }
        pb.transform.position = BasePos(bone) + root.rotation * offset;
        pb.transform.rotation = root.rotation * Quaternion.Euler(euler);
        pb.transform.SetParent(b, true);
    }

    static void Box(string n, HumanBodyBones bone, Vector3 off, Vector3 size, Vector3 euler, Material m, float bevel)
    {
        Place(Shape(ShapeType.Cube, size, Mathf.Min(bevel, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.45f), m, n), bone, off, euler);
    }

    static void Cyl(string n, HumanBodyBones bone, Vector3 off, Vector3 size, Vector3 euler, Material m)
    {
        Place(Shape(ShapeType.Cylinder, size, 0f, m, n), bone, off, euler);
    }

    // 2つの骨の間に、骨の向きに沿って置く
    static void Along(string n, HumanBodyBones from, HumanBodyBones to, float t, Vector3 size, Material m, float bevel, Vector3? off = null)
    {
        var a = Bone(from); var b = Bone(to); if (a == null || b == null) return;
        var pb = Shape(ShapeType.Cube, size, Mathf.Min(bevel, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.45f), m, n);
        pb.transform.position = Vector3.Lerp(a.position, b.position, t) + root.rotation * (off ?? Vector3.zero);
        var dir = (b.position - a.position).normalized;
        pb.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(root.forward, dir).normalized, dir);
        pb.transform.SetParent(a, true);
    }
}
