using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// ステージ（舞台）の空気。ステージのプレハブに付いていて、置かれた瞬間に
// 空・霧・光・光のにじみ（ブルーム）などを、このステージ用に切り替える。
// 数字はプレハブをキャラ一覧と同じように開いて、インスペクターで直せる。
public class ProtoStage : MonoBehaviour
{
    [Header("空")]
    public Material sky;

    [Header("霧（遠くほど地平の色に溶ける）")]
    public Color fogColor = new Color(0.9f, 0.3f, 0.5f);
    public float fogStart = 18f;
    public float fogEnd = 110f;

    [Header("光")]
    public Light sun;
    public Color ambient = new Color(0.35f, 0.3f, 0.45f);

    [Header("画面の仕上げ")]
    [Range(0, 3)] public float bloom = 1.1f;
    [Range(0, 2)] public float bloomThreshold = 0.95f;
    [Range(0, 1)] public float vignette = 0.3f;
    [Range(-50, 50)] public float saturation = 12f;

    Volume volume;

    void Awake() { Apply(); }

    public void Apply()
    {
        if (sky != null) { RenderSettings.skybox = sky; }
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogStartDistance = fogStart;
        RenderSettings.fogEndDistance = fogEnd;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = ambient;
        if (sun != null) RenderSettings.sun = sun;

        var cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = sky != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            cam.backgroundColor = fogColor;
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 300f);
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;
        }

        // 光のにじみ・周辺の暗さ・彩度（このステージにいる間だけ）
        if (volume == null)
        {
            volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = 10;
            volume.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        }
        var prof = volume.sharedProfile;
        if (!prof.TryGet(out Bloom b)) b = prof.Add<Bloom>(true);
        b.intensity.Override(bloom); b.threshold.Override(bloomThreshold); b.scatter.Override(0.65f);
        if (!prof.TryGet(out Vignette v)) v = prof.Add<Vignette>(true);
        v.intensity.Override(vignette); v.smoothness.Override(0.45f); v.color.Override(new Color(0.03f, 0.01f, 0.08f));
        if (!prof.TryGet(out ColorAdjustments ca)) ca = prof.Add<ColorAdjustments>(true);
        ca.saturation.Override(saturation);
        if (!prof.TryGet(out Tonemapping tm)) tm = prof.Add<Tonemapping>(true);
        tm.mode.Override(TonemappingMode.None);   // のっぺりした色をそのまま出す
    }
}
