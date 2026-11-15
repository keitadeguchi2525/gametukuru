using System.Collections.Generic;
using UnityEngine;

// 試作01 本体。空のGameObjectにこれを付けてPlayするだけで動く
public class ProtoGame : MonoBehaviour
{
    public enum EnemyState { Idle, Attack, Break, Dead }
    enum Mode { Title, Fight, Result, Shop }

    public class Attack
    {
        public char dir, need;
        public float W, t, rec = 0.3f;
        public bool struck, failed, parried, warned, flow;
    }

    class Weapon
    {
        public string id, name, desc;
        public float mul, brk;
        public Dictionary<string, int> req;
    }

    [Tooltip("日本語が□で表示される場合は、日本語フォント（.ttf/.otf）をここに入れる")]
    public Font uiFont;

    [Header("調整用の数値")]
    public float parryWindow = 0.28f;     // 迎え撃ちパリィの受付時間（攻撃が当たる何秒前から）
    [Tooltip("受け流し（攻撃と同じ向き）の受付時間。短いほど難しい")]
    public float flowWindow = 0.11f;
    [Tooltip("ステージ（Stages の下のプレハブ名）。見つからなければ昔の岩柱の舞台")]
    public string stageName = "NeonArena";
    [Tooltip("主人公に使うキャラ（キャラ一覧のプレハブ名）。見つからなければ武士")]
    public string playerCharacter = "KenshiCharacter";
    [Tooltip("技1発の基本ダメージ（モーション値20のとき）")]
    public float hitDamage = 2.2f;
    public float enemyMaxHp = 260f;
    public float playerMaxHp = 100f;
    public int maxShield = 4;
    [Range(0f, 1f)] public float rareDropRate = 0.2f;
    [Tooltip("F1キーでも切り替え。自動でパリィと部位攻撃をする（カメラ演出の確認用）")]
    public bool autoPlay = false;
    float autoSlashT;

    // --- シーン ---
    Camera cam;
    ProtoPlayer player;
    ProtoCameraRig rig;
    float introT, slowT, slowScale = 1f, orbitAng;
    Material darkMat, rockMat, sparkMat, sparkWMat, redMat;
    ProtoEnemy enemy;
    ProtoAudio sfx;
    ProtoRecorder recorder;
    // 王（人型・約2.3m）は近く、棘甲獣（約5m）は遠くに立つ
    static Vector3 ENEMY_POS => new Vector3(0, 0, ProtoEnemy.KingAvailable ? 3.0f : 4.2f);

    // --- 状態 ---
    Mode mode = Mode.Title;
    float time;
    float pHp, dodgeT, dodgeCd; int pShield, dodgeDir; bool guard;
    float eHp, idleT, breakT, deadT; EnemyState eState = EnemyState.Idle;
    struct AtkSpec { public char d; public float W, rec; public AtkSpec(char d, float W, float rec) { this.d = d; this.W = W; this.rec = rec; } }
    readonly List<AtkSpec> chain = new List<AtkSpec>(); int idx, chainOK, flowInChain, lastPattern = -1; bool chainHit, tParry, tDodge, tBlock;
    Attack atk; int slashN, parryCombo;

    // --- 技（モンハン太刀） ---
    Dictionary<string, IBProtoMove> moves;
    enum Act { Dir, Click, Kijin, Fade, Mikiri, Ult }
    class Req { public Act a; public char dir; public List<Vector2> pts; public float t; }
    Req queued;
    IBProtoMove lastMove; float lastMoveEnd;
    List<Vector2> moveAim = new List<Vector2>();
    ProtoEnemy.Part aimPart; Vector3 aimLocal; bool aimHit;   // 入力した瞬間に狙った部位（当たるのは少し後なので先に決めておく）
    bool moveCounter, mikiriOK, guardedMsg;
    float spirit, kaeshiT, pressT; int spiritLv;
    static readonly string[] LV_NAME = { "", "白", "黄", "赤" };
    static readonly float[] LV_MUL = { 1f, 1.05f, 1.1f, 1.2f };
    static readonly Color FLOWC = new Color(0.16f, 0.42f, 0.74f);
    float hitstop, hurtFlash, winDelay = -1f;

    Dictionary<string, int> inv = new Dictionary<string, int> { { "装甲片", 0 }, { "肩甲", 0 }, { "光輪片", 0 }, { "琥珀核", 0 }, { "紅玉", 0 } };
    Dictionary<string, int> fightLoot = new Dictionary<string, int>();
    readonly HashSet<string> owned = new HashSet<string> { "scrap" };
    List<Weapon> weapons;
    Weapon weapon;
    string resTitle = "", resSub = "", resLoot = "";
    string msgText = ""; Color msgColor = Color.black; float msgT; int msgSize = 34;

    // --- 入力 ---
    bool dragging, dragDone; Vector2 drag0; readonly List<Vector2> dragPts = new List<Vector2>();
    
    // --- 演出 ---
    class Spark { public Transform t; public Vector3 v; public float life; }
    readonly List<Spark> sparks = new List<Spark>();
    class Trail { public LineRenderer lr; public List<Vector2> pts; public float life, max; public Color c; }
    readonly List<Trail> trails = new List<Trail>();
    Material lineMat;

    // 迎え撃ち（普通のパリィ）：振ってくる刃に向かって振る。画面右から来る横薙ぎ(R)には左から右へ(R)、振り下ろし(D)には下から上へ(U)
    static readonly Dictionary<char, char> MEET = new Dictionary<char, char> { { 'R', 'R' }, { 'L', 'L' }, { 'D', 'U' } };
    // 受け流し：刃と同じ向きに振って流す。受付が短く、リターンが大きい
    static readonly Dictionary<char, char> FLOW = new Dictionary<char, char> { { 'R', 'L' }, { 'L', 'R' }, { 'D', 'D' } };
    static readonly Dictionary<char, string> ARROW = new Dictionary<char, string> { { 'L', "←" }, { 'R', "→" }, { 'U', "↑" }, { 'D', "↓" } };

    // ================= セットアップ =================
    void Start()
    {
        weapons = new List<Weapon>
        {
            new Weapon { id = "scrap", name = "残骸ブレード", mul = 1.0f, desc = "船の残骸から削り出した刃" },
            new Weapon { id = "shell", name = "装甲の刀", mul = 1.5f, desc = "攻撃 ×1.5", req = new Dictionary<string, int> { { "装甲片", 3 }, { "肩甲", 1 } } },
            new Weapon { id = "spine", name = "光輪の刃", mul = 1.25f, brk = 0.7f, desc = "攻撃 ×1.25／崩し +0.7秒", req = new Dictionary<string, int> { { "光輪片", 2 }, { "装甲片", 2 } } },
            new Weapon { id = "horn", name = "琥珀の大太刀", mul = 2.2f, desc = "攻撃 ×2.2（紅玉は低確率）", req = new Dictionary<string, int> { { "琥珀核", 2 }, { "紅玉", 1 } } },
        };
        weapon = weapons[0];
        sfx = gameObject.AddComponent<ProtoAudio>();
        moves = IBProtoMove.LoadAll();
        BuildScene();
        recorder = cam.gameObject.AddComponent<ProtoRecorder>();   // F9 で録画
        ResetFight();
    }

