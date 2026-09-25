using System.Collections.Generic;
using System.Text;

namespace HKCustomSceneMod
{
    /// <summary>
    /// 极简 JSON 读取器：只做一件事 —— 把**一层对象**（键是字符串，值是字符串/数字/true/false/null）
    /// 读成字典。**刻意不依赖任何第三方库**（游戏里不一定带，别赌；Unity 自带的 JsonUtility 又只认固定类型）。
    ///
    /// 比标准 JSON **宽松**的地方（都是为了让手写配置文件不容易写坏）：
    ///   · 允许 `//` 行注释 与 `/* */` 块注释；
    ///   · 允许最后一项后面多一个逗号；
    ///   · 允许文件开头有 UTF-8 BOM；
    ///   · 键**不加引号**也认（`entryDream: "…"`）。
    /// 值一律按"原样文本"收下：字符串去引号并解转义，数字/true/false 原样保留，null 变成空串。
    /// 出错时返回 false，并把「第几行第几列 + 原因」写进 error（给玩家看的，所以是中文）。
    /// </summary>
    internal static class JsonMini
    {
        internal static bool TryParseFlatObject(string json, out Dictionary<string, string> map, out string error)
        {
            map = new Dictionary<string, string>();
            error = null;
            if (json == null) { error = "内容为空"; return false; }

            int i = 0;
            if (json.Length > 0 && json[0] == '\uFEFF') i++;   // 记事本另存可能带 BOM

            SkipWs(json, ref i);
            if (!Take(json, ref i, '{'))
            {
                error = Where(json, i) + "开头必须是 '{'";
                return false;
            }

            while (true)
            {
                SkipWs(json, ref i);
                if (i >= json.Length) { error = Where(json, i) + "文件到这里就结束了，少一个 '}'"; return false; }
                if (json[i] == '}') { i++; break; }
                if (json[i] == ',') { i++; continue; }   // 容忍多余的逗号

                string key;
                if (json[i] == '"')
                {
                    if (!ReadString(json, ref i, out key, out error)) return false;
                }
                else if (!ReadBareKey(json, ref i, out key))
                {
                    error = Where(json, i) + "这里应该是键名（建议写成 \"键\": 值）";
                    return false;
                }
                if (key.Length == 0) { error = Where(json, i) + "键名是空的"; return false; }

                SkipWs(json, ref i);
                if (!Take(json, ref i, ':'))
                {
                    error = Where(json, i) + "键「" + key + "」后面少了 ':'";
                    return false;
                }
                SkipWs(json, ref i);

                string value;
                if (!ReadValue(json, ref i, out value, out error)) return false;
                map[key] = value;   // 重复键：后面的赢
            }

            SkipWs(json, ref i);
            if (i < json.Length)
            {
                error = Where(json, i) + "'}' 后面还有多余的内容";
                return false;
            }
            return true;
        }

        // ────────────────────────────────────────────────────────────────
        //  内部
        // ────────────────────────────────────────────────────────────────

        /// <summary>跳过空白与注释。</summary>
        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; continue; }

                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    i += 2;
                    while (i < s.Length && s[i] != '\n') i++;
                    continue;
                }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
                    i = (i + 1 < s.Length) ? i + 2 : s.Length;
                    continue;
                }
                break;
            }
        }

        private static bool Take(string s, ref int i, char c)
        {
            if (i < s.Length && s[i] == c) { i++; return true; }
            return false;
        }

        /// <summary>不加引号的键：一直读到空白 / 冒号 / 逗号 / 大括号 / 引号为止（中文键名也认）。</summary>
        private static bool ReadBareKey(string s, ref int i, out string key)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == ':' || c == ',' ||
                    c == '}' || c == '{' || c == '"') break;
                i++;
            }
            key = s.Substring(start, i - start);
            return key.Length > 0;
        }

        private static bool ReadString(string s, ref int i, out string value, out string error)
        {
            value = null;
            error = null;
            i++;   // 跳过开头的引号（调用方保证 s[i] == '"'）
            StringBuilder sb = new StringBuilder();

            while (true)
            {
                if (i >= s.Length) { error = Where(s, i) + "字符串少了收尾的双引号"; return false; }
                char c = s[i++];
                if (c == '"') { value = sb.ToString(); return true; }
                if (c != '\\') { sb.Append(c); continue; }

                if (i >= s.Length) { error = Where(s, i) + "反斜杠后面什么都没有"; return false; }
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        int code;
                        if (!TryHex4(s, i, out code))
                        {
                            error = Where(s, i) + "\\u 后面要跟 4 位十六进制（如 \\u4e2d）";
                            return false;
                        }
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default:
                        error = Where(s, i - 1) + "不认识的转义「\\" + e + "」（想写字面反斜杠要写 \\\\）";
                        return false;
                }
            }
        }

        private static bool ReadValue(string s, ref int i, out string value, out string error)
        {
            value = null;
            error = null;
            if (i >= s.Length) { error = Where(s, i) + "少了值"; return false; }

            char c = s[i];
            if (c == '"') return ReadString(s, ref i, out value, out error);
            if (c == '{' || c == '[')
            {
                error = Where(s, i) + "值只支持 字符串 / 数字 / true / false / null —— " +
                        "本配置是「一层」结构，不支持嵌套对象或数组";
                return false;
            }

            int start = i;
            while (i < s.Length)
            {
                char d = s[i];
                if (d == ',' || d == '}' || d == ' ' || d == '\t' || d == '\r' || d == '\n' || d == '/') break;
                i++;
            }
            string raw = s.Substring(start, i - start);
            if (raw.Length == 0) { error = Where(s, i) + "这里是空值"; return false; }

            value = raw == "null" ? "" : raw;   // 数字/true/false 原样留着
            return true;
        }

        private static bool TryHex4(string s, int i, out int code)
        {
            code = 0;
            if (i + 4 > s.Length) return false;
            for (int k = 0; k < 4; k++)
            {
                int d = HexDigit(s[i + k]);
                if (d < 0) return false;
                code = code * 16 + d;
            }
            return true;
        }

        private static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        /// <summary>「第 N 行第 M 列：」——玩家看到的报错要指得到地方。</summary>
        private static string Where(string s, int i)
        {
            int line = 1, col = 1;
            int end = i < s.Length ? i : s.Length;
            for (int k = 0; k < end; k++)
            {
                if (s[k] == '\n') { line++; col = 1; }
                else col++;
            }
            return "第 " + line + " 行第 " + col + " 列：";
        }
    }
}
