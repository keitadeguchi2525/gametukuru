using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

// UnityMeshSimplifier（ポリゴン削減ツール・MIT）をプロジェクトに追加する（一度だけ実行）
[InitializeOnLoad]
static class IBProtoInstallSimplifier
{
    static AddRequest req;
    static IBProtoInstallSimplifier()
    {
        if (EditorPrefs.GetBool("IBProto_SimplifierRequested", false)) return;
        EditorPrefs.SetBool("IBProto_SimplifierRequested", true);
        req = Client.Add("com.whinarn.unitymeshsimplifier");
        EditorApplication.update += Poll;
    }
    static void Poll()
    {
        if (req == null || !req.IsCompleted) return;
        if (req.Status == StatusCode.Success) Debug.Log("[IBProto] UnityMeshSimplifier を追加しました: " + req.Result.version);
        else Debug.LogError("[IBProto] UnityMeshSimplifier の追加に失敗: " + req.Error.message);
        EditorApplication.update -= Poll;
        req = null;
    }
}
