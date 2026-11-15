using System;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;

// Claude との橋渡し（画面を触らずに確認するための仕組み）。
//  プロジェクト直下の ClaudeBridge/cmd.txt に書かれた指示を1秒ごとに読んで実行し、
//  結果とエラーを ClaudeBridge/log.txt・status.txt に書く。ゲームの中身には関わらない。
//  指示の例： "12 refresh" / "13 open Assets/IBProto/Scenes/Iai.unity" / "14 play" / "15 stop" / "16 shot name" / "17 menu IBProto/..." / "14 play auto"（自動プレイで再生）
[InitializeOnLoad]
public static class IBProtoBridge
{
    static readonly string DIR = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ClaudeBridge");
    static string Cmd => Path.Combine(DIR, "cmd.txt");
    static string LogPath => Path.Combine(DIR, "log.txt");
    static string Status => Path.Combine(DIR, "status.txt");
    static double next; static string lastId;

    static IBProtoBridge()
    {
        try { Directory.CreateDirectory(DIR); Directory.CreateDirectory(Path.Combine(DIR, "shots")); } catch { }
        lastId = SessionState.GetString("IBProtoBridge.lastId", "");
        Application.logMessageReceivedThreaded += OnLog;
        CompilationPipeline.assemblyCompilationFinished += OnCompiled;
        EditorApplication.update += Tick;
        Write("loaded");
    }

    static void Append(string s)
    {
        try { File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss ") + s + "\n"); } catch { }
    }

    static void OnLog(string msg, string stack, LogType type)
    {
        if (type == LogType.Log && !msg.StartsWith("[I")) return;
        if (type == LogType.Warning) return;
        string st = (type == LogType.Error || type == LogType.Exception) && stack != null ? " | " + stack.Replace("\n", " / ") : "";
        if (st.Length > 600) st = st.Substring(0, 600);
        Append(type + ": " + msg + st);
    }

    static void OnCompiled(string asm, CompilerMessage[] msgs)
    {
        int err = 0;
        foreach (var m in msgs) if (m.type == CompilerMessageType.Error) { err++; Append("COMPILE ERROR: " + m.message); }
        Append("compiled " + Path.GetFileName(asm) + " errors=" + err);
    }

    static void Write(string note)
    {
        try
        {
            File.WriteAllText(Status, "time=" + DateTime.Now.ToString("HH:mm:ss") + "\nlast=" + lastId + "\nnote=" + note +
                "\nplaying=" + EditorApplication.isPlaying + "\ncompiling=" + EditorApplication.isCompiling +
                "\nscene=" + EditorSceneManager.GetActiveScene().path + "\n");
        }
        catch { }
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1.0;
        string line;
        try { if (!File.Exists(Cmd)) return; line = File.ReadAllText(Cmd).Trim(); } catch { return; }
        if (line.Length == 0) return;
        int sp = line.IndexOf(' ');
        if (sp < 0) return;
        string id = line.Substring(0, sp), rest = line.Substring(sp + 1).Trim();
        if (id == lastId) return;
        lastId = id; SessionState.SetString("IBProtoBridge.lastId", id);
        string cmd = rest, arg = "";
        int sp2 = rest.IndexOf(' ');
        if (sp2 > 0) { cmd = rest.Substring(0, sp2); arg = rest.Substring(sp2 + 1).Trim(); }
        Append("> " + line);
        try { Run(cmd, arg); Write("done " + rest); }
        catch (Exception e) { Append("BRIDGE ERROR: " + e); Write("error " + rest); }
    }

    static void Run(string cmd, string arg)
    {
        switch (cmd)
        {
            case "refresh": AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate); break;
            case "open":
                if (EditorApplication.isPlaying) { Append("cannot open while playing"); break; }
                EditorSceneManager.SaveOpenScenes();
                EditorSceneManager.OpenScene(arg);
                break;
            case "play": SessionState.SetBool("IBProtoBridge.auto", arg == "auto"); EditorApplication.isPlaying = true; break;
            case "stop": SessionState.SetBool("IBProtoBridge.auto", false); EditorApplication.isPlaying = false; break;
            case "menu": Append("menu " + arg + " -> " + EditorApplication.ExecuteMenuItem(arg)); break;
            case "shot":
                ScreenCapture.CaptureScreenshot(Path.Combine(DIR, "shots", arg + ".png"));
                break;
            case "timescale": Time.timeScale = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture); break;
            case "ping": break;
            default: Append("unknown command " + cmd); break;
        }
    }

}
