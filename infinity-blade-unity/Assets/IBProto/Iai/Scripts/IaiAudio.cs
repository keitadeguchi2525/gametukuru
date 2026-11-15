using UnityEngine;

// 効果音。音は Iai/Audio の音声ファイル（同じ名前で差し替えれば音が替わる）
public class IaiAudio : MonoBehaviour
{
    [Header("納刀（シュ… → コッ）")]
    public AudioClip[] notoSlide;
    public AudioClip[] notoClick;
    [Header("抜刀・剣戟・斬撃")]
    public AudioClip[] batto;
    public AudioClip[] clash;
    public AudioClip sae;
    public AudioClip[] cut;
    public AudioClip[] deflect;
    [Header("ボス")]
    public AudioClip warn;
    public AudioClip warnRed;
    public AudioClip[] whoosh;
    public AudioClip brk;
    [Header("そのほか")]
    public AudioClip hurt;
    public AudioClip finish;
    public AudioClip dodge;
    public AudioClip ambience;
    [Range(0, 1)] public float volume = 0.9f;
    [Range(0, 1)] public float ambienceVolume = 0.35f;

    AudioSource[] pool; int next; AudioSource amb;
    int[] last = new int[8];

    void Awake()
    {
#if UNITY_EDITOR
        FillInEditor();
#endif
        pool = new AudioSource[10];
        for (int i = 0; i < pool.Length; i++) { pool[i] = gameObject.AddComponent<AudioSource>(); pool[i].playOnAwake = false; pool[i].spatialBlend = 0f; }
        amb = gameObject.AddComponent<AudioSource>();
        amb.loop = true; amb.clip = ambience; amb.volume = ambienceVolume; amb.spatialBlend = 0f;
        if (ambience != null) amb.Play();
    }

#if UNITY_EDITOR
    // インスペクターで空のままなら、Iai/Audio から名前で探して入れる
    void FillInEditor()
    {
        const string D = "Assets/IBProto/Iai/Audio/";
        System.Func<string, AudioClip> one = n => UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(D + n + ".wav");
        System.Func<string, int, AudioClip[]> many = (n, c) => { var a = new System.Collections.Generic.List<AudioClip>(); for (int i = 0; i < c; i++) { var x = one(n + i); if (x != null) a.Add(x); } return a.ToArray(); };
        if (notoSlide == null || notoSlide.Length == 0) notoSlide = many("noto_slide", 3);
        if (notoClick == null || notoClick.Length == 0) notoClick = many("noto_click", 4);
        if (batto == null || batto.Length == 0) batto = many("batto", 3);
        if (clash == null || clash.Length == 0) clash = many("clash", 5);
        if (cut == null || cut.Length == 0) cut = many("cut", 3);
        if (deflect == null || deflect.Length == 0) deflect = many("deflect", 2);
        if (whoosh == null || whoosh.Length == 0) whoosh = many("whoosh", 2);
        if (sae == null) sae = one("sae");
        if (warn == null) warn = one("warn");
        if (warnRed == null) warnRed = one("warn_red");
        if (brk == null) brk = one("break");
        if (hurt == null) hurt = one("hurt");
        if (finish == null) finish = one("finish");
        if (dodge == null) dodge = one("dodge");
        if (ambience == null) ambience = one("ambience");
    }
#endif

    void Play(AudioClip c, float vol, float pitchJitter = 0.03f, float pitch = 1f)
    {
        if (c == null || pool == null) return;
        var s = pool[next]; next = (next + 1) % pool.Length;
        s.pitch = pitch * Random.Range(1f - pitchJitter, 1f + pitchJitter);
        s.volume = vol * volume;
        s.clip = c; s.Play();
    }

    // 同じ音が続かないように選ぶ
    void PlayAny(AudioClip[] a, int slot, float vol, float jitter = 0.03f, float pitch = 1f)
    {
        if (a == null || a.Length == 0) return;
        int i = Random.Range(0, a.Length);
        if (a.Length > 1 && i == last[slot]) i = (i + 1 + Random.Range(0, a.Length - 1)) % a.Length;
        last[slot] = i;
        Play(a[i], vol, jitter, pitch);
    }

    public void NotoSlide() { PlayAny(notoSlide, 0, 0.5f); }
    public void NotoClick(float vol = 1f) { PlayAny(notoClick, 1, 0.95f * vol, 0.02f); }
    public void Batto() { PlayAny(batto, 2, 0.75f); }
    public void Clash(bool perfect) { PlayAny(clash, 3, 1f, 0.025f); if (perfect) Play(sae, 0.55f, 0.01f); }
    public void Cut() { PlayAny(cut, 4, 0.95f, 0.05f); }
    public void Deflect() { PlayAny(deflect, 5, 0.6f, 0.05f); }
    public void Warn(bool red) { Play(red ? warnRed : warn, red ? 0.6f : 0.5f, 0.02f); }
    public void Whoosh(float pitch = 1f) { PlayAny(whoosh, 6, 0.7f, 0.05f, pitch); }
    public void Break() { Play(brk, 1f, 0f); }
    public void Hurt() { Play(hurt, 1f, 0.03f); }
    public void Finish() { Play(finish, 1f, 0f); }
    public void Dodge() { Play(dodge, 0.5f, 0.06f); }
}
