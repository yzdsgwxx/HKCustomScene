// HKCSMenu.cs —— 把 tools\ 里那条自动化流程挂到 Unity 菜单栏上
//
// 菜单：
//   HKModCustomSceneTool
//     ├─ 打包测试                        ← 本文件
//     ├─ Build AssetBundles Compressed   ← CreateAssetBundles.cs
//     └─ Build AssetBundles Uncompressed
//
// 「打包测试」做的是 tools\打包测试.cmd 那一整套：
//   打包 AssetBundle → 拷进 FinalMod\Resources → dotnet build FinalMod
//   → 装进空洞骑士 Mods\CustomScene → 重启游戏
//
// 实现方式：直接启动那个 .cmd（会弹一个控制台窗口，脚本跑完自己关；完整日志在 tools\_log\）。
// 不用把逻辑搬进 C# —— 因为流程里有 dotnet 编译和游戏开关，放在脚本里更好维护，
// 而且 Unity 里跑批处理会让编辑器卡住（编译期间不能响应 bridge 的打包命令）。
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
// System.Diagnostics 也有 Debug / Process，会和 UnityEngine 的撞名（CS0104）⇒ 显式取别名
using Debug = UnityEngine.Debug;

public static class HKCSMenu
{
    private const string MenuRoot = "HKModCustomSceneTool/";

    [MenuItem(MenuRoot + "打包测试", false, 1)]
    private static void RunPackAndTest()
    {
        // Assets/Editor → Assets → HKModCustomScene → UnityProject → 仓库根
        string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
        string toolsDir = Path.Combine(repoRoot, "tools");
        string cmdPath = Path.Combine(toolsDir, "打包测试.cmd");
        string ps1Path = Path.Combine(toolsDir, "打包测试.ps1");

        if (File.Exists(cmdPath))
        {
            Process.Start(new ProcessStartInfo(cmdPath)
            {
                WorkingDirectory = toolsDir,
                UseShellExecute = true,      // 让它自己弹控制台窗口（脚本跑完会自己关）
            });
            Debug.Log("[HKCS] 已启动「打包测试」：" + cmdPath + "\n（窗口跑完自动关；完整日志在 tools\\_log\\）");
            return;
        }

        if (File.Exists(ps1Path))
        {
            Process.Start(new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -File \"" + ps1Path + "\"")
            {
                WorkingDirectory = toolsDir,
                UseShellExecute = true,
            });
            Debug.Log("[HKCS] 已启动「打包测试」：" + ps1Path);
            return;
        }

        Debug.LogError("[HKCS] 找不到打包测试脚本。预期在：" + cmdPath);
    }

    [MenuItem(MenuRoot + "打开 tools 目录", false, 40)]
    private static void OpenToolsDir()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
        string toolsDir = Path.Combine(repoRoot, "tools");
        if (Directory.Exists(toolsDir)) EditorUtility.RevealInFinder(toolsDir);
        else Debug.LogError("[HKCS] 找不到目录：" + toolsDir);
    }

    [MenuItem(MenuRoot + "打开日志目录（tools\\_log）", false, 41)]
    private static void OpenLogDir()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
        string logDir = Path.Combine(Path.Combine(repoRoot, "tools"), "_log");
        if (Directory.Exists(logDir)) EditorUtility.RevealInFinder(logDir);
        else Debug.LogWarning("[HKCS] 还没有日志目录（跑过一次打包测试才会有）：" + logDir);
    }
}
