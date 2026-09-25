// HKCSPlacementGizmos.cs —— 只在编辑器的 Hierarchy 列表里给摆放点加一个小图标（不影响游戏）
//
// 注意：Scene 视图里的"真实效果预览"由 HKCSPlacementPreview.cs 负责 ——
// 那边是在场景里生成**真实尺寸的预览物体**（HideFlags.DontSave），会跟着缩放走，
// 能用来预览游戏最终的样子。本文件只负责 Hierarchy 里那个 16x16 小图标。
//
// 图标文件：Assets/Gizmos/HKCS_Bench.png、Assets/Gizmos/HKCS_Enemy.png（都是原版美术缩出来的）
using HKCustomSceneMod.Patchers;
using UnityEditor;
using UnityEngine;

public static class HKCSPlacementGizmos
{
    private const string BenchIconPath = "Assets/Gizmos/HKCS_Bench.png";
    private const string EnemyIconPath = "Assets/Gizmos/HKCS_Enemy.png";

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
