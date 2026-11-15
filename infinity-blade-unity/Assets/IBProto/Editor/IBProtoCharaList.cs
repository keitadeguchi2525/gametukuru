using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// キャラ一覧（メニュー「IBProto > キャラ一覧」）
// ・Characters フォルダの中の、保存済みキャラ（プレハブ）を名前で並べる
// ・名前を押すと、そのキャラを専用の編集空間（プレハブモード）で開く。毎回作り直すことはしない
// ・「保存」で、開いているキャラ本体を上書き保存する
// ・「今の版を残す」で、今の状態を 01, 02… と番号を付けて控える。控えは名前の下に並び、開く・戻す・削除ができる
public class IBProtoCharaList : EditorWindow
{
    const string ROOT = "Assets/IBProto/Characters";

    // ファイル名 → 一覧に出す名前
    static readonly Dictionary<string, string> LABEL = new Dictionary<string, string>
    {
        { "KenshiCharacter", "剣士（Furi風・主人公）" },
        { "SamuraiCharacter", "武士（旧主人公）" },
        { "EnemyKing", "星冠の王（敵）" },
        { "PBCharacter", "SF鎧（試作）" },
    };

    Vector2 scroll;
    readonly List<string> paths = new List<string>();
    readonly HashSet<string> folded = new HashSet<string>();

    [MenuItem("IBProto/キャラ一覧", false, 0)]
    static void Open()
    {
        var w = GetWindow<IBProtoCharaList>("キャラ一覧");
        w.minSize = new Vector2(360, 260);
        w.Refresh();
    }

    void OnEnable() { Refresh(); }
    void OnProjectChange() { Refresh(); Repaint(); }
    void OnHierarchyChange() { Repaint(); }

    void Refresh()
    {
        paths.Clear();
        if (!AssetDatabase.IsValidFolder(ROOT)) return;
        int depth = ROOT.Count(c => c == '/') + 1;
        foreach (var id in AssetDatabase.FindAssets("t:Prefab", new[] { ROOT }))
        {
            var p = AssetDatabase.GUIDToAssetPath(id);
            if (p.Contains("/History/")) continue;
            if (Dir(p).Count(c => c == '/') != depth) continue;   // Characters/<キャラ>/ 直下だけ
            paths.Add(p);
        }
        paths.Sort();
    }

    static string Dir(string p) { return Path.GetDirectoryName(p).Replace('\\', '/'); }
    static string Base(string p) { return Path.GetFileNameWithoutExtension(p); }
    static string Label(string path) { var n = Base(path); return LABEL.TryGetValue(n, out var l) ? l : n; }
    static string HistDir(string mainPath) { return Dir(mainPath) + "/History"; }

    // 控え（番号付き）を番号順に
    static List<(int no, string path)> Versions(string mainPath)
    {
        var list = new List<(int, string)>();
        var hd = HistDir(mainPath);
        if (!AssetDatabase.IsValidFolder(hd)) return list;
        var re = new Regex("^" + Regex.Escape(Base(mainPath)) + @"_(\d+)$");
        foreach (var id in AssetDatabase.FindAssets("t:Prefab", new[] { hd }))
        {
            var p = AssetDatabase.GUIDToAssetPath(id);
            var m = re.Match(Base(p));
            if (m.Success) list.Add((int.Parse(m.Groups[1].Value), p));
        }
        list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return list;
    }

    static PrefabStage Stage => PrefabStageUtility.GetCurrentPrefabStage();

    void OnGUI()
    {
        // ---- 今開いているキャラと、保存ボタン ----
        EditorGUILayout.Space(6);
        var st = Stage;
        using (new EditorGUILayout.HorizontalScope("box"))
        {
            if (st != null)
            {
                bool dirty = st.scene.isDirty;
                GUILayout.Label("開いているキャラ：" + Label(st.assetPath) + (dirty ? "（未保存の変更あり）" : ""), EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("モーション", GUILayout.Width(80), GUILayout.Height(22))) IBProtoMotionWindow.Open();
                GUI.enabled = dirty;
                if (GUILayout.Button("保存", GUILayout.Width(70), GUILayout.Height(22))) Save();
                GUI.enabled = true;
            }
            else GUILayout.Label("キャラは開いていません（下の名前を押すと開きます）", EditorStyles.miniLabel);
        }
        EditorGUILayout.Space(4);

        // ---- キャラの並び ----
        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (paths.Count == 0) EditorGUILayout.HelpBox("Characters フォルダにキャラがありません", MessageType.Info);
        foreach (var p in paths)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go == null) continue;
            bool isOpen = st != null && st.assetPath == p;
            var vers = Versions(p);

