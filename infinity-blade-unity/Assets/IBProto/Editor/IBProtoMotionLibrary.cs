using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// モーション置き場（Assets/IBProto/Motions）
// ・Motions の下に置いた FBX は、自動で「人型（Humanoid）」として読み込まれ、ファイル名がそのまま動きの名前になる
// ・台帳.csv に「日本語名・種類・ループ・出典」を書いておく（素材の控え帳）
// ・メニュー「IBProto > モーション > モーション一覧」で、開いているキャラに試しに当てて再生 / キャラに付ける
//   付けた動きは Characters/<キャラ>/<キャラ>_Motions.controller にたまっていく（キャラごとの持ち技リスト）
public static class IBProtoMotionLib
{
    public const string ROOT = "Assets/IBProto/Motions";
    public const string LEDGER = ROOT + "/台帳.csv";
    public static readonly string[] KINDS = { "待機", "構え", "攻撃", "防御", "奥義", "やられ", "移動", "未分類" };

    // 置き方：Motions/<ジャンル>/<ダウンロード元・パック>/<動き>.fbx
    //   例）Motions/女剣士/Mixamo_GreatSword/GS_Slash_01.fbx
    // ジャンルとパックはフォルダから決まる。フォルダを作って FBX を入れるだけで分類される
    public const string HEADER = "ファイル名,日本語名,ジャンル,パック,種類,ループ,出典,元の名前,メモ";

    public class Entry
    {
        public string file, jp, genre, pack, kind, loop, source, original, memo, path;
        public IBProtoMove move;   // 技データ（コードで動かす技）。FBX の動きなら null
        public bool Loop => loop == "する";
        public Entry Copy() { return (Entry)MemberwiseClone(); }
        AnimationClip clip;
        public AnimationClip Clip
        {
            get
            {
                if (clip == null && path != null)
                    clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                        .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                return clip;
            }
        }
    }

    // フォルダからジャンル・パックを出す
    public static void FromPath(string path, out string genre, out string pack)
    {
        var rel = path.Substring(ROOT.Length + 1).Split('/');
        genre = rel.Length >= 2 ? rel[0] : "未分類";
        pack = rel.Length >= 3 ? rel[1] : "";
    }

