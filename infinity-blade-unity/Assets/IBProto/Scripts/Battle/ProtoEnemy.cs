using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// 敵の見た目とポーズ。カメラは -Z 側から +Z を見ている前提
// ・EnemyKing.prefab（王）があればそれを使い、大剣を両手で振る
// ・無ければ昔の棘甲獣（箱）で動く
public class ProtoEnemy : MonoBehaviour
{
    public string displayName = "棘甲獣（仮）";
    public float height = 5.2f;          // カメラの構図に使う、おおよその背の高さ
    public bool isKing;

    // 王が使えるか（プレハブがあるか）
    static int kingAvail = -1;
    static GameObject kingPrefab; static RuntimeAnimatorController kingCtrl;
    public static bool KingAvailable
    {
        get
        {
            if (kingAvail < 0)
            {
#if UNITY_EDITOR
                foreach (var id in AssetDatabase.FindAssets("EnemyKing t:Prefab"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(id);
                    if (path.Contains("/History/") || System.IO.Path.GetFileNameWithoutExtension(path) != "EnemyKing") continue;
                    kingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (kingPrefab) break;
                }
                foreach (var id in AssetDatabase.FindAssets("StarterAssetsThirdPerson t:AnimatorController"))
                { kingCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AssetDatabase.GUIDToAssetPath(id)); if (kingCtrl) break; }
#endif
                kingAvail = kingPrefab != null ? 1 : 0;
            }
            return kingAvail == 1;
        }
    }

    public class Part
    {
        public string key, name, mat;
        public float hp, cur;
        public bool broken;
        public List<GameObject> objs = new List<GameObject>();
    }

    public Transform torso, armLPivot, armRPivot;
    public Material white, dark, partMat, red;
    public readonly List<Part> parts = new List<Part>();
    public readonly Dictionary<Collider, Part> colliderToPart = new Dictionary<Collider, Part>();
    public readonly HashSet<Collider> bodyColliders = new HashSet<Collider>();
    Color whiteBase = new Color(0.965f, 0.96f, 0.945f);

    public static ProtoEnemy Create(Vector3 pos)
    {
        var root = new GameObject("Enemy");
        root.transform.position = pos;
        var e = root.AddComponent<ProtoEnemy>();
        if (!(KingAvailable && e.BuildKing())) e.Build();
        e.basePos = pos;
        return e;
    }

    void Build()
    {
        white = ProtoBuild.Lit(whiteBase);
        dark = ProtoBuild.Lit(new Color(0.18f, 0.17f, 0.2f));
        partMat = ProtoBuild.Lit(new Color(0.42f, 0.106f, 0.133f));
        red = ProtoBuild.Unlit(new Color(0.78f, 0.06f, 0.18f));

        var t = transform;
        AddBody(ProtoBuild.Box("LegL", new Vector3(0.75f, 1.6f, 0.75f), new Vector3(-0.75f, 0.8f, 0), dark, t));
        AddBody(ProtoBuild.Box("LegR", new Vector3(0.75f, 1.6f, 0.75f), new Vector3(0.75f, 0.8f, 0), dark, t));

        torso = new GameObject("Torso").transform; torso.SetParent(t, false);
        AddBody(ProtoBuild.Box("Body", new Vector3(2.5f, 2.2f, 1.6f), new Vector3(0, 2.6f, 0), white, torso));
        AddBody(ProtoBuild.Box("Head", new Vector3(1.3f, 1.0f, 1.2f), new Vector3(0, 4.1f, -0.3f), white, torso));
        ProtoBuild.Box("EyeL", new Vector3(0.22f, 0.12f, 0.05f), new Vector3(-0.3f, 4.2f, -0.92f), red, torso, false);
        ProtoBuild.Box("EyeR", new Vector3(0.22f, 0.12f, 0.05f), new Vector3(0.3f, 4.2f, -0.92f), red, torso, false);

        var horn = ProtoBuild.Cone("Horn", 0.24f, 1.1f, 6, new Vector3(0, 5.1f, -0.4f), new Vector3(-14f, 0, 0), partMat, torso);

        armLPivot = MakeArm(-1, out var clawL);
        armRPivot = MakeArm(1, out var clawR);

        var spL = ProtoBuild.Cone("SpikeL", 0.2f, 1.4f, 5, new Vector3(-0.95f, 4.2f, 0.45f), new Vector3(20f, 0, 20f), partMat, torso);
        var spR = ProtoBuild.Cone("SpikeR", 0.2f, 1.4f, 5, new Vector3(0.95f, 4.2f, 0.45f), new Vector3(20f, 0, -20f), partMat, torso);

        AddPart("horn", "角", "光輪片", 40, horn);
        AddPart("clawL", "左爪", "肩甲", 32, clawL);
        AddPart("clawR", "右爪", "肩甲", 32, clawR);
        AddPart("spikes", "背棘", "琥珀核", 44, spL, spR);
    }

