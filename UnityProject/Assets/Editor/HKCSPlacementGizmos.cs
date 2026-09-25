// HKCSPlacementGizmos.cs —— 只在编辑器里给「摆放点」画提示，不影响游戏
//
// 为什么需要：PatchBench / PatchEnemy 是挂在**空物体**上的（游戏里由 C# 克隆原版 prefab），
// 空物体在 Scene 视图里什么都看不见 —— 摆了十几把椅子/几十只怪以后根本找不到谁是谁。
//
// 这个脚本做两件事（都是编辑器的 Gizmos / GUI 绘制，**不进 AssetBundle、不在游戏里执行**）：
//   1. Scene 视图：给挂了 PatchBench / PatchEnemy 的物体画一张图标 + 一个线框（线框是兜底，
//      万一图标没加载出来也还能看见位置和大致尺寸）；
//   2. Hierarchy 视图：在物体名字右边画一个小图标，方便在列表里一眼认出来。
//
// 图标文件：Assets/Gizmos/HKCS_Bench.png、Assets/Gizmos/HKCS_Enemy.png（想换图直接替换即可）
using HKCustomSceneMod.Patchers;
using UnityEditor;
using UnityEngine;

public static class HKCSPlacementGizmos
{
    private const string BenchIconPath = "Assets/Gizmos/HKCS_Bench.png";
    private const string EnemyIconPath = "Assets/Gizmos/HKCS_Enemy.png";

    // ────────────────────────────────────────────────────────────────
    //  1. Scene 视图：图标 + 线框
    //     三个 GizmoType 都写上，选中/没选中/被拖拽时都看得见
    // ────────────────────────────────────────────────────────────────
    [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.Active)]
    private static void DrawBench(PatchBench bench, GizmoType gizmoType)
    {
        DrawMarker(bench.transform.position, "HKCS_Bench", new Color(1f, 0.82f, 0.32f, 0.9f));
    }

    [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.Active)]
    private static void DrawEnemy(PatchEnemy enemy, GizmoType gizmoType)
    {
        DrawMarker(enemy.transform.position, "HKCS_Enemy", new Color(1f, 0.42f, 0.42f, 0.9f));
    }

    private static void DrawMarker(Vector3 position, string iconName, Color color)
    {
        // 图标浮在物体上方，别挡住地面；顶点跟随缩放，放大缩小时图标一起变
        Gizmos.DrawIcon(position + Vector3.up * 1.5f, iconName, true);

        // 兜底：图标没加载出来（或 Scene 视图关了 Gizmos 图标）时，至少能看见位置和尺寸
        Gizmos.color = color;
        Gizmos.DrawWireCube(position + Vector3.up * 0.6f, new Vector3(1.2f, 1.2f, 0.1f));
    }

    // ────────────────────────────────────────────────────────────────
    //  2. Hierarchy 视图：名字右边的小图标
    //     这里显式 LoadAssetAtPath，不依赖 DrawIcon 的按名字解析，最稳
    // ────────────────────────────────────────────────────────────────
    private static Texture2D _benchIcon;
    private static Texture2D _enemyIcon;
    private static bool _installed;

    [InitializeOnLoadMethod]
    private static void Install()
    {
        if (_installed) return;
        _installed = true;
        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItem;
    }

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