    // 台帳を見出し名で読む（列の順番が変わっても大丈夫）
    static List<Dictionary<string, string>> ReadLedger()
    {
        var rows = new List<Dictionary<string, string>>();
        if (!File.Exists(LEDGER)) return rows;
        var lines = File.ReadAllLines(LEDGER);
        if (lines.Length == 0) return rows;
        var head = lines[0].TrimStart('\uFEFF').Split(',').Select(h => h.Trim()).ToArray();
        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var c = line.Split(',');
            var d = new Dictionary<string, string>();
            for (int i = 0; i < head.Length; i++) d[head[i]] = i < c.Length ? c[i].Trim() : "";
            rows.Add(d);
        }
        return rows;
    }

    static string Get(Dictionary<string, string> d, string k) { return d.TryGetValue(k, out var v) ? v : ""; }

    // 台帳を読み、実際に置いてある FBX と突き合わせる。台帳に無い FBX は「未分類」で出す
    public static List<Entry> Load()
    {
        var byName = new Dictionary<string, Entry>();
        foreach (var d in ReadLedger())
        {
            var e = new Entry
            {
                file = Get(d, "ファイル名"), jp = Get(d, "日本語名"), kind = Get(d, "種類"), loop = Get(d, "ループ"),
                source = Get(d, "出典"), original = Get(d, "元の名前"), memo = Get(d, "メモ")
            };
            if (e.file != "") byName[e.file] = e;
        }
        var list = new List<Entry>();
        if (AssetDatabase.IsValidFolder(ROOT))
        {
            foreach (var id in AssetDatabase.FindAssets("t:Model", new[] { ROOT }))
            {
                var p = AssetDatabase.GUIDToAssetPath(id);
                var n = Path.GetFileNameWithoutExtension(p);
                Entry e;
                if (byName.TryGetValue(n, out var tmpl)) e = tmpl.Copy();
                else e = new Entry { file = n, jp = n, kind = "未分類", loop = GuessLoop(n) ? "する" : "しない" };
                if (string.IsNullOrEmpty(e.jp)) e.jp = n;
                if (string.IsNullOrEmpty(e.kind)) e.kind = "未分類";
                e.path = p;
                FromPath(p, out e.genre, out e.pack);
                list.Add(e);
            }
            // 技データ（モンハン太刀など）
            foreach (var id in AssetDatabase.FindAssets("t:IBProtoMove", new[] { ROOT }))
            {
                var p = AssetDatabase.GUIDToAssetPath(id);
                var mv = AssetDatabase.LoadAssetAtPath<IBProtoMove>(p);
                if (mv == null) continue;
                var n = Path.GetFileNameWithoutExtension(p);
                Entry e;
                if (byName.TryGetValue(n, out var tmpl)) e = tmpl.Copy();
                else e = new Entry { file = n, jp = mv.jpName, kind = mv.kind, loop = "しない" };
                if (string.IsNullOrEmpty(e.jp)) e.jp = mv.jpName;
                if (string.IsNullOrEmpty(e.kind) || e.kind == "未分類") e.kind = mv.kind;
                e.path = p; e.move = mv;
                FromPath(p, out e.genre, out e.pack);
                list.Add(e);
            }
        }
        list.Sort((a, b) =>
        {
            int g = string.CompareOrdinal(a.genre, b.genre); if (g != 0) return g;
            int k = Array.IndexOf(KINDS, a.kind).CompareTo(Array.IndexOf(KINDS, b.kind)); if (k != 0) return k;
            return string.CompareOrdinal(a.file, b.file);
        });
        return list;
    }

    static bool GuessLoop(string n)
    {
        foreach (var k in new[] { "Idle", "Walk", "Run", "Strafe", "Block", "Crouch" })
            if (n.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    public static bool IsLoop(string fileName)
    {
        foreach (var d in ReadLedger())
            if (Get(d, "ファイル名") == fileName) return Get(d, "ループ") == "する";
        return GuessLoop(fileName);
    }

    // 台帳に無い FBX を書き足す（日本語名などは後で手で直す）
    [MenuItem("IBProto/モーション/台帳に新しい素材を書き足す", false, 20)]
    public static void AppendNew()
    {
        var known = new HashSet<string>(ReadLedger().Select(d => Get(d, "ファイル名")));
        var add = new List<string>();
        foreach (var e in Load())
            if (!known.Contains(e.file))
                add.Add(string.Join(",", e.file, e.jp, e.genre, e.pack, "未分類", e.loop, "", "", ""));
        if (add.Count == 0) { EditorUtility.DisplayDialog("台帳", "新しい素材はありません", "OK"); return; }
        if (!File.Exists(LEDGER))
            File.WriteAllText(LEDGER, HEADER + "\n", new System.Text.UTF8Encoding(true));
        File.AppendAllLines(LEDGER, add);
        AssetDatabase.ImportAsset(LEDGER);
        EditorUtility.DisplayDialog("台帳", add.Count + " 件書き足しました。日本語名と種類はあとで台帳.csv を開いて直してね", "OK");
    }

    // 一度だけ：今までの置き方（Motions/<パック>）を、ジャンルの下（Motions/女剣士/<パック>）に引っ越す
    [InitializeOnLoadMethod]
    static void MigrateToGenres()
    {
        EditorApplication.delayCall += () =>
        {
            const string KEY = "IBProto.MotionGenres.v1";
            if (EditorPrefs.GetBool(KEY)) return;
            var oldDir = ROOT + "/Mixamo_GreatSword";
            if (AssetDatabase.IsValidFolder(oldDir))
            {
                if (!AssetDatabase.IsValidFolder(ROOT + "/女剣士")) AssetDatabase.CreateFolder(ROOT, "女剣士");
                var err = AssetDatabase.MoveAsset(oldDir, ROOT + "/女剣士/Mixamo_GreatSword");
                if (!string.IsNullOrEmpty(err)) { Debug.LogError("[IBProto] モーションの引っ越しに失敗: " + err); return; }
                Debug.Log("[IBProto] Mixamo_GreatSword を Motions/女剣士 の下に移しました");
            }
            EditorPrefs.SetBool(KEY, true);
        };
    }

    [MenuItem("IBProto/モーション/台帳を開く", false, 21)]
    static void OpenLedger()
    {
        var a = AssetDatabase.LoadAssetAtPath<TextAsset>(LEDGER);
        if (a != null) { EditorGUIUtility.PingObject(a); AssetDatabase.OpenAsset(a); }
        else EditorUtility.DisplayDialog("台帳", "台帳.csv がまだありません", "OK");
    }

    [MenuItem("IBProto/モーション/モーション置き場を見る", false, 22)]
    static void PingFolder()
    {
        var a = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ROOT);
        if (a != null) { Selection.activeObject = a; EditorGUIUtility.PingObject(a); }
    }

    // 設定がおかしくなった時用：Motions の FBX を全部、人型・名前付きで読み込み直す
    [MenuItem("IBProto/モーション/全部読み込み直す", false, 40)]
    public static void ReimportAll()
    {
        var ids = AssetDatabase.FindAssets("t:Model", new[] { ROOT });
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var id in ids)
            {
                var p = AssetDatabase.GUIDToAssetPath(id);
                if (AssetImporter.GetAtPath(p) is ModelImporter mi) { mi.userData = ""; mi.clipAnimations = new ModelImporterClipAnimation[0]; mi.SaveAndReimport(); }
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        Debug.Log("[IBProto] モーションを " + ids.Length + " 個読み込み直しました");
    }

    // ---- キャラごとの持ち技（AnimatorController） ----
    public static string CtrlPath(string prefabPath)
    {
        return Path.GetDirectoryName(prefabPath).Replace('\\', '/') + "/" + Path.GetFileNameWithoutExtension(prefabPath) + "_Motions.controller";
    }

    public static AnimatorController GetCtrl(string prefabPath, bool create)
    {
        var p = CtrlPath(prefabPath);
        var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(p);
        if (c == null && create) c = AnimatorController.CreateAnimatorControllerAtPath(p);
        return c;
    }

    public static HashSet<string> Attached(AnimatorController c)
    {
        var s = new HashSet<string>();
        if (c == null) return s;
        foreach (var st in c.layers[0].stateMachine.states) s.Add(st.state.name);
        return s;
    }

    public static void Attach(AnimatorController c, Entry e)
    {
        var sm = c.layers[0].stateMachine;
        if (sm.states.Any(s => s.state.name == e.file)) return;
        var st = sm.AddState(e.file, new Vector3(300 + 220 * (sm.states.Length / 12), 60 * (sm.states.Length % 12), 0));
        st.motion = e.Clip;
        if (e.kind == "待機" && (sm.defaultState == null || sm.defaultState.motion == null || !sm.defaultState.name.Contains("Idle")))
            sm.defaultState = st;
        EditorUtility.SetDirty(c); AssetDatabase.SaveAssets();
    }

    public static void Detach(AnimatorController c, string name)
    {
        var sm = c.layers[0].stateMachine;
        var hit = sm.states.FirstOrDefault(s => s.state.name == name);
        if (hit.state == null) return;
        sm.RemoveState(hit.state);
        EditorUtility.SetDirty(c); AssetDatabase.SaveAssets();
    }
}

