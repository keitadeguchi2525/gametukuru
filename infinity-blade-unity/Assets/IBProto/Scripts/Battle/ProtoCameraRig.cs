using UnityEngine;

// カメラワーク。狙いの位置・注視点・画角・傾きへなめらかに追従し、
// その上に揺れ（トラウマ方式）・ロールの蹴り・ズームの蹴り・手持ち感を重ねる
public class ProtoCameraRig
{
    readonly Camera cam;
    Vector3 pos, posVel, look, lookVel;
    float fov = 55f, fovVel, roll, rollVel;
    float trauma, rollKick, fovPunch, t;

    public ProtoCameraRig(Camera c) { cam = c; }

    public void Snap(Vector3 p, Vector3 l, float f)
    {
        pos = p; look = l; fov = f; posVel = lookVel = Vector3.zero; fovVel = rollVel = 0; roll = 0;
    }

    // trauma: 揺れの強さ(0〜1) / rollDeg: 傾きの蹴り / punch: ズームの蹴り（+で寄る）
    public void Kick(float traumaAdd, float rollDeg, float punch)
    {
        trauma = Mathf.Clamp01(trauma + traumaAdd);
        rollKick += rollDeg;
        fovPunch += punch;
    }

    public void Tick(float dt, Vector3 tPos, Vector3 tLook, float tFov, float tRoll, float smooth)
    {
        t += dt;
        pos = Vector3.SmoothDamp(pos, tPos, ref posVel, smooth, Mathf.Infinity, dt);
        look = Vector3.SmoothDamp(look, tLook, ref lookVel, smooth * 0.8f, Mathf.Infinity, dt);
        fov = Mathf.SmoothDamp(fov, tFov, ref fovVel, smooth, Mathf.Infinity, dt);
        roll = Mathf.SmoothDamp(roll, tRoll, ref rollVel, smooth, Mathf.Infinity, dt);

        trauma = Mathf.Max(0, trauma - dt * 1.6f);
        float decay = 1f - Mathf.Exp(-dt * 9f);
        rollKick = Mathf.Lerp(rollKick, 0, decay);
        fovPunch = Mathf.Lerp(fovPunch, 0, decay);

        float sh = trauma * trauma, n = t * 28f;
        var shakePos = new Vector3(Mathf.PerlinNoise(n, 1.3f) - 0.5f, Mathf.PerlinNoise(2.7f, n) - 0.5f, 0) * sh * 0.5f;
        float shakeRoll = (Mathf.PerlinNoise(n, n * 0.5f) - 0.5f) * sh * 10f;
        // 手持ちカメラのゆらぎ
        float hx = (Mathf.PerlinNoise(t * 0.35f, 10f) - 0.5f) * 0.9f;
        float hy = (Mathf.PerlinNoise(20f, t * 0.35f) - 0.5f) * 0.9f;

        var dir = look - pos;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
        var rot = Quaternion.LookRotation(dir) * Quaternion.Euler(hx, hy, roll + rollKick + shakeRoll);
        cam.transform.rotation = rot;
        cam.transform.position = pos + rot * shakePos;
        cam.fieldOfView = Mathf.Clamp(fov - fovPunch, 20f, 90f);
    }
}