    Transform MakeArm(int side, out GameObject claw)
    {
        var p = new GameObject(side < 0 ? "ArmL" : "ArmR").transform;
        p.SetParent(torso, false);
        p.localPosition = new Vector3(1.6f * side, 3.45f, 0);
        AddBody(ProtoBuild.Box("Arm", new Vector3(0.6f, 2.1f, 0.6f), new Vector3(0, -1.05f, 0), white, p));
        claw = ProtoBuild.Cone("Claw", 0.34f, 0.95f, 5, new Vector3(0, -2.55f, 0), new Vector3(0, 0, 180f), partMat, p);
        return p;
    }

    void AddBody(GameObject g) { var c = g.GetComponent<Collider>(); if (c) bodyColliders.Add(c); }

    void AddPart(string key, string name, string mat, float hp, params GameObject[] objs)
    {
        var p = new Part { key = key, name = name, mat = mat, hp = hp, cur = hp };
        foreach (var o in objs) { p.objs.Add(o); var c = o.GetComponent<Collider>(); if (c) colliderToPart[c] = p; }
        parts.Add(p);
    }

    // 部位を体から外して飛ばす
    public void DetachPart(Part p)
    {
        foreach (var o in p.objs)
        {
            var c = o.GetComponent<Collider>(); if (c) colliderToPart.Remove(c);
            if (c is MeshCollider mc) mc.convex = true;
            o.transform.SetParent(null, true);
            var rb = o.AddComponent<Rigidbody>();
            var vel = new Vector3(Random.Range(-2f, 2f), Random.Range(3f, 5f), Random.Range(-3f, -1f));
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = vel;
#else
            rb.velocity = vel;
#endif
            rb.angularVelocity = Random.insideUnitSphere * 8f;
            Destroy(o, 3f);
        }
    }

    // JS版と同じ値（ラジアン）で受け取る。X回転は向きが逆なので符号を反転する
    static void Arm(Transform pivot, float z, float x)
    {
        pivot.localRotation = Quaternion.Euler(-x * Mathf.Rad2Deg, 0, z * Mathf.Rad2Deg);
    }

