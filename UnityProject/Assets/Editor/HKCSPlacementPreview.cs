// HKCSPlacementPreview.cs —— 在场景里生成「真实尺寸的预览物体」：编辑器里看到什么，游戏里就是什么
//
// 为什么不用 Gizmo / Handles：那些是**屏幕空间**绘制，缩放 Scene 视图时大小不变，
// 既看不出真实尺寸、也没法跟地形对齐 —— 不能用来预览。
//
// 做法：给每个挂了 PatchBench / PatchEnemy 的摆放点，在场景里建一个**真正的 GameObject**
// （SpriteRenderer + 原版美术），位置/尺寸和 FinalMod 运行时克隆出来的那个**用同一套算法**：
//   · HideFlags.DontSave           ⇒ 不写进 .unity、不进 AssetBundle；游戏里完全没有它
//   · HideFlags.HideInHierarchy    ⇒ **不出现在层级列表里**（Scene 视图照常渲染）
//   · 因为隐藏物体 Unity 默认点不中，这里自己做了拾取：**点预览 = 选中真正的摆放点**（见 OnSceneGui）
//   · 每 0.3 秒自动同步（改摆放点、改字段、切场景都会重建）
//
// 对齐算法（和 FinalMod/Patchers/PatchBench.cs 保持一致）：
//   长椅轴心 = 摆放点 + (0, originLift + Bench.YOffset, 0.02)
//   originLift 优先取**游戏实测值**（PatchBench 写出的 HKCS_bench_metrics.txt），
//   没读到才用常量近似（原版长椅 trigger offset.y ≈ -0.59）。
//
// 菜单：工具 → HKCS 预览 → 开关预览物体
using System.Collections.Generic;
using HKCustomSceneMod.Patchers;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class HKCSPlacementPreview
{
    private const string BenchSpritePath = "Assets/Preview/bone_bench.png";
    private const string EnemySpritePath = "Assets/Preview/fly0000.png";

    private const float BenchWorldWidth = 2.86f;
    private const float EnemyWorldWidth = 1.0f;
    private const float BenchOriginLift = 0.59f;
    private const float PreviewZ = 0.02f;
    private const float PreviewYOffset = 0f;

    private const string PreviewPrefix = "[预览] ";
    private const string PrefKey = "HKCS.Preview.Enabled";

    /// <summary>预览物体 → 它对应的摆放点（点预览时要选中这个）。</summary>
    private sealed class Entry
    {
        public GameObject Preview;
        public GameObject Owner;
    }

    private static readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
    private static float _metricsWidth;
    private static float _metricsOriginLift;
    private static bool _metricsLoaded;
    private static bool _metricsLogged;
    private static double _nextMetricsRead;
    private static double _nextSync;
    private static bool _loggedOnce;

    /// <summary>鼠标按下时命中的摆放点（用来判断抬起时要不要接管选择）。</summary>
    private static GameObject _pressedOwner;

    private static bool Enabled
    {
        get { return EditorPrefs.GetBool(PrefKey, true); }
        set { EditorPrefs.SetBool(PrefKey, value); }
    }

    private static string MetricsPath
    {
        get
        {
            return System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "Low", "Team Cherry", "Hollow Knight", "HKCS_bench_metrics.txt");
        }
    }

    static HKCSPlacementPreview()
    {
        EditorApplication.update += Tick;
        SceneView.duringSceneGui += OnSceneGui;
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

    // ────────────────────────────────────────────────────────────────
    //  点预览 → 选中真正的摆放点
    // ────────────────────────────────────────────────────────────────
    private static void OnSceneGui(SceneView view)
    {
        if (Event.current == null) return;
        if (Event.current.button != 0 || Event.current.alt) return;

        if (Event.current.type == EventType.MouseDown)
        {
            // 按下时命中预览 → 记住它，并接管这次点击
            _pressedOwner = PickOwner(Event.current.mousePosition);
            if (_pressedOwner == null) return;

            Selection.activeGameObject = _pressedOwner;
            Event.current.Use();
            SceneView.RepaintAll();
            return;
        }

        if (Event.current.type == EventType.MouseUp)
        {
            // 只有「按下时命中的就是它」才在抬起时再兜一次；
            // 这样框选/拖拽不会被抢（那时 mouseDown 命中的不是预览）
            if (_pressedOwner == null) return;
            GameObject owner = PickOwner(Event.current.mousePosition);
            if (owner == _pressedOwner)
            {
                Selection.activeGameObject = owner;
                Event.current.Use();
                SceneView.RepaintAll();
            }
            _pressedOwner = null;
        }
    }

    /// <summary>鼠标位置命中的预览物体对应哪个摆放点（用精灵包围盒做射线求交）。</summary>
    private static GameObject PickOwner(Vector2 guiPoint)
    {
        if (_entries.Count == 0) return null;

        Ray ray = HandleUtility.GUIPointToWorldRay(guiPoint);
        GameObject best = null;
        float bestDist = float.MaxValue;

        foreach (KeyValuePair<int, Entry> kv in _entries)
        {
            Entry e = kv.Value;
            if (e.Preview == null || e.Owner == null) continue;
            SpriteRenderer sr = e.Preview.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) continue;

            float dist;
            if (sr.bounds.IntersectRay(ray, out dist) && dist < bestDist)
            {
                bestDist = dist;
                best = e.Owner;
            }
        }
        return best;
    }

    // ────────────────────────────────────────────────────────────────
    //  同步
    // ────────────────────────────────────────────────────────────────
    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < _nextSync) return;
        _nextSync = EditorApplication.timeSinceStartup + 0.3;

        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;

        if (!Enabled)
        {
            if (_entries.Count > 0) ClearAll();
            return;
        }

        Sync();
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

            string sWidth, sLift;
            float w = 0f, lift = 0f;
            if (!kv.TryGetValue("width", out sWidth) || !float.TryParse(sWidth, out w)) return;
            if (!kv.TryGetValue("originLift", out sLift) || !float.TryParse(sLift, out lift)) return;
            if (w <= 0.001f) return;

            _metricsWidth = w;
            _metricsOriginLift = lift;
            _metricsLoaded = true;

            if (!_metricsLogged)
            {
                _metricsLogged = true;
                Debug.Log(string.Format(
                    "[HKCS][Preview] 已读到游戏实测的长椅数据：宽 {0:0.###} 轴心偏移 {1:0.###} —— 预览完全按实测来", w, lift));
            }
        }
        catch
        {
            // 文件可能正被游戏写，下一轮再读
        }
    }

    private static void Sync()
    {
        RefreshMetrics();

        // ⚠ Assembly-CSharp 里也有一个全局的 SceneManager（HK 自己的类），这里必须全限定，否则 CS0104
        UnityEngine.SceneManagement.Scene active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        bool dirtyBefore = active.isDirty;

        Sprite benchSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BenchSpritePath);
        Sprite enemySprite = AssetDatabase.LoadAssetAtPath<Sprite>(EnemySpritePath);

        var seen = new HashSet<int>();

        foreach (PatchBench bench in Object.FindObjectsOfType<PatchBench>())
        {
            if (bench == null) continue;
            float width = _metricsLoaded ? _metricsWidth : BenchWorldWidth;
            float lift = _metricsLoaded ? _metricsOriginLift : (BenchOriginLift + bench.YOffset);
            SyncOne(bench.gameObject, bench.GetInstanceID(), benchSprite, width,
                    "长椅 " + bench.BenchName, lift, seen);
        }
        foreach (PatchEnemy enemy in Object.FindObjectsOfType<PatchEnemy>())
        {
            if (enemy == null) continue;
            SyncOne(enemy.gameObject, enemy.GetInstanceID(), enemySprite, EnemyWorldWidth,
                    "小怪 " + enemy.Kind, 0f, seen);
        }

        List<int> gone = null;
        foreach (KeyValuePair<int, Entry> kv in _entries)
        {
            if (!seen.Contains(kv.Key) || kv.Value.Preview == null || kv.Value.Owner == null)
            {
                if (gone == null) gone = new List<int>();
                gone.Add(kv.Key);
            }
        }
        if (gone != null)
        {
            foreach (int id in gone)
            {
                Entry e;
                if (_entries.TryGetValue(id, out e) && e.Preview != null) Object.DestroyImmediate(e.Preview);
                _entries.Remove(id);
            }
            SceneView.RepaintAll();
        }

        if (!_loggedOnce && _entries.Count > 0)
        {
            _loggedOnce = true;
            Debug.Log(string.Format(
                "[HKCS][Preview] 已生成 {0} 个预览物体（DontSave + HideInHierarchy：不进场景/bundle、不在层级列表、游戏里不可见）。" +
                " 创建前后 activeScene.isDirty = {1} -> {2}",
                _entries.Count, dirtyBefore, UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty));
        }
    }

    private static void SyncOne(GameObject placement, int id, Sprite sprite, float worldWidth,
                                string label, float originLift, HashSet<int> seen)
    {
        if (placement == null) return;
        seen.Add(id);
        if (sprite == null) return;

        Entry entry;
        if (!_entries.TryGetValue(id, out entry) || entry.Preview == null)
        {
            GameObject preview = new GameObject(PreviewPrefix + label);
            // ★ DontSave：不进场景文件/不进 bundle；★ HideInHierarchy：不出现在层级列表里
            preview.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;
            preview.AddComponent<SpriteRenderer>();

            entry = new Entry { Preview = preview, Owner = placement };
            _entries[id] = entry;

            if (placement.scene.IsValid())
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(preview, placement.scene);
            }
            SceneView.RepaintAll();
        }

        entry.Owner = placement;
        entry.Preview.name = PreviewPrefix + label;

        SpriteRenderer sr = entry.Preview.GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != sprite) sr.sprite = sprite;

        float spriteWidth = sprite.bounds.size.x;
        float scale = spriteWidth > 0.0001f ? worldWidth / spriteWidth : 1f;
        entry.Preview.transform.localScale = new Vector3(scale, scale, 1f);

        Vector3 p = placement.transform.position;
        entry.Preview.transform.position = new Vector3(p.x, p.y + originLift + PreviewYOffset, PreviewZ);
        entry.Preview.transform.rotation = Quaternion.identity;
    }

    private static void ClearAll()
    {
        foreach (KeyValuePair<int, Entry> kv in _entries)
        {
            if (kv.Value != null && kv.Value.Preview != null) Object.DestroyImmediate(kv.Value.Preview);
        }
        _entries.Clear();
        SceneView.RepaintAll();
    }
}
