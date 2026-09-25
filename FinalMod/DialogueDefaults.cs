using System.Collections.Generic;
using System.Text;

namespace HKCustomSceneMod
{
    /// <summary>
    /// 对话文本的**出厂默认值** + `DialogueConfig.json` 的**模板生成器**。
    ///
    /// 这里是"唯一的一份默认文本"：代码里缺文件时用 <see cref="FileText"/> 生成配置，
    /// 配置里某项留空时回退到这几个常量。仓库根目录那份 `DialogueConfig.json` 就是
    /// <see cref="FileText"/> 的输出（有单元测试逐字节比对，见 `tests/DialogueConfigTests`）。
    /// </summary>
    internal static class DialogueDefaults
    {
        // ── 当前的三句（就是改这个文件之前写死在代码里的那三句）──

        /// <summary>① 入口怪（德特茅斯那只）：**交互**（走到跟前按上/下）时的文本。</summary>
        internal const string EntryInteract = "不要打扰我，“否则我就送你去地狱。”";

        /// <summary>② 入口怪：**梦钉**抽它时的梦语。</summary>
        internal const string EntryDream = "既然你这么不知好歹，那就去地狱吧！";

        /// <summary>③ 出口怪（迷宫里摆的那只）：**交互**对话的文本。</summary>
        internal const string ExitInteract = "虽然你亵渎了我，但你的勇敢征服了我，我原谅你了，回去吧";

        // ── 配置键名（改键名 = 改配置文件格式，别乱动）──
        internal const string KeyEntryInteract = "entryInteract";
        internal const string KeyEntryDream = "entryDream";
        internal const string KeyExitInteract = "exitInteract";

        /// <summary>配置文件名（运行时放在 mod 目录里，也就是 DLL 旁边）。</summary>
        internal const string FileName = "DialogueConfig.json";

        /// <summary>出厂配置文件全文（缺文件时按这个写出来）。</summary>
        internal static string FileText()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  // ============================================================\n");
            sb.Append("  //  HKCS 对话文本配置（HK Custom Scene Mod）\n");
            sb.Append("  //\n");
            sb.Append("  //  · 改完**存盘 → 重启游戏**就生效，**不用重新打包**（也不要在游戏里改）。\n");
            sb.Append("  //  · 本文件位置 = 本 mod 的 DLL 旁边：\n");
            sb.Append("  //      <游戏>\\hollow_knight_Data\\Managed\\Mods\\CustomScene\\DialogueConfig.json\n");
            sb.Append("  //  · 值留空 \"\" = 这一项不配置，用出厂默认值；出口怪那项留空 = 用场景里\n");
            sb.Append("  //    PatchGhost 组件上填的 DialogueText（想给每只怪写不同文本时就这么用）。\n");
            sb.Append("  //  · 允许 // 行注释、/* */ 块注释，也允许最后一项后面多一个逗号。\n");
            sb.Append("  //  · 文本里的双引号写成 \\\" ，换行写成 \\n。\n");
            sb.Append("  // ============================================================\n");
            sb.Append("\n");
            sb.Append("  // ① 入口怪（德特茅斯掘墓者旁边那只；代码写死，编辑器里不用摆）\n");
            sb.Append("  //    走到跟前**按上/下交互**时显示的文本\n");
            sb.Append("  \"" + KeyEntryInteract + "\": " + Q(EntryInteract) + ",\n");
            sb.Append("\n");
            sb.Append("  // ② 入口怪 —— 用**梦钉**抽它时显示的梦语\n");
            sb.Append("  \"" + KeyEntryDream + "\": " + Q(EntryDream) + ",\n");
            sb.Append("\n");
            sb.Append("  // ③ 出口怪（你在迷宫里摆的那只 PatchGhost）—— **交互对话**的文本\n");
            sb.Append("  //    （对话一收起就死亡动画 → 在德特茅斯椅子上醒来）\n");
            sb.Append("  \"" + KeyExitInteract + "\": " + Q(ExitInteract) + "\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>把一段文本安全地塞进 JSON 字符串（引号/反斜杠/控制字符都转义）。</summary>
        internal static string Q(string s)
        {
            if (s == null) return "\"\"";
            StringBuilder sb = new StringBuilder("\"");
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        /// <summary>
        /// 从**解析好的配置**算出最终三句话（纯函数，方便单元测试）。
        /// 规则：
        ///   · 键**缺失** 或值写空 ⇒ 用出厂默认（①②③ 都是）；
        ///   · 只有 ③ 出口那句**显式写成 `""`** 才算"不配置这个" ⇒ 返回 null
        ///     （意思是"用场景里那只 PatchGhost 组件上填的 DialogueText"，想给每只怪写不同文本时用）。
        /// </summary>
        internal static void Resolve(IDictionary<string, string> map,
                                     out string entryInteract, out string entryDream, out string exitInteractOrNull)
        {
            entryInteract = Pick(map, KeyEntryInteract, EntryInteract);
            entryDream = Pick(map, KeyEntryDream, EntryDream);

            exitInteractOrNull = ExitInteract;   // 键缺失 ⇒ 出厂默认那句
            string v;
            if (map != null && map.TryGetValue(KeyExitInteract, out v))
                exitInteractOrNull = string.IsNullOrEmpty(v) ? null : v;
        }

        private static string Pick(IDictionary<string, string> map, string key, string fallback)
        {
            string v;
            if (map != null && map.TryGetValue(key, out v) && !string.IsNullOrEmpty(v)) return v;
            return fallback;
        }
    }
}
