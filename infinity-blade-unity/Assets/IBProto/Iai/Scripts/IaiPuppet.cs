using System;
using UnityEngine;

// 姿勢人形：骨アニメを使わず、保存してある「姿勢（ポーズ）」の間を一瞬で切り替える。
// 姿勢はこのプレハブの中に保存されている（Blender で作った形を取り込んだもの）。
[Serializable]
public class IaiPose
{
    public string name;
    public Vector3[] pos;
    public Quaternion[] rot;
}

public class IaiPuppet : MonoBehaviour
{
    public enum Ease { Linear, Out, InOut, Snap, Heavy, Whip, Settle }

    [Tooltip("関節（Root から順）")]
    public Transform[] joints;
    [Tooltip("保存してある姿勢")]
    public IaiPose[] poses;
    [Tooltip("布として揺らす関節の名前の頭")]
    public string[] clothPrefixes = { "Tail", "Tab" };
    [Tooltip("布の揺れの大きさ（度）")]
    public float clothSway = 4f;
    [Tooltip("息づかいの上下（m）")]
    public float breath = 0.006f;

    Vector3[] fromP; Quaternion[] fromR;
    IaiPose target;
    float t, dur; Ease ease;
    bool[] isCloth; float[] phase;
    int hips = -1, chest = -1;
    float clock;
    // 姿勢の上に重ねる「体の芯」の動き（度）。ゲーム側がばねで動かす
    [NonSerialized] public float chestYaw, chestPitch, chestRoll;
    [NonSerialized] public float swayBoost;     // 斬った直後に布を大きく揺らす
    public string Current => target != null ? target.name : "";
    public bool Moving => t < dur;

    void Awake() { Init(); }

    void Init()
    {
        if (fromP != null || joints == null) return;
        int n = joints.Length;
        fromP = new Vector3[n]; fromR = new Quaternion[n]; isCloth = new bool[n]; phase = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (joints[i] == null) continue;
            string nm = joints[i].name;
            foreach (var p in clothPrefixes) if (nm.StartsWith(p)) isCloth[i] = true;
            phase[i] = i * 1.7f;
            if (nm == "Hips") hips = i;
            if (nm == "Chest") chest = i;
        }
    }

    public IaiPose Find(string name)
    {
        if (poses == null) return null;
        foreach (var p in poses) if (p.name == name) return p;
        return null;
    }

    public Transform Joint(string name)
    {
        if (joints == null) return null;
        foreach (var j in joints) if (j != null && j.name == name) return j;
        return null;
    }

    public void Snap(string name)
    {
        Init();
        var p = Find(name); if (p == null) return;
        target = p; t = dur = 0f;
        Apply(1f);
    }

    public void Go(string name, float seconds, Ease e = Ease.Out)
    {
        Init();
        var p = Find(name); if (p == null) { Debug.LogWarning("[Iai] 姿勢がありません: " + name); return; }
        // いまの見た目（途中の姿勢）から始める
        Capture();
        target = p; t = 0f; dur = Mathf.Max(0.0001f, seconds); ease = e;
    }

    void Capture()
    {
        if (target == null) { for (int i = 0; i < joints.Length; i++) if (joints[i] != null) { fromP[i] = joints[i].localPosition; fromR[i] = joints[i].localRotation; } return; }
        float k = Curve(dur <= 0f ? 1f : Mathf.Clamp01(t / dur));
        for (int i = 0; i < joints.Length; i++)
        {
            if (i >= target.pos.Length) break;
            fromP[i] = Vector3.LerpUnclamped(fromP[i], target.pos[i], k);
            fromR[i] = Quaternion.SlerpUnclamped(fromR[i], target.rot[i], k);
        }
    }

    float Curve(float k)
    {
        switch (ease)
        {
            case Ease.Out: return 1f - (1f - k) * (1f - k) * (1f - k);
            case Ease.InOut: return k * k * (3f - 2f * k);
            case Ease.Snap: { float c = 1.9f; float u = k - 1f; return 1f + (c + 1f) * u * u * u + c * u * u; }   // 行き過ぎて戻る
            case Ease.Heavy: return k * k * k;                                                                    // 溜めてから一気に
            case Ease.Whip: { float a = 1f - Mathf.Pow(1f - k, 2.6f); float c = 1.1f; float u = a - 1f; return 1f + (c + 1f) * u * u * u + c * u * u; } // 一気に振り抜いて少し行き過ぎる
            case Ease.Settle: { float a = k * k * (3f - 2f * k); return a * a * (3f - 2f * a); }                  // ゆっくり出て、ゆっくり収まる
            default: return k;
        }
    }

    void Apply(float k)
    {
        if (target == null) return;
        int n = Mathf.Min(joints.Length, target.pos.Length);
        for (int i = 0; i < n; i++)
        {
            var j = joints[i]; if (j == null) continue;
            Vector3 p = k >= 1f ? target.pos[i] : Vector3.LerpUnclamped(fromP[i], target.pos[i], k);
            Quaternion r = k >= 1f ? target.rot[i] : Quaternion.SlerpUnclamped(fromR[i], target.rot[i], k);
            if (isCloth[i])
            {
                float a = clothSway * (1f + swayBoost * 3f);
                r = r * Quaternion.Euler(Mathf.Sin(clock * 1.9f + phase[i]) * a, 0f, Mathf.Sin(clock * 1.3f + phase[i] * 2f) * a * 0.6f);
            }
            if (i == hips) p.y += Mathf.Sin(clock * 1.7f) * breath;
            j.localPosition = p; j.localRotation = r;
        }
        // 上半身のひねり・反り（キャラの向きを基準に重ねる。腕と剣も一緒に回る）
        if (chest >= 0 && joints[chest] != null && (chestYaw != 0f || chestPitch != 0f || chestRoll != 0f))
        {
            var c = joints[chest];
            c.rotation = Quaternion.AngleAxis(chestYaw, transform.up) * Quaternion.AngleAxis(chestPitch, transform.right)
                       * Quaternion.AngleAxis(chestRoll, transform.forward) * c.rotation;
        }
    }

    void LateUpdate()
    {
        if (target == null || fromP == null) return;
        float dt = Time.deltaTime;
        clock += dt;
        swayBoost = Mathf.MoveTowards(swayBoost, 0f, dt * 2.5f);
        if (t < dur) t = Mathf.Min(dur, t + dt);
        Apply(dur <= 0f ? 1f : Curve(Mathf.Clamp01(t / dur)));
    }
}
