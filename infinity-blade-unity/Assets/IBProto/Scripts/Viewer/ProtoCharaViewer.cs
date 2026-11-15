using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// キャラを眺めるだけの表示用。ライト・床・回転カメラを用意し、素体に待機モーションを流す。
// ドラッグで回転、ホイールで寄る／引く。UIはなし。
public class ProtoCharaViewer : MonoBehaviour
{
    [Tooltip("空なら StarterAssets の StarterAssetsThirdPerson.controller を自動で読む")]
    public RuntimeAnimatorController animController;
    const string CTRL_PATH = "Assets/StarterAssets/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";

    Camera cam; readonly System.Collections.Generic.List<Animator> anims = new System.Collections.Generic.List<Animator>();
    Vector3 center = Vector3.zero; float height = 1.8f, maxDist = 6f;
    float yaw = 160f, pitch = 6f, dist = 4.2f, autoSpin = 1f;
    Vector2 lastMouse; bool dragging;

    void Start()
    {
#if UNITY_EDITOR
        if (animController == null) animController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CTRL_PATH);
#endif
        // シーンにいるキャラを全部動かし、全員が収まるように枠を決める
        var bounds = new Bounds(Vector3.zero, Vector3.zero); bool has = false;
        foreach (var n in new[] { "SamuraiCharacter", "PBCharacter", "EnemyKing" })
        {
            var ch = GameObject.Find(n); if (ch == null) continue;
            var a = ch.GetComponent<Animator>();
            if (a != null) { a.applyRootMotion = false; if (animController != null) a.runtimeAnimatorController = animController; anims.Add(a); }
            foreach (var r in ch.GetComponentsInChildren<Renderer>()) { if (!has) { bounds = r.bounds; has = true; } else bounds.Encapsulate(r.bounds); }
        }
        if (has)
        {
            center = new Vector3(bounds.center.x, 0, bounds.center.z);
            height = bounds.max.y;
            dist = Mathf.Clamp(Mathf.Max(height * 2.1f, bounds.size.x * 2.4f), 4.2f, 9f);
            maxDist = dist + 3f;
        }

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
        foreach (var l in lights) l.enabled = false;
        MakeLight("Key", new Color(1f, 0.95f, 0.88f), 1.25f, new Vector3(35f, 145f, 0), true);
        MakeLight("Rim", new Color(0.7f, 0.82f, 1f), 1.1f, new Vector3(20f, -20f, 0), false);
        MakeLight("Fill", new Color(1f, 0.9f, 0.85f), 0.35f, new Vector3(10f, 250f, 0), false);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ground.name = "Stage"; ground.transform.localScale = new Vector3(4.2f, 0.02f, 4.2f); ground.transform.position = new Vector3(0, -0.02f, 0);
        var gm = ProtoBuild.Lit(new Color(0.7f, 0.69f, 0.67f));
        if (gm.HasProperty("_Smoothness")) gm.SetFloat("_Smoothness", 0.3f);
        ground.GetComponent<Renderer>().sharedMaterial = gm;
    }

    void MakeLight(string n, Color c, float i, Vector3 euler, bool shadows)
    {
        var g = new GameObject(n + " Light"); var l = g.AddComponent<Light>();
        l.type = LightType.Directional; l.color = c; l.intensity = i; l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        g.transform.rotation = Quaternion.Euler(euler);
    }

    void Update()
    {
        foreach (var anim in anims)
            if (anim != null && anim.runtimeAnimatorController != null)
            {
                anim.SetBool("Grounded", true); anim.SetFloat("Speed", 0f); anim.SetFloat("MotionSpeed", 1f);
            }
        Vector2 mp = ProtoInput.MousePos;
        if (ProtoInput.LeftPressed) { dragging = true; lastMouse = mp; autoSpin = 0f; }
        if (ProtoInput.LeftReleased) dragging = false;
        if (dragging) { var d = mp - lastMouse; yaw += d.x * 0.35f; pitch = Mathf.Clamp(pitch - d.y * 0.2f, -10f, 40f); lastMouse = mp; }
        dist = Mathf.Clamp(dist - ProtoInput.Scroll * 0.0025f, 1.2f, maxDist);
        yaw += autoSpin * 12f * Time.deltaTime;
        Vector3 target = center + new Vector3(0f, Mathf.Lerp(height * 0.5f, height * 0.8f, Mathf.InverseLerp(4.2f, 1.2f, dist)), 0);
        cam.transform.position = target + Quaternion.Euler(pitch, yaw, 0) * new Vector3(0, 0, -dist);
        cam.transform.LookAt(target);
    }
}
