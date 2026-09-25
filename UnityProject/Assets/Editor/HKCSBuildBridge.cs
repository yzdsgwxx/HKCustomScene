// HKCSBuildBridge.cs —— 编辑器内的「命令桥」
//
// 干什么：Unity 打开着本工程时，这个脚本每 0.4 秒看一眼
//         <仓库根>\tools\.bridge\cmd.txt。看到命令就执行，然后把结果写到
//         <仓库根>\tools\.bridge\result.<id>.txt。
//
// 为什么要它：tools\打包测试.ps1 需要「运行打包」（菜单 HKModCustomSceneTool → Build AssetBundles Compressed）。
//             Unity 不允许第二个实例打开同一个工程，所以不能一边开着编辑器一边用
//             -batchmode 打包；而 Unity 也没有给外部程序留「点菜单」的接口。
//             于是让编辑器自己轮询一个命令文件，就能做到「不关 Unity、不用手点」。
//
// 支持的命令（cmd.txt 是 key=value 文本，一行一个）：
//   ping            —— 探活，脚本用它判断桥是否已生效
//   build_bundles   —— 保存打开的场景 + AssetDatabase.SaveAssets + BuildPipeline.BuildAssetBundles
//                      （BuildAssetBundleOptions.None = 和菜单 "HKModCustomSceneTool → Build AssetBundles Compressed" 完全一致）
//   quit            —— 保存打开的场景后正常退出编辑器（供「更新壳工程」重启 Unity 用）
//
// 手工测试：把 cmd.txt 写成下面两行，然后点一下 Unity 窗口：
//   id=test1
//   cmd=ping
// 结果会出现在 tools\.bridge\result.test1.txt。
//
// 注意：命令文件放在 Assets 之外（tools\.bridge），否则每写一次都会触发 Unity 重新导入资源。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

[InitializeOnLoad]
public static class HKCSBuildBridge
{
    /// <summary>命令目录的绝对路径（写在这里，路径里所有 \ 都要双写）。</summary>
    private const string BridgeDirOverride = "";
    /// <summary>命令文件超过这个年龄就丢弃（防止 Unity 下次启动时执行一条很旧的命令，比如 quit）。</summary>
    private static readonly TimeSpan MaxCommandAge = TimeSpan.FromMinutes(5);
    private const double PollIntervalSeconds = 0.4;

    private static double _nextPollTime;
    private static string _bridgeDir;

    static HKCSBuildBridge()
    {
        EditorApplication.update += Tick;
        string dir = ResolveBridgeDir();
        Debug.Log("[HKCS-Bridge] 已加载。命令目录 = " + (dir == null ? "<没找到仓库根目录>" : dir));
    }

    // ---------------------------------------------------------------- 路径

    private static string ResolveBridgeDir()
    {
        if (!string.IsNullOrEmpty(_bridgeDir) && Directory.Exists(_bridgeDir))
        {
            return _bridgeDir;
        }

        string candidates = null;
        try
        {
            if (!string.IsNullOrEmpty(BridgeDirOverride))
            {
                candidates = BridgeDirOverride;
            }
            else
            {
                // 从 Assets 往上找：哪个目录同时有 FinalMod\ 和 tools\，那里就是仓库根。
                DirectoryInfo d = new DirectoryInfo(Application.dataPath);
                for (int i = 0; i < 5 && d != null; i++)
                {
                    if (Directory.Exists(Path.Combine(d.FullName, "FinalMod")) &&
                        Directory.Exists(Path.Combine(d.FullName, "tools")))
                    {
                        candidates = Path.Combine(Path.Combine(d.FullName, "tools"), ".bridge");
                        break;
                    }
                    d = d.Parent;
                }
            }

            if (!string.IsNullOrEmpty(candidates))
            {
                Directory.CreateDirectory(candidates);
                _bridgeDir = candidates;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[HKCS-Bridge] 定位命令目录失败：" + e.Message);
        }

        return _bridgeDir;
    }

    // ---------------------------------------------------------------- 轮询

    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < _nextPollTime)
        {
            return;
        }
        _nextPollTime = EditorApplication.timeSinceStartup + PollIntervalSeconds;

        string dir = ResolveBridgeDir();
        if (dir == null)
        {
            return;
        }

        string cmdPath = Path.Combine(dir, "cmd.txt");
        if (!File.Exists(cmdPath))
        {
            return;
        }

        string text;
        try
        {
            text = File.ReadAllText(cmdPath, Encoding.UTF8);
        }
        catch (Exception)
        {
            return; // 可能正被别的进程写，下一轮再看
        }

        // 先删掉命令文件，保证只执行一次
        try
        {
            File.Delete(cmdPath);
        }
        catch (Exception)
        {
            // 删不掉也没关系，下面的 id 去重靠调用方保证
        }

        Dictionary<string, string> kv = ParseLines(text);
        string id;
        if (!kv.TryGetValue("id", out id) || string.IsNullOrEmpty(id))
        {
            return;
        }

