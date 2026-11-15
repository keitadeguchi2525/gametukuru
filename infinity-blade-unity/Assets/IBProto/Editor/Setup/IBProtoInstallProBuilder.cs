using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

// ProBuilder をプロジェクトに追加する（一度だけ実行）
[InitializeOnLoad]
static class IBProtoInstallProBuilder
{
    static AddRequest req;
    static IBProtoInstallProBuilder()
    {
        if (EditorPrefs.GetBool("IBProto_ProBuilderRequested", false)) return;
        EditorPrefs.SetBool("IBProto_ProBuilderRequested", true);
        req = Client.Add("com.unity.probuilder");
        EditorApplication.update += Poll;
    }
    static void Poll()
    {
        if (req == null || !req.IsCompleted) return;
        if (req.Status == StatusCode.Success) Debug.Log("[IBProto] ProBuilder を追加しました: " + req.Result.version);
        else Debug.LogError("[IBProto] ProBuilder の追加に失敗: " + req.Error.message);
        EditorApplication.update -= Poll;
        req = null;
    }
}
