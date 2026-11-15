using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// IBProto フォルダを役割ごとに整理する（初回だけ自動で実行。メニューからも実行できる）
// 　Scenes / Characters / Scripts / Editor / Old
// ファイルは .meta ごと移動するので、シーンやプレハブの参照は切れない。
// 使われていない古い生成メッシュはごみ箱へ（Windows のごみ箱から戻せる）。
[InitializeOnLoad]
public static class IBProtoOrganize
{
    const string PREF = "IBProto.Organized.v1";
    const string B = "Assets/IBProto";

    static IBProtoOrganize()
    {
        if (EditorPrefs.GetBool(PREF, false)) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            EditorPrefs.SetBool(PREF, true);
            Run();
        };
    }

    [MenuItem("IBProto/フォルダを整理")]
    public static void Run()
    {
        EditorSceneManager.SaveOpenScenes();
        int moved = 0, trashed = 0;

        foreach (var d in new[] { B + "/Scenes", B + "/Characters/Samurai/Meshes", B + "/Characters/Samurai/Materials",
                                  B + "/Characters/Samurai/Textures", B + "/Characters/SF/Materials",
                                  B + "/Scripts/Battle", B + "/Scripts/Common", B + "/Scripts/Viewer",
                                  B + "/Editor/Setup", B + "/Old" })
            IBProtoSamuraiBuilder.MakeFolder(d);

        void Move(string from, string to)
        {
            if (!File.Exists(from) && !AssetDatabase.IsValidFolder(from)) return;
            if (File.Exists(to) || AssetDatabase.IsValidFolder(to)) return;
            string err = AssetDatabase.MoveAsset(from, to);
            if (string.IsNullOrEmpty(err)) moved++; else Debug.LogWarning("[IBProto] 移動できなかった: " + from + " → " + err);
        }

        // シーン
        Move(B + "/Proto01.unity", B + "/Scenes/Battle.unity");
        Move(B + "/CharaProto.unity", B + "/Scenes/CharaView.unity");

        // スクリプト
        foreach (var n in new[] { "ProtoGame", "ProtoPlayer", "ProtoEnemy", "ProtoCameraRig" }) Move($"{B}/{n}.cs", $"{B}/Scripts/Battle/{n}.cs");
        foreach (var n in new[] { "ProtoInput", "ProtoAudio", "ProtoBuild" }) Move($"{B}/{n}.cs", $"{B}/Scripts/Common/{n}.cs");
        Move(B + "/ProtoCharaViewer.cs", B + "/Scripts/Viewer/ProtoCharaViewer.cs");

        // エディタ拡張（一度きりのインストーラは Setup へ）
        Move(B + "/Editor/IBProtoInstallProBuilder.cs", B + "/Editor/Setup/IBProtoInstallProBuilder.cs");
        Move(B + "/Editor/IBProtoInstallSimplifier.cs", B + "/Editor/Setup/IBProtoInstallSimplifier.cs");

        // もう使わないもの
        Move(B + "/ProtoCharacter.cs", B + "/Old/ProtoCharacter.cs");
        Move(B + "/README.txt", B + "/Old/README_試作01.txt");
        Move("Assets/Claude outputs", B + "/Old/Claude outputs");
        Move("Assets/AssistantCustomInstructions.txt", B + "/Old/AssistantCustomInstructions.txt");

        // SFキャラ（ProBuilder版）のマテリアル
        if (AssetDatabase.IsValidFolder(B + "/Materials"))
        {
            foreach (var g in AssetDatabase.FindAssets("", new[] { B + "/Materials" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                Move(p, B + "/Characters/SF/Materials/" + Path.GetFileName(p));
            }
            if (!AssetDatabase.FindAssets("", new[] { B + "/Materials" }).Any()) AssetDatabase.DeleteAsset(B + "/Materials");
        }

        // 武士キャラ
        string sOld = B + "/Samurai", sNew = B + "/Characters/Samurai";
        if (AssetDatabase.IsValidFolder(sOld))
        {
            string prefab = sOld + "/SamuraiCharacter.prefab";
            Move(prefab, sNew + "/SamuraiCharacter.prefab");
            if (!File.Exists(prefab)) prefab = sNew + "/SamuraiCharacter.prefab";
            // 使っているメッシュは GUID で判定（移動しても変わらない）
            var used = File.Exists(prefab)
                ? AssetDatabase.GetDependencies(prefab, true).Select(AssetDatabase.AssetPathToGUID).ToHashSet()
                : null;
            foreach (var p in Directory.GetFiles(sOld).Where(f => !f.EndsWith(".meta")).Select(f => f.Replace('\\', '/')).ToArray())
            {
                string g = AssetDatabase.AssetPathToGUID(p);
                string file = Path.GetFileName(p), ext = Path.GetExtension(p).ToLowerInvariant();
                if (ext == ".mat") Move(p, sNew + "/Materials/" + file);
                else if (ext == ".png") Move(p, sNew + "/Textures/" + file);
                else if (ext == ".asset" && (used == null || used.Contains(g))) Move(p, sNew + "/Meshes/" + file);
                else if (ext == ".asset") { if (AssetDatabase.MoveAssetToTrash(p)) trashed++; }
            }
            if (!Directory.GetFiles(sOld).Any(f => !f.EndsWith(".meta"))) AssetDatabase.DeleteAsset(sOld);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[IBProto] フォルダを整理しました。移動 {moved} 件、使っていない古いメッシュをごみ箱へ {trashed} 件");
    }

    // よく使うシーンをすぐ開けるように
    [MenuItem("IBProto/シーンを開く/戦闘（Battle）")]
    static void OpenBattle() { Open(B + "/Scenes/Battle.unity"); }

    [MenuItem("IBProto/シーンを開く/キャラ確認（CharaView）")]
    static void OpenCharaView() { Open(B + "/Scenes/CharaView.unity"); }

    static void Open(string path)
    {
        if (!File.Exists(path)) { Debug.LogWarning("[IBProto] シーンが見つからない: " + path); return; }
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(path);
    }
}
