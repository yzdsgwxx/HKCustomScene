// HKCSPlacementGizmos.cs —— 只在编辑器里给「摆放点」画提示，不影响游戏
//
// 为什么需要：PatchBench / PatchEnemy 是挂在**空物体**上的（游戏里由 C# 克隆原版 prefab），
// 空物体在 Scene 视图里什么都看不见 —— 摆了十几把椅子/几十只怪以后根本找不到谁是谁。
//
// 这个脚本做两件事（都是编辑器的绘制，**不进 AssetBundle、游戏里不执行**）：
//   1. Scene 视图：给挂了 PatchBench / PatchEnemy 的物体画线框 + 带图标的文字标签；
//   2. Hierarchy 视图：在物体名字右边画一个小图标。
//
// ⚠ 这里**故意不用 [DrawGizmo]**：普通 Gizmos 会被场景里的实心几何体（比如 .obj 地形 mesh）挡住，
//    出现"椅子图标埋在地形里"的情况。改用 SceneView.duringSceneGui + Handles.zTest = Always，
//    无论物体在地形前面还是里面，标记都画在最上层，方便定位和框选。
//
// 图标文件：Assets/Gizmos/HKCS_Bench.png、Assets/Gizmos/HKCS_Enemy.png（想换图直接替换即可）
using HKCustomSceneMod.Patchers;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class HKCSPlacementGizmos
{
    private const string BenchIconPath = "Assets/Gizmos/HKCS_Bench.png";
    private const string EnemyIconPath = "Assets/Gizmos/HKCS_Enemy.png";

    // 长椅在游戏里的身体尺寸（世界单位，宽 × 高）。Gizmo 就用这个尺寸画，所以
    // 「编辑器里底边贴着地面」= 「游戏里脚落在地面上」。
    // 想更精确：进游戏看 ModLog 的「长椅实测边界 y … ~ …，宽 X」那行，把数字填进来。
    private const float BenchWidth = 1.9f;
    private const float BenchHeight = 1.1f;

    private static Texture2D _benchIcon;
    private static Texture2D _enemyIcon;
    private static GUIStyle _labelStyle;
    private static bool _installed;

    [InitializeOnLoadMethod]
    private static void Install()
    {
        if (_installed) return;
        _installed = true;
        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItem;
        SceneView.duringSceneGui += OnSceneGui;
    }

    // ────────────────────────────────────────────────────────────────
    //  1. Scene 视图：线框 + 图标标签，强制画在最前面
    // ────────────────────────────────────────────────────────────────
    private static void OnSceneGui(SceneView view)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint) return;

        Handles.zTest = CompareFunction.Always;   // ★ 关键：不做深度测试 ⇒ 不会被地形挡住

        foreach (PatchBench bench in Object.FindObjectsOfType<PatchBench>())
        {
            Vector3 foot = bench.transform.position;   // 摆放点 = 长椅要站的那块地面

            // 地面线：一眼看出你把它摆在哪条地面线上
            Handles.color = new Color(0.35f, 1f, 0.9f, 1f);
            Handles.DrawLine(foot + Vector3.left * (BenchWidth * 0.75f), foot + Vector3.right * (BenchWidth * 0.75f));

            // 长椅身体：**底边贴在摆放点上** —— YOffset 调对以后，游戏里就是这个样子
            Handles.color = new Color(1f, 0.82f, 0.32f, 0.95f);
            Handles.DrawWireCube(foot + Vector3.up * (BenchHeight * 0.5f), new Vector3(BenchWidth, BenchHeight, 0.1f));

            Handles.Label(foot + Vector3.up * (BenchHeight + 0.3f),
                          new GUIContent(" " + bench.BenchName
                                         + "   y+=" + bench.YOffset.ToString("0.##")
                                         + "   z=" + bench.Z.ToString("0.##"), BenchIcon),
                          LabelStyle);
        }

        foreach (PatchEnemy enemy in Object.FindObjectsOfType<PatchEnemy>())
        {
            Vector3 foot = enemy.transform.position;

            Handles.color = new Color(0.35f, 1f, 0.9f, 1f);
            Handles.DrawLine(foot + Vector3.left * 0.5f, foot + Vector3.right * 0.5f);

            Handles.color = new Color(1f, 0.42f, 0.42f, 0.95f);
            Handles.DrawWireCube(foot + Vector3.up * 0.5f, new Vector3(1f, 1f, 0.1f));
            Handles.Label(foot + Vector3.up * 1.15f,
                          new GUIContent(" " + enemy.Kind + (string.IsNullOrEmpty(enemy.Id) ? "" : " #" + enemy.Id), EnemyIcon),
                          LabelStyle);
        }

        Handles.zTest = CompareFunction.LessEqual;   // 还原，别影响别人的 Scene GUI 绘制
        Handles.color = Color.white;
    }

    private static GUIStyle LabelStyle
    {
        get
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(EditorStyles.boldLabel);
                _labelStyle.normal.textColor = new Color(1f, 0.9f, 0.55f);
                _labelStyle.alignment = TextAnchor.MiddleLeft;
                _labelStyle.padding = new RectOffset(0, 2, 0, 0);
            }
            return _labelStyle;
        }
    }

    // ────────────────────────────────────────────────────────────────
    //  2. Hierarchy 视图：名字右边的小图标（显式加载贴图，不依赖按名解析）
    // ────────────────────────────────────────────────────────────────
    private static void OnHierarchyItem(int instanceId, Rect rect)
    {
        GameObject go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
        if (go == null) return;

        Texture2D icon = null;
        if (go.GetComponent<PatchBench>() != null) icon = BenchIcon;
        else if (go.GetComponent<PatchEnemy>() != null) icon = EnemyIcon;
        if (icon == null) return;

        GUI.DrawTexture(new Rect(rect.x + rect.width - 20f, rect.y + 1f, 16f, 16f),
                        icon, ScaleMode.ScaleToFit);
    }

    private static Texture2D BenchIcon
    {
        get
        {
            if (_benchIcon == null) _benchIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(BenchIconPath);
            return _benchIcon;
        }
    }

    private static Texture2D EnemyIcon
    {
        get
        {
            if (_enemyIcon == null) _enemyIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(EnemyIconPath);
            return _enemyIcon;
        }
    }
}
