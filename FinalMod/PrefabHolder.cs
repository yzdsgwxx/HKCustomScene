using System.Collections.Generic;
using Modding;
using UnityEngine;
using UObject = UnityEngine.Object;
// UnityEngine 也有 ILogger，而 Modding 的类型现在并进了 Assembly-CSharp.dll → 必须显式取别名，否则 CS0104
using ILogger = Modding.ILogger;

namespace HKCustomSceneMod
{
    /// <summary>
    /// 持有预载出来的原版 prefab。所有房间共用同一份，不要每间房各预载一次。
    ///
    /// 每个 prefab 都要 DontDestroyOnLoad + SetActive(false)：
    /// 不关掉的话它可能在被克隆之前就被别的场景清理掉了。
    /// </summary>
    public static class PrefabHolder
    {
        public static GameObject AreaTitleController { get; private set; }
        public static GameObject PlayMakerUnity2D { get; private set; }
        public static GameObject SceneManagerPrefab { get; private set; }

        /// <summary>原版长椅（存档点）。来源 Crossroads_47 —— 带长椅的最小场景，Benchwarp 用的也是这一条。</summary>
        public static GameObject BenchPrefab { get; private set; }

        /// <summary>原版小怪：爬虫（Crossroads_01 里的 "Zombie Runner"）。</summary>
        public static GameObject ZombieRunnerPrefab { get; private set; }

        /// <summary>原版小怪：复仇苍蝇（Crossroads_01 里的 "Fly"）。和爬虫同一个场景，不额外增加预载开销。</summary>
        public static GameObject FlyPrefab { get; private set; }

        public static void Preloaded(
            Dictionary<string, Dictionary<string, GameObject>> preloadedObjects,
            ILogger logger)
        {
            AreaTitleController = Grab(preloadedObjects, "White_Palace_18", "Area Title Controller", logger);
            PlayMakerUnity2D = Grab(preloadedObjects, "White_Palace_18", "_Managers/PlayMaker Unity 2D", logger);
            SceneManagerPrefab = Grab(preloadedObjects, "White_Palace_18", "_SceneManager", logger);

            // 椅子 / 小怪（都是"克隆原版 prefab"这一套，Unity 里造不出来）
            BenchPrefab = Grab(preloadedObjects, "Crossroads_47", "RestBench", logger);
            ZombieRunnerPrefab = Grab(preloadedObjects, "Crossroads_01", "Zombie Runner", logger);
            FlyPrefab = Grab(preloadedObjects, "Crossroads_01", "Fly", logger);
        }

        private static GameObject Grab(
            Dictionary<string, Dictionary<string, GameObject>> preloaded,
            string scene, string path, ILogger logger)
        {
            if (preloaded == null || !preloaded.ContainsKey(scene))
            {
                logger.LogError(string.Format("[HKCS] 预载里没有场景 {0}", scene));
                return null;
            }
            if (!preloaded[scene].ContainsKey(path))
            {
                logger.LogError(string.Format("[HKCS] 预载 {0} 里没有 {1}", scene, path));
                // 路径写错时，把「这个场景里实际预载成功的路径」打出来，方便一眼看出该换成什么名字
                logger.LogError(string.Format("[HKCS] {0} 里预载成功的路径有：{1}",
                    scene, string.Join(" | ", new List<string>(preloaded[scene].Keys).ToArray())));
                return null;
            }

            GameObject go = UObject.Instantiate(preloaded[scene][path]);
            go.name = path.Replace("/", "_");
            UObject.DontDestroyOnLoad(go);
            go.SetActive(false);
            return go;
        }
    }
}
