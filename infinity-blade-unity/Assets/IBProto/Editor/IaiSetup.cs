using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 居合バトルの取り込み係。
//  ・Iai/Source の Hero.json / Boss.json（Blender で作った形と姿勢）から、キャラのプレハブを作る
//    → Characters/Tabibito/TabibitoCharacter.prefab（主人公・旅の抜刀者）
//    → Characters/Kadomori/KadomoriCharacter.prefab（ボス・門守）
//  ・作るのは「まだ無いとき」だけ。作った後はプレハブが正（キャラ一覧で開いて直せる）
//  ・シーン Scenes/Iai.unity も無ければ作る
public static class IaiSetup
{
    const string SRC = "Assets/IBProto/Iai/Source";
    const string CHARS = "Assets/IBProto/Characters";
    const string SCENE = "Assets/IBProto/Scenes/Iai.unity";

    [Serializable] class JJoint { public string name; public int parent; }
    [Serializable] class JPose { public string name; public float[] t; }
    [Serializable] class JMat { public string name, kind; public float[] lit, shade, rimColor; public float rim, spec, intensity; }
    [Serializable] class JPart { public string name, obj, mat; public int joint; public float[] v, n; public int[] tri; }
    [Serializable] class JModel { public string name; public JJoint[] joints; public float[] rest; public JPose[] poses; public JMat[] materials; public JPart[] parts; }

