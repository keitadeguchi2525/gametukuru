using System.Collections.Generic;
using UnityEngine;

// 居合バトル 本体。
//  左クリック長押し：納刀（シュ… → コッ）　→　マウスを振る：抜刀（その方向へ一閃）
//  敵の太刀筋に「迎え撃つ向き」で抜けば弾く（パーンッ）。弾き続けて体勢を崩し、崩れた所へ斬り込む。
//  凝ったモーションは使わない。納刀の構えと、振り抜いた形の2つを、光の弧・音・カメラでつなぐ。
public class IaiGame : MonoBehaviour
{
    [Header("キャラ・舞台（空なら名前で探す）")]
    public GameObject heroPrefab;
    public GameObject bossPrefab;
    public GameObject stagePrefab;

    [Header("手触り")]
    [Tooltip("抜き身から納めて「コッ」と鳴るまでの秒数。短いほどテンポが速い")]
    public float sheatheTime = 0.2f;
    [Tooltip("抜いた直後、次の納刀に入れない硬直")]
    public float cutLock = 0.15f;
    [Tooltip("弾ける受付（当たる何秒前から抜いてよいか）")]
    public float parryWindow = 0.26f;
    [Tooltip("この秒数より引きつけて弾くと「冴え」")]
    public float perfectWindow = 0.085f;
    [Tooltip("抜刀と見なすマウスの振り幅（画面の高さに対する割合）")]
    public float flickSize = 0.04f;
    [Tooltip("先行入力が残る秒数（納刀中に振っても、コッの直後に抜ける）")]
    public float flickBuffer = 0.22f;

    [Header("強さ")]
    public float playerMaxHp = 100f;
    public float bossMaxHp = 170f;
    public float cutDamage = 8f;
    public float postureMax = 6f;
    public float breakTime = 3.8f;
    public float bossDistance = 3.0f;

    [Header("確認用")]
    [Tooltip("F1 でも切り替え。自動で弾いて斬る（演出の確認用）")]
    public bool autoPlay = false;
    [Tooltip("F2 でも切り替え。攻撃が来る向きの目印を出す")]
    public bool showGuide = true;
    public Font uiFont;

    public static bool AutoRequested;      // 外部（確認用の橋渡し）から自動プレイを指定する

    enum Mode { Title, Fight, Win, Lose }
    enum PS { Stand, ToKamae, Kamae, Cut, Drawn, Hurt, Dead }
    enum BS { Intro, Idle, Attack, Recoil, Guard, Broken, Dead }

    // dir：本当に来る向き　wind：溜めから当たるまでの秒数
    // fake：フェイント。最初はこの向きで構え、当たる FEINT_LEAD 秒前に dir へ切り替える（0 なら無し）
    class Atk
    {
        public char dir, fake; public float wind;
        public Atk(char d, float w) { dir = d; wind = w; }
        public Atk(char d, float w, char f) { dir = d; wind = w; fake = f; }
    }
    const float FEINT_LEAD = 0.55f;

    static readonly Color CYAN = new Color(0.25f, 0.95f, 1f);
    static readonly Color MAGENTA = new Color(1f, 0.2f, 0.55f);
    static readonly Color AMBER = new Color(1f, 0.62f, 0.2f);
    static readonly Color RED = new Color(1f, 0.12f, 0.1f);

    // ---- 場面 ----
    Camera cam; ProtoCameraRig rig; IaiFx fx; IaiAudio sfx;
    IaiPuppet hero, boss;
    Transform heroRoot, bossRoot, katana, saya, odachi, bossChest;
    Renderer heroBlade; MaterialPropertyBlock mpb;
    TrailRenderer bossTrail;

    // ---- 状態 ----
    Mode mode = Mode.Title;
    PS ps = PS.Stand; float pst; bool swordOut, sheathingFromDrawn, clickDone; char cutDir;
    float pHp, dodgeT = -1f, dodgeCd; int dodgeSide; float hurtK;
    BS bs = BS.Intro; float bst; float bHp, posture, breakT, bossShake;
    readonly List<Atk> chain = new List<Atk>(); int idx; Atk atk; float atkT; bool atkStruck, atkResolved, atkWarned, atkSwitched;
    // 見えている向き（フェイントの切り替え前は嘘の向き）
    char ShownDir => atk == null ? 'L' : (atk.fake != 0 && !atkSwitched ? atk.fake : atk.dir);
    // 戦績
    float fightTime; int nParry, nSae, nHit, nDodge, maxCombo; bool usedAuto, newBest; float bestTime;
    const string BEST_KEY = "IaiBestTime";
    int lastPattern = -1, patternCount; bool phase2;
    float hitstopT, slowT, slowScale = 1f, flash, flashRed, endT = -1f, introT, orbit, time;
    int parryCombo; string msg = ""; float msgT; Color msgCol = Color.white;
    // マウスの振り
    readonly Queue<KeyValuePair<float, Vector2>> trail = new Queue<KeyValuePair<float, Vector2>>();
    char flick; float flickT = -1f; float flickArm;
    float autoCutT;

    static readonly Atk[][] P1 =
    {
        new[] { new Atk('L', 0.9f) },
        new[] { new Atk('R', 0.9f) },
        new[] { new Atk('L', 0.85f), new Atk('R', 0.72f) },
        new[] { new Atk('R', 0.85f), new Atk('L', 0.72f), new Atk('D', 0.95f) },
        new[] { new Atk('D', 1.2f) },
        new[] { new Atk('L', 0.85f), new Atk('T', 0.95f) },
        new[] { new Atk('T', 1.05f) },
        new[] { new Atk('R', 1.6f) },                                   // 溜めの長い一太刀（早く抜くと空振り）
        new[] { new Atk('L', 1.3f, 'R') },                              // 右と見せて左
    };
    static readonly Atk[][] P2 =
    {
        new[] { new Atk('L', 0.7f), new Atk('R', 0.58f), new Atk('L', 0.58f), new Atk('D', 0.85f) },
        new[] { new Atk('T', 0.85f), new Atk('T', 0.75f) },
        new[] { new Atk('D', 1.35f), new Atk('R', 0.55f) },
        new[] { new Atk('R', 0.7f), new Atk('L', 0.58f), new Atk('T', 0.85f) },
        new[] { new Atk('L', 0.7f), new Atk('D', 0.75f), new Atk('R', 0.6f) },
        new[] { new Atk('R', 1.2f, 'L'), new Atk('L', 0.6f), new Atk('D', 0.8f) },   // 左と見せて右、そこから連撃
        new[] { new Atk('T', 1.25f, 'L'), new Atk('R', 0.65f) },                       // 斬ると見せて突き
        new[] { new Atk('D', 1.2f, 'R'), new Atk('L', 1.5f) },                         // 振り下ろしに化けて、間を外す
    };

    Vector3 BossPos => new Vector3(0, 0, bossDistance);

    // ================================================================ 準備
    void Start()
    {
        Application.runInBackground = true;
#if UNITY_EDITOR
        if (UnityEditor.SessionState.GetBool("IBProtoBridge.auto", false)) AutoRequested = true;
#endif
        if (AutoRequested) autoPlay = true;
        sfx = GetComponent<IaiAudio>();
        if (sfx == null) sfx = gameObject.AddComponent<IaiAudio>();
        BuildScene();
        ResetFight();
    }

