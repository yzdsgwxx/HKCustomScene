using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace HKCustomSceneMod
{
    /// <summary>
    /// **对话文本配置**：所有给玩家看的文字都从这里来（默认值见 <see cref="DialogueDefaults"/>）。
    ///
    /// 文件 = `DialogueConfig.json`，放在**本 mod 的 DLL 旁边**（`Managed\Mods\CustomScene\`）。
    /// 启动时读一次：
    ///   · 文件不存在 → 按出厂模板写一份出来（这样玩家第一次进游戏就能看到文件、直接改）；
    ///   · 读/解析失败 → 打红字说明"第几行第几列错在哪"，然后用出厂默认值继续跑（**绝不因为配置写坏就崩**）；
    ///   · 某项留空 → 该项退回出厂默认值（出口怪那项留空 ⇒ 用场景里 PatchGhost 组件上填的文本）。
    ///
    /// 改文本**不需要重新打包**：直接编辑那个 json，重启游戏即可。
    /// </summary>
    internal static class DialogueConfig
    {
        /// <summary>① 入口怪的**交互**文本。</summary>
        internal static string EntryInteract = DialogueDefaults.EntryInteract;

        /// <summary>② 入口怪的**梦钉**梦语。</summary>
        internal static string EntryDream = DialogueDefaults.EntryDream;

        /// <summary>
        /// ③ 出口怪的**交互**文本。**null / 空** = 没配置 ⇒ 用场景里那只 PatchGhost 组件上填的
        /// <c>DialogueText</c>（想给每只怪写不同文本时用这个）。
        /// </summary>
        internal static string ExitInteract = null;

        /// <summary>实际用的配置文件完整路径（日志里会打出来；出错时也靠它对账）。</summary>
        internal static string FilePath;

        /// <summary>这次是不是新建了配置文件。</summary>
        internal static bool FileWasCreated;

        /// <summary>读一次。由 <c>CustomSceneMod.Initialize()</c> 调；重复调也安全。</summary>
        internal static void Load()
        {
            // 先全部退回出厂值，再按文件覆盖 —— 这样任何异常路径下都有一份能用的文本
            EntryInteract = DialogueDefaults.EntryInteract;
            EntryDream = DialogueDefaults.EntryDream;
            ExitInteract = DialogueDefaults.ExitInteract;
            FileWasCreated = false;

            FilePath = ResolvePath();
            if (string.IsNullOrEmpty(FilePath))
            {
                Modding.Logger.LogError("[HKCS][对话配置] 找不到 mod 目录（Assembly.Location 为空）⇒ 用内置默认文本");
                LogResolved("(没有配置文件)");
                return;
            }

            try
            {
                if (!File.Exists(FilePath))
                {
                    File.WriteAllText(FilePath, DialogueDefaults.FileText(), new UTF8Encoding(false));
                    FileWasCreated = true;
                    Modding.Logger.Log("[HKCS][对话配置] 首次运行：已生成配置文件\n    " + FilePath +
                        "\n    （改里面的文字 → 存盘 → 重启游戏生效，不用重新打包）");
                }

                string text = File.ReadAllText(FilePath, Encoding.UTF8);

                Dictionary<string, string> map;
                string error;
                if (!JsonMini.TryParseFlatObject(text, out map, out error))
                {
                    Modding.Logger.LogError("[HKCS][对话配置] 解析失败，已改用内置默认文本：" + FilePath +
                        "\n    " + error);
                    LogResolved("(解析失败)");
                    return;
                }

                // 规则见 DialogueDefaults.Resolve（缺失/空 ⇒ 出厂默认；③ 显式写 "" ⇒ 用组件上的文本）
                string ei, ed, ex;
                DialogueDefaults.Resolve(map, out ei, out ed, out ex);
                EntryInteract = ei;
                EntryDream = ed;
                ExitInteract = ex;

                LogResolved(FileWasCreated ? "(本次新建)" : "(已读取)");
            }
            catch (Exception e)
            {
                Modding.Logger.LogError("[HKCS][对话配置] 读取失败，已改用内置默认文本：" + e.Message +
                    "\n    " + FilePath);
                LogResolved("(读取失败)");
            }
        }

        private static void LogResolved(string how)
        {
            Modding.Logger.Log("[HKCS][对话配置] " + how + " " + (FilePath ?? "(无路径)"));
            Modding.Logger.Log("[HKCS][对话配置]   · ① 入口怪交互：「" + EntryInteract + "」");
            Modding.Logger.Log("[HKCS][对话配置]   · ② 入口怪梦语：「" + EntryDream + "」");
            Modding.Logger.Log("[HKCS][对话配置]   · ③ 出口怪交互：" +
                (string.IsNullOrEmpty(ExitInteract)
                    ? "(留空 ⇒ 用场景里 PatchGhost 组件上填的文本)"
                    : "「" + ExitInteract + "」"));
        }

        /// <summary>配置文件路径 = DLL 所在目录 + DialogueConfig.json。</summary>
        private static string ResolvePath()
        {
            try
            {
                string loc = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(loc))
                {
                    string dir = Path.GetDirectoryName(loc);
                    if (!string.IsNullOrEmpty(dir))
                        return Path.Combine(dir, DialogueDefaults.FileName);
                }
            }
            catch (Exception)
            {
                // 落到下面返回 null
            }
            return null;
        }
    }
}