    void BuildScene()
    {
        Color bg = new Color(0.914f, 0.906f, 0.886f);
        cam = Camera.main;
        if (cam == null) { var cg = new GameObject("Main Camera"); cg.tag = "MainCamera"; cam = cg.AddComponent<Camera>(); cg.AddComponent<AudioListener>(); }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = bg;
        cam.nearClipPlane = 0.05f;
        cam.fieldOfView = 60f;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = bg;
        RenderSettings.fogStartDistance = 12f;
        RenderSettings.fogEndDistance = 34f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.62f, 0.61f, 0.59f);

        sparkMat = ProtoBuild.Unlit(new Color(1f, 0.48f, 0.1f));
        sparkWMat = ProtoBuild.Unlit(Color.white);
        redMat = ProtoBuild.Unlit(new Color(0.78f, 0.06f, 0.18f));
        lineMat = ProtoBuild.LineMat();

        // ステージのプレハブがあれば、それを置くだけ（空・霧・光はステージ側が切り替える）
        GameObject stagePrefab = null;
#if UNITY_EDITOR
        foreach (var id in UnityEditor.AssetDatabase.FindAssets(stageName + " t:Prefab"))
        {
            var path = UnityEditor.AssetDatabase.GUIDToAssetPath(id);
            if (System.IO.Path.GetFileNameWithoutExtension(path) != stageName) continue;
            stagePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (stagePrefab != null) break;
        }
#endif
        if (stagePrefab != null) { Instantiate(stagePrefab).name = stageName; rockMat = darkMat = null; }
        else BuildOldScene();