    [InitializeOnLoadMethod]
    static void Auto()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            try { EnsureAll(false); } catch (Exception e) { Debug.LogError("[Iai] 取り込みに失敗: " + e); }
        };
    }

    [MenuItem("IBProto/居合バトル/シーンを開く", false, 1)]
    public static void OpenScene()
    {
        EnsureAll(false);
        if (EditorApplication.isPlaying) return;
        if (EditorSceneManager.GetActiveScene().path == SCENE) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(SCENE);
    }

    [MenuItem("IBProto/居合バトル/キャラを取り込み直す（Blenderで作り直した後。プレハブを上書き）", false, 20)]
    public static void Rebuild() { EnsureAll(true); }

    static void EnsureAll(bool force)
    {
        if (Shader.Find("IBProto/Toon") == null) { Debug.LogWarning("[Iai] シェーダー IBProto/Toon がまだ読み込まれていません"); return; }
        Build("Hero", "Tabibito", "TabibitoCharacter", force);
        Build("Boss", "Kadomori", "KadomoriCharacter", force);
        EnsureScene();
    }

    static void Folder(string parent, string name) { if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name); }

    static Color Col(float[] a) { return (a == null || a.Length < 3) ? Color.white : new Color(a[0], a[1], a[2], 1f).gamma; }

    static void Build(string src, string folder, string prefabName, bool force)
    {
        string dir = CHARS + "/" + folder, prefabPath = dir + "/" + prefabName + ".prefab";
        if (!force && AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null) return;
        var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(SRC + "/" + src + ".json");
        if (ta == null) { Debug.LogWarning("[Iai] " + SRC + "/" + src + ".json がありません"); return; }
        var m = JsonUtility.FromJson<JModel>(ta.text);
        Folder(CHARS, folder); Folder(dir, "Meshes"); Folder(dir, "Materials");

        // マテリアル（あれば使い回す＝手で直した色は残る）
        var mats = new Dictionary<string, Material>();
        foreach (var jm in m.materials)
        {
            string mp = dir + "/Materials/" + jm.name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (mat == null)
            {
                if (jm.kind == "neon")
                {
                    mat = new Material(Shader.Find("IBProto/Neon"));
                    mat.SetColor("_Color", Col(jm.lit)); mat.SetFloat("_Intensity", jm.intensity);
                }
                else if (jm.kind == "line")
                {
                    mat = new Material(Shader.Find("IBProto/ToonLine"));
                    mat.SetColor("_Color", Col(jm.lit));
                }
                else
                {
                    mat = new Material(Shader.Find("IBProto/Toon"));
                    mat.SetColor("_Lit", Col(jm.lit)); mat.SetColor("_Shade", Col(jm.shade));
                    mat.SetFloat("_Rim", jm.rim); mat.SetFloat("_Spec", jm.spec);
                    if (jm.rimColor != null && jm.rimColor.Length >= 3) mat.SetColor("_RimColor", new Color(jm.rimColor[0], jm.rimColor[1], jm.rimColor[2], 1f));
                }
                AssetDatabase.CreateAsset(mat, mp);
            }
            mats[jm.name] = mat;
        }

        var root = new GameObject(prefabName);
        int n = m.joints.Length;
        var tj = new Transform[n];
        for (int i = 0; i < n; i++)
        {
            var g = new GameObject(m.joints[i].name);
            tj[i] = g.transform;
            tj[i].SetParent(m.joints[i].parent < 0 ? root.transform : tj[m.joints[i].parent], false);
            tj[i].localPosition = new Vector3(m.rest[i * 7], m.rest[i * 7 + 1], m.rest[i * 7 + 2]);
            tj[i].localRotation = new Quaternion(m.rest[i * 7 + 3], m.rest[i * 7 + 4], m.rest[i * 7 + 5], m.rest[i * 7 + 6]);
        }

        foreach (var p in m.parts)
        {
            var mesh = new Mesh { name = p.name };
            int vc = p.v.Length / 3;
            var vs = new Vector3[vc]; var ns = new Vector3[vc];
            for (int i = 0; i < vc; i++)
            {
                vs[i] = new Vector3(p.v[i * 3], p.v[i * 3 + 1], p.v[i * 3 + 2]);
                ns[i] = new Vector3(p.n[i * 3], p.n[i * 3 + 1], p.n[i * 3 + 2]);
            }
            mesh.vertices = vs; mesh.normals = ns; mesh.triangles = p.tri;
            mesh.RecalculateBounds();
            string mpath = dir + "/Meshes/" + p.name.Replace('.', '_') + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(mpath);
            if (old != null) { EditorUtility.CopySerialized(mesh, old); mesh = old; }
            else AssetDatabase.CreateAsset(mesh, mpath);

            var g = new GameObject(p.name);
            g.transform.SetParent(tj[p.joint], false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.AddComponent<MeshRenderer>();
            Material mat; mats.TryGetValue(p.mat, out mat);
            mr.sharedMaterial = mat;
            bool solid = mat != null && mat.shader.name == "IBProto/Toon";
            mr.shadowCastingMode = solid ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = solid;
        }

        var pup = root.AddComponent<IaiPuppet>();
        pup.joints = tj;
        pup.poses = new IaiPose[m.poses.Length];
        for (int k = 0; k < m.poses.Length; k++)
        {
            var jp = m.poses[k];
            var po = new IaiPose { name = jp.name, pos = new Vector3[n], rot = new Quaternion[n] };
            for (int i = 0; i < n; i++)
            {
                po.pos[i] = new Vector3(jp.t[i * 7], jp.t[i * 7 + 1], jp.t[i * 7 + 2]);
                po.rot[i] = new Quaternion(jp.t[i * 7 + 3], jp.t[i * 7 + 4], jp.t[i * 7 + 5], jp.t[i * 7 + 6]);
            }
            pup.poses[k] = po;
        }
        // 立ち姿で保存しておく（キャラ一覧で開いたときに見栄えがするように）
        if (pup.poses.Length > 0)
        {
            var p0 = pup.poses[0];
            for (int i = 0; i < n; i++) { tj[i].localPosition = p0.pos[i]; tj[i].localRotation = p0.rot[i]; }
        }

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("[Iai] キャラを作りました: " + prefabPath + "（部品 " + m.parts.Length + "、姿勢 " + m.poses.Length + "）");
    }

    static void EnsureScene()
    {
        if (File.Exists(SCENE)) return;
        if (EditorApplication.isPlaying) return;
        var sc = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var cg = new GameObject("Main Camera"); cg.tag = "MainCamera";
        cg.AddComponent<Camera>(); cg.AddComponent<AudioListener>();
        var g = new GameObject("Game");
        var game = g.AddComponent<IaiGame>();
        g.AddComponent<IaiAudio>();
        game.heroPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CHARS + "/Tabibito/TabibitoCharacter.prefab");
        game.bossPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CHARS + "/Kadomori/KadomoriCharacter.prefab");
        game.stagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/IBProto/Stages/NeonArena/NeonArena.prefab");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cg, sc);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(g, sc);
        EditorSceneManager.SaveScene(sc, SCENE);
        EditorSceneManager.CloseScene(sc, true);
        Debug.Log("[Iai] シーンを作りました: " + SCENE + "（メニュー IBProto > 居合バトル > シーンを開く）");
    }
}