    float leanCur;
    public void Pose(float time, ProtoGame.EnemyState state, ProtoGame.Attack a, float deadT)
    {
        if (isKing) { PoseKing(time, state, a, deadT); return; }
        float lz = 0.08f, lx = 0, rz = -0.08f, rx = 0, lean = 0, sway = Mathf.Sin(time * 2f) * 0.04f;
        if (state == ProtoGame.EnemyState.Attack && a != null)
        {
            float W = a.W;
            float p = EaseOut(Mathf.Clamp01(a.t / W)), s = Mathf.Clamp01((a.t - W) / 0.15f), r = Mathf.Clamp01((a.t - W - 0.15f) / 0.3f);
            if (a.dir == 'R')
            {
                float wz = -2.1f, wx = 0.35f, sz = 1.25f, sx = -0.9f;
                lz = a.t < W ? Mathf.Lerp(0.08f, wz, p) : r > 0 ? Mathf.Lerp(sz, 0.08f, r) : Mathf.Lerp(wz, sz, s);
                lx = a.t < W ? Mathf.Lerp(0, wx, p) : r > 0 ? Mathf.Lerp(sx, 0, r) : Mathf.Lerp(wx, sx, s);
                lean = a.t < W ? -0.12f * p : 0.15f;
            }
            else if (a.dir == 'L')
            {
                float wz = 2.1f, wx = 0.35f, sz = -1.25f, sx = -0.9f;
                rz = a.t < W ? Mathf.Lerp(-0.08f, wz, p) : r > 0 ? Mathf.Lerp(sz, -0.08f, r) : Mathf.Lerp(wz, sz, s);
                rx = a.t < W ? Mathf.Lerp(0, wx, p) : r > 0 ? Mathf.Lerp(sx, 0, r) : Mathf.Lerp(wx, sx, s);
                lean = a.t < W ? -0.12f * p : 0.15f;
            }
            else
            {
                float ux = -2.8f, dx = -0.45f;
                lx = rx = a.t < W ? Mathf.Lerp(0, ux, p) : r > 0 ? Mathf.Lerp(dx, 0, r) : Mathf.Lerp(ux, dx, s);
                lz = 0.25f; rz = -0.25f;
                lean = a.t < W ? -0.2f * p : 0.25f;
            }
            if (a.parried && r < 1) lean = -0.25f * (1 - r);
        }
        else if (state == ProtoGame.EnemyState.Break)
        {
            lean = -0.22f + Mathf.Sin(time * 9f) * 0.03f; lz = -0.85f; rz = 0.85f; lx = rx = -0.35f;
        }
        Arm(armLPivot, lz, lx);
        Arm(armRPivot, rz, rx);
        leanCur = Mathf.Lerp(leanCur, lean, state == ProtoGame.EnemyState.Attack ? 0.5f : 0.15f);
        torso.localRotation = Quaternion.Euler(-leanCur * Mathf.Rad2Deg, 0, 0);
        torso.localPosition = new Vector3(0, sway, 0);

        // 崩し中は赤く明滅
        if (state == ProtoGame.EnemyState.Break)
        {
            float k = 0.15f + 0.1f * Mathf.Sin(time * 12f);
            ProtoBuild.SetColor(white, Color.Lerp(whiteBase, new Color(1f, 0.45f, 0.45f), k * 2f));
        }
        else ProtoBuild.SetColor(white, whiteBase);

        if (state == ProtoGame.EnemyState.Dead)
        {
            float k = Mathf.Clamp01(deadT * 1.5f);
            transform.rotation = Quaternion.Euler(Mathf.Lerp(0, 90f, EaseOut(k)), 0, 0);
            var pos = transform.position; pos.y = Mathf.Lerp(0, -0.6f, k); transform.position = pos;
        }
    }

    // ================= 王 =================
    Vector3 basePos;
    Animator kAnim;
    Transform kSpine, kChest, rUA, rLA, rH, lUA, lLA, lH, sword;
    Light breakLight;
    static readonly Vector3 GRIP = new Vector3(0, 1.35f, 0);   // 大剣のローカル座標での右手の位置（原点は切っ先）

