using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// キャラ試作：StarterAssetsの人型素体に、コードで作ったSFアーマーを骨ごとに取り付ける。
// 色とパーツを切り替える簡易キャラクリ付き。空のGameObjectに付けてPlay。
public class ProtoCharacter : MonoBehaviour
{
    [Tooltip("空なら StarterAssets の Armature.fbx を自動で読む")]
    public GameObject bodyModel;
    [Tooltip("空なら StarterAssets の StarterAssetsThirdPerson.controller を自動で読む")]
    public RuntimeAnimatorController animController;
    public Font uiFont;

    const string MODEL_PATH = "Assets/StarterAssets/ThirdPersonController/Character/Models/Armature.fbx";
    const string CTRL_PATH = "Assets/StarterAssets/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";

    Camera cam;
    GameObject body;
    Animator anim;
    readonly List<Material> suitMats = new List<Material>();
    Material armorMat, darkMat, accentMat, visorMat;
    readonly Dictionary<string, List<GameObject>> groups = new Dictionary<string, List<GameObject>>();
    string error;

    // キャラクリの選択肢
    static readonly Color[] ARMOR = { new Color(0.92f, 0.91f, 0.88f), new Color(0.2f, 0.22f, 0.25f), new Color(0.55f, 0.6f, 0.45f), new Color(0.75f, 0.28f, 0.2f), new Color(0.3f, 0.45f, 0.7f) };
    static readonly string[] ARMOR_N = { "白", "黒", "オリーブ", "赤錆", "青" };
    static readonly Color[] SUIT = { new Color(0.16f, 0.16f, 0.18f), new Color(0.35f, 0.33f, 0.3f), new Color(0.12f, 0.17f, 0.24f), new Color(0.3f, 0.2f, 0.15f) };
    static readonly string[] SUIT_N = { "黒", "グレー", "紺", "茶" };
    static readonly Color[] ACCENT = { new Color(1f, 0.45f, 0.08f), new Color(0.1f, 0.85f, 1f), new Color(1f, 0.15f, 0.25f), new Color(0.6f, 1f, 0.2f) };
    static readonly string[] ACCENT_N = { "橙", "水色", "赤", "黄緑" };
    static readonly string[] HELMET_N = { "フル", "バイザー", "なし" };
    int armorI, suitI, accentI, helmetI;
    bool showPack = true, showShoulder = true, showLegs = true;

    // 表示
    float yaw = 160f, pitch = 6f, dist = 4.2f, autoSpin = 1f;
    Vector2 lastMouse; bool dragging;
    int triCount;

    void Start()
    {
#if UNITY_EDITOR
        if (bodyModel == null) bodyModel = AssetDatabase.LoadAssetAtPath<GameObject>(MODEL_PATH);
        if (animController == null) animController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CTRL_PATH);
#endif
        BuildStage();
        if (bodyModel == null) { error = "素体モデルが見つかりません（" + MODEL_PATH + "）"; return; }

        body = Instantiate(bodyModel, Vector3.zero, Quaternion.identity);
        body.name = "Character";
        anim = body.GetComponent<Animator>();
        if (anim == null) anim = body.GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.applyRootMotion = false;
            if (animController != null) anim.runtimeAnimatorController = animController;
        }
        foreach (var r in body.GetComponentsInChildren<Renderer>())
        {
            var mats = r.materials;                      // 素体のマテリアルを複製して色を変えられるようにする
            foreach (var m in mats) suitMats.Add(m);
            r.materials = mats;
        }

        armorMat = ProtoBuild.Lit(ARMOR[0]); SetSurface(armorMat, 0.15f, 0.62f);
        darkMat = ProtoBuild.Lit(new Color(0.1f, 0.1f, 0.11f)); SetSurface(darkMat, 0.85f, 0.55f);
        accentMat = ProtoBuild.Lit(ACCENT[0]); SetSurface(accentMat, 0f, 0.5f);
        visorMat = ProtoBuild.Lit(new Color(0.05f, 0.05f, 0.06f)); SetSurface(visorMat, 0.9f, 0.95f);

        if (anim != null && anim.isHuman) BuildArmor();
        else error = "素体がHumanoidではないため、アーマーを付けられません";
        ApplyLook();
        triCount = CountTris();
    }

    // ================= 舞台（ライト・床・カメラ） =================
    void BuildStage()
    {
        cam = Camera.main;
        if (cam == null) { var cg = new GameObject("Main Camera"); cg.tag = "MainCamera"; cam = cg.AddComponent<Camera>(); }
        var bg = new Color(0.86f, 0.85f, 0.83f);
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = bg; cam.fieldOfView = 32f; cam.nearClipPlane = 0.05f;
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = bg;
        RenderSettings.fogStartDistance = 6f; RenderSettings.fogEndDistance = 22f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.75f, 0.78f, 0.82f);
        RenderSettings.ambientEquatorColor = new Color(0.55f, 0.54f, 0.52f);
        RenderSettings.ambientGroundColor = new Color(0.3f, 0.28f, 0.26f);

        #if UNITY_2023_1_OR_NEWER
        var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