        string cmd;
        if (!kv.TryGetValue("cmd", out cmd) || string.IsNullOrEmpty(cmd))
        {
            WriteResult(dir, id, "cmd=<空>", false, "命令文件缺少 cmd=", new List<string>());
            return;
        }

        string ts;
        if (kv.TryGetValue("ts", out ts))
        {
            DateTime stamp;
            if (DateTime.TryParse(ts, out stamp) && DateTime.Now - stamp > MaxCommandAge)
            {
                WriteResult(dir, id, cmd, false, "命令太旧了（" + stamp + "），已忽略", new List<string>());
                return;
            }
        }

        Execute(dir, id, cmd);
    }

    private static void Execute(string dir, string id, string cmd)
    {
        List<string> files = new List<string>();
        bool ok = true;
        string message;

        try
        {
            if (cmd == "ping")
            {
                message = "pong（Unity " + Application.unityVersion + "）";
            }
            else if (cmd == "build_bundles")
            {
                message = BuildBundles(files);
            }
            else if (cmd == "quit")
            {
                message = "保存场景后退出编辑器";
            }
            else
            {
                ok = false;
                message = "未知命令：" + cmd;
            }
        }
        catch (Exception e)
        {
            ok = false;
            message = e.GetType().Name + ": " + e.Message;
            Debug.LogError("[HKCS-Bridge] " + cmd + " 失败：" + e);
        }

        Debug.Log("[HKCS-Bridge] " + cmd + " → " + (ok ? "OK" : "失败") + "：" + message);
        WriteResult(dir, id, cmd, ok, message, files);

        if (cmd == "quit")
        {
            try
            {
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.isPlaying = false;
                }
                EditorSceneManager.SaveOpenScenes();
                AssetDatabase.SaveAssets();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HKCS-Bridge] 退出前保存失败：" + e.Message);
            }
            EditorApplication.Exit(0);
        }
    }

    // ---------------------------------------------------------------- 命令实现

    private static string BuildBundles(List<string> files)
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[HKCS-Bridge] 当前处于 Play 模式，仍然按 Edit 模式的内容打包。");
        }

        // 场景没存盘的话，打进包里的就是磁盘上的旧内容 → 先存盘。
        bool saved = EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        const string relativeOutDir = "Assets/AssetBundles";
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string absOutDir = Path.Combine(Path.Combine(projectRoot, "Assets"), "AssetBundles");
        if (!Directory.Exists(absOutDir))
        {
            Directory.CreateDirectory(absOutDir);
        }

        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
            relativeOutDir,
            BuildAssetBundleOptions.None, // 与菜单 "Build AssetBundles Compressed" 完全一致
            BuildTarget.StandaloneWindows64);

        if (manifest == null)
        {
            throw new Exception("BuildPipeline.BuildAssetBundles 返回 null —— 请看 Unity Console 里的具体报错");
        }

        string[] names = manifest.GetAllAssetBundles();
        for (int i = 0; i < names.Length; i++)
        {
            string full = Path.Combine(absOutDir, names[i]);
            if (File.Exists(full))
            {
                FileInfo fi = new FileInfo(full);
                files.Add(fi.FullName + "|" + fi.Length);
            }
        }

        AssetDatabase.Refresh();

        string sizeNote = files.Count > 0 ? "" : "（⚠ 没有任何包产出：场景/资源的 AssetBundle 名可能没填）";
        return "打包完成，共 " + names.Length + " 个包：" + string.Join(", ", names) + sizeNote +
               (saved ? "；打开的场景已保存" : "；");
    }

    // ---------------------------------------------------------------- 小工具

    private static Dictionary<string, string> ParseLines(string text)
    {
        Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line == null)
            {
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq < 1)
            {
                continue;
            }
            string key = line.Substring(0, eq).Trim();
            string value = line.Substring(eq + 1).Trim();
            if (key.Length > 0)
            {
                result[key] = value;
            }
        }
        return result;
    }

    private static void WriteResult(string dir, string id, string cmd, bool ok, string message, List<string> files)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("id=").Append(id).Append("\r\n");
        sb.Append("cmd=").Append(cmd).Append("\r\n");
        sb.Append("ok=").Append(ok ? "1" : "0").Append("\r\n");
        sb.Append("message=").Append(OneLine(message)).Append("\r\n");
        sb.Append("files=").Append(string.Join(";", files.ToArray())).Append("\r\n");
        sb.Append("unity=").Append(Application.unityVersion).Append("\r\n");
        sb.Append("project=").Append(Application.dataPath).Append("\r\n");
        sb.Append("pid=").Append(Process.GetCurrentProcess().Id).Append("\r\n");
        sb.Append("done=").Append(DateTime.Now.ToString("o")).Append("\r\n");

        string path = Path.Combine(dir, "result." + id + ".txt");
        string tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(tmp, path);
        }
        catch (Exception e)
        {
            Debug.LogError("[HKCS-Bridge] 写结果文件失败：" + path + " —— " + e.Message);
        }
    }

    /// <summary>结果文件是 key=value 行格式，值里不能有换行。</summary>
    private static string OneLine(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "";
        }
        return s.Replace("\r", " ").Replace("\n", " ");
    }
}
