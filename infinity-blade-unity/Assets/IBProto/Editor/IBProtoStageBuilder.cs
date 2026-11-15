using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// ステージ「ネオン闘技場」を一度だけ作ってプレハブにする（作った後はプレハブが本物）
//  ・夜の空（紺→紫→地平のピンク）と、敵の後ろに大きな月とネオンの輪
//  ・宙に浮く円い闘技場。床にネオンの輪と目盛り
//  ・遠景は3段のシルエット（石柱・山並み・遠い山並み）。遠いほど地平の色に溶ける
//  ・光は1灯だけ。影はくっきり一色で落ちる
public static class IBProtoStageBuilder
{
    const string DIR = "Assets/IBProto/Stages/NeonArena";
    const string PREFAB = DIR + "/NeonArena.prefab";
    static readonly Vector3 CENTER = new Vector3(0, 0, 1.5f);

    [InitializeOnLoadMethod]
    static void Auto()
    {
        EditorApplication.delayCall += () =>
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB) == null && Shader.Find("IBProto/Sky") != null) Build();
        };
    }

    [MenuItem("IBProto/ステージ/ネオン闘技場を作り直す", false, 10)]
    static void Rebuild()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB) != null &&
            !EditorUtility.DisplayDialog("ステージ", "ネオン闘技場を最初の形に作り直します。手で直したところは消えます。", "作り直す", "やめる")) return;
        Build();
    }

    [MenuItem("IBProto/ステージ/ネオン闘技場を開く", false, 11)]
    static void Open()
    {
        var p = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB);
        if (p != null) AssetDatabase.OpenAsset(p);
    }

    // ---------- 材料 ----------
    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        Folder(Path.GetDirectoryName(path).Replace('\\', '/'));
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
    }
    static Material Save(Material m, string name) { AssetDatabase.CreateAsset(m, DIR + "/Materials/" + name + ".mat"); return m; }
    static Material Toon(string name, Color lit, Color shade, float rim = 0.2f)
    {
        var m = new Material(Shader.Find("IBProto/Toon"));
        m.SetColor("_Lit", lit); m.SetColor("_Shade", shade); m.SetFloat("_Rim", rim); m.SetFloat("_Spec", 0f);
        return Save(m, name);
    }
    static Material Flat(string name, Color c, Color baseC, float fb, float ft)
    {
        var m = new Material(Shader.Find("IBProto/Flat"));
        m.SetColor("_Color", c); m.SetColor("_BaseColor", baseC); m.SetFloat("_FadeBottom", fb); m.SetFloat("_FadeTop", ft);
        return Save(m, name);
    }
    static Material Neon(string name, float k)
    {
        var m = new Material(Shader.Find("IBProto/Neon"));
        m.SetColor("_Color", new Color(0.15f, 0.95f, 1f).gamma); m.SetFloat("_Intensity", k);
        return Save(m, name);
    }
    static Mesh SaveMesh(Mesh m, string name) { m.RecalculateBounds(); AssetDatabase.CreateAsset(m, DIR + "/Meshes/" + name + ".asset"); return m; }

    static GameObject Obj(string name, Mesh mesh, Material[] mats, Transform parent, Vector3 pos, bool shadow = true)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false); g.transform.localPosition = pos;
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = g.AddComponent<MeshRenderer>(); r.sharedMaterials = mats;
        r.shadowCastingMode = shadow ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        return g;
    }

    // ---------- 形 ----------
    // 円盤（上の面＝0番、側面＝1番）
    static Mesh Disc(float r, float h, int n)
    {
        var v = new List<Vector3>(); var top = new List<int>(); var side = new List<int>();
        v.Add(Vector3.zero);
        for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2 / n; v.Add(new Vector3(Mathf.Sin(a) * r, 0, Mathf.Cos(a) * r)); }
        for (int i = 0; i < n; i++) { top.Add(0); top.Add(1 + i); top.Add(1 + (i + 1) % n); }
        int b = v.Count;
        for (int i = 0; i <= n; i++)
        {
            float a = i * Mathf.PI * 2 / n; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            v.Add(d * r); v.Add(d * r + Vector3.down * h);
        }
        for (int i = 0; i < n; i++)
        {
            int k = b + i * 2;
            side.Add(k); side.Add(k + 1); side.Add(k + 2);
            side.Add(k + 2); side.Add(k + 1); side.Add(k + 3);
        }
        var m = new Mesh(); m.SetVertices(v); m.subMeshCount = 2; m.SetTriangles(top, 0); m.SetTriangles(side, 1);
        m.RecalculateNormals(); return m;
    }
    // 浮島の下の岩（角ばった逆さの円錐）
    static Mesh Rock(float r0, float r1, float h, int n, System.Random rnd)
    {
        var v = new List<Vector3>(); var t = new List<int>();
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2 / n, j = 0.85f + (float)rnd.NextDouble() * 0.3f;
            var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            v.Add(d * r0); v.Add(d * r1 * j + Vector3.down * h * (0.8f + 0.4f * (float)rnd.NextDouble()));
        }
        for (int i = 0; i < n; i++)
        {
            int a0 = i * 2, a1 = ((i + 1) % n) * 2;
            t.Add(a0); t.Add(a0 + 1); t.Add(a1); t.Add(a1); t.Add(a0 + 1); t.Add(a1 + 1);
        }
        int tip = v.Count; v.Add(Vector3.down * h * 1.35f);
        for (int i = 0; i < n; i++) { t.Add(i * 2 + 1); t.Add(tip); t.Add(((i + 1) % n) * 2 + 1); }
        var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); return m;
    }
    // 床の輪（平らな帯）
    static Mesh Ring(float r0, float r1, int n, float a0 = 0, float a1 = 360)
    {
        var v = new List<Vector3>(); var t = new List<int>();
        for (int i = 0; i <= n; i++)
        {
            float a = Mathf.Lerp(a0, a1, i / (float)n) * Mathf.Deg2Rad; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            v.Add(d * r0); v.Add(d * r1);
        }
        for (int i = 0; i < n; i++) { int k = i * 2; t.Add(k); t.Add(k + 1); t.Add(k + 2); t.Add(k + 1); t.Add(k + 3); t.Add(k + 2); }   // 上向き
        var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); return m;
    }
    // 山並み（ぐるりと一周の切り絵。内側を向く）
    static Mesh Ridge(float radius, float baseH, float amp, int n, System.Random rnd)
    {
        var v = new List<Vector3>(); var t = new List<int>();
        float[] hs = new float[n];
        for (int i = 0; i < n; i++) hs[i] = baseH + amp * (float)rnd.NextDouble() * (i % 3 == 0 ? 1.6f : 0.7f);
        for (int i = 0; i <= n; i++)
        {
            float a = i * Mathf.PI * 2 / n; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            v.Add(d * radius + Vector3.down * 25f); v.Add(d * radius + Vector3.up * hs[i % n]);
        }
        for (int i = 0; i < n; i++)
        {
            int k = i * 2;
            t.Add(k); t.Add(k + 1); t.Add(k + 2); t.Add(k + 2); t.Add(k + 1); t.Add(k + 3);   // 内向き
            t.Add(k); t.Add(k + 2); t.Add(k + 1); t.Add(k + 1); t.Add(k + 2); t.Add(k + 3);   // 外向き（念のため両面）
        }
        var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); return m;
    }
    // 浮かぶ結晶（八面体）
    static Mesh Octa()
    {
        var p = new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        int[] f = { 0, 4, 3, 0, 3, 5, 0, 5, 2, 0, 2, 4, 1, 3, 4, 1, 5, 3, 1, 2, 5, 1, 4, 2 };
        var v = new List<Vector3>(); var t = new List<int>();
        for (int i = 0; i < f.Length; i++) { v.Add(p[f[i]]); t.Add(i); }   // 面ごとに頂点を分けて角を立てる
        var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); return m;
    }

    public static void Build()
    {
        Folder(DIR + "/Materials"); Folder(DIR + "/Meshes");
        foreach (var sub in new[] { "/Materials", "/Meshes" })
            foreach (var f in Directory.GetFiles(DIR + sub)) if (!f.EndsWith(".meta")) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
        var rnd = new System.Random(11);

        // マテリアル（色は見た目どおりの sRGB）
        var sky = new Material(Shader.Find("IBProto/Sky"));
        sky.SetVector("_SunDir", new Vector4(0.12f, 0.2f, 1f, 0)); Save(sky, "空");
        var floorTop = Toon("床", new Color(0.12f, 0.13f, 0.27f), new Color(0.05f, 0.05f, 0.15f), 0f);
        var floorSide = Toon("床の縁", new Color(0.08f, 0.08f, 0.2f), new Color(0.03f, 0.03f, 0.1f), 0.3f);
        var rockM = Flat("浮島の岩", new Color(0.09f, 0.06f, 0.2f), new Color(0.05f, 0.03f, 0.12f), -8f, 0f);
        var neon = Neon("ネオン", 1.8f);
        var neonDim = Neon("ネオン（弱）", 0.55f);
        var mono = Flat("石柱", new Color(0.17f, 0.07f, 0.3f), new Color(0.6f, 0.2f, 0.45f), -3f, 9f);
        var ridge2 = Flat("山並み", new Color(0.33f, 0.1f, 0.4f), new Color(0.85f, 0.3f, 0.52f), -6f, 14f);
        var ridge3 = Flat("遠い山並み", new Color(0.55f, 0.2f, 0.47f), new Color(0.9f, 0.3f, 0.5f), -6f, 22f);
        var shardM = Toon("結晶", new Color(0.14f, 0.11f, 0.32f), new Color(0.05f, 0.04f, 0.13f), 0.55f);

        var root = new GameObject("NeonArena");
        var stage = root.AddComponent<ProtoStage>();
        stage.sky = sky; stage.fogColor = new Color(0.9f, 0.3f, 0.5f);

        // 光：右上前から1灯。影はくっきり
        var lg = new GameObject("太陽"); lg.transform.SetParent(root.transform, false);
        lg.transform.rotation = Quaternion.Euler(42f, -35f, 0);
        var sun = lg.AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1f; sun.shadows = LightShadows.Hard; sun.shadowStrength = 1f;
        stage.sun = sun;

        // 闘技場
        var arena = new GameObject("闘技場").transform; arena.SetParent(root.transform, false); arena.localPosition = CENTER;
        Obj("床", SaveMesh(Disc(6.6f, 0.45f, 72), "床"), new[] { floorTop, floorSide }, arena, Vector3.zero);
        Obj("浮島", SaveMesh(Rock(6.5f, 2.2f, 6.5f, 14, rnd), "浮島"), new[] { rockM }, arena, new Vector3(0, -0.45f, 0));
        Obj("縁の輪", SaveMesh(Ring(6.25f, 6.38f, 96), "縁の輪"), new[] { neon }, arena, new Vector3(0, 0.012f, 0), false);
        Obj("内の輪", SaveMesh(Ring(3.0f, 3.06f, 96), "内の輪"), new[] { neon }, arena, new Vector3(0, 0.012f, 0), false);
        Obj("中の輪", SaveMesh(Ring(4.6f, 4.64f, 96), "中の輪"), new[] { neonDim }, arena, new Vector3(0, 0.012f, 0), false);
        var tick = SaveMesh(Ring(4.8f, 5.35f, 1, -0.6f, 0.6f), "目盛り");
        for (int i = 0; i < 24; i++)
        {
            var g = Obj("目盛り", tick, new[] { i % 6 == 0 ? neon : neonDim }, arena, new Vector3(0, 0.012f, 0), false);
            g.transform.localRotation = Quaternion.Euler(0, i * 15f, 0);
        }

        // 遠景1：石柱（ぐるりと）
        var far = new GameObject("遠景").transform; far.SetParent(root.transform, false); far.localPosition = CENTER;
        var cube = SaveMesh(Object.Instantiate(Resources.GetBuiltinResource<Mesh>("Cube.fbx")), "石柱");
        for (int i = 0; i < 20; i++)
        {
            float a = (i / 20f + (float)rnd.NextDouble() * 0.03f) * Mathf.PI * 2, d = 20f + (float)rnd.NextDouble() * 12f;
            float h = 6f + (float)rnd.NextDouble() * 16f, w = 1f + (float)rnd.NextDouble() * 2.2f;
            var g = Obj("石柱", cube, new[] { mono }, far, new Vector3(Mathf.Sin(a) * d, h / 2f - 3f, Mathf.Cos(a) * d), false);
            g.transform.localScale = new Vector3(w, h, w * 0.8f);
            g.transform.localRotation = Quaternion.Euler((float)rnd.NextDouble() * 10f - 5f, (float)rnd.NextDouble() * 180f, (float)rnd.NextDouble() * 12f - 6f);
        }
        // 遠景2・3：山並み
        Obj("山並み", SaveMesh(Ridge(58f, 6f, 10f, 48, rnd), "山並み"), new[] { ridge2 }, far, Vector3.zero, false);
        Obj("遠い山並み", SaveMesh(Ridge(100f, 12f, 16f, 36, rnd), "遠い山並み"), new[] { ridge3 }, far, Vector3.zero, false);

        // 浮かぶ結晶
        var octa = SaveMesh(Octa(), "結晶");
        for (int i = 0; i < 14; i++)
        {
            float a = (float)rnd.NextDouble() * Mathf.PI * 2, d = 9f + (float)rnd.NextDouble() * 7f;
            var g = Obj("結晶", octa, new[] { shardM }, far, new Vector3(Mathf.Sin(a) * d, 1.5f + (float)rnd.NextDouble() * 6f, Mathf.Cos(a) * d));
            float s = 0.35f + (float)rnd.NextDouble() * 0.6f;
            g.transform.localScale = new Vector3(s, s * 2.6f, s);
            g.transform.localRotation = Quaternion.Euler((float)rnd.NextDouble() * 40f - 20f, (float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 40f - 20f);
        }

        PrefabUtility.SaveAsPrefabAsset(root, PREFAB);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("[IBProto] ステージ「ネオン闘技場」を作りました: " + PREFAB);
    }
}
