using System.Collections.Generic;
using System.Text;
using Modding;
using UnityEngine;
using UnityEngine.SceneManagement;
using ILogger = Modding.ILogger;

namespace HKCustomSceneMod
{
    /// <summary>
    /// 调试用：进指定原版场景时，把**名称含关键字的物体**的完整层级路径打进日志。
    ///
    /// 用途：`GetPreloadNames()` 里写的是「从场景根算起的 Transform 路径」（例如 `_Managers/PlayMaker Unity 2D`），
    /// 光凭物体名猜经常猜错（原版小怪就不在场景根）。跑一次游戏，日志里就能看到准确路径。
    ///
    /// 用法：改下面两个数组 → 重新打包 → 进游戏走到那个场景 → 看 `ModLog.txt` 里 `[HKCS][Dump]` 开头的行。
    /// 找完把 <see cref="Enabled"/> 切成 false（或清空 Scenes）就不再有日志。
    /// </summary>
    internal static class ScenePathDump
    {
        /// <summary>用完就关掉。</summary>
        internal static bool Enabled = true;

        /// <summary>要 dump 的场景名。</summary>
        private static readonly string[] Scenes = { "Crossroads_01", "Town" };

        /// <summary>只看名字里含这些关键字的物体（大小写不敏感）。</summary>
        private static readonly string[] Keywords = { "Zombie", "Fly", "Venge", "Crawl", "Bench", "Enemies", "Enemy" };

        internal static void TryDump(Scene scene)
        {
            if (!Enabled) return;

            bool wanted = false;
            for (int i = 0; i < Scenes.Length; i++)
            {
                if (Scenes[i] == scene.name) { wanted = true; break; }
            }
            if (!wanted) return;

            try
            {
                Modding.Logger.Log("[HKCS][Dump] === 场景 " + scene.name + " 命中关键字的物体路径 ===");
                int found = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root == null) continue;
                    found += Walk(root.transform, "");
                }
                Modding.Logger.Log("[HKCS][Dump] === 共 " + found + " 条（关键字：" + string.Join(",", Keywords) + "）===");
            }
            catch (System.Exception e)
            {
                Modding.Logger.LogError("[HKCS][Dump] 失败：" + e);
            }
        }

        private static int Walk(Transform t, string parentPath)
        {
            if (t == null) return 0;

            string path = parentPath.Length == 0 ? t.name : parentPath + "/" + t.name;
            int count = 0;
            if (Matches(t.name))
            {
                Modding.Logger.Log("[HKCS][Dump] " + path);
                count++;
            }
            for (int i = 0; i < t.childCount; i++)
            {
                count += Walk(t.GetChild(i), path);
            }
            return count;
        }

        private static bool Matches(string name)
        {
            for (int i = 0; i < Keywords.Length; i++)
            {
                if (name.IndexOf(Keywords[i], System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }
}
