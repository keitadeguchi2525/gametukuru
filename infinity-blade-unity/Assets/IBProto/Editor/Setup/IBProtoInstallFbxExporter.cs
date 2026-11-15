using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

// FBX Exporter をプロジェクトに追加する（一度だけ実行）。Blender へキャラを渡すのに使う
[InitializeOnLoad]
static class IBProtoInstallFbxExporter
{
    static AddRequest req;
    static IBProtoInstallFbxExporter()
    {
        if (EditorPrefs.GetBool("IBProto_FbxExporterRequested", false)) return;
        EditorPrefs.SetBool("IBProto_FbxExporterRequested", true);
        req = Client.Add("com.unity.formats.fbx");
        EditorApplication.update += Poll;
    }
    static void Poll()
    {
        if (req == null || !req.IsCompleted) return;
        if (req.Status == StatusCode.Success) Debug.Log("[IBProto] FBX Exporter を追加しました: " + req.Result.version);
        else Debug.LogError("[IBProto] FBX Exporter の追加に失敗: " + req.Error.message);
        EditorApplication.update -= Poll;
        req = null;
    }
}