    // 姿勢のキー：G＝胸から見た握りの位置（右・上・前）、B＝刃の向き
    struct Key { public Vector3 g, b; public Key(Vector3 g, Vector3 b) { this.g = g; this.b = b.normalized; } }
    static Key K(float gx, float gy, float gz, float bx, float by, float bz) { return new Key(new Vector3(gx, gy, gz), new Vector3(bx, by, bz)); }
    static readonly Key IDLE = K(0.18f, -0.4f, 0.4f, 0.05f, -0.6f, 0.8f);
    static readonly Key BREAK = K(0.22f, -0.55f, 0.3f, 0.1f, -0.95f, 0.3f);
    static readonly Key KNOCK = K(0.1f, 0.45f, -0.05f, 0.25f, 0.75f, -0.45f);
    // 画面右（王の左）から来る横薙ぎ
    static readonly Key R_WIND = K(-0.38f, 0.25f, 0.05f, -0.45f, 0.55f, -0.7f), R_MID = K(0f, 0.02f, 0.55f, 0f, 0.05f, 1f), R_END = K(0.42f, -0.15f, 0.35f, 0.85f, -0.2f, 0.3f);
    // 画面左（王の右）から来る横薙ぎ
    static readonly Key L_WIND = K(0.4f, 0.25f, 0.05f, 0.45f, 0.55f, -0.7f), L_MID = K(0f, 0.02f, 0.55f, 0f, 0.05f, 1f), L_END = K(-0.42f, -0.15f, 0.35f, -0.85f, -0.2f, 0.3f);
    // 振り下ろし
    static readonly Key D_WIND = K(0.05f, 0.5f, 0f, 0f, 0.6f, -0.8f), D_MID = K(0f, 0.35f, 0.45f, 0f, 0.6f, 0.8f), D_END = K(0f, -0.3f, 0.55f, 0f, -0.8f, 0.6f);

    Key cur = IDLE; float twistCur, lungeCur, crouchCur;

    static Key Mix(Key a, Key b, float t) { return new Key(Vector3.Lerp(a.g, b.g, t), Vector3.Slerp(a.b, b.b, t)); }
    static Key Mix3(Key a, Key m, Key b, float t) { return t < 0.5f ? Mix(a, m, t * 2f) : Mix(m, b, (t - 0.5f) * 2f); }

    bool BuildKing()
    {
        var inst = Instantiate(kingPrefab, transform, false);
        inst.name = "King";
        inst.transform.localPosition = new Vector3(0, inst.transform.localPosition.y, 0);
        kAnim = inst.GetComponent<Animator>();
        if (kAnim == null || !kAnim.isHuman) { Destroy(inst); return false; }
        kAnim.applyRootMotion = false;
        if (kingCtrl != null) kAnim.runtimeAnimatorController = kingCtrl;
        transform.rotation = Quaternion.Euler(0, 180f, 0);   // プレイヤーの方を向く
        isKing = true; displayName = "星冠の王（仮）"; height = 2.3f;

        kSpine = kAnim.GetBoneTransform(HumanBodyBones.Spine);
        kChest = kAnim.GetBoneTransform(HumanBodyBones.UpperChest) ?? kAnim.GetBoneTransform(HumanBodyBones.Chest);
        rUA = kAnim.GetBoneTransform(HumanBodyBones.RightUpperArm); rLA = kAnim.GetBoneTransform(HumanBodyBones.RightLowerArm); rH = kAnim.GetBoneTransform(HumanBodyBones.RightHand);
        lUA = kAnim.GetBoneTransform(HumanBodyBones.LeftUpperArm); lLA = kAnim.GetBoneTransform(HumanBodyBones.LeftLowerArm); lH = kAnim.GetBoneTransform(HumanBodyBones.LeftHand);
        sword = inst.transform.Find("Greatsword");

        // 体の当たり判定（カプセル）。部位は形どおりの当たり判定
        var cap = inst.AddComponent<CapsuleCollider>();
        cap.center = new Vector3(0, 1.0f, 0); cap.height = 2.0f; cap.radius = 0.32f;
        bodyColliders.Add(cap);

        var halo = new List<GameObject>(); var shL = new List<GameObject>(); var shR = new List<GameObject>(); var core = new List<GameObject>();
        foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
        {
            string n = mf.name; List<GameObject> to = null;
            if (n.StartsWith("Halo")) to = halo;
            else if (n.StartsWith("Pauldron"))
            {
                string rest = n.Replace("PauldronLame", "").Replace("PauldronHorn", "").Replace("Pauldron", "");
                to = rest.StartsWith("L") ? shL : shR;
            }
            else if (n.StartsWith("Core")) to = core;
            if (to == null) continue;
            var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh;
            to.Add(mf.gameObject);
        }
        AddPart("halo", "光輪", "光輪片", 40, halo.ToArray());
        AddPart("shL", "左肩", "肩甲", 32, shL.ToArray());
        AddPart("shR", "右肩", "肩甲", 32, shR.ToArray());
        AddPart("core", "胸の核", "琥珀核", 44, core.ToArray());

        // 崩しのときに赤く光らせる
        var lg = new GameObject("BreakLight"); lg.transform.SetParent(transform, false); lg.transform.localPosition = new Vector3(0, 1.5f, 0.9f);
        breakLight = lg.AddComponent<Light>(); breakLight.type = LightType.Point; breakLight.color = new Color(1f, 0.25f, 0.2f); breakLight.range = 4f; breakLight.intensity = 0f;
        return true;
    }