#else
        var lights = FindObjectsOfType<Light>();
#endif
        foreach (var l in lights) l.enabled = false;   // 既存のライトは消して、撮影用のライトにする
        MakeLight("Key", new Color(1f, 0.95f, 0.88f), 1.25f, new Vector3(35f, 145f, 0), true);
        MakeLight("Rim", new Color(0.7f, 0.82f, 1f), 1.1f, new Vector3(20f, -20f, 0), false);
        MakeLight("Fill", new Color(1f, 0.9f, 0.85f), 0.35f, new Vector3(10f, 250f, 0), false);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ground.name = "Stage"; ground.transform.localScale = new Vector3(3f, 0.02f, 3f); ground.transform.position = new Vector3(0, -0.02f, 0);
        var gm = ProtoBuild.Lit(new Color(0.7f, 0.69f, 0.67f)); SetSurface(gm, 0f, 0.3f);
        ground.GetComponent<Renderer>().sharedMaterial = gm;
    }

    void MakeLight(string n, Color c, float i, Vector3 euler, bool shadows)
    {
        var g = new GameObject(n + " Light"); var l = g.AddComponent<Light>();
        l.type = LightType.Directional; l.color = c; l.intensity = i; l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        g.transform.rotation = Quaternion.Euler(euler);
    }

    static void SetSurface(Material m, float metallic, float smooth)
    {
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
    }

    static void SetEmission(Material m, Color c)
    {
        m.EnableKeyword("_EMISSION");
        if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c);
    }

    // ================= アーマー =================
    Transform B(HumanBodyBones b) { return anim.GetBoneTransform(b); }

    // 骨の位置から「キャラの向き基準の」ずらし量で置き、その骨に追従させる
    GameObject Piece(string group, PrimitiveType type, HumanBodyBones bone, Vector3 offset, Vector3 size, Vector3 euler, Material mat)
    {
        var b = B(bone);
        if (b == null && bone == HumanBodyBones.UpperChest) b = B(HumanBodyBones.Chest);
        if (b == null) return null;
        var g = GameObject.CreatePrimitive(type);
        Destroy(g.GetComponent<Collider>());
        g.name = group + "_" + bone;
        Vector3 basePos = b.position;
        if (bone == HumanBodyBones.UpperChest && B(HumanBodyBones.Neck) != null)
            basePos = B(HumanBodyBones.Neck).position + body.transform.rotation * new Vector3(0, -0.17f, -0.01f);   // 胸の中心（首の付け根の少し下）
        g.transform.position = basePos + body.transform.rotation * offset;
        g.transform.rotation = body.transform.rotation * Quaternion.Euler(euler);
        g.transform.localScale = size;
        g.GetComponent<Renderer>().sharedMaterial = mat;
        g.transform.SetParent(b, true);
        if (!groups.ContainsKey(group)) groups[group] = new List<GameObject>();
        groups[group].Add(g);
        return g;
    }

    // 2つの骨の間に、骨の向きに沿ったパーツを置く（腕・脚の装甲用）
    GameObject Between(string group, HumanBodyBones from, HumanBodyBones to, float t, Vector3 offset, Vector3 size, Material mat, PrimitiveType type = PrimitiveType.Capsule)
    {
        var a = B(from); var b = B(to);
        if (a == null || b == null) return null;
        var g = GameObject.CreatePrimitive(type);
        Destroy(g.GetComponent<Collider>());
        g.name = group + "_" + from;
        g.transform.position = Vector3.Lerp(a.position, b.position, t) + body.transform.rotation * offset;
        g.transform.rotation = Quaternion.FromToRotation(Vector3.up, (b.position - a.position).normalized);
        g.transform.localScale = size;
        g.GetComponent<Renderer>().sharedMaterial = mat;
        g.transform.SetParent(a, true);
        if (!groups.ContainsKey(group)) groups[group] = new List<GameObject>();
        groups[group].Add(g);
        return g;
    }

    void BuildArmor()
    {
        var S = PrimitiveType.Sphere; var C = PrimitiveType.Cube; var Cy = PrimitiveType.Cylinder;
        var H = HumanBodyBones.Head;

        // ヘルメット（フル）
        Piece("helmet0", S, H, new Vector3(0, 0.075f, 0.005f), new Vector3(0.215f, 0.245f, 0.245f), Vector3.zero, armorMat);
        Piece("helmet0", S, H, new Vector3(0, 0.07f, 0.07f), new Vector3(0.19f, 0.09f, 0.13f), Vector3.zero, visorMat);
        Piece("helmet0", C, H, new Vector3(0, 0.0f, 0.075f), new Vector3(0.1f, 0.045f, 0.06f), new Vector3(10f, 0, 0), darkMat);
        Piece("helmet0", Cy, H, new Vector3(0.108f, 0.08f, -0.01f), new Vector3(0.04f, 0.012f, 0.04f), new Vector3(0, 0, 90f), accentMat);
        // ヘルメット（バイザーだけ）
        Piece("helmet1", C, H, new Vector3(0, 0.085f, 0.09f), new Vector3(0.17f, 0.04f, 0.03f), new Vector3(-5f, 0, 0), visorMat);
        Piece("helmet1", C, H, new Vector3(0, 0.15f, 0.0f), new Vector3(0.18f, 0.03f, 0.2f), Vector3.zero, armorMat);
        Piece("helmet1", Cy, H, new Vector3(0.095f, 0.085f, 0.02f), new Vector3(0.04f, 0.012f, 0.04f), new Vector3(0, 0, 90f), accentMat);

        // 胴（胸当ては左右2枚に分けて体に沿わせる）
        Piece("chest", C, HumanBodyBones.UpperChest, new Vector3(-0.065f, 0.0f, 0.1f), new Vector3(0.13f, 0.16f, 0.045f), new Vector3(-8f, -12f, 0), armorMat);
        Piece("chest", C, HumanBodyBones.UpperChest, new Vector3(0.065f, 0.0f, 0.1f), new Vector3(0.13f, 0.16f, 0.045f), new Vector3(-8f, 12f, 0), armorMat);
        Piece("chest", C, HumanBodyBones.UpperChest, new Vector3(0, -0.11f, 0.09f), new Vector3(0.16f, 0.04f, 0.04f), Vector3.zero, darkMat);
        Piece("chest", C, HumanBodyBones.UpperChest, new Vector3(-0.07f, 0.05f, 0.125f), new Vector3(0.04f, 0.012f, 0.008f), new Vector3(-8f, -12f, 0), accentMat);
        Piece("chest", C, HumanBodyBones.Hips, new Vector3(0, 0.05f, 0), new Vector3(0.28f, 0.045f, 0.19f), Vector3.zero, darkMat);
        Piece("chest", C, HumanBodyBones.Hips, new Vector3(0.1f, 0.02f, 0.085f), new Vector3(0.07f, 0.08f, 0.03f), Vector3.zero, armorMat);

        // バックパック
        Piece("pack", C, HumanBodyBones.UpperChest, new Vector3(0, -0.02f, -0.13f), new Vector3(0.24f, 0.28f, 0.1f), Vector3.zero, armorMat);
        Piece("pack", Cy, HumanBodyBones.UpperChest, new Vector3(-0.075f, -0.04f, -0.2f), new Vector3(0.055f, 0.1f, 0.055f), Vector3.zero, darkMat);
        Piece("pack", Cy, HumanBodyBones.UpperChest, new Vector3(0.075f, -0.04f, -0.2f), new Vector3(0.055f, 0.1f, 0.055f), Vector3.zero, darkMat);
        Piece("pack", C, HumanBodyBones.UpperChest, new Vector3(0, 0.09f, -0.185f), new Vector3(0.14f, 0.02f, 0.015f), Vector3.zero, accentMat);

        // 肩
        Piece("shoulder", S, HumanBodyBones.LeftUpperArm, new Vector3(-0.02f, 0.035f, 0), new Vector3(0.13f, 0.09f, 0.14f), new Vector3(0, 0, 25f), armorMat);
        Piece("shoulder", S, HumanBodyBones.RightUpperArm, new Vector3(0.02f, 0.035f, 0), new Vector3(0.13f, 0.09f, 0.14f), new Vector3(0, 0, -25f), armorMat);

        // 腕（前腕のガード）
        Between("arms", HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.55f, Vector3.zero, new Vector3(0.068f, 0.085f, 0.068f), armorMat);
        Between("arms", HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.55f, Vector3.zero, new Vector3(0.068f, 0.085f, 0.068f), armorMat);

        // 脚（太もものホルスター、膝、すね）
        Between("legs", HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.35f, new Vector3(0.065f, 0, 0), new Vector3(0.05f, 0.09f, 0.08f), darkMat, C);
        Piece("legs", S, HumanBodyBones.LeftLowerLeg, new Vector3(0, 0.0f, 0.045f), new Vector3(0.075f, 0.085f, 0.05f), Vector3.zero, armorMat);
        Piece("legs", S, HumanBodyBones.RightLowerLeg, new Vector3(0, 0.0f, 0.045f), new Vector3(0.075f, 0.085f, 0.05f), Vector3.zero, armorMat);
        Between("legs", HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.5f, new Vector3(0, 0, 0.035f), new Vector3(0.065f, 0.13f, 0.03f), armorMat, C);
        Between("legs", HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.5f, new Vector3(0, 0, 0.035f), new Vector3(0.065f, 0.13f, 0.03f), armorMat, C);
    }

    void ApplyLook()
    {
        ProtoBuild.SetColor(armorMat, ARMOR[armorI]);
        foreach (var m in suitMats) ProtoBuild.SetColor(m, SUIT[suitI]);
        ProtoBuild.SetColor(accentMat, ACCENT[accentI]);
        SetEmission(accentMat, ACCENT[accentI] * 2.5f);
        Show("helmet0", helmetI == 0);
        Show("helmet1", helmetI == 1);
        Show("pack", showPack);
        Show("shoulder", showShoulder);
        Show("legs", showLegs);
        triCount = CountTris();
    }

    void Show(string g, bool on) { if (groups.ContainsKey(g)) foreach (var o in groups[g]) if (o) o.SetActive(on); }

    int CountTris()
    {
        if (body == null) return 0;
        int n = 0;
        foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>()) if (smr.sharedMesh) n += smr.sharedMesh.triangles.Length / 3;
        foreach (var mf in body.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh && mf.gameObject.activeInHierarchy) n += mf.sharedMesh.triangles.Length / 3;
        return n;
    }

    // ================= 毎フレーム =================
    void Update()
    {
        if (anim != null && anim.runtimeAnimatorController != null)
        {
            anim.SetBool("Grounded", true);
            anim.SetFloat("Speed", 0f);
            anim.SetFloat("MotionSpeed", 1f);
        }

        // マウスドラッグで回す／ホイールで寄る（UIの上は除く）
        Vector2 mp = ProtoInput.MousePos;
        bool overUI = mp.x < 330f * (Screen.height / 720f);
        if (ProtoInput.LeftPressed && !overUI) { dragging = true; lastMouse = mp; autoSpin = 0f; }
        if (ProtoInput.LeftReleased) dragging = false;
        if (dragging) { var d = mp - lastMouse; yaw += d.x * 0.35f; pitch = Mathf.Clamp(pitch - d.y * 0.2f, -10f, 40f); lastMouse = mp; }
        dist = Mathf.Clamp(dist - ProtoInput.Scroll * 0.0025f, 1.2f, 6f);
        yaw += autoSpin * 12f * Time.deltaTime;

        Vector3 target = new Vector3(0f, Mathf.Lerp(0.92f, 1.45f, Mathf.InverseLerp(4.2f, 1.2f, dist)), 0);
        var rot = Quaternion.Euler(pitch, yaw, 0);
        cam.transform.position = target + rot * new Vector3(0, 0, -dist);
        cam.transform.LookAt(target);
    }

    // ================= キャラクリ画面 =================
    GUIStyle sTitle, sLabel, sBtn, sBtnOn, sSmall;
    void InitStyles()
    {
        if (sTitle != null) return;
        sLabel = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
        sLabel.normal.textColor = new Color(0.13f, 0.13f, 0.13f);
        sTitle = new GUIStyle(sLabel) { fontSize = 22 };
        sSmall = new GUIStyle(sLabel) { fontSize = 11, fontStyle = FontStyle.Normal, wordWrap = true };
        sBtn = new GUIStyle(GUI.skin.button) { fontSize = 12, fixedHeight = 26 };
        sBtnOn = new GUIStyle(sBtn) { fontStyle = FontStyle.Bold };
        sBtnOn.normal.textColor = Color.white; sBtnOn.normal.background = Texture2D.whiteTexture;
        if (uiFont) { sLabel.font = sTitle.font = sSmall.font = sBtn.font = sBtnOn.font = uiFont; }
    }

    int Row(string label, string[] names, int cur, Color[] swatch)
    {
        GUILayout.Label(label, sLabel);
        GUILayout.BeginHorizontal();
        for (int i = 0; i < names.Length; i++)
        {
            var old = GUI.backgroundColor;
            if (i == cur) GUI.backgroundColor = swatch != null ? swatch[i] * 0.8f : new Color(0.2f, 0.2f, 0.2f);
            if (GUILayout.Button(names[i], i == cur ? sBtnOn : sBtn)) cur = i;
            GUI.backgroundColor = old;
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(6);
        return cur;
    }

    bool Toggle(string label, bool v)
    {
        if (GUILayout.Button((v ? "■ " : "□ ") + label, sBtn)) v = !v;
        return v;
    }

    void OnGUI()
    {
        InitStyles();
        float s = Screen.height / 720f;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1));
        var o = GUI.color; GUI.color = new Color(1, 1, 1, 0.82f);
        GUI.DrawTexture(new Rect(12, 12, 310, 696), Texture2D.whiteTexture); GUI.color = o;

        GUILayout.BeginArea(new Rect(24, 22, 286, 680));
        GUILayout.Label("キャラクリ（試作）", sTitle);
        GUILayout.Label("素体：StarterAssets の人型　アーマー：コードで生成", sSmall);
        GUILayout.Space(10);
        if (error != null) { GUILayout.Label(error, sSmall); GUILayout.EndArea(); return; }

        int a = Row("アーマーの色", ARMOR_N, armorI, ARMOR);
        int su = Row("スーツの色", SUIT_N, suitI, SUIT);
        int ac = Row("差し色（発光）", ACCENT_N, accentI, ACCENT);
        int h = Row("ヘルメット", HELMET_N, helmetI, null);
        GUILayout.Label("パーツ", sLabel);
        bool p = Toggle("バックパック", showPack);
        bool sh = Toggle("肩アーマー", showShoulder);
        bool lg = Toggle("脚アーマー", showLegs);
        if (a != armorI || su != suitI || ac != accentI || h != helmetI || p != showPack || sh != showShoulder || lg != showLegs)
        {
            armorI = a; suitI = su; accentI = ac; helmetI = h; showPack = p; showShoulder = sh; showLegs = lg;
            ApplyLook();
        }
        GUILayout.Space(12);
        GUILayout.Label("合計ポリゴン数：" + triCount.ToString("N0") + " tris", sLabel);
        GUILayout.Space(8);
        GUILayout.Label("ドラッグ：回転　ホイール：寄る／引く", sSmall);
        GUILayout.EndArea();
    }
}