    void OnDisable() { Time.timeScale = 1f; }

    GameObject FindPrefab(GameObject given, string name)
    {
        if (given != null) return given;
#if UNITY_EDITOR
        foreach (var id in UnityEditor.AssetDatabase.FindAssets(name + " t:Prefab"))
        {
            var path = UnityEditor.AssetDatabase.GUIDToAssetPath(id);
            if (System.IO.Path.GetFileNameWithoutExtension(path) != name) continue;
            var p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (p != null) return p;
        }
#endif
        return null;
    }

    void BuildScene()
    {
        cam = Camera.main;
        if (cam == null) { var cg = new GameObject("Main Camera"); cg.tag = "MainCamera"; cam = cg.AddComponent<Camera>(); cg.AddComponent<AudioListener>(); }
        cam.nearClipPlane = 0.05f; cam.fieldOfView = 52f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.1f, 0.04f, 0.16f);

        var stage = FindPrefab(stagePrefab, "NeonArena");
        if (stage != null) Instantiate(stage).name = stage.name;
        else
        {
            // 舞台が無いときの最低限：床と光
            var lg = new GameObject("Sun"); var l = lg.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1f; l.shadows = LightShadows.Hard;
            lg.transform.rotation = Quaternion.Euler(40, -30, 0);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder); floor.name = "Floor";
            floor.transform.position = new Vector3(0, -0.1f, 1.5f); floor.transform.localScale = new Vector3(13, 0.1f, 13);
            floor.GetComponent<Renderer>().sharedMaterial = ProtoBuild.Lit(new Color(0.12f, 0.1f, 0.2f));
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.4f, 0.35f, 0.5f);
        }

        var hp = FindPrefab(heroPrefab, "TabibitoCharacter");
        var bp = FindPrefab(bossPrefab, "KadomoriCharacter");
        if (hp == null || bp == null) { Debug.LogError("[Iai] キャラのプレハブがありません。メニュー「IBProto > 居合バトル > キャラを取り込み直す」を実行してください"); enabled = false; return; }

        heroRoot = new GameObject("Hero").transform;
        hero = Instantiate(hp, heroRoot).GetComponent<IaiPuppet>();
        hero.transform.localPosition = Vector3.zero; hero.transform.localRotation = Quaternion.identity;
        bossRoot = new GameObject("Boss").transform;
        bossRoot.position = BossPos; bossRoot.rotation = Quaternion.Euler(0, 180, 0);
        boss = Instantiate(bp, bossRoot).GetComponent<IaiPuppet>();
        boss.transform.localPosition = Vector3.zero; boss.transform.localRotation = Quaternion.identity;
        boss.breath = 0.012f; boss.clothSway = 3f;

        katana = hero.Joint("Katana"); saya = hero.Joint("Saya");
        odachi = boss.Joint("Odachi"); bossChest = boss.Joint("Chest");
        foreach (var r in hero.GetComponentsInChildren<Renderer>()) if (r.name.StartsWith("Blade")) heroBlade = r;
        mpb = new MaterialPropertyBlock();

        fx = IaiFx.Create(cam);
        rig = new ProtoCameraRig(cam);
        rig.Snap(new Vector3(1.6f, 1.2f, -3.2f), new Vector3(0, 1.5f, bossDistance), 46f);

        // 大太刀の風切り（切っ先に付ける光の尾）
        if (odachi != null)
        {
            var tg = new GameObject("Trail"); tg.transform.SetParent(odachi, false); tg.transform.localPosition = new Vector3(0, -0.06f, -1.55f);
            bossTrail = tg.AddComponent<TrailRenderer>();
            bossTrail.time = 0.16f; bossTrail.minVertexDistance = 0.04f; bossTrail.widthMultiplier = 0.9f;
            bossTrail.widthCurve = new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 0f));
            var gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(MAGENTA, 0.25f), new GradientColorKey(MAGENTA, 1f) },
                       new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.5f, 0.4f), new GradientAlphaKey(0f, 1f) });
            bossTrail.colorGradient = gr;
            bossTrail.sharedMaterial = fx.GlowMaterial;
            bossTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bossTrail.alignment = LineAlignment.View;
            bossTrail.emitting = false;
        }
    }

    void ResetFight()
    {
        if (hero == null) return;
        pHp = playerMaxHp; bHp = bossMaxHp; posture = 0; phase2 = false; patternCount = 0; lastPattern = -1;
        ps = PS.Stand; pst = 0; swordOut = false; dodgeT = -1f; hurtK = 0; parryCombo = 0;
        bs = BS.Intro; bst = 0; atk = null; chain.Clear(); endT = -1f; introT = 0;
        hero.Snap("stand"); boss.Snap("intro");
        heroRoot.position = Vector3.zero; heroRoot.rotation = Quaternion.identity;
        if (bossTrail != null) { bossTrail.emitting = false; bossTrail.Clear(); }
        flickT = -1f; trail.Clear();
        fightTime = 0f; nParry = nSae = nHit = nDodge = maxCombo = 0; usedAuto = false; newBest = false;
        bestTime = PlayerPrefs.GetFloat(BEST_KEY, 0f);
        ResetBossBody();
    }

    // ================================================================ 毎フレーム
    void Update()
    {
        if (hero == null) return;
        float udt = Time.unscaledDeltaTime;
        // 止め絵（ヒットストップ）とスロー
        if (hitstopT > 0f) { hitstopT -= udt; Time.timeScale = 0.02f; }
        else if (slowT > 0f) { slowT -= udt; Time.timeScale = slowScale; }
        else Time.timeScale = 1f;
        float dt = Time.deltaTime;
        time += dt;
        flash = Mathf.MoveTowards(flash, 0f, udt * 5f);
        flashRed = Mathf.MoveTowards(flashRed, 0f, udt * 2.2f);
        if (msgT > 0f) msgT -= udt;

        if (IaiInput.AutoToggle) autoPlay = !autoPlay;
        if (IaiInput.GuideToggle) showGuide = !showGuide;

        if (mode == Mode.Title)
        {
            if (IaiInput.LeftPressed || autoPlay) { mode = Mode.Fight; introT = 0; bs = BS.Intro; bst = 0; sfx.Warn(false); boss.Go("idle", 0.9f, IaiPuppet.Ease.InOut); }
        }
        else if (mode == Mode.Fight)
        {
            introT += dt;
            fightTime += udt;
            if (autoPlay) usedAuto = true;
            TrackMouse();
            UpdatePlayer(dt);
            UpdateBoss(dt);
        }
        else
        {
            UpdateBoss(dt);
            if (endT > 0f) { endT -= udt; }
            else if (IaiInput.LeftPressed || (autoPlay && endT <= -0.01f)) { ResetFight(); mode = Mode.Title; }
            if (endT <= 0f && endT > -1f) endT -= udt * 0.25f;   // 自動プレイ時は少し待って戻る
        }

        UpdateHeroRoot(dt);
        UpdateBlade();
        UpdateCamera(udt);
    }

    // ---------------------------------------------------------------- マウスの振りを読む
    void TrackMouse()
    {
        float now = Time.unscaledTime;
        Vector2 mp = IaiInput.MousePos;
        trail.Enqueue(new KeyValuePair<float, Vector2>(now, mp));
        while (trail.Count > 0 && now - trail.Peek().Key > 0.09f) trail.Dequeue();
        if (flickT >= 0f) { flickT -= Time.unscaledDeltaTime; }
        flickArm -= Time.unscaledDeltaTime;
        if (autoPlay || !IaiInput.LeftHeld || trail.Count < 2 || flickArm > 0f) return;
        Vector2 d = mp - trail.Peek().Value;
        if (d.magnitude >= flickSize * Screen.height)
        {
            flick = Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? (d.x > 0 ? 'R' : 'L') : (d.y > 0 ? 'U' : 'D');
            flickT = flickBuffer; flickArm = 0.13f;
            trail.Clear();
        }
    }

    bool Held => autoPlay ? autoHeld : IaiInput.LeftHeld;
    bool autoHeld = true;

    // ---------------------------------------------------------------- 主人公
    void UpdatePlayer(float dt)
    {
        pst += dt;
        if (dodgeCd > 0) dodgeCd -= dt;
        if (autoPlay) AutoPlay();

        bool canAct = ps == PS.Stand || ps == PS.Kamae || ps == PS.Drawn || ps == PS.ToKamae || (ps == PS.Cut && pst >= cutLock);
        if (canAct && dodgeT < 0f && dodgeCd <= 0f && (IaiInput.DodgeLeft || IaiInput.DodgeRight || autoDodge != 0))
        {
            dodgeSide = autoDodge != 0 ? autoDodge : (IaiInput.DodgeLeft ? -1 : 1); autoDodge = 0;
            dodgeT = 0f; dodgeCd = 0.55f; sfx.Dodge(); hero.swayBoost = 0.8f;
        }

        switch (ps)
        {
            case PS.Stand:
                if (Held) BeginSheathe(false);
                break;
            case PS.Drawn:
                if (Held) BeginSheathe(true);
                break;
            case PS.ToKamae:
            {
                float total = sheathingFromDrawn ? sheatheTime : 0.11f;
                if (sheathingFromDrawn && pst >= total * 0.38f && hero.Current == "noto" && !hero.Moving) hero.Go("kamae", total * 0.62f, IaiPuppet.Ease.InOut);
                if (pst >= total && !clickDone)
                {
                    // コッ：鍔が鯉口に当たる
                    clickDone = true; swordOut = false;
                    if (hero.Current != "kamae") hero.Go("kamae", 0.04f, IaiPuppet.Ease.Out);
                    sfx.NotoClick(sheathingFromDrawn ? 1f : 0.55f);
                    if (saya != null) fx.Sparks(saya.position, sheathingFromDrawn ? 5 : 2, CYAN, 1.6f, Vector3.up * 0.5f, 0.2f, 0.018f);
                    rig.Kick(sheathingFromDrawn ? 0.06f : 0.02f, 0f, 1.2f);
                    ps = PS.Kamae; pst = 0f;
                }
                break;
            }
            case PS.Kamae:
                if (flickT >= 0f) { char d = flick; flickT = -1f; DoCut(d); }
                else if (!Held) { hero.Go("stand", 0.28f, IaiPuppet.Ease.InOut); ps = PS.Stand; pst = 0; }
                break;
            case PS.Cut:
                if (pst >= cutLock && Held) BeginSheathe(true);
                else if (pst >= 0.5f) { hero.Go("drawn", 0.32f, IaiPuppet.Ease.InOut); ps = PS.Drawn; pst = 0; }
                break;
            case PS.Hurt:
                if (pst >= 0.45f) { ps = swordOut ? PS.Drawn : PS.Stand; pst = 0; }
                break;
        }
    }

    void BeginSheathe(bool fromDrawn)
    {
        sheathingFromDrawn = fromDrawn; clickDone = false; ps = PS.ToKamae; pst = 0f;
        if (fromDrawn) { hero.Go("noto", sheatheTime * 0.38f, IaiPuppet.Ease.Out); sfx.NotoSlide(); }
        else hero.Go("kamae", 0.11f, IaiPuppet.Ease.Out);
    }

    Vector3 HeroBase => heroRoot.position;

    void DoCut(char dir)
    {
        ps = PS.Cut; pst = 0f; swordOut = true; cutDir = dir;
        hero.Go("cut" + dir, 0.05f, IaiPuppet.Ease.Snap);
        hero.swayBoost = 1f;
        sfx.Batto();
        // 光の弧：刀の通り道を光で描く
        Vector3 c = HeroBase + new Vector3(0, 1.12f, 0.05f);
        Vector3 R = Vector3.right, F = Vector3.forward, U = Vector3.up;
        float rad = 1.55f, w = 0.2f;
        switch (dir)
        {
            case 'R': fx.Arc(c, R, F + U * 0.1f, rad, 165f, -20f, w, CYAN); rig.Kick(0.1f, -2.2f, 1.6f); break;
            case 'L': fx.Arc(c, R, F + U * 0.12f, rad, 15f, 200f, w, CYAN); rig.Kick(0.1f, 2.2f, 1.6f); break;
            case 'U': fx.Arc(c + U * 0.1f, R * 0.5f + U * 0.86f, F, rad, 175f, 5f, w, CYAN); rig.Kick(0.1f, -1.5f, 1.6f); break;
            default:  fx.Arc(c + U * 0.3f, U, F, rad, -15f, 140f, w * 1.15f, CYAN); rig.Kick(0.14f, 0f, 2.2f); break;
        }

        // 何を斬ったか
        if (bs == BS.Attack && atk != null && !atkResolved)
        {
            float remain = atk.wind - atkT;
            bool meets = (atk.dir == 'L' && dir == 'L') || (atk.dir == 'R' && dir == 'R') || (atk.dir == 'D' && dir == 'U');
            if (atk.dir != 'T' && meets && remain <= parryWindow) { Parry(remain <= perfectWindow); return; }
            if (remain <= parryWindow && atk.dir != 'T') Msg(meets ? "" : "向きが違う", Color.white);
            else if (atk.dir != 'T') Msg("早い", new Color(1, 1, 1, 0.7f));
            return;
        }
        if (bs == BS.Broken) { Damage(dir); return; }
        if (bs == BS.Idle || bs == BS.Guard || bs == BS.Recoil)
        {
            // 崩れていない相手には通らない（弾かれる）
            Vector3 p = BossPos + new Vector3(0, 1.45f, -0.75f);
            sfx.Deflect(); fx.Sparks(p, 8, AMBER, 3.5f, -Vector3.forward * 0.6f, 0.3f);
            if (bs == BS.Idle) { bs = BS.Guard; bst = 0; boss.Go("guard", 0.07f, IaiPuppet.Ease.Out); }
            BossKnock(1.5f, 60f, 0f);
        }
    }

    Vector3 ClashPoint(char d)
    {
        if (d == 'L') return HeroBase + new Vector3(-0.45f, 1.3f, 1.2f);
        if (d == 'R') return HeroBase + new Vector3(0.45f, 1.3f, 1.2f);
        return HeroBase + new Vector3(0f, 1.85f, 1.1f);
    }

    void Parry(bool perfect)
    {
        atkResolved = true; parryCombo++;
        nParry++; if (perfect) nSae++; maxCombo = Mathf.Max(maxCombo, parryCombo);
        char d = atk.dir;
        Vector3 p = ClashPoint(d);
        sfx.Clash(perfect);
        fx.Sparks(p, perfect ? 46 : 24, AMBER, perfect ? 9f : 6.5f, Vector3.up * 0.3f - Vector3.forward * 0.2f, 0.55f, 0.035f);
        fx.Sparks(p, perfect ? 16 : 8, Color.white, 4f, Vector3.up * 0.2f, 0.3f, 0.025f);
        fx.Ring(p, 0.1f, perfect ? 1.5f : 1.0f, Color.white, 0.2f, 0.1f);
        float side = d == 'L' ? -1f : d == 'R' ? 1f : 0f;
        Vector2 sdir = d == 'D' ? new Vector2(0.25f, 1f) : new Vector2(1f, 0.18f * (side == 0 ? 1 : side));
        Vector3 vp = cam.WorldToViewportPoint(p);
        if (perfect) { fx.ScreenLine(new Vector2(vp.x, vp.y), sdir, CYAN, 0.22f, 0.014f); flash = 0.55f; Msg("冴え", CYAN); }
        else { fx.ScreenLine(new Vector2(vp.x, vp.y), sdir, Color.white, 0.12f, 0.007f); flash = 0.22f; }
        rig.Kick(perfect ? 0.55f : 0.36f, -side * (perfect ? 7f : 4.5f) + (d == 'D' ? 3f : 0f), perfect ? 9f : 5.5f);
        hitstopT = perfect ? 0.13f : 0.075f;
        if (bossTrail != null) bossTrail.emitting = false;
        boss.Go(d == 'R' ? "recoilR" : "recoilL", 0.08f, IaiPuppet.Ease.Snap);
        boss.swayBoost = 1f;
        BossKnock(perfect ? 5.5f : 3.5f, perfect ? 320f : 200f, CoilSide(d) * (perfect ? 260f : 160f));
        bs = BS.Recoil; bst = 0f;
        posture += perfect ? 1.6f : 1f;
    }

    void Damage(char dir)
    {
        float dmg = cutDamage * (dir == 'D' ? 1.35f : 1f);
        bHp -= dmg;
        Vector3 c = BossPos + new Vector3(Random.Range(-0.25f, 0.25f), Random.Range(1.0f, 1.6f), -0.45f);
        Vector3 along = dir == 'R' ? new Vector3(1, 0.15f, 0) : dir == 'L' ? new Vector3(-1, 0.2f, 0) : dir == 'U' ? new Vector3(0.5f, 1, 0) : new Vector3(0.1f, -1, 0);
        along.Normalize();
        sfx.Cut();
        fx.Streak(c - along * 1.3f, c + along * 1.3f, CYAN, 0.09f, 0.34f);
        fx.Sparks(c, 16, MAGENTA, 5.5f, along * 0.8f - Vector3.forward * 0.3f, 0.45f);
        fx.Sparks(c, 8, Color.white, 3.5f, along * 0.5f, 0.3f, 0.02f);
        rig.Kick(0.22f, along.x * -3f, 3.2f);
        hitstopT = 0.05f; bossShake = 0.16f; flash = Mathf.Max(flash, 0.1f);
        boss.swayBoost = 0.7f;
        BossKnock(1.2f, 90f, -along.x * 140f);
        if (bHp <= 0f) Finish(c, along);
    }

    void Finish(Vector3 c, Vector3 along)
    {
        bHp = 0; bs = BS.Dead; bst = 0; atk = null;
        sfx.Finish();
        Vector3 vp = cam.WorldToViewportPoint(c);
        fx.ScreenLine(new Vector2(vp.x, vp.y), new Vector2(along.x, along.y + 0.05f), CYAN, 0.7f, 0.02f);
        fx.Sparks(c, 60, MAGENTA, 8f, Vector3.up * 0.4f, 0.9f, 0.04f);
        fx.Ring(c, 0.2f, 3.2f, Color.white, 0.5f, 0.16f);
        flash = 1f; hitstopT = 0.22f; slowT = 2.2f; slowScale = 0.22f;
        rig.Kick(0.7f, 6f, 10f);
        boss.Go("dead", 1.1f, IaiPuppet.Ease.Heavy);
        mode = Mode.Win; endT = 4.2f; orbit = 0f;
        Debug.Log($"[Iai] 勝ち 時間={fightTime:0.0} 弾き={nParry} 冴え={nSae} 最大連続={maxCombo} 見切り={nDodge} 被弾={nHit} 自動={usedAuto}");
        // 最速記録（自動プレイを使った戦いは数えない）
        if (!usedAuto && (bestTime <= 0f || fightTime < bestTime))
        {
            newBest = bestTime > 0f; bestTime = fightTime;
            PlayerPrefs.SetFloat(BEST_KEY, bestTime); PlayerPrefs.Save();
        }
    }

    void PlayerHit(float dmg)
    {
        if (ps == PS.Dead) return;
        pHp -= dmg; parryCombo = 0; posture = Mathf.Max(0, posture - 1f); nHit++;
        sfx.Hurt(); flashRed = 0.75f; hitstopT = 0.09f; hurtK = 1f;
        rig.Kick(0.75f, Random.Range(-6f, 6f), -4f);
        fx.Sparks(HeroBase + new Vector3(0, 1.2f, 0.3f), 14, RED, 5f, -Vector3.forward, 0.4f);
        flickT = -1f;
        if (pHp <= 0f)
        {
            pHp = 0; ps = PS.Dead; pst = 0; swordOut = true;
            hero.Go("dead", 0.7f, IaiPuppet.Ease.Heavy);
            slowT = 1.2f; slowScale = 0.35f;
            mode = Mode.Lose; endT = 2.6f;
            return;
        }
        ps = PS.Hurt; pst = 0;
        hero.Go(swordOut ? "drawn" : "stand", 0.25f, IaiPuppet.Ease.Out);
    }

    // かわし・被弾は、体ごとの動きで見せる（専用の姿勢は持たない）
    void UpdateHeroRoot(float dt)
    {
        float x = 0, roll = 0, z = 0, pitch = 0;
        if (dodgeT >= 0f)
        {
            dodgeT += dt;
            float k = Mathf.Sin(Mathf.Clamp01(dodgeT / 0.42f) * Mathf.PI);
            x = dodgeSide * 0.95f * k; roll = -dodgeSide * 16f * k;
            if (dodgeT >= 0.42f) dodgeT = -1f;
        }
        hurtK = Mathf.MoveTowards(hurtK, 0f, dt * 2.6f);
        if (hurtK > 0f) { float k = hurtK * hurtK; z = -0.4f * k; pitch = -14f * k; }
        heroRoot.position = new Vector3(x, 0, z);
        heroRoot.rotation = Quaternion.Euler(pitch, 0, roll);

        if (bossRoot != null)
        {
            bossShake = Mathf.MoveTowards(bossShake, 0f, Time.unscaledDeltaTime);
            Vector3 o = bossShake > 0f ? new Vector3(Mathf.Sin(Time.unscaledTime * 90f), 0, Mathf.Cos(Time.unscaledTime * 70f)) * bossShake * 0.25f : Vector3.zero;
            UpdateBossBody(dt);
            var face = Quaternion.Euler(0, 180f + faceYaw, 0);
            bossRoot.position = BossPos + o + face * new Vector3(0, sDrop.x, sFwd.x);
            bossRoot.rotation = face * Quaternion.Euler(sLean.x, 0, 0);
        }
    }

    bool Dodging => dodgeT >= 0.03f && dodgeT <= 0.34f;

    // 構えている間、刃（と鞘の中の光）が強く光る
    float bladeGlow = 2.4f;
    void UpdateBlade()
    {
        if (heroBlade == null) return;
        float target = ps == PS.Cut ? Mathf.Lerp(7f, 2.6f, pst / 0.3f) : 2.4f;
        bladeGlow = Mathf.Lerp(bladeGlow, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 18f));
        if (ps == PS.Cut && pst < 0.02f) bladeGlow = 7f;
        heroBlade.GetPropertyBlock(mpb);
        mpb.SetFloat("_Intensity", bladeGlow);
        heroBlade.SetPropertyBlock(mpb);
    }

    // ---------------------------------------------------------------- ボスの体（ばね）
    // 姿勢の切り替えだけだと「止まった絵」になるので、体の芯をばねで動かして重ねる。
    //   溜め＝ゆっくり深く引き絞る / 斬り＝踏み込んで一気にほどく / 振り抜き＝行き過ぎて戻る / 弾かれ＝のけぞる
    class Spring
    {
        public float x, v, target;
        public float k = 160f, damp = 13f;     // 硬さと戻りの粘り（damp が小さいほど行き過ぎる）
        public void Step(float dt) { v += (-(x - target) * k - v * damp) * dt; x += v * dt; }
        public void Set(float t, float kk, float d) { target = t; k = kk; damp = d; }
    }
    readonly Spring sYaw = new Spring(), sPitch = new Spring(), sRoll = new Spring(),
                    sFwd = new Spring(), sDrop = new Spring(), sLean = new Spring(), sTurn = new Spring();
    float faceYaw;            // 主人公の方を向く角度（度）
    bool faceLock;            // 斬りに入ったら向きを固定する（かわせる理由になる）
    float glintT = -1f;       // 振り下ろす直前の刃の光
    [Header("ボスの体の動き")]
    [Tooltip("溜めの深さ（上半身のひねり・度）")] public float coilTwist = 16f;
    [Tooltip("斬りの踏み込み（m）")] public float lunge = 0.42f;
    [Tooltip("振り抜いた後に反対側へ行き過ぎる量（0〜1）")] public float followThrough = 0.45f;

    static float CoilSide(char d) => d == 'L' ? 1f : d == 'R' ? -1f : d == 'T' ? 0.7f : 0f;

    void ResetBossBody()
    {
        foreach (var sp in new[] { sYaw, sPitch, sRoll, sFwd, sDrop, sLean, sTurn }) { sp.x = sp.v = sp.target = 0f; sp.k = 160f; sp.damp = 13f; }
        faceYaw = 0f; faceLock = false; glintT = -1f;
    }

    void UpdateBossBody(float dt)
    {
        if (boss == null) return;
        float t = time;
        // ---- 目標（今の状態で体の芯がどこへ行きたいか）
        if (bs == BS.Attack && atk != null)
        {
            char sd = atkStruck ? atk.dir : ShownDir;     // 溜めの間は「見せている向き」で引き絞る
            float side = CoilSide(sd);
            float strikeAt = atk.wind - STRIKE;
            if (!atkStruck)
            {
                // 溜め：姿勢に入った後も、じわじわと深く引き絞り続ける（止まって見せない）
                float pull = Mathf.Min(0.24f, atk.wind * 0.3f);
                float c = Mathf.Clamp01((atkT - pull * 0.5f) / Mathf.Max(0.05f, strikeAt - pull * 0.5f));
                c = c * c * (3f - 2f * c);
                float tremble = c > 0.6f ? Mathf.Sin(t * 61f) * (c - 0.6f) * 1.6f : 0f;   // 張り詰めた震え
                if (sd == 'D')
                {
                    sPitch.Set(-12f * c + tremble, 90f, 12f); sYaw.Set(0f, 90f, 12f); sRoll.Set(0f, 90f, 12f);
                    sDrop.Set(-0.05f * c, 90f, 12f); sFwd.Set(-0.1f * c, 90f, 12f); sLean.Set(-3f * c, 90f, 12f);
                }
                else if (sd == 'T')
                {
                    sYaw.Set(side * coilTwist * 0.8f * c + tremble, 90f, 12f); sPitch.Set(2f * c, 90f, 12f); sRoll.Set(0f, 90f, 12f);
                    sDrop.Set(-0.1f * c, 90f, 12f); sFwd.Set(-0.22f * c, 90f, 12f); sLean.Set(-2f * c, 90f, 12f);
                }
                else
                {
                    sYaw.Set(side * coilTwist * c + tremble, 90f, 12f); sPitch.Set(-4f * c, 90f, 12f); sRoll.Set(side * 4f * c, 90f, 12f);
                    sDrop.Set(-0.06f * c, 90f, 12f); sFwd.Set(-0.12f * c, 90f, 12f); sLean.Set(-2f * c, 90f, 12f);
                }
                if (glintT < 0f && atkT >= strikeAt - 0.2f) { glintT = 0f; Glint(); }
            }
            else
            {
                // 振り抜き：反対側へ行き過ぎたところで粘り、ゆっくり収まる（固まらせない）
                float since = atkT - strikeAt;
                float settle = Mathf.Clamp01((since - 0.18f) / 0.4f);
                float ft = followThrough * (1f - settle);
                float kk = since < 0.16f ? 230f : 70f, dd = since < 0.16f ? 17f : 8.5f;   // 振り抜く瞬間は硬く速く、その後は粘る
                if (atk.dir == 'D')      { sPitch.Set(10f * (1f - settle), kk, dd); sYaw.Set(0f, kk, dd); }
                else if (atk.dir == 'T') { sYaw.Set(-side * coilTwist * ft * 0.6f, kk, dd); sPitch.Set(4f * (1f - settle), kk, dd); }
                else                     { sYaw.Set(-side * coilTwist * ft, kk, dd); sPitch.Set(3f * (1f - settle), kk, dd); }
                sRoll.Set(0f, kk, dd);
                sFwd.Set((atk.dir == 'T' ? lunge * 1.4f : lunge) * (1f - settle * 0.7f), kk * 0.8f, dd + 2f);
                sDrop.Set(-0.05f * (1f - settle), kk, dd);
                sLean.Set(4f * (1f - settle), kk, dd);
            }
        }
        else if (bs == BS.Idle || bs == BS.Intro)
        {
            // 待ち：重心をゆっくり揺らし、肩で息をする。静止させない
            sYaw.Set(Mathf.Sin(t * 0.55f) * 3f, 30f, 8f);
            sPitch.Set(Mathf.Sin(t * 1.7f) * 0.8f, 30f, 8f);
            sRoll.Set(Mathf.Sin(t * 0.55f + 1f) * 1.5f, 30f, 8f);
            sFwd.Set(bs == BS.Idle ? Mathf.Sin(t * 0.4f) * 0.06f : 0f, 20f, 7f);
            sDrop.Set(0f, 30f, 8f); sLean.Set(0f, 30f, 8f);
        }
        else if (bs == BS.Broken)
        {
            sYaw.Set(0f, 40f, 9f); sPitch.Set(Mathf.Sin(t * 2.4f) * 2.5f, 30f, 6f);   // 崩れて荒く息をつく
            sRoll.Set(0f, 40f, 9f); sFwd.Set(0f, 40f, 9f); sDrop.Set(0f, 40f, 9f); sLean.Set(0f, 40f, 9f);
        }
        else
        {
            foreach (var sp in new[] { sYaw, sPitch, sRoll, sFwd, sDrop, sLean }) sp.Set(0f, 70f, 10f);
        }

        // ---- 向き：溜めの間は主人公を追い、斬りに入ったら固める
        faceLock = bs == BS.Attack && atk != null && atkStruck;
        if (!faceLock && bs != BS.Dead && bs != BS.Broken)
        {
            Vector3 to = heroRoot.position - BossPos;
            float want = Mathf.Atan2(-to.x, -to.z) * Mathf.Rad2Deg;   // ボスは -Z（主人公側）を向いている
            want = Mathf.Clamp(want, -20f, 20f);
            float rate = bs == BS.Attack ? 5f : 2.5f;
            faceYaw = Mathf.Lerp(faceYaw, want, 1f - Mathf.Exp(-dt * rate));
        }

        foreach (var sp in new[] { sYaw, sPitch, sRoll, sFwd, sDrop, sLean, sTurn }) sp.Step(dt);
        boss.chestYaw = sYaw.x; boss.chestPitch = sPitch.x; boss.chestRoll = sRoll.x;
    }

    // 振り下ろす直前、刃先が一瞬光る（いつ来るかの合図）
    void Glint()
    {
        if (odachi == null) return;
        Vector3 tip = odachi.TransformPoint(new Vector3(0, -0.06f, -1.6f));   // 光の尾と同じ切っ先
        Color c = atk != null && atk.dir == 'T' ? RED : Color.white;
        fx.Sparks(tip, 6, c, 1.5f, Vector3.zero, 0.25f, 0.03f);
        fx.Ring(tip, 0.02f, 0.45f, c, 0.16f, 0.05f);
    }

    // 弾かれた・斬られた勢いを体に入れる
    void BossKnock(float back, float pitch, float yaw)
    {
        sFwd.v -= back; sPitch.v -= pitch; sYaw.v += yaw; sLean.v -= pitch * 0.4f;
    }

    // ---------------------------------------------------------------- ボス
    void UpdateBoss(float dt)
    {
        bst += dt;
        switch (bs)
        {
            case BS.Intro:
                if (mode == Mode.Fight && bst >= 1.5f) { bs = BS.Idle; bst = 0; idleWait = 0.6f; }
                break;
            case BS.Idle:
                posture = Mathf.Max(0, posture - dt * 0.12f);
                if (mode == Mode.Fight && bst >= idleWait) StartChain();
                break;
            case BS.Guard:
                if (bst >= 0.34f) { bs = BS.Idle; bst = Mathf.Max(0f, idleWait - 0.45f); boss.Go("idle", 0.3f, IaiPuppet.Ease.InOut); }
                break;
            case BS.Attack:
                UpdateAttack(dt);
                break;
            case BS.Recoil:
                if (posture >= postureMax && bst >= 0.12f) { Break(); break; }
                if (bst >= 0.42f) NextAttack();
                break;
            case BS.Broken:
                breakT -= dt;
                if (breakT <= 0f)
                {
                    posture = 0; bs = BS.Idle; bst = 0; idleWait = 1.0f;
                    boss.Go("idle", 0.55f, IaiPuppet.Ease.InOut); sfx.Warn(false);
                    if (!phase2 && bHp <= bossMaxHp * 0.5f) { phase2 = true; Msg("門守 ― 怒り", MAGENTA); }
                }
                break;
        }
    }
    float idleWait = 1f;

    void StartChain()
    {
        if (!phase2 && bHp <= bossMaxHp * 0.5f) phase2 = true;
        chain.Clear();
        Atk[][] set = phase2 ? P2 : P1;
        int i;
        if (patternCount == 0) i = 0;                                  // 最初は左からの一太刀で教える
        else if (patternCount == 1 && !phase2) i = 2;
        else { i = Random.Range(0, set.Length); if (i == lastPattern) i = (i + 1) % set.Length; }
        if (phase2 && Random.value < 0.3f) { set = P1; i = Random.Range(2, P1.Length); }
        lastPattern = i; patternCount++;
        chain.AddRange(set[i]);
        idx = -1;
        NextAttack();
    }

    void NextAttack()
    {
        idx++;
        if (bossTrail != null) bossTrail.emitting = false;
        if (idx >= chain.Count || mode != Mode.Fight)
        {
            bs = BS.Idle; bst = 0; atk = null;
            idleWait = phase2 ? Random.Range(0.7f, 1.2f) : Random.Range(1.1f, 1.8f);
            boss.Go("idle", 0.6f, IaiPuppet.Ease.Settle);
            return;
        }
        atk = chain[idx]; atkT = 0; atkStruck = false; atkResolved = false; atkWarned = false; atkSwitched = false;
        bs = BS.Attack; bst = 0;
        boss.Go("wind" + ShownDir, Mathf.Min(0.24f, atk.wind * 0.3f), IaiPuppet.Ease.Out);
        glintT = -1f;
        sfx.Warn(ShownDir == 'T');
    }

    // フェイント：構えを本当の向きへ素早く組み替える
    void SwitchFeint()
    {
        atkSwitched = true;
        boss.Go("wind" + atk.dir, 0.13f, IaiPuppet.Ease.Snap);
        boss.swayBoost = 0.6f;
        sYaw.v += (CoilSide(atk.dir) - CoilSide(atk.fake)) * 90f;    // 体ごと切り返す勢い
        sfx.Warn(atk.dir == 'T');
        rig.Kick(0.08f, 0f, 1f);
    }

    const float STRIKE = 0.11f;
    void UpdateAttack(float dt)
    {
        if (atk == null) { bs = BS.Idle; return; }
        atkT += dt;
        if (atk.fake != 0 && !atkSwitched && atkT >= atk.wind - FEINT_LEAD) SwitchFeint();
        if (!atkStruck && atkT >= atk.wind - STRIKE)
        {
            atkStruck = true;
            boss.Go("hit" + atk.dir, STRIKE, IaiPuppet.Ease.Whip);
            boss.swayBoost = 1f;
            float side = CoilSide(atk.dir);
            sYaw.v -= side * coilTwist * 22f;                 // ひねりを一気にほどく
            sFwd.v += atk.dir == 'T' ? 6f : 4f;               // 踏み込み
            if (atk.dir == 'D') sPitch.v += 260f;
            rig.Kick(0.12f, side * 1.5f, 1.5f);
            sfx.Whoosh(atk.dir == 'D' ? 0.85f : 1f);
            if (bossTrail != null) { bossTrail.Clear(); bossTrail.emitting = true; }
        }
        if (!atkResolved && atkT >= atk.wind)
        {
            atkResolved = true;
            if (Dodging)
            {
                Msg("見切り", Color.white); slowT = 0.25f; slowScale = 0.35f; nDodge++;
                posture += atk.dir == 'T' ? 0.8f : 0.3f;
            }
            else PlayerHit(atk.dir == 'L' || atk.dir == 'R' ? 20f : 28f);
            rig.Kick(0.25f, 0f, 2f);
            if (atk.dir == 'D' || atk.dir == 'T') fx.Ring(HeroBase + new Vector3(0, 0.1f, 0.9f), 0.2f, 1.6f, MAGENTA, 0.3f, 0.1f);
        }
        if (atkResolved && bs == BS.Attack && atkT >= atk.wind + 0.38f)
        {
            if (posture >= postureMax) Break(); else NextAttack();
        }
    }

    void Break()
    {
        bs = BS.Broken; bst = 0; breakT = breakTime; atk = null; posture = postureMax;
        if (bossTrail != null) bossTrail.emitting = false;
        boss.Go("broken", 0.4f, IaiPuppet.Ease.Out);
        BossKnock(2.5f, 200f, 0f);
        sfx.Break();
        Vector3 p = BossPos + new Vector3(0, 1.5f, -0.6f);
        fx.Ring(p, 0.3f, 2.6f, MAGENTA, 0.4f, 0.14f);
        fx.Sparks(p, 40, MAGENTA, 7f, Vector3.up * 0.5f, 0.7f, 0.035f);
        rig.Kick(0.5f, 5f, 6f);
        slowT = 0.45f; slowScale = 0.3f; flash = Mathf.Max(flash, 0.3f);
        Msg("崩し", MAGENTA);
    }

    // ---------------------------------------------------------------- 自動プレイ（演出の確認用）
    int autoDodge;
    void AutoPlay()
    {
        autoHeld = true;
        if (ps != PS.Kamae) return;
        if (bs == BS.Attack && atk != null && !atkResolved)
        {
            float remain = atk.wind - atkT;
            if (atk.dir == 'T') { if (remain <= 0.16f && dodgeT < 0f) autoDodge = Random.value < 0.5f ? -1 : 1; return; }
            float want = (patternCount % 3 == 0) ? 0.06f : 0.16f;
            if (remain <= want) { flick = atk.dir == 'D' ? 'U' : atk.dir; flickT = 0.1f; }
        }
        else if (bs == BS.Broken && pst >= 0.05f)
        {
            autoCutT += 1f;
            int k = (int)autoCutT % 4;
            flick = k == 0 ? 'R' : k == 1 ? 'L' : k == 2 ? 'U' : 'D'; flickT = 0.1f;
        }
    }

    // ================================================================ カメラ
    static float Ease01(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }

    void UpdateCamera(float udt)
    {
        Vector3 E = BossPos, P = heroRoot.position;
        // 基本：右肩越し。主人公は左下、ボスを見上げる
        Vector3 tPos = new Vector3(P.x * 0.6f + 0.9f, 1.48f, -2.3f);
        Vector3 tLook = new Vector3(P.x * 0.25f + 0.05f, 1.8f, E.z);
        float tFov = 52f, tRoll = 0f, smooth = 0.2f;

        if (mode == Mode.Title)
        {
            orbit += udt * 0.1f;
            tPos = new Vector3(1.5f + Mathf.Sin(orbit) * 0.5f, 0.75f, -2.9f);
            tLook = new Vector3(0.25f, 1.55f, E.z * 0.6f); tFov = 44f; smooth = 0.7f;
        }
        else if (mode == Mode.Win)
        {
            orbit += udt * 0.22f;
            float a = 2.4f + orbit;
            tPos = new Vector3(Mathf.Sin(a) * 4.2f, 1.1f, 1.2f + Mathf.Cos(a) * 4.2f);
            tLook = new Vector3(0, 1.1f, 1.4f); tFov = 46f; smooth = 0.5f;
        }
        else if (mode == Mode.Lose)
        {
            tPos = new Vector3(1.6f, 0.5f, -2.0f); tLook = new Vector3(0, 0.9f, 0.4f); tFov = 40f; tRoll = -6f; smooth = 0.6f;
        }
        else
        {
            if (introT < 2.0f)
            {
                // 登場：ボスの顔から、肩越しへ引く
                float k = Ease01((introT - 0.4f) / 1.5f);
                tPos = Vector3.Lerp(E + new Vector3(0.6f, 2.0f, -2.2f), tPos, k);
                tLook = Vector3.Lerp(E + new Vector3(0, 2.15f, 0), tLook, k);
                tFov = Mathf.Lerp(34f, tFov, k); smooth = 0.14f;
            }
            if (ps == PS.Kamae || ps == PS.ToKamae)
            {
                // 構え：低く寄って、溜めを見せる
                tPos += new Vector3(-0.1f, -0.22f, 0.25f); tFov -= 3f;
            }
            if (bs == BS.Attack && atk != null && !atkResolved)
            {
                float p = Ease01(atkT / atk.wind);
                char sd = ShownDir;
                float side = sd == 'R' ? 1f : sd == 'L' ? -1f : 0f;
                tPos += new Vector3(-side * 0.25f * p, (sd == 'D' ? -0.35f : -0.1f) * p, 0.35f * p);
                tLook += new Vector3(side * 0.4f * p, (sd == 'D' ? 0.55f : 0.1f) * p, 0);
                tFov -= 5f * p; tRoll += side * 2.5f * p;
            }
            else if (bs == BS.Broken)
            {
                // 崩し：横へ回り込んだローアングル。斬り込む主人公と膝をつくボスを一画面に
                tPos = new Vector3(-1.9f, 0.7f, -1.7f);
                tLook = new Vector3(0.1f, 1.25f, E.z * 0.62f);
                tFov = 56f; tRoll = 3f; smooth = 0.28f;
            }
            else if (bs == BS.Recoil)
            {
                tPos += new Vector3(0, -0.15f, 0.25f); tFov -= 3f;
            }
        }
        rig.Tick(udt, tPos, tLook, tFov, tRoll, smooth);
    }

    // ================================================================ 画面表示
    GUIStyle sText; Texture2D white;
    void Msg(string t, Color c) { if (string.IsNullOrEmpty(t)) return; msg = t; msgCol = c; msgT = 0.9f; }

    static void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = o; }

    void Label(Rect r, string t, int size, Color c, TextAnchor a = TextAnchor.MiddleCenter, FontStyle fs = FontStyle.Normal)
    {
        if (sText == null) { sText = new GUIStyle(GUI.skin.label); sText.wordWrap = false; sText.richText = false; if (uiFont != null) sText.font = uiFont; }
        sText.fontSize = size; sText.alignment = a; sText.fontStyle = fs;
        var sh = new Color(0, 0, 0, c.a * 0.6f);
        sText.normal.textColor = sh; GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), t, sText);
        sText.normal.textColor = c; GUI.Label(r, t, sText);
    }

    void OnGUI()
    {
        if (hero == null) return;
        float W = Screen.width, H = Screen.height, u = H / 1080f;
        if (flashRed > 0f) Fill(new Rect(0, 0, W, H), new Color(0.8f, 0.02f, 0.05f, flashRed * 0.38f));
        if (flash > 0f) Fill(new Rect(0, 0, W, H), new Color(1, 1, 1, flash * 0.75f));

        if (mode == Mode.Title)
        {
            Label(new Rect(0, H * 0.3f, W, 120 * u), "居 合", (int)(96 * u), Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Label(new Rect(0, H * 0.3f + 120 * u, W, 40 * u), "左クリック長押しで納刀　　マウスを振って抜刀　　A / D でかわす", (int)(24 * u), new Color(1, 1, 1, 0.85f));
            float bl = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3f);
            Label(new Rect(0, H * 0.78f, W, 40 * u), "クリックで開始", (int)(26 * u), new Color(CYAN.r, CYAN.g, CYAN.b, bl));
            if (bestTime > 0f) Label(new Rect(0, H * 0.78f + 44 * u, W, 32 * u), "最速　" + Clock(bestTime), (int)(20 * u), new Color(1, 1, 1, 0.6f));
            return;
        }

        // ボスの体力と体勢
        float bw = W * 0.42f, bx = (W - bw) / 2f, by = 54 * u;
        Label(new Rect(bx, by - 34 * u, bw, 30 * u), phase2 ? "門 守 ― 怒" : "門 守", (int)(22 * u), new Color(1, 1, 1, 0.9f), TextAnchor.MiddleLeft);
        Fill(new Rect(bx, by, bw, 6 * u), new Color(0, 0, 0, 0.55f));
        Fill(new Rect(bx, by, bw * Mathf.Clamp01(bHp / bossMaxHp), 6 * u), MAGENTA);
        int pips = Mathf.RoundToInt(postureMax);
        float pw = 26 * u, gap = 6 * u, px = bx + bw - pips * (pw + gap) + gap;
        for (int i = 0; i < pips; i++)
        {
            float f = Mathf.Clamp01(posture - i);
            Fill(new Rect(px + i * (pw + gap), by + 14 * u, pw, 4 * u), new Color(0, 0, 0, 0.55f));
            if (f > 0f) Fill(new Rect(px + i * (pw + gap), by + 14 * u, pw * f, 4 * u), bs == BS.Broken ? Color.white : AMBER);
        }
        if (bs == BS.Broken) Fill(new Rect(bx, by + 24 * u, bw * Mathf.Clamp01(breakT / breakTime), 2 * u), Color.white);

        // 主人公の体力
        float hw = W * 0.24f, hx = 60 * u, hy = H - 60 * u;
        Fill(new Rect(hx, hy, hw, 6 * u), new Color(0, 0, 0, 0.55f));
        Fill(new Rect(hx, hy, hw * Mathf.Clamp01(pHp / playerMaxHp), 6 * u), CYAN);

        // 攻撃の向きの目印
        if (showGuide && mode == Mode.Fight && bs == BS.Attack && atk != null && !atkResolved)
        {
            float remain = atk.wind - atkT, k = Mathf.Clamp01(1f - remain / atk.wind);
            bool now = remain <= parryWindow;
            char sd = ShownDir;
            Color c = sd == 'T' ? RED : (now ? Color.white : new Color(CYAN.r, CYAN.g, CYAN.b, 0.35f + 0.5f * k));
            int sz = (int)((now ? 84 : 60) * u);
            if (sd == 'L') Label(new Rect(W * 0.14f, H * 0.4f, 120 * u, 120 * u), "◀", sz, c);
            else if (sd == 'R') Label(new Rect(W * 0.86f - 120 * u, H * 0.4f, 120 * u, 120 * u), "▶", sz, c);
            else if (sd == 'D') Label(new Rect(W * 0.5f - 60 * u, H * 0.16f, 120 * u, 120 * u), "▲", sz, c);
            else { Label(new Rect(W * 0.5f - 200 * u, H * 0.18f, 400 * u, 90 * u), "！", (int)(84 * u), c, TextAnchor.MiddleCenter, FontStyle.Bold); Label(new Rect(W * 0.5f - 200 * u, H * 0.18f + 80 * u, 400 * u, 40 * u), "A / D", (int)(28 * u), c); }
        }

        if (msgT > 0f) { var c = msgCol; c.a *= Mathf.Clamp01(msgT / 0.3f); Label(new Rect(0, H * 0.3f, W, 90 * u), msg, (int)(64 * u), c, TextAnchor.MiddleCenter, FontStyle.Bold); }
        if (mode == Mode.Fight && time < 25f && bs != BS.Broken)
            Label(new Rect(0, H - 52 * u, W, 34 * u), "長押し：納刀　→　振る：抜刀（◀▶は同じ向き、▲は上へ）　　A / D：かわす　　F2：目印", (int)(19 * u), new Color(1, 1, 1, 0.6f));
        if (autoPlay) Label(new Rect(W - 260 * u, H - 52 * u, 240 * u, 34 * u), "自動プレイ中（F1）", (int)(18 * u), new Color(1, 1, 1, 0.6f), TextAnchor.MiddleRight);

        if (mode == Mode.Win && endT < 2.6f)
        {
            Label(new Rect(0, H * 0.26f, W, 130 * u), "一 閃", (int)(110 * u), Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            if (endT < 1.8f) DrawRecord(u, W, H, true);
            if (endT <= 0f) Label(new Rect(0, H * 0.86f, W, 40 * u), "クリックでもう一度", (int)(24 * u), new Color(1, 1, 1, 0.8f));
        }
        if (mode == Mode.Lose)
        {
            Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.35f));
            Label(new Rect(0, H * 0.26f, W, 130 * u), "敗 北", (int)(100 * u), new Color(0.9f, 0.15f, 0.2f), TextAnchor.MiddleCenter, FontStyle.Bold);
            if (endT < 1.6f) DrawRecord(u, W, H, false);
            if (endT <= 0f) Label(new Rect(0, H * 0.86f, W, 40 * u), "クリックでもう一度", (int)(24 * u), new Color(1, 1, 1, 0.8f));
        }
    }

    static string Clock(float s) { int m = (int)(s / 60f); return m > 0 ? $"{m}分{s - m * 60f:00.0}秒" : $"{s:0.0}秒"; }

    // 勝ち：腕前の段 / 負け：あとどれだけだったか
    string Grade()
    {
        if (nHit == 0 && nSae >= 6) return "極";
        if (nHit == 0) return "上";
        if (nHit <= 2) return "中";
        return "下";
    }

    void DrawRecord(float u, float W, float H, bool win)
    {
        float y = H * 0.46f, lh = 38 * u, cw = 300 * u, x0 = W * 0.5f - cw;
        Color dim = new Color(1, 1, 1, 0.75f);
        int rows = win ? (bestTime > 0f ? 7 : 6) : 5;
        Fill(new Rect(x0 - 20 * u, y - 14 * u, cw * 2f + 40 * u, rows * lh + 28 * u), new Color(0.04f, 0.02f, 0.08f, 0.62f));
        void Row(string k, string v, Color vc)
        {
            Label(new Rect(x0, y, cw - 24 * u, lh), k, (int)(24 * u), dim, TextAnchor.MiddleRight);
            Label(new Rect(W * 0.5f + 24 * u, y, cw, lh), v, (int)(26 * u), vc, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += lh;
        }
        if (win)
        {
            Row("腕 前", Grade(), CYAN);
            string t = Clock(fightTime);
            if (usedAuto) t += "（自動）";
            else if (newBest) t += "　新記録";
            Row("時 間", t, newBest && !usedAuto ? AMBER : Color.white);
            if (bestTime > 0f) Row("最 速", Clock(bestTime), Color.white);
        }
        else
        {
            Row("門守の残り", Mathf.CeilToInt(bHp / bossMaxHp * 100f) + "%", MAGENTA);
        }
        Row("弾 き", nParry + "（冴え " + nSae + "）", Color.white);
        Row("最大連続", maxCombo + " 連", Color.white);
        Row("見切り", nDodge.ToString(), Color.white);
        Row("被 弾", nHit.ToString(), nHit == 0 ? CYAN : Color.white);
    }
}