    void PoseKing(float time, ProtoGame.EnemyState state, ProtoGame.Attack a, float deadT)
    {
        Key target = IDLE; float twist = 0, lean = 0, lunge = 0, crouch = 0, follow = 0.15f;
        if (state == ProtoGame.EnemyState.Attack && a != null)
        {
            float W = a.W;
            // 振りかぶりは最大0.55秒で上がりきり、溜め（ディレイ）の間はそのまま止めて小さく震える
            float windT = Mathf.Min(W, 0.55f);
            float p = EaseOut(Mathf.Clamp01(a.t / windT)), s = Mathf.Clamp01((a.t - W) / 0.12f), r = Mathf.Clamp01((a.t - W - 0.12f) / 0.3f);
            Key wind, mid, end; float tw0, tw1;
            if (a.dir == 'R') { wind = R_WIND; mid = R_MID; end = R_END; tw0 = -35f; tw1 = 35f; }
            else if (a.dir == 'L') { wind = L_WIND; mid = L_MID; end = L_END; tw0 = 35f; tw1 = -35f; }
            else { wind = D_WIND; mid = D_MID; end = D_END; tw0 = 0f; tw1 = 0f; }
            if (a.t < W) { target = Mix(IDLE, wind, p); twist = tw0 * p + (a.t > windT ? Mathf.Sin(time * 40f) * 1.2f : 0f); lean = -6f * p; crouch = 0.06f * p; }
            else if (r <= 0) { target = Mix3(wind, mid, end, EaseOut(s)); twist = Mathf.Lerp(tw0, tw1, s); lean = 14f * s; lunge = 0.3f * s; crouch = a.dir == 'D' ? 0.12f * s : 0.04f; }
            else if (a.parried) { target = r < 0.5f ? Mix(end, KNOCK, r * 2f) : Mix(KNOCK, IDLE, (r - 0.5f) * 2f); twist = tw1 * (1 - r); lean = -16f * (1 - r); lunge = 0.3f * (1 - r) - 0.2f * Mathf.Sin(r * Mathf.PI); }
            else { target = Mix(end, IDLE, r); twist = tw1 * (1 - r); lean = 14f * (1 - r); lunge = 0.3f * (1 - r); }
            follow = 0.6f;
        }
        else if (state == ProtoGame.EnemyState.Break)
        {
            target = BREAK; lean = 18f + Mathf.Sin(time * 9f) * 3f; twist = Mathf.Sin(time * 3f) * 6f; crouch = 0.18f;
        }
        else
        {
            twist = Mathf.Sin(time * 0.9f) * 4f;
        }
        cur = Mix(cur, target, follow);
        twistCur = Mathf.Lerp(twistCur, twist, follow);
        leanCur = Mathf.Lerp(leanCur, lean, follow);
        lungeCur = Mathf.Lerp(lungeCur, lunge, follow);
        crouchCur = Mathf.Lerp(crouchCur, crouch, follow);

        if (kAnim.runtimeAnimatorController != null) { kAnim.SetBool("Grounded", true); kAnim.SetFloat("Speed", 0f); kAnim.SetFloat("MotionSpeed", 1f); }

        if (breakLight != null)
            breakLight.intensity = state == ProtoGame.EnemyState.Break ? 2.5f + Mathf.Sin(time * 12f) * 1.5f : Mathf.MoveTowards(breakLight.intensity, 0f, 0.3f);

        if (state == ProtoGame.EnemyState.Dead)
        {
            float k = EaseOut(Mathf.Clamp01(deadT * 1.2f));
            transform.rotation = Quaternion.Euler(0, 180f, 0) * Quaternion.Euler(-80f * k, 0, 0);   // 後ろへ倒れる
            transform.position = basePos + new Vector3(0, -0.35f * k, 0.4f * k);
        }
        else transform.position = basePos + transform.forward * lungeCur + Vector3.down * crouchCur;
    }