// Motions の下の FBX を、人型・名前付きで読み込む
[InitializeOnLoad]
public class IBProtoMotionImport : AssetPostprocessor
{
    // このツールより先に読み込まれてしまった FBX があれば、一度だけ自動で読み込み直す
    static IBProtoMotionImport()
    {
        EditorApplication.delayCall += () =>
        {
            if (!AssetDatabase.IsValidFolder(IBProtoMotionLib.ROOT)) return;
            foreach (var id in AssetDatabase.FindAssets("t:Model", new[] { IBProtoMotionLib.ROOT }))
                if (AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(id)) is ModelImporter mi && mi.animationType != ModelImporterAnimationType.Human)
                { IBProtoMotionLib.ReimportAll(); return; }
        };
    }

    const string MARK = "IBProtoMotion v1";

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(IBProtoMotionLib.ROOT + "/")) return;
        var mi = (ModelImporter)assetImporter;
        if (mi.userData == MARK) return;
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importAnimation = true;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.importCameras = false;
        mi.importLights = false;
    }

    void OnPreprocessAnimation()
    {
        if (!assetPath.StartsWith(IBProtoMotionLib.ROOT + "/")) return;
        var mi = (ModelImporter)assetImporter;
        if (mi.userData == MARK) return;
        var clips = mi.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return;
        string name = Path.GetFileNameWithoutExtension(assetPath);
        bool loop = IBProtoMotionLib.IsLoop(name);
        for (int i = 0; i < clips.Length; i++)
        {
            var c = clips[i];
            c.name = i == 0 ? name : name + "_" + i;
            c.loopTime = loop;
            // ループする動き（待機・歩き等）はその場で動くように根っこを固定。攻撃などは踏み込みを残す
            c.lockRootRotation = loop; c.keepOriginalOrientation = true;
            c.lockRootHeightY = loop; c.keepOriginalPositionY = true;
            c.lockRootPositionXZ = loop; c.keepOriginalPositionXZ = true;
        }
        mi.clipAnimations = clips;
        mi.userData = MARK;
    }
}