            using (new EditorGUILayout.HorizontalScope("box"))
            {
                var tex = AssetPreview.GetAssetPreview(go) ?? AssetPreview.GetMiniThumbnail(go);
                GUILayout.Label(tex, GUILayout.Width(56), GUILayout.Height(56));
                using (new EditorGUILayout.VerticalScope())
                {
                    var c = GUI.backgroundColor;
                    if (isOpen) GUI.backgroundColor = new Color(0.55f, 0.8f, 1f);
                    if (GUILayout.Button(Label(p) + (isOpen ? "　（編集中）" : ""), GUILayout.Height(28))) AssetDatabase.OpenAsset(go);
                    GUI.backgroundColor = c;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label("最終保存 " + File.GetLastWriteTime(Path.GetFullPath(p)).ToString("MM/dd HH:mm"), EditorStyles.miniLabel);
                        GUILayout.FlexibleSpace();
                        if (vers.Count > 0 && GUILayout.Button((folded.Contains(p) ? "▶ " : "▼ ") + "控え " + vers.Count, EditorStyles.miniButton, GUILayout.Width(70)))
                        { if (!folded.Remove(p)) folded.Add(p); }
                        if (GUILayout.Button("今の版を残す", EditorStyles.miniButton, GUILayout.Width(90))) Keep(p);
                        if (GUILayout.Button("場所", EditorStyles.miniButton, GUILayout.Width(40))) EditorGUIUtility.PingObject(go);
                    }
                }
            }

            // ---- 控え（01, 02 …） ----
            if (folded.Contains(p)) continue;
            foreach (var (no, vp) in vers.AsEnumerable().Reverse())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(66);
                    GUILayout.Label($"{Label(p)}　{no:00}", GUILayout.MinWidth(120));
                    GUILayout.Label(File.GetLastWriteTime(Path.GetFullPath(vp)).ToString("MM/dd HH:mm"), EditorStyles.miniLabel, GUILayout.Width(70));
                    if (GUILayout.Button("開く", EditorStyles.miniButton, GUILayout.Width(44)))
                        AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<GameObject>(vp));
                    if (GUILayout.Button("この版に戻す", EditorStyles.miniButton, GUILayout.Width(84))) Restore(p, vp, no);
                    if (GUILayout.Button("削除", EditorStyles.miniButton, GUILayout.Width(40))) Delete(p, vp, no);
                }
            }
            EditorGUILayout.Space(4);
        }
        EditorGUILayout.EndScrollView();
        if (AssetPreview.IsLoadingAssetPreviews()) Repaint();
    }

    // 開いているキャラを上書き保存
    static void Save()
    {
        var st = Stage;
        if (st == null) return;
        PrefabUtility.SaveAsPrefabAsset(st.prefabContentsRoot, st.assetPath);
        st.ClearDirtiness();
        Debug.Log("[IBProto] 保存しました: " + Label(st.assetPath));
    }

    // 今の状態を「名前_番号」で控える（開いていて未保存なら、先に保存する）
    static string Keep(string mainPath)
    {
        var st = Stage;
        if (st != null && st.assetPath == mainPath && st.scene.isDirty) Save();
        var hd = HistDir(mainPath);
        if (!AssetDatabase.IsValidFolder(hd)) AssetDatabase.CreateFolder(Dir(mainPath), "History");
        var vers = Versions(mainPath);
        int next = vers.Count == 0 ? 1 : vers.Last().no + 1;
        var dst = $"{hd}/{Base(mainPath)}_{next:00}.prefab";
        if (AssetDatabase.CopyAsset(mainPath, dst)) { Debug.Log($"[IBProto] {Label(mainPath)} を {next:00} として残しました"); return dst; }
        Debug.LogWarning("[IBProto] 控えを作れませんでした: " + dst);
        return null;
    }

    // 控えを削除する（Windows のごみ箱へ移すので、ごみ箱から戻せる）
    static void Delete(string mainPath, string verPath, int no)
    {
        if (!EditorUtility.DisplayDialog("控えを削除",
            $"{Label(mainPath)} の {no:00} を削除します。\n（Windows のごみ箱に移るので、ごみ箱から戻せます）", "削除", "やめる")) return;
        var st = Stage;
        if (st != null && st.assetPath == verPath) StageUtility.GoToMainStage();
        if (AssetDatabase.MoveAssetToTrash(verPath)) Debug.Log($"[IBProto] {Label(mainPath)} の {no:00} を削除しました");
        else Debug.LogWarning("[IBProto] 削除できませんでした: " + verPath);
    }

    // 控えの状態をキャラ本体に戻す。戻す前の状態も番号付きで残す
    static void Restore(string mainPath, string verPath, int no)
    {
        if (!EditorUtility.DisplayDialog("この版に戻す",
            $"{Label(mainPath)} を {no:00} の状態に戻します。\n戻す前の今の状態も、新しい番号で残します。", "戻す", "やめる")) return;
        var st = Stage;
        if (st != null && (st.assetPath == mainPath || st.assetPath == verPath)) StageUtility.GoToMainStage();
        Keep(mainPath);
        var root = PrefabUtility.LoadPrefabContents(verPath);
        try { PrefabUtility.SaveAsPrefabAsset(root, mainPath); }   // 本体のファイル（参照）はそのまま、中身だけ入れ替える
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Debug.Log($"[IBProto] {Label(mainPath)} を {no:00} に戻しました");
    }
}
