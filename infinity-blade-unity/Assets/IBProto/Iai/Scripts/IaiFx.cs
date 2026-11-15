using System.Collections.Generic;
using UnityEngine;

// 演出：抜刀の光の弧、画面を走る光の線、火花、衝撃の輪、斬り痕。
// 凝ったモーションの代わりに「光が走ること」で斬ったように見せる。全部ひとつの光るメッシュに毎フレーム描く
public class IaiFx : MonoBehaviour
{
    class ArcFx { public Vector3 c, e1, e2; public float r, a0, a1, width, life, reveal, t; public Color col; }
    class Spark { public Vector3 p, v; public float life, t, size, drag; public Color col; }
    class RingFx { public Vector3 p; public float r0, r1, life, t, width; public Color col; }
    class LineFx { public Vector2 center, dir; public float len, width, life, t; public Color col; }       // 画面上の線（0〜1 の画面座標）
    class StreakFx { public Vector3 a, b; public float width, life, t; public Color col; }

    readonly List<ArcFx> arcs = new List<ArcFx>();
    readonly List<Spark> sparks = new List<Spark>();
    readonly List<RingFx> rings = new List<RingFx>();
    readonly List<LineFx> lines = new List<LineFx>();
    readonly List<StreakFx> streaks = new List<StreakFx>();

    readonly List<Vector3> vs = new List<Vector3>();
    readonly List<Color> cs = new List<Color>();
    readonly List<int> ts = new List<int>();
    Mesh mesh; Camera cam; Material mat;
    public Material GlowMaterial => mat;

    public static IaiFx Create(Camera cam)
    {
        var g = new GameObject("Iai FX");
        var fx = g.AddComponent<IaiFx>();
        fx.cam = cam;
        fx.mesh = new Mesh { name = "IaiFx" };
        fx.mesh.MarkDynamic();
        var sh = Shader.Find("IBProto/IaiGlow");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        fx.mat = new Material(sh);
        if (fx.mat.HasProperty("_Intensity")) fx.mat.SetFloat("_Intensity", 2.2f);
        if (fx.mat.HasProperty("_ZTest")) fx.mat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
        g.AddComponent<MeshFilter>().sharedMesh = fx.mesh;
        var mr = g.AddComponent<MeshRenderer>();
        mr.sharedMaterial = fx.mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return fx;
    }

    // 弧：中心 c、面を張る2本の軸 e1・e2、半径 r、角度 a0→a1（度）。reveal 秒で先端が走り、life 秒で消える
    public void Arc(Vector3 c, Vector3 e1, Vector3 e2, float r, float a0, float a1, float width, Color col, float life = 0.26f, float reveal = 0.045f)
    {
        arcs.Add(new ArcFx { c = c, e1 = e1.normalized, e2 = e2.normalized, r = r, a0 = a0, a1 = a1, width = width, col = col, life = life, reveal = reveal });
    }

    public void Sparks(Vector3 p, int n, Color col, float speed, Vector3 bias, float life = 0.45f, float size = 0.03f)
    {
        for (int i = 0; i < n; i++)
        {
            Vector3 d = (Random.onUnitSphere + bias).normalized;
            sparks.Add(new Spark { p = p, v = d * speed * Random.Range(0.35f, 1f), life = life * Random.Range(0.5f, 1f), size = size * Random.Range(0.6f, 1.3f), col = col, drag = Random.Range(2f, 4.5f) });
        }
    }

    public void Ring(Vector3 p, float r0, float r1, Color col, float life = 0.22f, float width = 0.08f)
    {
        rings.Add(new RingFx { p = p, r0 = r0, r1 = r1, col = col, life = life, width = width });
    }

    // 画面を横切る光の線。center は画面座標(0〜1)、dir は向き
    public void ScreenLine(Vector2 center, Vector2 dir, Color col, float life = 0.16f, float width = 0.012f, float len = 3f)
    {
        lines.Add(new LineFx { center = center, dir = dir.normalized, col = col, life = life, width = width, len = len });
    }