// モーション一覧：試しに再生・キャラに付ける
public class IBProtoMotionWindow : EditorWindow
{
    List<IBProtoMotionLib.Entry> entries = new List<IBProtoMotionLib.Entry>();
    int kindTab, genreTab;
    Vector2 scroll;

    // 再生
    IBProtoMotionLib.Entry playing;
    GameObject playTarget;
    float playTime, speed = 1f;
    bool paused;
    double lastTick;

    [MenuItem("IBProto/モーション/モーション一覧（試しに再生・キャラに付ける）", false, 0)]
    public static void Open()
    {
        var w = GetWindow<IBProtoMotionWindow>("モーション");
        w.minSize = new Vector2(380, 300);
        w.Reload();
    }

    void OnEnable()
    {
        Reload();
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayMode;
        PrefabStage.prefabStageClosing += OnStageClosing;
    }

    void OnDisable()
    {
        Stop();
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        PrefabStage.prefabStageClosing -= OnStageClosing;
    }

    void OnProjectChange() { Reload(); Repaint(); }
    void OnSelectionChange() { Repaint(); }
    void OnPlayMode(PlayModeStateChange s) { Stop(); }
    void OnStageClosing(PrefabStage s) { Stop(); }

    void Reload() { entries = IBProtoMotionLib.Load(); }

    // 対象：キャラ一覧で開いているキャラ（なければ、ヒエラルキーで選んだ人型）
    static Animator FindTarget(out string label, out string prefabPath)
    {
        label = null; prefabPath = null;
        var st = PrefabStageUtility.GetCurrentPrefabStage();
        if (st != null)
        {
            var a = st.prefabContentsRoot.GetComponentInChildren<Animator>();
            if (a != null && a.isHuman) { prefabPath = st.assetPath; label = Path.GetFileNameWithoutExtension(st.assetPath); return a; }
        }
        var go = Selection.activeGameObject;
        if (go != null && !EditorUtility.IsPersistent(go))
        {
            var a = go.GetComponentInParent<Animator>() ?? go.GetComponentInChildren<Animator>();
            if (a != null && a.isHuman)
            {
                label = a.gameObject.name;
                var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(a.gameObject);
                if (src != null) prefabPath = AssetDatabase.GetAssetPath(src);
                return a;
            }
        }
        return null;
    }

