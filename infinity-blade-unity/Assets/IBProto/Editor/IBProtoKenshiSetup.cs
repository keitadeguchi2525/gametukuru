using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// 剣士（Blender で作った Furi 風のキャラ）を、Unity のキャラにする
//  ・Characters/Kenshi/Kenshi.fbx を人型（Humanoid）で読み込む
//  ・Blender のマテリアル名ごとに、アニメ調シェーダーのマテリアルを当てる
//  ・Characters/Kenshi/KenshiCharacter.prefab を作る（無いときだけ一度。作った後はキャラ一覧で開いて直す）
public class IBProtoKenshiSetup : AssetPostprocessor
{
    const string DIR = "Assets/IBProto/Characters/Kenshi";
    const string FBX = DIR + "/Kenshi.fbx";
    const string PREFAB = DIR + "/KenshiCharacter.prefab";
    const string MAT_DIR = DIR + "/Materials";

    void OnPreprocessModel()
    {
        if (assetPath != FBX) return;
        var mi = (ModelImporter)assetImporter;
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importAnimation = false;
        mi.importCameras = false; mi.importLights = false;
        mi.importNormals = ModelImporterNormals.Import;
    }

    // Blender の色（リニア）そのまま。shade=影、rim=縁の光、spec=ハイライト
    struct Toon { public Color lit, shade; public float rim, spec; }
    static readonly Dictionary<string, Toon> TOON = new Dictionary<string, Toon>
    {
        { "黒",         new Toon { lit = new Color(0.035f, 0.034f, 0.045f), shade = new Color(0.006f, 0.006f, 0.012f), rim = 0.35f, spec = 0.12f } },
        { "墨（下着）", new Toon { lit = new Color(0.05f, 0.05f, 0.065f),   shade = new Color(0.01f, 0.01f, 0.02f),    rim = 0.35f } },
        { "濃紺（袴）", new Toon { lit = new Color(0.035f, 0.045f, 0.1f),   shade = new Color(0.008f, 0.01f, 0.03f),   rim = 0.35f } },
        { "白（胴）",   new Toon { lit = new Color(0.86f, 0.85f, 0.82f),    shade = new Color(0.32f, 0.34f, 0.48f),    rim = 0.15f, spec = 0.1f } },
        { "朱",         new Toon { lit = new Color(0.62f, 0.03f, 0.025f),   shade = new Color(0.17f, 0.006f, 0.03f),   rim = 0.25f } },
        { "金",         new Toon { lit = new Color(0.75f, 0.52f, 0.16f),    shade = new Color(0.24f, 0.12f, 0.05f),    rim = 0.2f, spec = 0.2f } },
        { "刃",         new Toon { lit = new Color(0.8f, 0.84f, 0.9f),      shade = new Color(0.3f, 0.34f, 0.45f),     rim = 0.3f, spec = 0.15f } },
    };

    [InitializeOnLoadMethod]
    static void Auto()
    {
        EditorApplication.delayCall += () =>
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FBX) != null && AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB) == null) Build();
        };
    }

    [MenuItem("IBProto/旧・生成ツール（作り直し用）/剣士のプレハブを作り直す（Blenderから取り込み直した後）")]
    public static void Build()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(FBX);
        if (model == null) { Debug.LogError("[IBProto] " + FBX + " がありません"); return; }
        if (!AssetDatabase.IsValidFolder(MAT_DIR)) AssetDatabase.CreateFolder(DIR, "Materials");

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
        inst.name = "KenshiCharacter";
        int replaced = 0;
        foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                var m = GetMat(mats[i].name.Replace(" (Instance)", "").Trim());
                if (m != null) { mats[i] = m; replaced++; }
            }
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        // 前を向いているか確かめる（目のネオンが頭より +Z 側にあれば正しい）
        var anim = inst.GetComponent<Animator>();
        Transform head = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
        Renderer visor = null;
        foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) if (r.name.StartsWith("面_一文字")) { visor = r; break; }
        if (head != null && visor != null)
        {
            Vector3 d = inst.transform.InverseTransformPoint(visor.bounds.center) - inst.transform.InverseTransformPoint(head.position);
            if (d.z < 0) { inst.transform.rotation = Quaternion.Euler(0, 180, 0); Debug.Log("[IBProto] 剣士の向きを180度回しました"); }
        }
        PrefabUtility.SaveAsPrefabAsset(inst, PREFAB);
        Object.DestroyImmediate(inst);
        AssetDatabase.SaveAssets();
        Debug.Log("[IBProto] 剣士のプレハブを作りました: " + PREFAB + "（マテリアル " + replaced + " か所、人型=" + (anim != null && anim.isHuman) + "）");
    }

    static Material GetMat(string name)
    {
        string path = MAT_DIR + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        if (name == "輪郭線")
        {
            m = new Material(Shader.Find("IBProto/ToonLine"));
            m.SetColor("_Color", new Color(0.006f, 0.005f, 0.012f).gamma);
        }
        else if (name == "ネオン")
        {
            m = new Material(Shader.Find("IBProto/Neon"));
            m.SetColor("_Color", new Color(0.15f, 0.95f, 1f).gamma);
            m.SetFloat("_Intensity", 1.8f);
        }
        else if (TOON.TryGetValue(name, out var t))
        {
            m = new Material(Shader.Find("IBProto/Toon"));
            m.SetColor("_Lit", t.lit.gamma); m.SetColor("_Shade", t.shade.gamma);
            m.SetFloat("_Rim", t.rim); m.SetFloat("_Spec", t.spec);
        }
        else return null;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
