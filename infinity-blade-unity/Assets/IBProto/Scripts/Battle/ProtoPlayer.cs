using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// プレイヤー（背中越しに見える主人公）。
// 武士モデル（SamuraiCharacter.prefab）があればそれを使い、待機モーションの上から
// 腕・胴・刀の動きをコードで重ねる。モデルが無ければ昔の箱キャラで動く。
public class ProtoPlayer : MonoBehaviour
{
    // 共通
    Transform body;
    float swingT = -1f, swingDur = 0.16f; char swingDir;
    float hitT, blockT, parryT, time;
    Quaternion armRotR = Quaternion.Euler(IDLE_R), torsoRot = Quaternion.identity;
    float guardW;

    // 箱キャラ用
    Transform torso, armR, armL;

    // 武士用
    bool samurai;
    Animator anim;
    Transform spine, chest, rUA, rLA, rH, lUA, lLA, lH;
    Transform katana;

    // 技データの再生（モンハン太刀など）
    IBProtoMove move; float moveT, moveSpeed = 1f, moveW, blendT; bool moveNoHits; int nextHit;
    IBProtoMove.Key mk = new IBProtoMove.Key { blade = Vector3.forward };
    Vector3 lastHandW, lastBladeW = Vector3.forward, blendHand, blendBlade;
    readonly System.Collections.Generic.List<IBProtoMove.Hit> firedHits = new System.Collections.Generic.List<IBProtoMove.Hit>();
    const float BLEND_IN = 0.06f, BLEND_OUT = 0.14f;

    public IBProtoMove CurrentMove => move;
    public float MoveT => moveT;
    public bool CanChain => move == null || moveT >= move.chainFrom;
    public bool Invulnerable => move != null && move.invulnFrom >= 0 && moveT >= move.invulnFrom && moveT <= move.invulnTo;
    public bool IsSamurai => samurai;

    // 技を始める。speed で速さ、startAt で途中から、noHits で当たり無し（パリィの構えに使う）
    public void PlayMove(IBProtoMove m, float speed = 1f, float startAt = 0f, bool noHits = false)
    {
        if (m == null) return;
        if (!samurai) { Swing(m.Dir, noHits); return; }
        move = m; moveT = startAt; moveSpeed = speed; moveNoHits = noHits; nextHit = 0;
        while (m.hits != null && nextHit < m.hits.Length && m.hits[nextHit].t < startAt) nextHit++;
        blendHand = lastHandW; blendBlade = lastBladeW; blendT = BLEND_IN;
        swingT = -1f;
        mk = m.Sample(moveT);
    }
    public void StopMove() { move = null; }

    // この1フレームで当たった時間を受け取る（ProtoGame が読む）
    public System.Collections.Generic.List<IBProtoMove.Hit> TakeHits()
    {
        var l = new System.Collections.Generic.List<IBProtoMove.Hit>(firedHits); firedHits.Clear(); return l;
    }

    public static ProtoPlayer Create(Vector3 pos)
    {
        var g = new GameObject("Player");
        g.transform.position = pos;
        var p = g.AddComponent<ProtoPlayer>();
        p.body = new GameObject("Body").transform; p.body.SetParent(g.transform, false);
        if (!p.BuildSamurai()) p.BuildBox();
        return p;
    }

    // 主人公に使うキャラのプレハブ名（ProtoGame の「主人公のキャラ」で変えられる）
    public static string CharacterName = "KenshiCharacter";
    bool toonKatana;

#if UNITY_EDITOR
    static GameObject FindPrefab(string name)
    {
        foreach (var id in AssetDatabase.FindAssets(name + " t:Prefab"))
        {
            var path = AssetDatabase.GUIDToAssetPath(id);
            if (path.Contains("/History/") || System.IO.Path.GetFileNameWithoutExtension(path) != name) continue;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null) return go;
        }
        return null;
    }
#endif

    // ================= 武士モデル（人型のキャラなら何でも） =================
    bool BuildSamurai()
    {
        GameObject prefab = null; RuntimeAnimatorController ctrl = null;
#if UNITY_EDITOR
        prefab = FindPrefab(CharacterName);
        if (prefab == null) prefab = FindPrefab("SamuraiCharacter");
        foreach (var id in AssetDatabase.FindAssets("StarterAssetsThirdPerson t:AnimatorController"))
        {
            ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AssetDatabase.GUIDToAssetPath(id));
            if (ctrl != null) break;
        }