        // プレイヤー（背中越し）とカメラ
        ProtoPlayer.CharacterName = playerCharacter;
        player = ProtoPlayer.Create(Vector3.zero);
        rig = new ProtoCameraRig(cam);
        rig.Snap(new Vector3(7f, 3f, -2f), ENEMY_POS + new Vector3(0, 2.5f, 0), 50f);
    }

    // 昔の舞台（白い岩柱の惑星）
    void BuildOldScene()
    {
        var lg = new GameObject("Proto Sun");
        var light = lg.AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 0.9f; light.shadows = LightShadows.Soft;
        lg.transform.rotation = Quaternion.Euler(50f, -30f, 0);

        var groundMat = ProtoBuild.Lit(new Color(0.847f, 0.835f, 0.808f));
        rockMat = ProtoBuild.Lit(new Color(0.81f, 0.8f, 0.77f));
        darkMat = ProtoBuild.Lit(new Color(0.18f, 0.17f, 0.2f));
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground"; ground.transform.localScale = new Vector3(8, 1, 8);
        ground.GetComponent<Renderer>().sharedMaterial = groundMat;

        // 惑星の岩柱
        var rocks = new GameObject("Rocks").transform;
        var r = new System.Random(7);
        for (int i = 0; i < 26; i++)
        {
            float h = Rand(r, 2f, 11f), w = Rand(r, 0.4f, 1.4f), ang = Rand(r, -1.3f, 1.3f), dist = Rand(r, 10f, 26f);
            var g = ProtoBuild.Box("Rock", new Vector3(w, h, w), new Vector3(Mathf.Sin(ang) * dist, h / 2f, Mathf.Cos(ang) * dist + 3f), rockMat, rocks, false);
            g.transform.rotation = Quaternion.Euler(0, Rand(r, 0, 180f), Rand(r, -5f, 5f));
        }
        // 墜落した船の残骸
        var wreck = ProtoBuild.Box("Wreck", new Vector3(3.5f, 0.8f, 1.6f), new Vector3(-7.5f, 0.3f, 9f), darkMat, null, false);
        wreck.transform.rotation = Quaternion.Euler(-6f, -40f, 14f);
    }

    static float Rand(System.Random r, float a, float b) { return a + (float)r.NextDouble() * (b - a); }

    void ResetFight()
    {
        pHp = playerMaxHp; pShield = maxShield; dodgeT = 0; dodgeCd = 0;
        eHp = enemyMaxHp; eState = EnemyState.Idle; idleT = 1.6f; atk = null; slashN = 0; deadT = 0;
        parryCombo = 0; fightLoot = new Dictionary<string, int>(); winDelay = -1f;
        spirit = 0; spiritLv = 0; queued = null; mikiriOK = false; kaeshiT = 0; lastMove = null;
        if (player != null) player.StopMove();
        if (enemy != null) Destroy(enemy.gameObject);
        enemy = ProtoEnemy.Create(ENEMY_POS);
        introT = 0; slowT = 0;
    }

    void StartFight()
    {
        ResetFight();
        mode = Mode.Fight;
    }

    // ================= 毎フレーム =================
    void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        time += dt;
        if (ProtoInput.RecordToggle && recorder != null) recorder.Toggle();
        // スロー演出とヒットストップ
        float dtE = dt * (slowT > 0 ? slowScale : 1f);
        slowT = Mathf.Max(0, slowT - dt);
        if (hitstop > 0) { hitstop -= dt; dtE = 0; }
        if (mode == Mode.Fight) introT += dt;

        if (mode == Mode.Fight)
        {
            HandleInput();
            dodgeT = Mathf.Max(0, dodgeT - dt); dodgeCd = Mathf.Max(0, dodgeCd - dt);
            if (eState == EnemyState.Idle) { idleT -= dtE; if (idleT <= 0) StartChain(); }
            else if (eState == EnemyState.Attack)
            {
                atk.t += dtE;
                if (!atk.warned) { atk.warned = true; sfx.Warn(); }
                if (!atk.struck && atk.t >= atk.W) Strike();
                if (mode == Mode.Fight && atk != null && atk.t >= atk.W + 0.12f + atk.rec)
                {
                    idx++;
                    if (idx < chain.Count) NextAttack(); else EndChain();
                }
            }
            else if (eState == EnemyState.Break) { breakT -= dtE; if (breakT <= 0) { eState = EnemyState.Idle; idleT = 0.7f; } }
        }
        if (eState == EnemyState.Dead) deadT += dtE;
        if (winDelay > 0) { winDelay -= dt; if (winDelay <= 0) { winDelay = -1f; mode = Mode.Result; } }

        if (enemy != null) enemy.Pose(time, eState, atk, deadT);
        float dodgeK = dodgeT > 0 ? Mathf.Sin((1f - dodgeT / 0.42f) * Mathf.PI) : 0f;
        player.Animate(dtE, false, dodgeK, dodgeDir, false);
        if (mode == Mode.Fight)
        {
            foreach (var h in player.TakeHits()) OnMoveHit(h);
            kaeshiT = Mathf.Max(0, kaeshiT - dt);
            if (queued != null && time - queued.t > 0.5f) queued = null;
            if (queued != null && player.CanChain) { var q = queued; queued = null; Execute(q); }
        }
        UpdateCamera(dt, dodgeK);
        UpdateSparks(dt);
        UpdateTrails(dt);
        msgT = Mathf.Max(0, msgT - dt);
        hurtFlash = Mathf.Max(0, hurtFlash - dt);
    }

    // ================= 入力 =================
    void HandleInput()
    {
        if (ProtoInput.DebugToggle) { autoPlay = !autoPlay; Msg(autoPlay ? "オート ON" : "オート OFF", new Color(0.33f, 0.33f, 0.33f), 22); }
        if (autoPlay) AutoPlay();
        if (ProtoInput.DodgeLeft) Dodge(-1);
        if (ProtoInput.DodgeRight) Dodge(1);
        guard = false;   // ガードは今は無し（あとで足せる）

        float s = Screen.height / 720f;
        Vector2 mp = ProtoInput.MousePos;
        if (ProtoInput.LeftPressed) { dragging = true; dragDone = false; drag0 = mp; dragPts.Clear(); dragPts.Add(mp); pressT = time; }
        if (dragging && !dragDone)
        {
            if ((mp - dragPts[dragPts.Count - 1]).sqrMagnitude > 1f) dragPts.Add(mp);
            if ((mp - drag0).magnitude >= 60f * s) CompleteSwipe();
        }
        if (ProtoInput.LeftReleased)
        {
            if (dragging && !dragDone)
            {
                if ((mp - drag0).magnitude >= 25f * s) CompleteSwipe();
                else if (time - pressT < 0.35f) Request(Act.Click, 'D', new List<Vector2> { mp });
            }
            dragging = false;
        }
        if (ProtoInput.RightPressed) Request(Act.Kijin, 'D', new List<Vector2> { mp });
        if (ProtoInput.FadePressed) Request(Act.Fade, 'D', new List<Vector2> { mp });
        if (ProtoInput.CounterPressed) Request(Act.Mikiri, 'D', new List<Vector2> { mp });
        if (ProtoInput.UltPressed) Request(Act.Ult, 'D', new List<Vector2> { mp });
    }

    // 確認用の自動操作（F1）
    void AutoPlay()
    {
        if (eState == EnemyState.Attack && atk != null && !atk.struck && !atk.failed && atk.W - atk.t <= 0.09f)
        {
            bool flow = Random.value < 0.3f;
            char d = flow ? FLOW[atk.dir] : atk.need;
            dragPts.Clear(); dragPts.Add(Vector2.zero);
            TryParry(d);
        }
        else if (eState == EnemyState.Break)
        {
            autoSlashT -= Time.deltaTime;
            if (autoSlashT > 0) return;
            autoSlashT = 0.15f;
            ProtoEnemy.Part target = null;
            foreach (var p in enemy.parts) if (!p.broken) { target = p; break; }
            Renderer tr = target != null ? target.objs[0].GetComponent<Renderer>() : null;
            Vector3 w = tr != null ? tr.bounds.center : target != null ? target.objs[0].transform.position : enemy.transform.position + new Vector3(0, enemy.height * 0.5f, 0);
            Vector3 sp = cam.WorldToScreenPoint(w);
            var pts = new List<Vector2> { new Vector2(sp.x - 30f, sp.y), new Vector2(sp.x, sp.y), new Vector2(sp.x + 30f, sp.y) };
            if (spiritLv >= 1 && Random.value < 0.15f) Request(Act.Ult, 'D', pts);
            else if (spirit >= 30f && Random.value < 0.6f) Request(Act.Kijin, 'D', pts);
            else Request(Act.Dir, "UDLR"[Random.Range(0, 4)], pts);
        }
    }

    static char DirOf(Vector2 d)
    {
        if (Mathf.Abs(d.x) > Mathf.Abs(d.y)) return d.x > 0 ? 'R' : 'L';
        return d.y > 0 ? 'U' : 'D';   // Unityの画面座標は上がプラス
    }

    void CompleteSwipe()
    {
        dragDone = true;
        var last = dragPts[dragPts.Count - 1];
        char dir = DirOf(last - drag0);
        var pts = new List<Vector2>(dragPts);
        AddTrail(pts, eState == EnemyState.Break ? new Color(1f, 0.48f, 0.1f) : new Color(0.13f, 0.13f, 0.13f));
        sfx.Slash();
        // 受け流しの直後：返し斬り（試作の派生）
        if (kaeshiT > 0f && M("MT_Kaeshi") != null) { kaeshiT = 0f; queued = null; StartMove(M("MT_Kaeshi"), pts, true); return; }
        // 敵の攻撃中はパリィ
        if (eState == EnemyState.Attack && atk != null && !atk.struck) { TryParry(dir); return; }
        Request(Act.Dir, dir, pts);
    }

    void Dodge(int side)
    {
        if (dodgeCd > 0) return;
        dodgeT = 0.42f; dodgeDir = side; dodgeCd = 0.6f;
        player.StopMove(); queued = null;
        sfx.Dodge();
    }

    // ================= 技（モンハン太刀を参考） =================
    IBProtoMove M(string id) { return moves != null && moves.TryGetValue(id, out var m) ? m : null; }

    IBProtoMove DirMove(char d)
    {
        switch (d)
        {
            case 'U': return M("MT_Kiriage");
            case 'L': return M("MT_Kesa");
            case 'R': return M("MT_Gyakukesa");
            default: return M("MT_Tate");
        }
    }

    // 入力を受ける。今の技の「次を受け付ける時間」になっていなければ、少しのあいだ覚えておく（先行入力）
    void Request(Act a, char dir, List<Vector2> pts)
    {
        var r = new Req { a = a, dir = dir, pts = pts, t = time };
        // 斬り下がり・見切りは、どの技の当たりの後からでも出せる（キャンセル技）
        bool cancel = (a == Act.Fade || a == Act.Mikiri) && player.CurrentMove != null && player.MoveT >= player.CurrentMove.FirstHit;
        if (player.CanChain || cancel) Execute(r); else queued = r;
    }

    void Execute(Req r)
    {
        if (mode != Mode.Fight || eState == EnemyState.Dead) return;
        IBProtoMove prev = player.CurrentMove ?? (time - lastMoveEnd < 0.4f ? lastMove : null);
        switch (r.a)
        {
            case Act.Dir: StartMove(DirMove(r.dir), r.pts, false); break;
            case Act.Click: StartMove(M("MT_Tsuki"), r.pts, false); break;
            case Act.Fade: StartMove(M("MT_Kirisagari"), r.pts, false); break;
            case Act.Kijin:
            {
                string pn = prev != null ? prev.name : "";
                IBProtoMove next;
                bool afterMikiri = pn == "MT_Mikiri" && mikiriOK;
                if (pn == "MT_Kijin1") next = M("MT_Kijin2");
                else if (pn == "MT_Kijin2") next = M("MT_Kijin3");
                else if (pn == "MT_Kijin3" || afterMikiri) next = M("MT_Daikaiten");
                else next = M("MT_Kijin1");
                if (next == null) return;
                float cost = afterMikiri ? 0f : next.spiritCost;
                if (spirit < cost) { Msg("練気が足りない", new Color(0.5f, 0.5f, 0.5f), 22); return; }
                spirit -= cost;
                StartMove(next, r.pts, afterMikiri);
                break;
            }
            case Act.Mikiri:
                if (spirit <= 0.5f) { Msg("練気が無い（斬って溜める）", new Color(0.5f, 0.5f, 0.5f), 22); return; }
                spirit = 0f; mikiriOK = false;
                StartMove(M("MT_Mikiri"), r.pts, false);
                break;
            case Act.Ult:
                if (spiritLv < 1) { Msg("練気の色が足りない（大回転斬りで上げる）", new Color(0.5f, 0.5f, 0.5f), 22); return; }
                spiritLv--;
                StartMove(M("MT_Kabuto"), r.pts, true);
                Msg("気刃兜割り", RED, 34); Slow(0.25f, 0.5f);
                break;
        }
    }

    void StartMove(IBProtoMove m, List<Vector2> pts, bool counter)
    {
        if (m == null) return;
        moveAim = pts != null && pts.Count > 0 ? pts : new List<Vector2> { ProtoInput.MousePos };
        aimPart = null; aimHit = false;
        if (enemy != null && PartUnder(moveAim, out var ap0, out Vector3 ap)) { aimHit = true; aimPart = ap0; aimLocal = enemy.transform.InverseTransformPoint(ap); }
        // 当たり判定で部位が取れなかったときは、画面上でなぞった線に一番近い部位を狙う
        if (enemy != null && aimPart == null)
        {
            float best = 70f * Screen.height / 720f;
            foreach (var part in enemy.parts)
            {
                if (part.broken || part.objs == null || part.objs.Count == 0 || part.objs[0] == null) continue;
                var r = part.objs[0].GetComponent<Renderer>();
                Vector3 c = r != null ? r.bounds.center : part.objs[0].transform.position;
                Vector3 sp = cam.WorldToScreenPoint(c);
                if (sp.z <= 0) continue;
                foreach (var q in moveAim)
                {
                    float d = Vector2.Distance(q, new Vector2(sp.x, sp.y));
                    if (d < best) { best = d; aimPart = part; aimHit = true; aimLocal = enemy.transform.InverseTransformPoint(c); }
                }
            }
        }
        moveCounter = counter;
        guardedMsg = false;
        player.PlayMove(m);
        lastMove = m; lastMoveEnd = time + m.duration;
    }

    // 技の当たりの瞬間
    void OnMoveHit(IBProtoMove.Hit h)
    {
        var m = player.CurrentMove ?? lastMove;
        if (m == null || enemy == null || eState == EnemyState.Dead) return;
        bool counterHit = moveCounter || (m.name == "MT_Mikiri" && mikiriOK);
        bool full = eState == EnemyState.Break || counterHit;
        float dmg = hitDamage * (h.mv / 20f) * weapon.mul * LV_MUL[spiritLv];

        var part = aimPart != null && !aimPart.broken ? aimPart : null;
        Vector3 pt = aimHit ? enemy.transform.TransformPoint(aimLocal) : enemy.transform.position + new Vector3(0, enemy.height * 0.6f, 0) - enemy.transform.forward * 0.3f;

        if (full)
        {
            slashN++;
            dmg *= 1f + 0.05f * Mathf.Min(slashN, 10);
            eHp -= dmg;
            spirit = Mathf.Min(100f, spirit + m.spiritGain);
            // ヒットストップ：重い技ほど長く止める
            float stop = h.mv >= 30f ? 0.11f : h.mv >= 20f ? 0.075f : 0.055f;
            hitstop = Mathf.Max(hitstop, stop);
            sfx.Cut(); if (h.mv >= 30f) sfx.Heavy();
            rig.Kick(0.12f + h.mv * 0.008f, Random.value < 0.5f ? -2.5f : 2.5f, 2f + h.mv * 0.08f);
            SpawnSparks(pt, 8 + (int)(h.mv * 0.4f), redMat, 4f + h.mv * 0.05f);
            if (part != null) { part.cur -= dmg; if (part.cur <= 0) BreakPart(part); }
            if (slashN >= 2) Msg(slashN + " HIT", INK, 26);
        }
        else
        {
            // 構えている敵には、ほとんど通らない
            eHp -= dmg * 0.15f;
            spirit = Mathf.Min(100f, spirit + m.spiritGain * 0.3f);
            hitstop = Mathf.Max(hitstop, 0.03f);
            sfx.Guarded(); rig.Kick(0.06f, 0, 0); SpawnSparks(pt, 5, sparkWMat, 2.5f);
            if (!guardedMsg) { guardedMsg = true; Msg("構えている", new Color(0.5f, 0.5f, 0.5f), 22); }
            if (eState == EnemyState.Idle) idleT = Mathf.Min(idleT, 0.6f);
        }
        // 大回転斬りが当たると練気の色が1段上がる
        if (m.name == "MT_Daikaiten" && spiritLv < 3)
        {
            spiritLv++;
            sfx.Break(); Msg("練気 " + LV_NAME[spiritLv], spiritLv == 3 ? RED : spiritLv == 2 ? new Color(0.75f, 0.6f, 0.05f) : INK, 30);
        }
        CheckDead();
    }

    // ================= 敵の攻撃 =================
    // 連続攻撃の型（向き, 振りかぶりから当たるまでの秒, 当たった後の戻り）
    static readonly AtkSpec[][] PATTERNS =
    {
        new[] { new AtkSpec('R', 0.8f, 0.26f), new AtkSpec('L', 0.5f, 0.26f), new AtkSpec('D', 0.62f, 0.4f) },                 // 0 三連斬り
        new[] { new AtkSpec('L', 0.75f, 0.24f), new AtkSpec('R', 0.42f, 0.3f), new AtkSpec('R', 0.95f, 0.4f) },                // 1 払い返し（最後は同じ向きの溜め）
        new[] { new AtkSpec('D', 0.7f, 0.2f), new AtkSpec('D', 0.48f, 0.4f) },                                                 // 2 二段振り下ろし
        new[] { new AtkSpec('D', 1.3f, 0.24f), new AtkSpec('L', 0.45f, 0.4f) },                                                // 3 溜め割り（長い溜めに焦らない）
        new[] { new AtkSpec('R', 0.65f, 0.2f), new AtkSpec('L', 0.42f, 0.2f), new AtkSpec('R', 0.42f, 0.2f), new AtkSpec('L', 0.45f, 0.24f), new AtkSpec('D', 0.8f, 0.45f) },  // 4 乱れ斬り
        new[] { new AtkSpec('L', 0.62f, 0.22f), new AtkSpec('D', 0.9f, 0.22f), new AtkSpec('R', 0.4f, 0.4f) },                 // 5 間を外す
    };

    void StartChain()
    {
        float ratio = eHp / enemyMaxHp;
        // 体力が減るほど、長くて速い型が混ざる
        var pool = new List<int> { 0, 1, 2 };
        if (ratio < 0.75f) { pool.Add(3); pool.Add(5); }
        if (ratio < 0.45f) { pool.Add(4); pool.Add(4); }
        int pi = pool[Random.Range(0, pool.Count)];
        if (pi == lastPattern && pool.Count > 1) pi = pool[(pool.IndexOf(pi) + 1 + Random.Range(0, pool.Count - 1)) % pool.Count];
        lastPattern = pi;
        float spd = ratio < 0.33f ? 0.9f : 1f;
        chain.Clear();
        foreach (var a in PATTERNS[pi]) chain.Add(new AtkSpec(a.d, a.W * spd, a.rec * spd));
        idx = 0; chainOK = 0; flowInChain = 0; chainHit = false; tParry = tDodge = tBlock = false;
        eState = EnemyState.Attack;
        NextAttack();
    }

    void NextAttack()
    {
        var c = chain[idx];
        atk = new Attack { dir = c.d, need = MEET[c.d], W = c.W, rec = c.rec, t = 0 };
    }

    void ParryFail(Attack a, string why)
    {
        a.failed = true;
        Msg(why, new Color(0.5f, 0.5f, 0.5f), 26);
    }

    bool TryParry(char dir)
    {
        var a = atk;
        if (a == null || a.struck || a.failed) return false;
        float left = a.W - a.t;                       // 当たるまでの残り時間
        bool flow;
        if (dir == MEET[a.dir])
        {
            if (left > parryWindow) { ParryFail(a, "早い！"); player.PlayMove(DirMove(dir), 1.6f, 0f, true); return false; }
            flow = false;
        }
        else if (dir == FLOW[a.dir])
        {
            if (left > flowWindow) { ParryFail(a, left <= parryWindow ? "受け流しは引きつけて" : "早い！"); player.PlayMove(DirMove(dir), 1.6f, 0f, true); return false; }
            flow = true;
        }
        else { ParryFail(a, "向きが違う"); player.PlayMove(DirMove(dir), 1.6f, 0f, true); return false; }

        // パリィ成功
        a.parried = true; a.struck = true; a.flow = flow; a.t = a.W + 0.12f;
        parryCombo++; chainOK++; tParry = true;
        queued = null;
        var pm = DirMove(dir);
        if (pm != null) player.PlayMove(pm, 2.2f, pm.WindTime, true);
        int c = Mathf.Min(parryCombo, 10);
        float side = a.dir == 'R' ? -1f : a.dir == 'L' ? 1f : 0f;
        var p = new Vector3(a.dir == 'R' ? 0.35f : a.dir == 'L' ? -0.35f : 0, 1.7f, 1.2f);
        if (flow)
        {
            flowInChain++;
            hitstop = 0.15f; Slow(0.32f, 0.35f);
            spirit = Mathf.Min(100f, spirit + 16f);
            kaeshiT = 0.4f;
            sfx.Flow();
            rig.Kick(0.45f, side * 8f, 9f);
            SpawnSparks(p, 26, sparkWMat, 7f);
            SpawnSparks(p, 10, sparkMat, 5f);
            Msg(parryCombo >= 2 ? "受け流し ×" + parryCombo : "受け流し", FLOWC, 36);
        }
        else
        {
            hitstop = 0.085f;
            spirit = Mathf.Min(100f, spirit + 8f);
            sfx.Parry(parryCombo);
            rig.Kick(0.3f + 0.03f * c, side * (5f + c * 0.6f), 6f + c * 0.5f);
            SpawnSparks(p, 16 + c * 2, sparkMat, 6f);
            SpawnSparks(p, 6, sparkWMat, 4f);
            Msg(parryCombo >= 2 ? "PARRY ×" + parryCombo : "PARRY", RED, 34);
        }
        return true;
    }

    void Strike()
    {
        var a = atk; a.struck = true;
        // 見切り斬り・無敵のある技
        if (!a.failed && player.Invulnerable)
        {
            var m = player.CurrentMove;
            if (m != null && m.name == "MT_Mikiri")
            {
                mikiriOK = true; spirit = 100f; chainOK++; tDodge = true;
                sfx.Mikiri(); hitstop = 0.1f; Slow(0.4f, 0.3f); rig.Kick(0.2f, 0, 3f);
                SpawnSparks(new Vector3(0, 1.5f, 0.9f), 14, sparkWMat, 5f);
                Msg("見切り！", FLOWC, 38);
                return;
            }
            chainOK++; tDodge = true; Msg("回避", new Color(0.33f, 0.33f, 0.33f), 26);
            return;
        }
        if (!a.failed && dodgeT > 0) { chainOK++; tDodge = true; rig.Kick(0.08f, 0, 0); Msg("回避", new Color(0.33f, 0.33f, 0.33f), 26); return; }
        // 被弾
        float dmg = a.dir == 'D' ? 22f : 16f;
        pHp -= dmg; chainHit = true; parryCombo = 0; kaeshiT = 0; mikiriOK = false;
        player.StopMove(); queued = null;
        sfx.Hurt(); player.Hit(); rig.Kick(0.75f, Random.value < 0.5f ? -9f : 9f, -5f); hitstop = 0.07f; hurtFlash = 0.12f;
        if (pHp <= 0) Lose();
    }

    void EndChain()
    {
        if (!chainHit && chainOK == chain.Count)
        {
            float t = tParry ? 2.6f + Mathf.Min(parryCombo, 10) * 0.12f + flowInChain * 0.5f : tDodge ? 1.8f : 1.4f;
            t += weapon.brk;
            eState = EnemyState.Break; breakT = t; slashN = 0;
            sfx.Break(); rig.Kick(0.25f, 0, 4f); Msg("崩し！", RED, 40);
        }
        else { eState = EnemyState.Idle; idleT = Random.Range(1.0f, 1.7f); }
        atk = null;
    }

    // ================= プレイヤーの攻撃 =================
    bool PartUnder(List<Vector2> pts, out ProtoEnemy.Part part, out Vector3 point)
    {
        part = null; point = Vector3.zero;
        if (pts == null || pts.Count == 0) return false;
        bool bodyHit = false;
        Vector2[] samples = { pts[0], pts[pts.Count / 2], pts[pts.Count - 1] };
        foreach (var s in samples)
        {
            var ray = cam.ScreenPointToRay(new Vector3(s.x, s.y, 0));
            // 体の奥にある部位（頭の後ろの光輪など）も狙えるよう、線上の全部を調べる
            var hits = Physics.RaycastAll(ray, 60f);
            System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            foreach (var hit in hits)
            {
                if (enemy.colliderToPart.TryGetValue(hit.collider, out var p) && !p.broken) { part = p; point = hit.point; return true; }
                if (!bodyHit && (enemy.bodyColliders.Contains(hit.collider) || enemy.colliderToPart.ContainsKey(hit.collider))) { bodyHit = true; point = hit.point; }
            }
        }
        return bodyHit;
    }

    void BreakPart(ProtoEnemy.Part p)
    {
        p.broken = true;
        enemy.DetachPart(p);
        Add(fightLoot, p.mat, 1);
        hitstop = 0.12f; rig.Kick(0.6f, 0, 10f); Slow(0.5f, 0.25f);
        sfx.Part(); Msg("部位破壊！ " + p.mat + "を入手", RED, 30);
    }

    void CheckDead()
    {
        if (eHp <= 0 && eState != EnemyState.Dead) { eState = EnemyState.Dead; deadT = 0; player.StopMove(); queued = null; Win(); }
    }

    // ================= 勝敗・素材 =================
    static void Add(Dictionary<string, int> d, string k, int n) { d[k] = (d.ContainsKey(k) ? d[k] : 0) + n; }

    string LootText(Dictionary<string, int> d)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var kv in d) if (kv.Value > 0) sb.Append(kv.Key).Append(" ×").Append(kv.Value).Append(kv.Key == "紅玉" ? "  レア！" : "").Append("\n");
        return sb.Length > 0 ? sb.ToString() : "なし";
    }

    void Win()
    {
        var loot = new Dictionary<string, int>(fightLoot);
        Add(loot, "装甲片", 2 + Random.Range(0, 2));
        if (Random.value < rareDropRate) Add(loot, "紅玉", 1);
        foreach (var kv in loot) Add(inv, kv.Key, kv.Value);
        int broken = 0; foreach (var p in enemy.parts) if (p.broken) broken++;
        resTitle = "撃破";
        resSub = "部位破壊 " + broken + "／" + enemy.parts.Count;
        resLoot = "手に入れた素材\n" + LootText(loot);
        winDelay = 2.2f;
        Slow(1.3f, 0.3f);
        orbitAng = 0;
    }

    void Lose()
    {
        foreach (var kv in fightLoot) Add(inv, kv.Key, kv.Value);
        resTitle = "力尽きた";
        resSub = "船の再生装置で再起動。この周回の最初に戻る。";
        resLoot = "持ち帰った素材（ロストなし）\n" + LootText(fightLoot);
        mode = Mode.Result;
    }

    bool CanCraft(Weapon w)
    {
        if (w.req == null) return false;
        foreach (var kv in w.req) if ((inv.ContainsKey(kv.Key) ? inv[kv.Key] : 0) < kv.Value) return false;
        return true;
    }

    void Msg(string t, Color c, int size) { msgText = t; msgColor = c; msgSize = size; msgT = 0.85f; }

    // ================= 演出 =================
    void SpawnSparks(Vector3 pos, int n, Material mat, float speed)
    {
        for (int i = 0; i < n; i++)
        {
            var g = ProtoBuild.Box("Spark", Vector3.one * 0.05f, pos, mat, null, false);
            var v = new Vector3(Random.Range(-1f, 1f), Random.Range(-0.3f, 1.2f), Random.Range(-1f, 0.2f)).normalized * Random.Range(speed * 0.4f, speed);
            sparks.Add(new Spark { t = g.transform, v = v, life = Random.Range(0.25f, 0.5f) });
        }
    }

    void UpdateSparks(float dt)
    {
        for (int i = sparks.Count - 1; i >= 0; i--)
        {
            var s = sparks[i];
            s.life -= dt; s.v.y -= 9f * dt; s.t.position += s.v * dt;
            if (s.life <= 0) { Destroy(s.t.gameObject); sparks.RemoveAt(i); }
        }
    }

    void AddTrail(List<Vector2> pts, Color c)
    {
        var g = new GameObject("Trail");
        var lr = g.AddComponent<LineRenderer>();
        lr.material = lineMat; lr.useWorldSpace = true; lr.widthMultiplier = 0.006f;
        lr.numCapVertices = 4; lr.numCornerVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trails.Add(new Trail { lr = lr, pts = pts, life = 0.28f, max = 0.28f, c = c });
    }

    void UpdateTrails(float dt)
    {
        for (int i = trails.Count - 1; i >= 0; i--)
        {
            var t = trails[i];
            t.life -= dt;
            if (t.life <= 0) { Destroy(t.lr.gameObject); trails.RemoveAt(i); continue; }
            var c = t.c; c.a = Mathf.Clamp01(t.life / t.max);
            t.lr.startColor = c; t.lr.endColor = c;
            t.lr.positionCount = t.pts.Count;
            for (int j = 0; j < t.pts.Count; j++)
                t.lr.SetPosition(j, cam.ScreenToWorldPoint(new Vector3(t.pts[j].x, t.pts[j].y, 0.3f)));
        }
    }

    void Slow(float realSeconds, float scale) { slowT = realSeconds; slowScale = scale; }

    static float Ease(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }

    // カメラワーク：状況ごとに「どこから・どこを・どの画角で」見るかを決め、リグがなめらかに追う
    void UpdateCamera(float dt, float dodgeK)
    {
        Vector3 E = ENEMY_POS;
        Vector3 P = player.BodyPos;
        // 敵の背の高さで構図を合わせる（棘甲獣 5.2m を基準）
        float EH = enemy != null ? enemy.height : 5.2f, hs = EH / 5.2f, hw = 0.5f + 0.5f * hs;
        Vector3 tPos = new Vector3(P.x * 0.75f + 0.8f + 0.15f * hs, 1.75f + EH * 0.14f, -1.6f - EH * 0.33f);
        Vector3 tLook = new Vector3(P.x * 0.3f + 0.1f, 0.4f + EH * 0.46f, E.z);
        float tFov = 46f + EH * 2.3f, tRoll = -dodgeDir * 5f * dodgeK, smooth = dodgeK > 0 ? 0.1f : 0.22f;

        if (mode == Mode.Title || mode == Mode.Shop || (mode == Mode.Result && eState != EnemyState.Dead))
        {
            // 待機画面：敵の周りをゆっくり回る
            orbitAng += dt * 0.12f;
            tPos = E + new Vector3(Mathf.Sin(orbitAng) * 8f * hw, Mathf.Max(1.3f, 2.2f * hs), -Mathf.Cos(orbitAng) * 8f * hw);
            tLook = E + new Vector3(0, 2.6f * hs, 0); tFov = 45f; tRoll = 0; smooth = 0.6f;
        }
        else if (eState == EnemyState.Dead)
        {
            // 撃破：スローの中で回り込む
            orbitAng += dt * 0.35f;
            float a = -1.45f - orbitAng * 0.6f;
            tPos = E + new Vector3(Mathf.Sin(a) * 7.5f * hw, Mathf.Max(1.4f, 3.0f * hs), -Mathf.Cos(a) * 7.5f * hw);
            tLook = E + new Vector3(0, 1.2f * hs, 0); tFov = 48f; tRoll = 0; smooth = 0.45f;
        }
        else
        {
            // 登場：敵の顔のアップから、背中越しへ引く
            if (introT < 2.4f)
            {
                float k = Ease((introT - 0.5f) / 1.7f);
                tPos = Vector3.Lerp(E + new Vector3(0.7f * hw, EH * 0.85f, -2.6f * hw), tPos, k);
                tLook = Vector3.Lerp(E + new Vector3(0, EH * 0.82f, 0), tLook, k);
                tFov = Mathf.Lerp(38f, tFov, k); smooth = 0.12f;
            }
            if (eState == EnemyState.Attack && atk != null && !atk.struck)
            {
                // 予兆：攻撃側へ寄って傾く。振り下ろしは低く構えて見上げる
                float p = Ease(atk.t / atk.W);
                float side = atk.dir == 'R' ? -1f : atk.dir == 'L' ? 1f : 0f;
                tPos += new Vector3(side * 0.3f * p, (atk.dir == 'D' ? -0.45f : -0.2f) * p * hw, 0.5f * p * hw);
                tLook += new Vector3(side * 0.45f * p, (atk.dir == 'D' ? 0.7f : 0.2f) * p * hs, 0);
                tFov -= 8f * p; tRoll += -side * 3f * p;
            }
            else if (eState == EnemyState.Break)
            {
                // 崩し：横に回り込んだローアングルで見上げる
                tPos = E + new Vector3(-2.4f * hw, Mathf.Max(0.6f, 1.2f * hs), -4.6f * hw);
                tLook = E + new Vector3(0.2f, 3.2f * hs, 0);
                tFov = 60f; tRoll = 4f; smooth = 0.3f;
            }
        }
        rig.Tick(dt, tPos, tLook, tFov, tRoll, smooth);
    }

    // ================= 画面表示（OnGUI） =================
    GUIStyle sLabel, sCenter, sBig, sTele, sPanel, sBtn, sSmall;
    static readonly Color RED = new Color(0.78f, 0.06f, 0.18f);
    static readonly Color INK = new Color(0.13f, 0.13f, 0.13f);

    void InitStyles()
    {
        if (sLabel != null) return;
        sLabel = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
        sLabel.normal.textColor = INK;
        if (uiFont) sLabel.font = uiFont;
        sSmall = new GUIStyle(sLabel) { fontSize = 12 };
        sCenter = new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        sBig = new GUIStyle(sCenter) { fontSize = 34 };
        sTele = new GUIStyle(sCenter) { fontSize = 90 };
        sPanel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(18, 18, 14, 14), alignment = TextAnchor.UpperLeft, fontSize = 14, wordWrap = true };
        sPanel.normal.background = Texture2D.whiteTexture; sPanel.normal.textColor = INK;
        if (uiFont) sPanel.font = uiFont;
        sBtn = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold, fixedHeight = 40 };
        if (uiFont) sBtn.font = uiFont;
    }

    static void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = o; }

    void Label(Rect r, string t, GUIStyle st, Color c, int size = 0)
    {
        var o = st.normal.textColor; int os = st.fontSize;
        st.normal.textColor = c; if (size > 0) st.fontSize = size;
        GUI.Label(r, t, st);
        st.normal.textColor = o; st.fontSize = os;
    }

    void OnGUI()
    {
        InitStyles();
        float s = Screen.height / 720f, W = Screen.width / s, H = 720f;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1));

        if (hurtFlash > 0) Fill(new Rect(0, 0, W, H), new Color(0.78f, 0.06f, 0.18f, 0.25f));
        DrawHud(W, H);

        if (mode == Mode.Title) DrawTitle(W, H);
        else if (mode == Mode.Result) DrawResult(W, H);
        else if (mode == Mode.Shop) DrawShop(W, H);
    }

    void DrawHud(float W, float H)
    {
        // 敵
        float bw = Mathf.Min(560f, W * 0.8f), bx = (W - bw) / 2f;
        Label(new Rect(bx, 10, bw, 22), enemy != null ? enemy.displayName : "", sCenter, INK, 14);
        Fill(new Rect(bx, 36, bw, 10), new Color(0, 0, 0, 0.12f));
        Fill(new Rect(bx, 36, bw * Mathf.Clamp01(eHp / enemyMaxHp), 10), RED);
        if (enemy != null)
        {
            float px = W / 2f - enemy.parts.Count * 32f;
            foreach (var p in enemy.parts)
            {
                var r = new Rect(px, 52, 58, 20);
                        Fill(r, new Color(1, 1, 1, p.broken ? 0.25f : 0.7f));
                Label(r, p.broken ? "×" + p.name : p.name, sCenter, p.broken ? new Color(0, 0, 0, 0.35f) : INK, 11);
                px += 64f;
            }
        }
        // 操作説明
        Fill(new Rect(14, 12, 400, 96), new Color(1, 1, 1, 0.55f));
        Label(new Rect(22, 14, 390, 94),
            "ドラッグ：斬る（↓縦斬り ↑斬り上げ ←袈裟 →逆袈裟）　クリック：突き\n" +
            "敵の攻撃中：矢印の向きにドラッグでパリィ（迎え撃ち）\n" +
            "　逆の向き＝攻撃と同じ向きでギリギリ：受け流し（難しい・大きい）\n" +
            "右クリック：気刃斬り（Ⅰ→Ⅱ→Ⅲ→大回転）　Space：見切り斬り\n" +
            "S：斬り下がり　E：気刃兜割り（ウルト）　A / D：回避", sSmall, INK, 11);
        // プレイヤー
        Label(new Rect(16, H - 92, 100, 18), "HP", sSmall, INK, 11);
        Fill(new Rect(16, H - 74, 260, 10), new Color(0, 0, 0, 0.12f));
        Fill(new Rect(16, H - 74, 260 * Mathf.Clamp01(pHp / playerMaxHp), 10), INK);
        // 練気ゲージ（色は 白→黄→赤 で攻撃力アップ）
        Color lvC = spiritLv == 3 ? RED : spiritLv == 2 ? new Color(0.85f, 0.68f, 0.08f) : spiritLv == 1 ? new Color(0.97f, 0.97f, 0.95f) : new Color(0.45f, 0.45f, 0.45f);
        Label(new Rect(16, H - 58, 200, 18), "練気" + (spiritLv > 0 ? "　色：" + LV_NAME[spiritLv] : ""), sSmall, INK, 11);
        Fill(new Rect(16, H - 40, 260, 12), new Color(0, 0, 0, 0.18f));
        Fill(new Rect(16, H - 40, 260 * Mathf.Clamp01(spirit / 100f), 12), new Color(0.2f, 0.62f, 0.85f));
        Fill(new Rect(16, H - 26, 260, 4), lvC);
        // 武器
        var right = new GUIStyle(sLabel) { alignment = TextAnchor.LowerRight, fontStyle = FontStyle.Bold };
        Label(new Rect(W - 316, H - 60, 300, 22), weapon.name, right, INK, 14);
        Label(new Rect(W - 316, H - 38, 300, 20), "攻撃 ×" + weapon.mul + (weapon.brk > 0 ? "／崩し +" + weapon.brk + "秒" : ""), right, INK, 12);
        // 連続パリィ
        if (parryCombo >= 2)
        {
            var rs = new GUIStyle(sLabel) { alignment = TextAnchor.UpperRight, fontStyle = FontStyle.Bold };
            Label(new Rect(W - 224, 64, 200, 40), parryCombo.ToString(), rs, RED, 30);
            Label(new Rect(W - 224, 100, 200, 20), "連続パリィ", rs, INK, 11);
        }
        // 予兆の矢印
        if (mode == Mode.Fight && eState == EnemyState.Attack && atk != null && !atk.struck)
        {
            bool inWin = atk.t >= atk.W - parryWindow;
            float a = atk.failed ? 0.25f : Mathf.Clamp01(atk.t / 0.25f);
            int size = inWin ? 112 : 90;
            var r = new Rect(W / 2f - 100, H / 2f - 100, 200, 200);
            if (inWin)
            {
                for (int i = 0; i < 8; i++)
                {
                    float ang = i * Mathf.PI / 4f;
                    Label(new Rect(r.x + Mathf.Cos(ang) * 4, r.y + Mathf.Sin(ang) * 4, r.width, r.height), ARROW[atk.need], sTele, new Color(RED.r, RED.g, RED.b, a * 0.6f), size);
                }
                Label(r, ARROW[atk.need], sTele, new Color(1, 1, 1, a), size);
            }
            else Label(r, ARROW[atk.need], sTele, new Color(RED.r, RED.g, RED.b, a), size);
        }
        // メッセージ
        if (msgT > 0)
        {
            float a = Mathf.Clamp01(msgT / 0.5f);
            var r = new Rect(0, H * 0.3f - 30, W, 60);
            Label(new Rect(r.x, r.y + 2, r.width, r.height), msgText, sBig, new Color(1, 1, 1, a), msgSize);
            Label(r, msgText, sBig, new Color(msgColor.r, msgColor.g, msgColor.b, a), msgSize);
        }
    }

    Rect Panel(float W, float H, float w, float h) { var r = new Rect((W - w) / 2f, (H - h) / 2f, w, h); return r; }

    void DrawTitle(float W, float H)
    {
        Fill(new Rect(0, 0, W, H), new Color(0.914f, 0.906f, 0.886f, 0.92f));
        var r = Panel(W, H, 640, 600);
        GUILayout.BeginArea(r);
        Label(GUILayoutUtility.GetRect(640, 44), "試作 01", sCenter, INK, 30);
        Label(GUILayoutUtility.GetRect(640, 26), "墜落した惑星。最初の生命体と戦う。", sCenter, INK, 14);
        GUILayout.Space(8);
        GUILayout.Box(
            "【操作】\n" +
            "・ドラッグ：斬る（向きで技が変わる）／クリック：突き／右クリック：気刃斬り\n" +
            "・敵の攻撃中は、矢印の向きにドラッグでパリィ（矢印が白く光ったら）\n" +
            "・攻撃と同じ向きにギリギリで振ると受け流し。受付は短いが、崩しが長くなり返し斬りが出せる\n" +
            "・Space：見切り斬り（練気を使い、下がる間に攻撃を受けると練気満タン）\n" +
            "・S：斬り下がり　E：気刃兜割り（練気の色を使うウルト）　A / D：回避\n\n" +
            "【流れ】\n" +
            "・構えている敵には攻撃がほぼ通らない\n" +
            "・敵の連続攻撃を全部しのぐと崩し（パリィで崩すと長い・連続パリィでさらに延長）\n" +
            "・崩し中に角・爪・背棘を狙って斬ると部位破壊 → その部位の素材が確定で手に入る\n" +
            "・素材を集めて工房で武器を作る。負けても素材は失われない", sPanel, GUILayout.Width(640));
        GUILayout.Space(10);
        if (GUILayout.Button("戦う", sBtn)) StartFight();
        GUILayout.EndArea();
    }

    void DrawResult(float W, float H)
    {
        Fill(new Rect(0, 0, W, H), new Color(0.914f, 0.906f, 0.886f, 0.92f));
        var r = Panel(W, H, 420, 360);
        GUILayout.BeginArea(r);
        Label(GUILayoutUtility.GetRect(420, 40), resTitle, sCenter, INK, 26);
        Label(GUILayoutUtility.GetRect(420, 40), resSub, sCenter, INK, 14);
        GUILayout.Box(resLoot, sPanel, GUILayout.Width(420), GUILayout.MinHeight(120));
        GUILayout.Space(10);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("もう一度戦う", sBtn)) StartFight();
        if (GUILayout.Button("工房へ", sBtn)) mode = Mode.Shop;
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    void DrawShop(float W, float H)
    {
        Fill(new Rect(0, 0, W, H), new Color(0.914f, 0.906f, 0.886f, 0.94f));
        var r = Panel(W, H, 560, 560);
        GUILayout.BeginArea(r);
        Label(GUILayoutUtility.GetRect(560, 40), "工房", sCenter, INK, 24);
        Label(GUILayoutUtility.GetRect(560, 24), "船の残骸と、集めた素材で武器を作る。", sCenter, INK, 13);
        var sb = new System.Text.StringBuilder("素材\n");
        foreach (var kv in inv) sb.Append(kv.Key).Append(" ×").Append(kv.Value).Append("　");
        sb.Append("\n装甲片：撃破で確定　肩甲・光輪片・琥珀核：部位破壊で確定　紅玉：撃破時に低確率");
        GUILayout.Box(sb.ToString(), sPanel, GUILayout.Width(560));
        GUILayout.Space(6);
        foreach (var w in weapons)
        {
            GUILayout.BeginHorizontal(sPanel, GUILayout.Width(560));
            string req = "初期装備";
            if (w.req != null) { var rb = new System.Text.StringBuilder(); foreach (var kv in w.req) rb.Append(kv.Key).Append(kv.Value).Append(" "); req = rb.ToString(); }
            GUILayout.Label("<b>" + w.name + "</b>\n" + w.desc + "／" + req, new GUIStyle(sLabel) { richText = true, fontSize = 13 }, GUILayout.Width(390));
            if (owned.Contains(w.id))
            {
                GUI.enabled = weapon != w;
                if (GUILayout.Button(weapon == w ? "装備中" : "装備", sBtn, GUILayout.Width(110))) weapon = w;
                GUI.enabled = true;
            }
            else
            {
                GUI.enabled = CanCraft(w);
                if (GUILayout.Button("作る", sBtn, GUILayout.Width(110)))
                {
                    foreach (var kv in w.req) inv[kv.Key] -= kv.Value;
                    owned.Add(w.id); weapon = w; sfx.Break();
                }
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.Space(8);
        if (GUILayout.Button("戦いに行く", sBtn)) StartFight();
        GUILayout.EndArea();
    }
}