    // 待機モーションの後に、胴・両腕・大剣を上書きする
    void LateUpdate()
    {
        if (!isKing || rUA == null) return;
        Vector3 Rt = transform.right, U = transform.up, Fw = transform.forward;
        foreach (var bone in new[] { kSpine, kChest })
        {
            if (bone == null) continue;
            bone.rotation = Quaternion.AngleAxis(twistCur * 0.5f, U) * Quaternion.AngleAxis(leanCur * 0.5f, Rt) * bone.rotation;
        }
        Vector3 C = (kChest != null ? kChest : rUA).position;
        // 胴をひねった分、握りの位置も一緒に回す
        Quaternion tw = Quaternion.AngleAxis(twistCur, U);
        Vector3 G = C + tw * (Rt * cur.g.x + U * cur.g.y + Fw * cur.g.z);
        Vector3 B = (tw * (Rt * cur.b.x + U * cur.b.y + Fw * cur.b.z)).normalized;

        IK(rUA, rLA, rH, G, rUA.position - U * 0.6f + Rt * 0.45f - Fw * 0.2f);
        if (sword != null)
        {
            Vector3 flat = Vector3.Cross(B, Rt); if (flat.sqrMagnitude < 0.01f) flat = Vector3.Cross(B, U);
            sword.rotation = Quaternion.LookRotation(flat.normalized, -B);
            sword.position += rH.position + B * 0.03f - sword.TransformPoint(GRIP);
        }
        Vector3 lT = rH.position - B * 0.26f;
        IK(lUA, lLA, lH, lT, lUA.position - U * 0.6f - Rt * 0.45f - Fw * 0.2f);
    }

    static void IK(Transform up, Transform lo, Transform end, Vector3 target, Vector3 pole)
    {
        Vector3 s = up.position;
        float la = Vector3.Distance(s, lo.position), lb = Vector3.Distance(lo.position, end.position);
        Vector3 toT = target - s;
        float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(la - lb) + 0.001f, (la + lb) * 0.999f);
        Vector3 dir = toT.normalized;
        float cosA = (la * la + d * d - lb * lb) / (2f * la * d);
        float A = Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f)) * Mathf.Rad2Deg;
        Vector3 n = Vector3.Cross(dir, pole - s);
        if (n.sqrMagnitude < 1e-6f) n = Vector3.Cross(dir, Vector3.up);
        n.Normalize();
        Vector3 elbow = s + Quaternion.AngleAxis(A, n) * dir * la;
        Aim(up, lo.position, elbow);
        Aim(lo, end.position, s + dir * d);
    }

    static void Aim(Transform bone, Vector3 childPos, Vector3 targetPos)
    {
        Vector3 a = childPos - bone.position, b = targetPos - bone.position;
        if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) return;
        bone.rotation = Quaternion.FromToRotation(a, b) * bone.rotation;
    }

    static float EaseOut(float t) { return 1f - Mathf.Pow(1f - t, 3f); }
}