#endif
        if (prefab == null) return false;

        var inst = Instantiate(prefab, body, false);
        inst.name = prefab.name;
        toonKatana = prefab.name != "SamuraiCharacter";
        anim = inst.GetComponent<Animator>();
        if (anim == null || !anim.isHuman) { Destroy(inst); return false; }
        anim.applyRootMotion = false;
        if (ctrl != null) anim.runtimeAnimatorController = ctrl;

        spine = anim.GetBoneTransform(HumanBodyBones.Spine);
        chest = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Chest);
        rUA = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        rLA = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
        rH = anim.GetBoneTransform(HumanBodyBones.RightHand);
        lUA = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        lLA = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        lH = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        if (rUA == null || rLA == null || rH == null || lUA == null || lLA == null || lH == null) { Destroy(inst); return false; }

        // 腰の刀は抜いた状態にする（柄と鍔を隠して鞘だけ残す）
        foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            if (r.name.StartsWith("Katana_Tsuka") || r.name.StartsWith("Katana_Tsuba") || r.name.StartsWith("刀_")) r.enabled = false;

        BuildKatana();
        samurai = true;
        return true;
    }

    // 手に持つ刀。+Z 方向が切っ先、原点が右手の握り位置
    void BuildKatana()
    {
        if (toonKatana) { BuildToonKatana(); return; }
        var steel = ProtoBuild.Lit(new Color(0.78f, 0.79f, 0.8f));
        if (steel.HasProperty("_Metallic")) steel.SetFloat("_Metallic", 0.9f);
        if (steel.HasProperty("_Smoothness")) steel.SetFloat("_Smoothness", 0.78f);
        var ito = ProtoBuild.Lit(new Color(0.07f, 0.06f, 0.06f));
        var kin = ProtoBuild.Lit(new Color(0.6f, 0.47f, 0.26f));
        if (kin.HasProperty("_Metallic")) kin.SetFloat("_Metallic", 0.8f);
        if (kin.HasProperty("_Smoothness")) kin.SetFloat("_Smoothness", 0.55f);
        var hamon = ProtoBuild.Lit(new Color(0.93f, 0.94f, 0.95f));
        if (hamon.HasProperty("_Smoothness")) hamon.SetFloat("_Smoothness", 0.9f);

        katana = new GameObject("Katana").transform; katana.SetParent(transform, false);
        ProtoBuild.Box("Tsuka", new Vector3(0.034f, 0.04f, 0.27f), new Vector3(0, 0, -0.04f), ito, katana, false);
        ProtoBuild.Box("Kashira", new Vector3(0.038f, 0.044f, 0.02f), new Vector3(0, 0, -0.18f), kin, katana, false);
        ProtoBuild.Box("Tsuba", new Vector3(0.085f, 0.075f, 0.01f), new Vector3(0, 0, 0.1f), kin, katana, false);
        ProtoBuild.Box("Habaki", new Vector3(0.012f, 0.036f, 0.03f), new Vector3(0, 0.002f, 0.12f), kin, katana, false);
        // 刃（わずかに反らせるため3分割）
        var blade = new GameObject("Blade").transform; blade.SetParent(katana, false); blade.localPosition = new Vector3(0, 0, 0.13f);
        float[] lens = { 0.32f, 0.3f, 0.26f };
        float z = 0, y = 0, ang = 0;
        for (int i = 0; i < 3; i++)
        {
            var seg = new GameObject("Seg" + i).transform; seg.SetParent(blade, false);
            seg.localPosition = new Vector3(0, y, z); seg.localRotation = Quaternion.Euler(-ang, 0, 0);
            float w = Mathf.Lerp(0.03f, 0.024f, i / 2f);
            ProtoBuild.Box("Ji", new Vector3(0.007f, w, lens[i]), new Vector3(0, 0.003f, lens[i] / 2f), steel, seg, false);
            ProtoBuild.Box("Ha", new Vector3(0.004f, 0.007f, lens[i]), new Vector3(0, 0.003f - w / 2f - 0.002f, lens[i] / 2f), hamon, seg, false);
            float rad = ang * Mathf.Deg2Rad;
            z += Mathf.Cos(rad) * lens[i]; y += Mathf.Sin(rad) * lens[i];
            ang += 2.5f;
        }
        var tip = ProtoBuild.Box("Kissaki", new Vector3(0.006f, 0.022f, 0.05f), Vector3.zero, steel, blade, false);
        tip.transform.localPosition = new Vector3(0, y - 0.004f, z + 0.012f);
        tip.transform.localRotation = Quaternion.Euler(-ang - 28f, 0, 0);
    }

    // ================= 箱キャラ（予備） =================
    // アニメ調のキャラ用の刀：白い刃に、ネオンの刃筋
    void BuildToonKatana()
    {
        Material Toon(Color lit, Color shade, float spec = 0f)
        {
            var sh = Shader.Find("IBProto/Toon");
            if (sh == null) return ProtoBuild.Unlit(lit.gamma);
            var m = new Material(sh);
            m.SetColor("_Lit", lit.gamma); m.SetColor("_Shade", shade.gamma); m.SetFloat("_Spec", spec); m.SetFloat("_Rim", 0.3f);
            return m;
        }
        var blade = Toon(new Color(0.8f, 0.84f, 0.9f), new Color(0.3f, 0.34f, 0.45f), 0.15f);
        var black = Toon(new Color(0.035f, 0.034f, 0.045f), new Color(0.006f, 0.006f, 0.012f));
        var red = Toon(new Color(0.62f, 0.03f, 0.025f), new Color(0.17f, 0.006f, 0.03f));
        var gold = Toon(new Color(0.75f, 0.52f, 0.16f), new Color(0.24f, 0.12f, 0.05f), 0.2f);
        Material neonM;
        var ns = Shader.Find("IBProto/Neon");
        if (ns != null) { neonM = new Material(ns); neonM.SetColor("_Color", new Color(0.15f, 0.95f, 1f).gamma); neonM.SetFloat("_Intensity", 1.8f); }
        else neonM = ProtoBuild.Unlit(new Color(0.4f, 1f, 1f));

        katana = new GameObject("Katana").transform; katana.SetParent(transform, false);
        ProtoBuild.Box("Tsuka", new Vector3(0.032f, 0.038f, 0.27f), new Vector3(0, 0, -0.04f), black, katana, false);
        ProtoBuild.Box("Kashira", new Vector3(0.038f, 0.044f, 0.02f), new Vector3(0, 0, -0.18f), gold, katana, false);
        ProtoBuild.Box("Tsuba", new Vector3(0.09f, 0.08f, 0.012f), new Vector3(0, 0, 0.1f), red, katana, false);
        var bl = new GameObject("Blade").transform; bl.SetParent(katana, false); bl.localPosition = new Vector3(0, 0, 0.11f);
        float[] lens = { 0.32f, 0.31f, 0.27f };
        float z = 0, y = 0, ang = 0;
        for (int i = 0; i < 3; i++)
        {
            var seg = new GameObject("Seg" + i).transform; seg.SetParent(bl, false);
            seg.localPosition = new Vector3(0, y, z); seg.localRotation = Quaternion.Euler(-ang, 0, 0);
            float w = Mathf.Lerp(0.03f, 0.022f, i / 2f);
            ProtoBuild.Box("Ji", new Vector3(0.006f, w, lens[i]), new Vector3(0, 0.003f, lens[i] / 2f), blade, seg, false);
            ProtoBuild.Box("Neon", new Vector3(0.008f, 0.006f, lens[i]), new Vector3(0, 0.003f - w / 2f - 0.002f, lens[i] / 2f), neonM, seg, false);
            float rad = ang * Mathf.Deg2Rad;
            z += Mathf.Cos(rad) * lens[i]; y += Mathf.Sin(rad) * lens[i];
            ang += 2.5f;
        }
        var tip = ProtoBuild.Box("Kissaki", new Vector3(0.006f, 0.02f, 0.05f), Vector3.zero, blade, bl, false);
        tip.transform.localPosition = new Vector3(0, y - 0.004f, z + 0.012f);
        tip.transform.localRotation = Quaternion.Euler(-ang - 28f, 0, 0);
    }

    void BuildBox()
    {
        var suit = ProtoBuild.Lit(new Color(0.16f, 0.155f, 0.18f));
        var plate = ProtoBuild.Lit(new Color(0.93f, 0.925f, 0.91f));
        var blade = ProtoBuild.Lit(new Color(0.97f, 0.97f, 0.96f));
        var accent = ProtoBuild.Unlit(new Color(1f, 0.48f, 0.1f));

        ProtoBuild.Box("LegL", new Vector3(0.18f, 0.9f, 0.2f), new Vector3(-0.12f, 0.45f, 0), suit, body, false);
        ProtoBuild.Box("LegR", new Vector3(0.18f, 0.9f, 0.2f), new Vector3(0.12f, 0.45f, 0), suit, body, false);

        torso = new GameObject("Torso").transform; torso.SetParent(body, false);
        torso.localPosition = new Vector3(0, 0.92f, 0);
        ProtoBuild.Box("Chest", new Vector3(0.5f, 0.66f, 0.28f), new Vector3(0, 0.33f, 0), suit, torso, false);
        ProtoBuild.Box("BackPlate", new Vector3(0.42f, 0.46f, 0.06f), new Vector3(0, 0.4f, -0.16f), plate, torso, false);
        ProtoBuild.Box("Light", new Vector3(0.06f, 0.06f, 0.02f), new Vector3(0.12f, 0.52f, -0.2f), accent, torso, false);
        ProtoBuild.Box("Head", new Vector3(0.26f, 0.3f, 0.28f), new Vector3(0, 0.84f, 0), plate, torso, false);
        ProtoBuild.Box("Visor", new Vector3(0.22f, 0.07f, 0.02f), new Vector3(0, 0.86f, 0.14f), suit, torso, false);

        armR = MakeArm(1, suit, plate);
        armL = MakeArm(-1, suit, plate);

        var sword = new GameObject("Sword").transform; sword.SetParent(armR, false);
        sword.localPosition = new Vector3(0, -0.62f, 0);
        ProtoBuild.Box("Hilt", new Vector3(0.05f, 0.18f, 0.05f), new Vector3(0, -0.02f, 0), suit, sword, false);
        ProtoBuild.Box("Guard", new Vector3(0.24f, 0.035f, 0.06f), new Vector3(0, -0.11f, 0), suit, sword, false);
        ProtoBuild.Box("Blade", new Vector3(0.07f, 1.15f, 0.02f), new Vector3(0, -0.7f, 0), blade, sword, false);

        ProtoBuild.Box("Shield", new Vector3(0.5f, 0.62f, 0.05f), new Vector3(0, -0.42f, 0.12f), suit, armL, false);
        ProtoBuild.Box("ShieldRim", new Vector3(0.42f, 0.06f, 0.06f), new Vector3(0, -0.16f, 0.13f), plate, armL, false);
    }

    Transform MakeArm(int side, Material suit, Material plate)
    {
        var p = new GameObject(side > 0 ? "ArmR" : "ArmL").transform;
        p.SetParent(torso, false);
        p.localPosition = new Vector3(0.33f * side, 0.6f, 0);
        ProtoBuild.Box("Arm", new Vector3(0.13f, 0.62f, 0.13f), new Vector3(0, -0.31f, 0), suit, p, false);
        ProtoBuild.Box("Pauldron", new Vector3(0.2f, 0.14f, 0.22f), new Vector3(0.02f * side, -0.02f, 0), plate, p, false);
        return p;
    }

    // ================= 動き =================
    public void Swing(char dir, bool parry)
    {
        swingDir = dir; swingT = 0f; swingDur = parry ? 0.12f : 0.16f;
        if (parry) parryT = 0.3f;
    }
    public void Hit() { hitT = 0.4f; }
    public void Block() { blockT = 0.22f; }

    static readonly Vector3 IDLE_R = new Vector3(-60f, -15f, 0);
    static readonly Vector3 IDLE_L = new Vector3(-30f, 20f, 0);
    static readonly Vector3 GUARD_L = new Vector3(-82f, 38f, 0);

    // dodgeK: 0〜1〜0 の回避の進み具合、dodgeDir: -1 左 / 1 右
    public void Animate(float dt, bool guard, float dodgeK, int dodgeDir, bool dead)
    {
        time += dt;
        if (move != null)
        {
            moveT += dt * moveSpeed;
            while (move.hits != null && nextHit < move.hits.Length && moveT >= move.hits[nextHit].t)
            {
                if (!moveNoHits) firedHits.Add(move.hits[nextHit]);
                nextHit++;
            }
            mk = move.Sample(moveT);
            if (moveT >= move.duration) move = null;
        }
        moveW = move != null ? 1f : Mathf.MoveTowards(moveW, 0f, dt / BLEND_OUT);
        blendT = Mathf.Max(0, blendT - dt);
        hitT = Mathf.Max(0, hitT - dt); blockT = Mathf.Max(0, blockT - dt); parryT = Mathf.Max(0, parryT - dt);
        float follow = 1f - Mathf.Exp(-dt * 18f);

        // 右腕（刀）の向き
        float torsoYaw = 0;
        bool swinging = swingT >= 0;
        if (swinging)
        {
            swingT += dt;
            float k = Mathf.Clamp01(swingT / swingDur), e = 1f - Mathf.Pow(1f - k, 3f);
            Vector3 from, to;
            switch (swingDir)
            {
                case 'R': from = new Vector3(-85f, -75f, 0); to = new Vector3(-85f, 70f, 0); torsoYaw = Mathf.Lerp(-25f, 25f, e); break;
                case 'L': from = new Vector3(-85f, 70f, 0); to = new Vector3(-85f, -75f, 0); torsoYaw = Mathf.Lerp(25f, -25f, e); break;
                case 'U': from = new Vector3(-10f, 0, 0); to = new Vector3(-170f, 0, 0); break;
                default: from = new Vector3(-170f, 0, 0); to = new Vector3(-25f, 0, 0); break;
            }
            armRotR = Quaternion.Euler(Vector3.Lerp(from, to, e));
            if (k >= 1f && swingT > swingDur + (parryT > 0 ? 0.12f : 0.05f)) swingT = -1f;
        }
        else armRotR = Quaternion.Slerp(armRotR, Quaternion.Euler(IDLE_R), follow * 0.6f);

        bool guarding = guard || blockT > 0;
        guardW = Mathf.MoveTowards(guardW, guarding && !swinging ? 1f : 0f, dt * (guarding ? 9f : 6f));

        // 胴体：振りに合わせたひねり、被弾でのけぞり、ガードで前かがみ、呼吸
        float lean = hitT > 0 ? -18f * (hitT / 0.4f) : guard ? 8f : 0f;
        if (blockT > 0) lean -= 6f * (blockT / 0.22f);
        float breathe = samurai ? 0f : Mathf.Sin(time * 2.2f) * 1.2f;   // 武士は待機モーションが呼吸する
        torsoRot = Quaternion.Slerp(torsoRot, Quaternion.Euler(lean + breathe, torsoYaw, 0), follow);
        if (moveW > 0f) torsoRot = Quaternion.Slerp(torsoRot, Quaternion.Euler(mk.lean + lean, mk.yaw, 0), moveW);

        if (!samurai)
        {
            armR.localRotation = armRotR;
            var lTarget = guarding ? GUARD_L : IDLE_L;
            armL.localRotation = Quaternion.Slerp(armL.localRotation, Quaternion.Euler(lTarget), guard ? follow * 1.5f : follow);
            torso.localRotation = torsoRot;
        }
        else if (anim.runtimeAnimatorController != null)
        {
            anim.SetBool("Grounded", true); anim.SetFloat("Speed", 0f); anim.SetFloat("MotionSpeed", 1f);
            anim.speed = dt > 0 ? 1f : 0f;   // ヒットストップ中は止める
        }

        // 体全体：回避の横移動と傾き、被弾の後退
        float back = hitT > 0 ? -0.25f * Mathf.Sin(hitT / 0.4f * Mathf.PI) : 0f;
        body.localPosition = new Vector3(dodgeDir * 1.1f * dodgeK, -0.12f * dodgeK + mk.rise * moveW, back + mk.step * moveW);
        body.localRotation = Quaternion.Euler(0, Mathf.DeltaAngle(0f, mk.turn) * moveW, 0) * Quaternion.Euler(0, 0, -dodgeDir * 14f * dodgeK);
        if (dead) body.localRotation = Quaternion.identity;
    }

    // 待機モーションが骨を動かした後に、腕・胴・刀を上書きする
    void LateUpdate()
    {
        if (!samurai) return;
        Quaternion B = body.rotation;
        Vector3 R = B * Vector3.right, U = B * Vector3.up, F = B * Vector3.forward;

        // 胴のひねり・前後の傾きを背骨と胸に半分ずつ
        Vector3 te = torsoRot.eulerAngles;
        float yaw = Mathf.DeltaAngle(0, te.y), pitch = Mathf.DeltaAngle(0, te.x);
        foreach (var bone in new[] { spine, chest })
        {
            if (bone == null) continue;
            bone.rotation = Quaternion.AngleAxis(yaw * 0.5f, U) * Quaternion.AngleAxis(pitch * 0.5f, R) * bone.rotation;
        }

        // 右腕：振りの向き（箱キャラと同じ角度表）から手の位置を決めて IK
        Quaternion frame = B * torsoRot;
        Vector3 armDir = frame * armRotR * Vector3.down;
        Vector3 bladeDir = frame * armRotR * Quaternion.Euler(-38f, 0, 0) * Vector3.down;
        Vector3 sh = rUA.position;
        float reach = Vector3.Distance(rUA.position, rLA.position) + Vector3.Distance(rLA.position, rH.position);
        Vector3 handT = sh + armDir * reach * 0.86f;

        // ガード：刀を顔の前で横に寝かせて受ける
        if (guardW > 0f)
        {
            float g = guardW * guardW * (3f - 2f * guardW);
            Vector3 gHand = sh + F * 0.42f + U * 0.08f - R * 0.08f;
            Vector3 gBlade = (-R * 0.88f + U * 0.38f + F * 0.25f).normalized;
            handT = Vector3.Lerp(handT, gHand, g);
            bladeDir = Vector3.Slerp(bladeDir, gBlade, g).normalized;
        }
        // 技データ：胸から見た握りと刃の向きで上書き
        if (moveW > 0f)
        {
            Vector3 C = (chest != null ? chest : spine != null ? spine : rUA).position;
            Vector3 hM = C + B * mk.grip;
            Vector3 bM = (B * mk.blade).normalized;
            if (blendT > 0f && blendHand != Vector3.zero)
            {
                float k = 1f - blendT / BLEND_IN;
                hM = Vector3.Lerp(blendHand, hM, k); bM = Vector3.Slerp(blendBlade, bM, k).normalized;
            }
            float w = moveW * moveW * (3f - 2f * moveW);
            handT = Vector3.Lerp(handT, hM, w);
            bladeDir = Vector3.Slerp(bladeDir, bM, w).normalized;
        }
        lastHandW = handT; lastBladeW = bladeDir;
        IK(rUA, rLA, rH, handT, sh - U * 0.6f + R * 0.35f - F * 0.2f);

        // 刀：右手で握る。刃の平らな面が振る向きに対して立つように
        Vector3 grip = rH.position + bladeDir * 0.02f;
        Vector3 edgeUp = Vector3.Cross(bladeDir, Vector3.Cross(U + F * 0.3f, bladeDir));
        if (edgeUp.sqrMagnitude < 1e-4f) edgeUp = F;
        katana.position = grip;
        katana.rotation = Quaternion.LookRotation(bladeDir, edgeUp);

        // 左手：基本は柄頭側を握る両手持ち。ガード中は峰に添える
        Vector3 lHilt = grip - bladeDir * 0.13f;
        Vector3 lSupport = grip + bladeDir * 0.48f;
        Vector3 lT = Vector3.Lerp(lHilt, lSupport, guardW);
        // 横薙ぎの振り切りなど、届かない所は無理に握らない
        Vector3 lsh = lUA.position;
        float lReach = Vector3.Distance(lUA.position, lLA.position) + Vector3.Distance(lLA.position, lH.position);
        float over = Vector3.Distance(lsh, lT) / lReach;
        float lw = Mathf.Clamp01((1.25f - over) / 0.25f);
        if (lw > 0.01f)
        {
            Vector3 lt = Vector3.Lerp(lH.position, lT, lw);
            IK(lUA, lLA, lH, lt, lsh - U * 0.6f - R * 0.35f - F * 0.2f);
        }
    }

    // 2本の骨（上腕・前腕）で手を目標へ届かせる。pole は肘を向けたい方向の点
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

    // カメラが追いかけるための体の位置
    public Vector3 BodyPos => body.position;
}
