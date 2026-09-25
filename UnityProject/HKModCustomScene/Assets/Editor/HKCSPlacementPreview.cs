// HKCSPlacementPreview.cs —— 在场景里生成「真实尺寸的预览物体」：编辑器里看到什么，游戏里就是什么
//
// 为什么不用 Gizmo / Handles：那些是**屏幕空间**绘制，缩放 Scene 视图时大小不变，
// 既看不出真实尺寸、也没法跟地形对齐 —— 不能用来预览。
//
// 做法：给每个挂了 PatchBench / PatchEnemy 的摆放点，在场景里建一个**真正的 GameObject**
// （SpriteRenderer + 原版美术），位置/尺寸和 FinalMod 运行时克隆出来的那个**用同一套算法**：
//   · HideFlags.DontSave ⇒ 不写进 .unity、不进 AssetBundle；游戏里完全没有它；
//   · 每 0.3 秒自动同步（改摆放点、改字段、切场景都会重建）；
//   · 于是 Scene 视图里它和真物体表现一致（缩放跟随、能贴着地形看），游戏里看不见。
//
// 对齐算法（要和 FinalMod/Patchers/PatchBench.cs 保持一致）：
//   长椅轴心 = 摆放点 + (0, BenchOriginLift + Bench.YOffset, 0.02)
//   其中 BenchOriginLift = 原版长椅 trigger 的 |offset.y| ≈ 0.59
//   （来自 Benchwarp 的 styles.json：triggerOffset.y ≈ -0.59）
//   —— 注意这里用的是**轴心位置**，不是"底边贴地"：因为游戏里就是把轴心放在那里，
//      精灵相对轴心怎么画由原版 prefab 决定（默认居中）。这样编辑器=游戏。
//
// 菜单：工具 → HKCS 预览 → 开/关
using System.Collections.Generic;
using HKCustomSceneMod.Patchers;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class HKCSPlacementPreview
{
    private const string BenchSpritePath = "Assets/Preview/bone_bench.png";
    private const string EnemySpritePath = "Assets/Preview/fly0000.png";

    /// <summary>原版长椅 sprite 的世界宽度（183px，HK 里约 2.86 世界单位）。
    /// 预览按这个宽度缩放，不依赖贴图的 PPU 导入设置。</summary>
    private const float BenchWorldWidth = 2.86f;

    /// <summary>小怪预览的世界宽度。</summary>
    private const float EnemyWorldWidth = 1.0f;

    /// <summary>原版长椅轴心离"骑士站的那块地"的高度（trigger offset.y ≈ -0.59，取绝对值）。</summary>
    private const float BenchOriginLift = 0.59f;

    /// <summary>长椅深度，和 FinalMod 一致。</summary>
    private const float PreviewZ = 0.02f;

    /// <summary>整体微调（世界单位）。</summary>
    private const float PreviewYOffset = 0f;

    private const string PreviewPrefix = "[预览] ";
    private const string PrefKey = "HKCS.Preview.Enabled";

    // ── 自校准：读游戏里实测出来的长椅尺寸/轴心偏移（由 FinalMod 的 PatchBench 写出）──
    //    有了它，预览的位置和大小**就是游戏里的实测值**，不用猜 PPU / 轴心。
    private static readonly Dictionary<int, GameObject> _previews = new Dictionary<int, GameObject>();
    private static float _metricsWidth;
    private static float _metricsHeight;
    private static float _metricsOriginLift;
    private static bool _metricsLoaded;
    private static bool _metricsLogged;
    private static double _nextMetricsRead;
    private static double _nextSync;
    private static bool _loggedOnce;

    private static string MetricsPath
    {
        get
        {
            return System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "Low", "Team Cherry", "Hollow Knight", "HKCS_bench_metrics.txt");
        }
    }

    private static void RefreshMetrics()
    {
        if (EditorApplication.timeSinceStartup < _nextMetricsRead) return;
        _nextMetricsRead = EditorApplication.timeSinceStartup + 2.0;

        try
        {
            string path = MetricsPath;
            if (!System.IO.File.Exists(path)) return;

            var kv = new Dictionary<string, string>();
            foreach (string line in System.IO.File.ReadAllLines(path))
            {
                int i = line.IndexOf('=');
                if (i > 0) kv[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
            }

            string sWidth, sHeight, sLift;
            float w = 0f, h = 0f, lift = 0f;
            bool ok = kv.TryGetValue("width", out sWidth) && float.TryParse(sWidth, out w)
                   && kv.TryGetValue("height", out sHeight) && float.TryParse(sHeight, out h)
                   && kv.TryGetValue("originLift", out sLift) && float.TryParse(sLift, out lift);

            if (!ok || w <= 0.001f) return;

            _metricsWidth = w;
            _metricsHeight = h;
            _metricsOriginLift = lift;
            _metricsLoaded = true;

            if (!_metricsLogged)
            {
                _metricsLogged = true;
                Debug.Log(string.Format(
                    "[HKCS][Preview] 已读到游戏实测的长椅数据：宽 {0:0.###} 高 {1:0.###} 轴心偏移 {2:0.###} —— 预览完全按实测来",
                    w, h, lift));
            }
        }
        catch
        {
            // 文件可能正被游戏写，下一轮再读
        }
    }

    private static bool Enabled
    {
        get { return EditorPrefs.GetBool(PrefKey, true); }
        set { EditorPrefs.SetBool(PrefKey, value); }
    }

    static HKCSPlacementPreview()
    {
        EditorApplication.update += Tick;
    }

    [MenuItem("工具/HKCS 预览/开关预览物体")]
    private static void Toggle()
    {
        Enabled = !Enabled;
        if (!Enabled) ClearAll();
        _loggedOnce = false;
        Debug.Log("[HKCS][Preview] 预览物体 = " + (Enabled ? "开" : "关"));
        SceneView.RepaintAll();
    }

    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < _nextSync) return;
        _nextSync = EditorApplication.timeSinceStartup + 0.3;

        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;

        if (!Enabled)
        {
            if (_previews.Count > 0) ClearAll();
            return;
        }

        Sync();
    }

    private static void Sync()
    {
        RefreshMetrics();

        // ⚠ Assembly-CSharp 里也有一个全局的 SceneManager（HK 自己的类），
        //   所以这里必须全限定，否则 CS0104 二义。
        UnityEngine.SceneManagement.Scene active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        bool dirtyBefore = active.isDirty;

        Sprite benchSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BenchSpritePath);
        Sprite enemySprite = AssetDatabase.LoadAssetAtPath<Sprite>(EnemySpritePath);

        var seen = new HashSet<int>();

        foreach (PatchBench bench in Object.FindObjectsOfType<PatchBench>())
        {
            // 有实测数据就用实测（位置/大小 = 游戏里一模一样），否则用内置常量近似
            float width = _metricsLoaded ? _metricsWidth : BenchWorldWidth;
            float lift = _metricsLoaded ? _metricsOriginLift : (BenchOriginLift + bench.YOffset);
            SyncOne(bench.gameObject, bench.GetInstanceID(), benchSprite, width,
                    "长椅 " + bench.BenchName, lift, seen);
        }
        foreach (PatchEnemy enemy in Object.FindObjectsOfType<PatchEnemy>())
        {
            SyncOne(enemy.gameObject, enemy.GetInstanceID(), enemySprite, EnemyWorldWidth,
                    "小怪 " + enemy.Kind, 0f, seen);
        }

        List<int> gone = null;
        foreach (KeyValuePair<int, GameObject> kv in _previews)
        {
            if (!seen.Contains(kv.Key) || kv.Value == null)
            {
                if (gone == null) gone = new List<int>();
                gone.Add(kv.Key);
            }
        }
        if (gone != null)
        {
            foreach (int id in gone)
            {
                GameObject go;
                if (_previews.TryGetValue(id, out go) && go != null) Object.DestroyImmediate(go);
                _previews.Remove(id);
            }
            SceneView.RepaintAll();
        }

        if (!_loggedOnce && _previews.Count > 0)
        {
            _loggedOnce = true;
            Debug.Log(string.Format(
                "[HKCS][Preview] 已生成 {0} 个预览物体（HideFlags.DontSave：不写进场景、不进 bundle、游戏里不可见）。" +
                " 创建前后 activeScene.isDirty = {1} -> {2}",
                _previews.Count, dirtyBefore, UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty));
        }
    }

    private static void SyncOne(GameObject placement, int id, Sprite sprite, float worldWidth,
                                string label, float originLift, HashSet<int> seen)
    {
        if (placement == null) return;
        seen.Add(id);
        if (sprite == null) return;

        GameObject preview;
        if (!_previews.TryGetValue(id, out preview) || preview == null)
        {
            preview = new GameObject(PreviewPrefix + label);
            preview.hideFlags = HideFlags.DontSave;          // ★ 不进场景文件、不进 bundle
            preview.AddComponent<SpriteRenderer>();
            _previews[id] = preview;

            if (placement.scene.IsValid())
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(preview, placement.scene);
            }
            SceneView.RepaintAll();
        }

        preview.name = PreviewPrefix + label;

        SpriteRenderer sr = preview.GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != sprite) sr.sprite = sprite;

        float spriteWidth = sprite.bounds.size.x;
        float scale = spriteWidth > 0.0001f ? worldWidth / spriteWidth : 1f;
        preview.transform.localScale = new Vector3(scale, scale, 1f);

        // 和 FinalMod 同一套：轴心 = 摆放点 + (0, originLift, 0.02)
        Vector3 p = placement.transform.position;
        preview.transform.position = new Vector3(p.x, p.y + originLift + PreviewYOffset, PreviewZ);
        preview.transform.rotation = Quaternion.identity;
    }

    private static void ClearAll()
    {
        foreach (KeyValuePair<int, GameObject> kv in _previews)
        {
            if (kv.Value != null) Object.DestroyImmediate(kv.Value);
        }
        _previews.Clear();
        SceneView.RepaintAll();
    }
}
