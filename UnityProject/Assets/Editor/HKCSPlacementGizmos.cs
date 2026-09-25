// HKCSPlacementGizmos.cs —— 只在编辑器里给「摆放点」画一个图标，不影响游戏
//
// 为什么需要：PatchBench / PatchEnemy 是挂在**空物体**上的（游戏里由 C# 克隆原版 prefab），
// 空物体在 Scene 视图里什么都看不见 —— 摆多把椅子/多只怪以后根本找不到谁是谁。
//
// 只画**图标**（不画文字、不画框）：
//   · 用 SceneView.duringSceneGui + Handles.zTest = Always ⇒ 不会被地形 mesh 挡住（普通 Gizmos 会被挡）；
//   · Hierarchy 视图里也在物体名字右边显示同一个小图标。
//   · 全是编辑器绘制：不进 AssetBundle、游戏里不执行。
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

    /// <summary>图标抬离物体原点多高（世界单位），免得压在物体/地面上看不出来。</summary>
    private const float BenchIconLift = 0.9f;
    private const float EnemyIconLift = 0.9f;

    private static Texture2D _benchIcon;
    private static Texture2D _enemyIcon;
    private static GUIStyle _iconStyle;
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
    //  Scene 视图：只画图标（永远在最上层）
    // ────────────────────────────────────────────────────────────────
    private static void OnSceneGui(SceneView view)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint) return;

        Handles.zTest = CompareFunction.Always;   // ★ 不做深度测试 ⇒ 不会被地形挡住

        foreach (PatchBench bench in Object.FindObjectsOfType<PatchBench>())
        {
            Texture2D icon = BenchIcon;
            if (icon != null)
            {
                Handles.Label(bench.transform.position + Vector3.up * BenchIconLift,
                              new GUIContent(icon), IconStyle);
            }
        }

        foreach (PatchEnemy enemy in Object.FindObjectsOfType<PatchEnemy>())
        {
            Texture2D icon = EnemyIcon;
            if (icon != null)
            {
                Handles.Label(enemy.transform.position + Vector3.up * EnemyIconLift,
                              new GUIContent(icon), IconStyle);
            }
        }

        Handles.zTest = CompareFunction.LessEqual;   // 还原，别影响别人的 Scene GUI 绘制
        Handles.color = Color.white;
    }

    /// <summary>图标样式：去掉所有内边距/外边距，就是一张干净的图。</summary>
    private static GUIStyle IconStyle
    {
        get
        {
            if (_iconStyle == null)
            {
                _iconStyle = new GUIStyle();
                _iconStyle.padding = new RectOffset(0, 0, 0, 0);
                _iconStyle.margin = new RectOffset(0, 0, 0, 0);
                _iconStyle.alignment = TextAnchor.MiddleCenter;
            }
            return _iconStyle;
        }
    }

    // ────────────────────────────────────────────────────────────────
    //  Hierarchy 视图：名字右边的小图标（显式加载贴图，不依赖按名解析）
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
