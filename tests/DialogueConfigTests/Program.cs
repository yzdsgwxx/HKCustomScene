using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HKCustomSceneMod;

/// <summary>
/// DialogueConfig 的纯逻辑测试 + 仓库根目录 DialogueConfig.json 的**生成与一致性校验**。
/// 用法：dotnet run --project tests\DialogueConfigTests
///   退出码 0 = 全过；1 = 有失败（每一条失败都带原因）。
/// 不带参数时会按"往上找含 FinalMod 目录的那一层"定位仓库根。
/// </summary>
internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        string repoRoot = args.Length > 0 ? args[0] : FindRepoRoot();
        Console.WriteLine("仓库根：" + repoRoot);
        Console.WriteLine();

        TestParserHappyPath();
        TestParserCommentsAndBom();
        TestParserEscapes();
        TestParserNumbersAndNull();
        TestParserErrors();
        TestDefaultsAndResolve();
        TestRepoConfigFile(repoRoot);
        TestScriptEncoding(repoRoot);

        Console.WriteLine();
        Console.WriteLine("通过 " + _passed + " 条，失败 " + _failed + " 条。");
        return _failed == 0 ? 0 : 1;
    }

    // ────────────────────────────────────────────────────────────────
    //  用例
    // ────────────────────────────────────────────────────────────────

    private static void TestParserHappyPath()
    {
        var m = Parse("{\"a\": \"1\", \"b\": \"中文\"}");
        Eq("普通对象 a", "1", m["a"]);
        Eq("普通对象 b", "中文", m["b"]);
    }

    private static void TestParserCommentsAndBom()
    {
        string json = "\uFEFF{\n" +
                      "  // 行注释\n" +
                      "  \"a\": \"1\",   // 行尾注释\n" +
                      "  /* 块注释\n" +
                      "     还能换行 */\n" +
                      "  b: \"2\",\n" +          // 键不加引号
                      "  \"c\": \"3\",\n" +      // 尾逗号
                      "}";
        var m = Parse(json);
        Eq("带 BOM/注释/裸键 a", "1", m["a"]);
        Eq("带 BOM/注释/裸键 b", "2", m["b"]);
        Eq("带 BOM/注释/裸键 c", "3", m["c"]);
    }

    private static void TestParserEscapes()
    {
        var m = Parse("{\"q\": \"他说\\\"你好\\\"\", \"bs\": \"a\\\\b\", \"nl\": \"x\\ny\", \"u\": \"\\u4e2d\\u6587\"}");
        Eq("转义的双引号", "他说\"你好\"", m["q"]);
        Eq("转义的反斜杠", "a\\b", m["bs"]);
        Eq("转义的换行", "x\ny", m["nl"]);
        Eq("\\uXXXX 中文", "中文", m["u"]);
    }

    private static void TestParserNumbersAndNull()
    {
        var m = Parse("{\"n\": 123, \"f\": 1.5, \"t\": true, \"z\": null}");
        Eq("数字", "123", m["n"]);
        Eq("小数", "1.5", m["f"]);
        Eq("true", "true", m["t"]);
        Eq("null → 空串", "", m["z"]);
    }

    private static void TestParserErrors()
    {
        Fail("嵌套对象要报错", "{ \"a\": { \"b\": \"1\" } }", "不支持嵌套");
        Fail("嵌套数组要报错", "{ \"a\": [1] }", "不支持嵌套");
        Fail("少冒号要报错", "{ \"a\" \"1\" }", "少了 ':'");
        Fail("字符串没闭合要报错", "{ \"a\": \"1 }", "收尾的双引号");
        Fail("不认识的转义要报错", "{ \"a\": \"\\q\" }", "不认识的转义");
        Fail("后面有多余内容要报错", "{ \"a\": \"1\" } xxx", "多余的内容");
        Fail("少右括号要报错", "{ \"a\": \"1\"", "少一个 '}'");
        Fail("报错要带行列号", "{ \"a\" \"1\" }", "第 1 行第");
    }

    private static void TestDefaultsAndResolve()
    {
        // 出厂模板本身必须先能解析，而且解析出来正好等于三个默认值
        var def = Parse(DialogueDefaults.FileText());
        string ei, ed, ex;
        DialogueDefaults.Resolve(def, out ei, out ed, out ex);
        Eq("模板 → ① 入口交互", DialogueDefaults.EntryInteract, ei);
        Eq("模板 → ② 入口梦语", DialogueDefaults.EntryDream, ed);
        Eq("模板 → ③ 出口交互", DialogueDefaults.ExitInteract, ex);

        // 键缺失 ⇒ 出厂默认
        DialogueDefaults.Resolve(Parse("{}"), out ei, out ed, out ex);
        Eq("空配置 → ①", DialogueDefaults.EntryInteract, ei);
        Eq("空配置 → ②", DialogueDefaults.EntryDream, ed);
        Eq("空配置 → ③", DialogueDefaults.ExitInteract, ex);

        // ① ② 写空 ⇒ 也回退默认；③ 写空 ⇒ null（= 用场景里组件上填的）
        DialogueDefaults.Resolve(Parse("{\"entryInteract\": \"\", \"entryDream\": \"\", \"exitInteract\": \"\"}"),
            out ei, out ed, out ex);
        Eq("① 留空 → 默认", DialogueDefaults.EntryInteract, ei);
        Eq("② 留空 → 默认", DialogueDefaults.EntryDream, ed);
        Eq("③ 留空 → null", null, ex);

        // 自定义值原样生效（含中文标点）
        DialogueDefaults.Resolve(Parse("{\"entryInteract\": \"甲\", \"entryDream\": \"乙\", \"exitInteract\": \"丙\"}"),
            out ei, out ed, out ex);
        Eq("① 自定义", "甲", ei);
        Eq("② 自定义", "乙", ed);
        Eq("③ 自定义", "丙", ex);

        // 生成器本身要能正确转义
        Eq("Q() 转义引号", "\"a\\\"b\"", DialogueDefaults.Q("a\"b"));
        Eq("Q() 转义反斜杠", "\"a\\\\b\"", DialogueDefaults.Q("a\\b"));
        Eq("Q() 转义换行", "\"a\\nb\"", DialogueDefaults.Q("a\nb"));
    }

    /// <summary>仓库根目录那份 DialogueConfig.json：不存在就生成，存在就必须和模板逐字节一致。</summary>
    private static void TestRepoConfigFile(string repoRoot)
    {
        if (repoRoot == null)
        {
            Fail("找不到仓库根（往上没有含 FinalMod 的目录）", "", "");
            return;
        }

        string path = Path.Combine(repoRoot, DialogueConfigFileName);
        string template = DialogueDefaults.FileText();

        if (!File.Exists(path))
        {
            File.WriteAllText(path, template, new UTF8Encoding(false));
            Ok("仓库里没有 " + DialogueConfigFileName + " ⇒ 已按模板生成：" + path);
            return;
        }

        string onDisk = File.ReadAllText(path, Encoding.UTF8);

        // ⚠ 这份文件是**给人改的**（用户会自己填词、删注释、留空），所以**不要求**和模板一致，
        //   只校验：能解析、三个键都在、没拼错的键 —— 拼错的键在游戏里是静默无效的，最难查。
        Dictionary<string, string> map;
        string error;
        if (!JsonMini.TryParseFlatObject(onDisk, out map, out error))
        {
            _failed++;
            Console.WriteLine("  [FAIL] 仓库 " + DialogueConfigFileName + " 解析失败：" + error);
            return;
        }
        Ok("仓库 " + DialogueConfigFileName + " 能解析（" + map.Count + " 个键）");

        string ei, ed, ex;
        DialogueDefaults.Resolve(map, out ei, out ed, out ex);
        Eq("仓库文件 ① 入口交互非空", "true", string.IsNullOrEmpty(ei) ? "false" : "true");
        Eq("仓库文件 ② 入口梦语非空", "true", string.IsNullOrEmpty(ed) ? "false" : "true");
        Eq("仓库文件 ③ 出口交互非空", "true", string.IsNullOrEmpty(ex) ? "false" : "true");

        foreach (string k in map.Keys)
        {
            if (k != DialogueDefaults.KeyEntryInteract &&
                k != DialogueDefaults.KeyEntryDream &&
                k != DialogueDefaults.KeyExitInteract)
            {
                _failed++;
                Console.WriteLine("  [FAIL] 仓库 " + DialogueConfigFileName + " 里有 mod 不认识的键「" + k +
                                  "」（拼写错了？只认 " + DialogueDefaults.KeyEntryInteract + " / " +
                                  DialogueDefaults.KeyEntryDream + " / " + DialogueDefaults.KeyExitInteract + "）");
            }
        }
    }

    private const string DialogueConfigFileName = "DialogueConfig.json";

    /// <summary>
    /// 守门：`tools\*.ps1` **必须**是 **UTF-8 带 BOM**。
    ///
    /// 原因：双击 `.cmd` 走的是 **Windows PowerShell 5.1**，它只有在文件带 BOM 时才按 UTF-8 读，
    /// 否则按系统 ANSI 代码页（简中 = GBK）读 ⇒ 中文全变乱码、脚本直接语法报错
    /// （2026-09-26 就是这么被搞坏过一次：改脚本的工具把 BOM 吃掉了，用户双击直接崩）。
    /// 以后凡是重写 `.ps1`，都要用 <c>new UTF8Encoding(true)</c>。
    /// </summary>
    private static void TestScriptEncoding(string repoRoot)
    {
        if (repoRoot == null) return;

        string dir = Path.Combine(repoRoot, "tools");
        if (!Directory.Exists(dir))
        {
            _failed++;
            Console.WriteLine("  [FAIL] 找不到 tools 目录：" + dir);
            return;
        }

        string[] files = Directory.GetFiles(dir, "*.ps1");
        Array.Sort(files);
        foreach (string f in files)
        {
            byte[] head = new byte[3];
            int n;
            using (FileStream fs = File.OpenRead(f)) { n = fs.Read(head, 0, 3); }

            string name = "tools\\" + Path.GetFileName(f);
            if (n < 3)
            {
                _failed++;
                Console.WriteLine("  [FAIL] " + name + " 文件太短（" + n + " 字节）");
            }
            else if (head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
            {
                Ok(name + " 是 UTF-8 带 BOM");
            }
            else
            {
                _failed++;
                Console.WriteLine("  [FAIL] " + name + " **缺少 UTF-8 BOM** —— PowerShell 5.1 会按 ANSI/GBK 读，" +
                                  "中文全乱码且直接语法报错。请用 new UTF8Encoding(true) 重写这个文件。");
            }
        }
    }

    // ────────────────────────────────────────────────────────────────
    //  小工具
    // ────────────────────────────────────────────────────────────────

    private static Dictionary<string, string> Parse(string json)
    {
        Dictionary<string, string> map;
        string error;
        if (!JsonMini.TryParseFlatObject(json, out map, out error))
        {
            throw new Exception("这条应该能解析成功，却报错：" + error + "\n原文：" + json);
        }
        return map;
    }

    private static void Ok(string what)
    {
        _passed++;
        Console.WriteLine("  [OK]   " + what);
    }

    private static void Eq(string what, string expected, string actual)
    {
        if (expected == actual)
        {
            Ok(what);
        }
        else
        {
            _failed++;
            Console.WriteLine("  [FAIL] " + what + "\n         期望：" + Show(expected) + "\n         实际：" + Show(actual));
        }
    }

    private static void Fail(string what, string json, string mustContain)
    {
        Dictionary<string, string> map;
        string error;
        bool ok = JsonMini.TryParseFlatObject(json, out map, out error);

        if (!ok && error != null && error.Contains(mustContain))
        {
            Ok(what + "（报错：" + error + "）");
        }
        else if (ok)
        {
            _failed++;
            Console.WriteLine("  [FAIL] " + what + " —— 本该报错却解析成功了：" + json);
        }
        else
        {
            _failed++;
            Console.WriteLine("  [FAIL] " + what + " —— 报错信息里没有「" + mustContain + "」：" + error);
        }
    }

    private static string Show(string s)
    {
        return s == null ? "<null>" : "「" + s.Replace("\n", "\\n") + "」";
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "FinalMod")) &&
                Directory.Exists(Path.Combine(d.FullName, "tools")))
            {
                return d.FullName;
            }
            d = d.Parent;
        }
        return null;
    }
}
