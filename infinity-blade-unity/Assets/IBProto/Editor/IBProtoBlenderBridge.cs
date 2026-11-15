using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Unity ⇄ Blender の受け渡し
// メニュー「IBProto > Blender > キャラをFBXで書き出す」
//   武士（SamuraiCharacter）と王（EnemyKing）を、プロジェクト直下の Blender/Export に FBX で書き出す。
//   使っているテクスチャも Blender/Export/Textures にコピーする。
public static class IBProtoBlenderBridge
{
    static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Blender"));

    [MenuItem("IBProto/Blender/キャラをFBXで書き出す")]
    public static void ExportAll()
    {
        var exporter = Type.GetType("UnityEditor.Formats.Fbx.Exporter.ModelExporter, Unity.Formats.Fbx.Editor");
        if (exporter == null) { Debug.LogError("[IBProto] FBX Exporter がまだ入っていません。少し待ってからもう一度実行してください"); return; }
        // Blender は ASCII 形式の FBX を読めないので、バイナリ形式で書き出す
        var optType = Type.GetType("UnityEditor.Formats.Fbx.Exporter.ExportModelOptions, Unity.Formats.Fbx.Editor");
        var fmtType = Type.GetType("UnityEditor.Formats.Fbx.Exporter.ExportFormat, Unity.Formats.Fbx.Editor");
        object opts = null;
        if (optType != null && fmtType != null)
        {
            opts = Activator.CreateInstance(optType);
            optType.GetProperty("ExportFormat")?.SetValue(opts, Enum.Parse(fmtType, "Binary"));
        }
        System.Reflection.MethodInfo export = null;
        foreach (var m in exporter.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (m.Name != "ExportObject") continue;
            var ps = m.GetParameters();
            if (ps.Length == 3 && ps[0].ParameterType == typeof(string) && opts != null && ps[2].ParameterType.IsAssignableFrom(optType)) { export = m; break; }
            if (ps.Length == 2 && ps[0].ParameterType == typeof(string) && export == null) export = m;
        }
        if (export == null) { Debug.LogError("[IBProto] ExportObject が見つからない"); return; }
        Debug.Log("[IBProto] FBX 書き出し: " + (export.GetParameters().Length == 3 ? "バイナリ" : "既定（ASCII の可能性）"));

        string dir = Path.Combine(Root, "Export"), tex = Path.Combine(dir, "Textures");
        Directory.CreateDirectory(tex);
        int n = 0;
        foreach (var name in new[] { "SamuraiCharacter", "EnemyKing" })
        {
            GameObject prefab = null;
            foreach (var id in AssetDatabase.FindAssets(name + " t:Prefab"))
            {
                var p = AssetDatabase.GUIDToAssetPath(id);
                if (Path.GetFileNameWithoutExtension(p) == name) { prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p); break; }
            }
            if (prefab == null) { Debug.LogWarning("[IBProto] 見つからない: " + name); continue; }

            // 一時的に原点に置いて書き出す（大きさはそのまま）
            var inst = (GameObject)Object.Instantiate(prefab);
            inst.name = name;
            inst.transform.SetPositionAndRotation(new Vector3(0, inst.transform.position.y, 0), Quaternion.identity);
            string path = Path.Combine(dir, name + ".fbx");
            try { export.Invoke(null, export.GetParameters().Length == 3 ? new object[] { path, inst, opts } : new object[] { path, inst }); n++; }
            catch (Exception e) { Debug.LogError("[IBProto] 書き出し失敗 " + name + ": " + (e.InnerException ?? e).Message); }

            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !m.HasProperty("_BaseMap")) continue;
                    var t = m.GetTexture("_BaseMap");
                    if (t == null) continue;
                    string src = AssetDatabase.GetAssetPath(t);
                    if (string.IsNullOrEmpty(src)) continue;
                    File.Copy(Path.GetFullPath(src), Path.Combine(tex, Path.GetFileName(src)), true);
                }
            Object.DestroyImmediate(inst);
        }
        Debug.Log($"[IBProto] Blender 用に {n} 体を書き出しました: {dir}");
    }
}
