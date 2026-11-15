using UnityEngine;

// 効果音をコードで合成する（音素材なしで動かすため）
public class ProtoAudio : MonoBehaviour
{
    AudioSource src, pitched;
    AudioClip parry, block, dodge, hurt, slash, cut, guarded, brk, part, warn, flow, mikiri, heavy;
    AudioClip[] clangs;
    const int SR = 44100;
    System.Random rng = new System.Random(1);

    void Awake()
    {
        src = gameObject.AddComponent<AudioSource>();
        pitched = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false; pitched.playOnAwake = false;

        parry   = Mix("parry", 0.18f, t => Tri(t, 900f) * Env(t, 22f) * 0.6f + Sq(t, 1800f) * Env(t, 40f) * 0.12f + Noise() * Env(t, 60f) * 0.4f);
        block   = Mix("block", 0.14f, t => Sq(t, 260f) * Env(t, 25f) * 0.35f + Noise() * Env(t, 30f) * 0.35f);
        dodge   = Mix("dodge", 0.16f, t => Noise() * Env(t, 18f) * 0.25f);
        hurt    = Mix("hurt", 0.3f, t => Saw(t, Mathf.Lerp(140f, 50f, t / 0.3f)) * Env(t, 10f) * 0.45f + Noise() * Env(t, 15f) * 0.4f);
        slash   = Mix("slash", 0.11f, t => Noise() * Env(t, 30f) * 0.25f);
        cut     = Mix("cut", 0.1f, t => Sq(t, 240f) * Env(t, 40f) * 0.25f + Noise() * Env(t, 45f) * 0.45f);
        guarded = Mix("guarded", 0.07f, t => Sq(t, 500f) * Env(t, 60f) * 0.15f + Noise() * Env(t, 70f) * 0.2f);
        brk     = Mix("brk", 0.32f, t => Tri(t, Mathf.Lerp(330f, 990f, t / 0.32f)) * Env(t, 8f) * 0.45f);
        part    = Mix("part", 0.4f, t => Saw(t, Mathf.Lerp(180f, 60f, t / 0.4f)) * Env(t, 7f) * 0.4f + Noise() * Env(t, 10f) * 0.5f);
        warn    = Mix("warn", 0.07f, t => Mathf.Sin(2f * Mathf.PI * 420f * t) * Env(t, 50f) * 0.15f);

        // パリィ：鈍い金属音。太い刃同士がぶつかる「ゴィン」。音の高さは連続しても変えず、5種類から選ぶ
        float[] bases = { 168f, 196f, 231f, 152f, 212f };
        float[] spread = { 2.71f, 2.43f, 2.92f, 2.58f, 2.36f };
        clangs = new AudioClip[bases.Length];
        for (int i = 0; i < bases.Length; i++)
        {
            float f0 = bases[i], r2 = spread[i];
            clangs[i] = Mix("clang" + i, 0.55f, t =>
                Mathf.Sin(2f * Mathf.PI * f0 * t) * Env(t, 7f) * 0.42f
              + Mathf.Sin(2f * Mathf.PI * f0 * r2 * t + 0.7f) * Env(t, 11f) * 0.3f
              + Mathf.Sin(2f * Mathf.PI * f0 * 4.17f * t + 1.3f) * Env(t, 19f) * 0.18f
              + Mathf.Sin(2f * Mathf.PI * f0 * 6.3f * t) * Env(t, 30f) * 0.09f
              + Mathf.Sin(2f * Mathf.PI * 70f * t) * Env(t, 24f) * 0.35f        // ドンという低い芯
              + Noise() * Env(t, 90f) * 0.55f);                                 // 当たった瞬間のカツッ
        }
        // 受け流し：刃が滑る「シャッ」＋短い金属音
        flow   = Mix("flow", 0.42f, t => Noise() * Env(t, 9f) * 0.22f * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 37f * t))
                                       + Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(1180f, 860f, t / 0.42f) * t) * Env(t, 8f) * 0.18f
                                       + Mathf.Sin(2f * Mathf.PI * 205f * t) * Env(t, 14f) * 0.25f);
        mikiri = Mix("mikiri", 0.6f, t => Mathf.Sin(2f * Mathf.PI * 1320f * t) * Env(t, 5f) * 0.18f + Mathf.Sin(2f * Mathf.PI * 1985f * t) * Env(t, 7f) * 0.1f + Noise() * Env(t, 40f) * 0.25f);
        heavy  = Mix("heavy", 0.3f, t => Sq(t, 120f) * Env(t, 14f) * 0.25f + Mathf.Sin(2f * Mathf.PI * 55f * t) * Env(t, 9f) * 0.5f + Noise() * Env(t, 25f) * 0.5f);
    }

    float Env(float t, float k) { return Mathf.Exp(-t * k); }
    float Sq(float t, float f) { return Mathf.Sin(2f * Mathf.PI * f * t) >= 0 ? 1f : -1f; }
    float Tri(float t, float f) { float p = (t * f) % 1f; return 4f * Mathf.Abs(p - 0.5f) - 1f; }
    float Saw(float t, float f) { return 2f * ((t * f) % 1f) - 1f; }
    float Noise() { return (float)(rng.NextDouble() * 2.0 - 1.0); }

    AudioClip Mix(string name, float dur, System.Func<float, float> f)
    {
        int n = Mathf.CeilToInt(SR * dur);
        var d = new float[n];
        for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(f(i / (float)SR), -1f, 1f);
        var clip = AudioClip.Create(name, n, 1, SR, false);
        clip.SetData(d, 0);
        return clip;
    }

    int lastClang = -1;
    public void Parry(int combo)
    {
        // 同じ音が続かないように選ぶ。高さはほんの少しだけ揺らす
        int i = Random.Range(0, clangs.Length);
        if (i == lastClang) i = (i + 1 + Random.Range(0, clangs.Length - 1)) % clangs.Length;
        lastClang = i;
        pitched.pitch = Random.Range(0.96f, 1.04f);
        pitched.PlayOneShot(clangs[i], 1f);
    }
    public void Flow() { pitched.pitch = Random.Range(0.97f, 1.03f); pitched.PlayOneShot(flow, 1f); src.PlayOneShot(clangs[Random.Range(0, clangs.Length)], 0.45f); }
    public void Mikiri() { src.PlayOneShot(mikiri, 0.9f); }
    public void Heavy() { src.PlayOneShot(heavy, 0.9f); }
    public void Block() { src.PlayOneShot(block); }
    public void Dodge() { src.PlayOneShot(dodge); }
    public void Hurt() { src.PlayOneShot(hurt); }
    public void Slash() { src.PlayOneShot(slash); }
    public void Cut() { src.PlayOneShot(cut); }
    public void Guarded() { src.PlayOneShot(guarded); }
    public void Break() { src.PlayOneShot(brk); }
    public void Part() { src.PlayOneShot(part); }
    public void Warn() { src.PlayOneShot(warn); }
}