    public void Streak(Vector3 a, Vector3 b, Color col, float width = 0.06f, float life = 0.3f)
    {
        streaks.Add(new StreakFx { a = a, b = b, col = col, width = width, life = life });
    }

    void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb, Color cc, Color cd)
    {
        int i = vs.Count;
        vs.Add(a); vs.Add(b); vs.Add(c); vs.Add(d);
        cs.Add(ca); cs.Add(cb); cs.Add(cc); cs.Add(cd);
        ts.Add(i); ts.Add(i + 1); ts.Add(i + 2); ts.Add(i); ts.Add(i + 2); ts.Add(i + 3);
    }

    static Color A(Color c, float a) { c.a *= a; return c; }

    // 点 a→b を結ぶ、カメラの方を向いた帯（両端で細くなる）
    void Ribbon(Vector3 a, Vector3 b, float wa, float wb, Color ca, Color cb, Vector3 eye)
    {
        Vector3 t = b - a; if (t.sqrMagnitude < 1e-10f) return;
        Vector3 na = Vector3.Cross(t, eye - a).normalized, nb = Vector3.Cross(t, eye - b).normalized;
        Quad(a - na * wa, a + na * wa, b + nb * wb, b - nb * wb, ca, ca, cb, cb);
    }

    void LateUpdate()
    {
        if (cam == null || mesh == null) return;
        float dt = Time.deltaTime, udt = Time.unscaledDeltaTime;
        Vector3 eye = cam.transform.position;
        vs.Clear(); cs.Clear(); ts.Clear();

        // ---- 弧 ----
        for (int k = arcs.Count - 1; k >= 0; k--)
        {
            var a = arcs[k]; a.t += dt;
            float head = Mathf.Clamp01(a.t / a.reveal);
            float fade = 1f - Mathf.Clamp01((a.t - a.reveal) / a.life);
            if (fade <= 0f) { arcs.RemoveAt(k); continue; }
            const int N = 28;
            float tail = Mathf.Clamp01((a.t - a.reveal) / a.life * 0.8f);     // 後ろから消えていく
            Vector3 prev = Vector3.zero; float pw = 0; bool has = false;
            for (int i = 0; i <= N; i++)
            {
                float s = i / (float)N;
                if (s > head) break;
                float ang = Mathf.Lerp(a.a0, a.a1, s) * Mathf.Deg2Rad;
                Vector3 p = a.c + (a.e1 * Mathf.Cos(ang) + a.e2 * Mathf.Sin(ang)) * a.r;
                float shape = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(s * 0.92f + 0.04f)), 0.7f);
                float w = a.width * shape * fade * Mathf.Clamp01((s - tail) * 6f + 0.02f);
                if (has)
                {
                    Ribbon(prev, p, pw, w, A(a.col, 0.75f * fade), A(a.col, 0.75f * fade), eye);
                    Ribbon(prev, p, pw * 0.28f, w * 0.28f, A(Color.white, fade), A(Color.white, fade), eye);
                }
                prev = p; pw = w; has = true;
            }
        }

        // ---- 火花 ----
        for (int k = sparks.Count - 1; k >= 0; k--)
        {
            var s = sparks[k]; s.t += dt;
            if (s.t >= s.life) { sparks.RemoveAt(k); continue; }
            s.v += Vector3.down * 9f * dt; s.v *= Mathf.Exp(-s.drag * dt); s.p += s.v * dt;
            if (s.p.y < 0.02f) { s.p.y = 0.02f; s.v.y = Mathf.Abs(s.v.y) * 0.35f; }
            float f = 1f - s.t / s.life;
            Vector3 tailP = s.p - s.v * 0.035f - s.v.normalized * s.size;
            Ribbon(tailP, s.p, 0f, s.size * f, A(s.col, 0f), A(Color.Lerp(s.col, Color.white, f * 0.7f), f), eye);
        }

        // ---- 衝撃の輪（カメラの方を向く） ----
        Vector3 cr = cam.transform.right, cu = cam.transform.up;
        for (int k = rings.Count - 1; k >= 0; k--)
        {
            var r = rings[k]; r.t += dt;
            float u = r.t / r.life;
            if (u >= 1f) { rings.RemoveAt(k); continue; }
            float e = 1f - (1f - u) * (1f - u);
            float rad = Mathf.Lerp(r.r0, r.r1, e), w = r.width * (1f - u);
            Color c = A(r.col, 1f - u);
            const int N = 30;
            for (int i = 0; i < N; i++)
            {
                float a0 = i / (float)N * Mathf.PI * 2f, a1 = (i + 1) / (float)N * Mathf.PI * 2f;
                Vector3 d0 = cr * Mathf.Cos(a0) + cu * Mathf.Sin(a0), d1 = cr * Mathf.Cos(a1) + cu * Mathf.Sin(a1);
                Quad(r.p + d0 * rad, r.p + d0 * (rad + w), r.p + d1 * (rad + w), r.p + d1 * rad, c, c, c, c);
            }
        }

        // ---- 斬り痕 ----
        for (int k = streaks.Count - 1; k >= 0; k--)
        {
            var s = streaks[k]; s.t += dt;
            float u = s.t / s.life;
            if (u >= 1f) { streaks.RemoveAt(k); continue; }
            float f = 1f - u * u;
            Vector3 m = (s.a + s.b) * 0.5f;
            Ribbon(s.a, m, 0f, s.width * f, A(s.col, f), A(s.col, f), eye);
            Ribbon(m, s.b, s.width * f, 0f, A(s.col, f), A(s.col, f), eye);
            Ribbon(s.a, m, 0f, s.width * f * 0.3f, A(Color.white, f), A(Color.white, f), eye);
            Ribbon(m, s.b, s.width * f * 0.3f, 0f, A(Color.white, f), A(Color.white, f), eye);
        }

        // ---- 画面を走る線（カメラのすぐ前に置く。止め絵の間も動くよう実時間で進める） ----
        float dist = cam.nearClipPlane + 0.35f;
        float hh = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * dist, hw = hh * cam.aspect;
        Vector3 fwd = cam.transform.forward;
        for (int k = lines.Count - 1; k >= 0; k--)
        {
            var l = lines[k]; l.t += udt;
            float u = l.t / l.life;
            if (u >= 1f) { lines.RemoveAt(k); continue; }
            float f = 1f - u;
            Vector3 c0 = eye + fwd * dist + cr * ((l.center.x - 0.5f) * 2f * hw) + cu * ((l.center.y - 0.5f) * 2f * hh);
            Vector3 d = (cr * l.dir.x * cam.aspect + cu * l.dir.y).normalized;
            Vector3 n = Vector3.Cross(d, fwd).normalized;
            float len = l.len * hh, w = l.width * hh * (0.25f + 0.75f * f * f);
            Vector3 a = c0 - d * len, b = c0 + d * len;
            Color c = A(l.col, f), z = A(l.col, 0f);
            Quad(a - n * w, a + n * w, c0 + n * w, c0 - n * w, z, z, c, c);
            Quad(c0 - n * w, c0 + n * w, b + n * w, b - n * w, c, c, z, z);
            Color wc = A(Color.white, f), wz = A(Color.white, 0f);
            Quad(a - n * w * 0.3f, a + n * w * 0.3f, c0 + n * w * 0.3f, c0 - n * w * 0.3f, wz, wz, wc, wc);
            Quad(c0 - n * w * 0.3f, c0 + n * w * 0.3f, b + n * w * 0.3f, b - n * w * 0.3f, wc, wc, wz, wz);
        }

        mesh.Clear();
        if (vs.Count > 0)
        {
            mesh.SetVertices(vs); mesh.SetColors(cs); mesh.SetTriangles(ts, 0);
        }
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
    }
}
