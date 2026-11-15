using System.IO;
using UnityEditor;
using UnityEngine;

// モンハン太刀の技データ（MT_*.asset）を、無いものだけ一度作る。
// 作った後は Motions/モンハン太刀/MH_Tachi の中のファイルが本物。数字はインスペクターで直せる。
public static class IBProtoMoveSetup
{
    [InitializeOnLoadMethod]
    static void AutoCreate()
    {
        EditorApplication.delayCall += () => Create(false);
    }

    [MenuItem("IBProto/モーション/モンハン太刀の技データを初期値に戻す", false, 41)]
    static void Reset()
    {
        if (!EditorUtility.DisplayDialog("技データ", "MT_ の技データを全部、最初の数字に戻します。手で直した数字は消えます。", "戻す", "やめる")) return;
        Create(true);
    }

    static void Create(bool overwrite)
    {
        MakeFolders(IBProtoMove.DIR);
        int n = 0;
        foreach (var m in IBProtoMove.Defaults())
        {
            string path = IBProtoMove.DIR + "/" + m.name + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<IBProtoMove>(path);
            if (old != null && !overwrite) continue;
            if (old != null) { EditorUtility.CopySerialized(m, old); old.name = m.name; EditorUtility.SetDirty(old); }
            else AssetDatabase.CreateAsset(m, path);
            n++;
        }
        if (n > 0)
        {
            AssetDatabase.SaveAssets();
            Debug.Log("[IBProto] モンハン太刀の技データを " + n + " 個用意しました（" + IBProtoMove.DIR + "）");
        }
    }

    static void MakeFolders(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        MakeFolders(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