    void OnGUI()
    {
        var anim = FindTarget(out var label, out var prefabPath);
        EditorGUILayout.Space(6);
        using (new EditorGUILayout.HorizontalScope("box"))
        {
            if (anim != null) GUILayout.Label("対象のキャラ：" + label, EditorStyles.boldLabel);
            else GUILayout.Label("キャラ一覧でキャラを開くと、ここで動かせます", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("キャラ一覧", EditorStyles.miniButton, GUILayout.Width(70))) EditorApplication.ExecuteMenuItem("IBProto/キャラ一覧");
        }

        // 再生バー
        if (playing != null && playTarget != null)
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                var clip = playing.Clip;
                float len = clip != null ? clip.length : 1f;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("再生中：" + playing.jp + (playing.Loop ? "（ループ）" : ""), EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(paused ? "▶" : "❚❚", GUILayout.Width(34))) paused = !paused;
                    if (GUILayout.Button("止める", GUILayout.Width(60))) Stop();
                }
                if (playing != null)
                {
                    EditorGUI.BeginChangeCheck();
                    float t = EditorGUILayout.Slider("時間（秒）", playTime, 0f, len);
                    if (EditorGUI.EndChangeCheck()) { playTime = t; paused = true; Sample(); }
                    speed = EditorGUILayout.Slider("速さ", speed, 0.1f, 2f);
                }
            }
        }

        // ジャンルタブ（Motions の下のフォルダ）→ 種類タブ
        var genres = entries.Select(e => e.genre).Distinct().ToList();
        genres.Insert(0, "全ジャンル");
        genreTab = Mathf.Clamp(genreTab, 0, genres.Count - 1);
        genreTab = GUILayout.Toolbar(genreTab, genres.ToArray());
        var shown = genreTab == 0 ? entries : entries.Where(e => e.genre == genres[genreTab]).ToList();
        var kinds = IBProtoMotionLib.KINDS.Where(k => shown.Any(e => e.kind == k)).ToList();
        kinds.Insert(0, "すべて");
        kindTab = Mathf.Clamp(kindTab, 0, kinds.Count - 1);
        kindTab = GUILayout.Toolbar(kindTab, kinds.ToArray());

        var ctrl = prefabPath != null ? IBProtoMotionLib.GetCtrl(prefabPath, false) : null;
        var attached = IBProtoMotionLib.Attached(ctrl);
        if (anim != null && prefabPath != null)
            GUILayout.Label("このキャラに付いている動き：" + attached.Count + " 個", EditorStyles.miniLabel);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (entries.Count == 0) EditorGUILayout.HelpBox("Motions フォルダに動きがありません", MessageType.Info);
        string lastGroup = null;
        foreach (var e in shown)
        {
            if (kindTab > 0 && e.kind != kinds[kindTab]) continue;
            var group = e.genre + (e.pack != "" ? "　／　" + e.pack : "");
            if (group != lastGroup) { GUILayout.Label(group, EditorStyles.boldLabel); lastGroup = group; }
            using (new EditorGUILayout.HorizontalScope())
            {
                var c = GUI.backgroundColor;
                if (e == playing) GUI.backgroundColor = new Color(0.55f, 0.8f, 1f);
                GUI.enabled = anim != null && e.Clip != null;
                if (GUILayout.Button("▶", GUILayout.Width(28))) Play(e, anim.gameObject);
                GUI.enabled = true;
                GUI.backgroundColor = c;
                GUILayout.Label(e.jp, GUILayout.Width(120));
                GUILayout.Label(e.Clip != null ? e.Clip.length.ToString("0.0") + "秒" : e.move != null ? e.move.duration.ToString("0.00") + "秒" : "読込中", EditorStyles.miniLabel, GUILayout.Width(44));
                GUILayout.Label(e.kind + (e.Loop ? "・ループ" : ""), EditorStyles.miniLabel, GUILayout.Width(70));
                GUILayout.FlexibleSpace();
                if (e.move != null)
                {
                    string hitsTxt = string.Join("・", e.move.hits.Select(h => h.t.ToString("0.00")));
                    GUILayout.Label(new GUIContent("当たり " + hitsTxt + "秒", "入力：" + e.move.input + "\n" + e.move.note + "\n（技データはバトルで再生されます）"), EditorStyles.miniLabel, GUILayout.Width(120));
                    if (GUILayout.Button("数字", EditorStyles.miniButton, GUILayout.Width(40))) { Selection.activeObject = e.move; EditorGUIUtility.PingObject(e.move); }
                }
                else if (prefabPath != null)
                {
                    if (attached.Contains(e.file))
                    {
                        GUILayout.Label("✓ 付いてる", EditorStyles.miniLabel, GUILayout.Width(60));
                        if (GUILayout.Button("外す", EditorStyles.miniButton, GUILayout.Width(40))) IBProtoMotionLib.Detach(ctrl, e.file);
                    }
                    else
                    {
                        GUI.enabled = e.Clip != null;
                        if (GUILayout.Button("＋ 付ける", EditorStyles.miniButton, GUILayout.Width(70))) AttachTo(anim, prefabPath, e);
                        GUI.enabled = true;
                    }
                }
            }
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(2);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("台帳を開く", EditorStyles.miniButton)) EditorApplication.ExecuteMenuItem("IBProto/モーション/台帳を開く");
            if (GUILayout.Button("新しい素材を台帳に書き足す", EditorStyles.miniButton)) IBProtoMotionLib.AppendNew();
            if (GUILayout.Button("読み直す", EditorStyles.miniButton, GUILayout.Width(60))) Reload();
        }
    }

    void AttachTo(Animator anim, string prefabPath, IBProtoMotionLib.Entry e)
    {
        var ctrl = IBProtoMotionLib.GetCtrl(prefabPath, true);
        IBProtoMotionLib.Attach(ctrl, e);
        // キャラ本体に持ち技リストを結び付ける（初回だけ）
        var st = PrefabStageUtility.GetCurrentPrefabStage();
        if (st != null && st.assetPath == prefabPath && anim.runtimeAnimatorController != ctrl)
        {
            Undo.RecordObject(anim, "持ち技リストを付ける");
            anim.runtimeAnimatorController = ctrl;
            EditorSceneManager.MarkSceneDirty(st.scene);
            ShowNotification(new GUIContent("付けました。キャラ一覧の「保存」で残ります"));
        }
        else ShowNotification(new GUIContent("付けました"));
    }

    // ---- 試しに再生（キャラ自体は書き換えない。止めると元の姿勢に戻る） ----
    void Play(IBProtoMotionLib.Entry e, GameObject target)
    {
        Stop();
        playing = e; playTarget = target; playTime = 0f; paused = false;
        lastTick = EditorApplication.timeSinceStartup;
        if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
        Sample();
    }

    void Stop()
    {
        if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        playing = null; playTarget = null;
        SceneView.RepaintAll();
        Repaint();
    }

    void Sample()
    {
        if (playing == null || playTarget == null || playing.Clip == null) return;
        AnimationMode.BeginSampling();
        AnimationMode.SampleAnimationClip(playTarget, playing.Clip, playTime);
        AnimationMode.EndSampling();
        SceneView.RepaintAll();
    }

    void Tick()
    {
        if (playing == null) return;
        if (playTarget == null || playing.Clip == null) { Stop(); return; }
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - lastTick); lastTick = now;
        if (paused) return;
        float len = playing.Clip.length;
        playTime += dt * speed;
        if (playTime > len)
        {
            if (playing.Loop) playTime %= Mathf.Max(len, 0.01f);
            else playTime = 0f;   // 単発の動きも見やすいように頭から繰り返す
        }
        Sample();
        Repaint();
    }
}
