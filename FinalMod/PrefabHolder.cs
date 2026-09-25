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

        /// <summary>原版小怪：僵尸行者（`_Enemies/Zombie Runner`，1.22×1.63）。</summary>
        public static GameObject ZombieRunnerPrefab { get; private set; }

        /// <summary>原版小怪：复仇苍蝇（`Crossroads_07` 的 `Uninfected Parent/Fly`，0.89×0.84）。
        /// ⚠ `Crossroads_01` 里**没有**独立苍蝇物体（那里的 "Fly"/"Fly Left"/"Fly Right" 只是
        /// 爆裂僵尸 FSM 里的变量/状态名），所以苍蝇必须多预载一个来源场景。</summary>
        public static GameObject FlyPrefab { get; private set; }

        /// <summary>原版小怪：爬虫（`_Enemies/Crawler 1`，1.42×0.91）。和僵尸同一个场景，零额外预载开销。</summary>
        public static GameObject CrawlerPrefab { get; private set; }

        /// <summary>原版小怪：攀爬虫（`_Enemies/Climber`，1.09×0.92）。和僵尸同一个场景，零额外预载开销。</summary>
        public static GameObject ClimberPrefab { get; private set; }

        /// <summary>
        /// 小怪的预载路径（**物体在场景里的完整层级路径**）。
        ///
        /// ⚠ 下面这些是 2026-09-26 用 UnityPy 直接读游戏自己的场景文件得到的，不是猜的：
        ///   `level37` = `Crossroads/Crossroads_01`（`_Enemies` 下只有 Climber / Climber 1 / Crawler 1 / Zombie Runner）
        ///   `level43` = `Crossroads/Crossroads_07`（苍蝇在 `Uninfected Parent` 下）
        /// 加新怪种时：先离线扫场景拿到路径（或看运行时日志里 `[HKCS] [Dump]` 打出来的层级路径），
        /// 再把路径同时加进这里**和** `CustomSceneMod.GetPreloadNames()` —— 两处必须一字不差。
        /// 路径写错不会拖垮别的预载：API 只打一行 `could not load '场景/路径.prefab'` 然后跳过。
        /// </summary>
        private static readonly string[] ZombieRunnerPaths = { "_Enemies/Zombie Runner" };

        private static readonly string[] CrawlerPaths = { "_Enemies/Crawler 1" };

        private static readonly string[] ClimberPaths = { "_Enemies/Climber" };

        private static readonly string[] FlyPaths = { "Uninfected Parent/Fly" };

        public static void Preloaded(
            Dictionary<string, Dictionary<string, GameObject>> preloadedObjects,
            ILogger logger)
        {
            AreaTitleController = Grab(preloadedObjects, "White_Palace_18", "Area Title Controller", logger);
            PlayMakerUnity2D = Grab(preloadedObjects, "White_Palace_18", "_Managers/PlayMaker Unity 2D", logger);
            SceneManagerPrefab = Grab(preloadedObjects, "White_Palace_18", "_SceneManager", logger);

            // 长椅（路径已实测可用）
            BenchPrefab = Grab(preloadedObjects, "Crossroads_47", "RestBench", logger);

            // 小怪（路径已实测；Crossroads_01 提供 3 种，苍蝇从 Crossroads_07 单独预载）
            ZombieRunnerPrefab = GrabFirst(preloadedObjects, "Crossroads_01", ZombieRunnerPaths, logger);
            CrawlerPrefab = GrabFirst(preloadedObjects, "Crossroads_01", CrawlerPaths, logger);
            ClimberPrefab = GrabFirst(preloadedObjects, "Crossroads_01", ClimberPaths, logger);
            FlyPrefab = GrabFirst(preloadedObjects, "Crossroads_07", FlyPaths, logger);
        }

        private static GameObject Grab(
            Dictionary<string, Dictionary<string, GameObject>> preloaded,
            string scene, string path, ILogger logger, bool quiet = false)
        {
            if (preloaded == null || !preloaded.ContainsKey(scene))
            {
                if (!quiet) logger.LogError(string.Format("[HKCS] 预载里没有场景 {0}", scene));
                return null;
            }
            if (!preloaded[scene].ContainsKey(path))
            {
                if (!quiet)
                {
                    logger.LogError(string.Format("[HKCS] 预载 {0} 里没有 {1}", scene, path));
                    // 路径写错时，把「这个场景里实际预载成功的路径」打出来，方便一眼看出该换成什么名字
                    logger.LogError(string.Format("[HKCS] {0} 里预载成功的路径有：{1}",
                        scene, string.Join(" | ", new List<string>(preloaded[scene].Keys).ToArray())));
                }
                return null;
            }

            GameObject go = UObject.Instantiate(preloaded[scene][path]);
            go.name = path.Replace("/", "_");
            UObject.DontDestroyOnLoad(go);
            go.SetActive(false);
            return go;
        }

        /// <summary>按候选列表依次尝试，返回第一个能用的；命中哪个会打进日志。</summary>
        private static GameObject GrabFirst(
            Dictionary<string, Dictionary<string, GameObject>> preloaded,
            string scene, string[] paths, ILogger logger)
        {
            foreach (string path in paths)
            {
                GameObject go = Grab(preloaded, scene, path, logger, quiet: true);
                if (go != null)
                {
                    logger.Log(string.Format("[HKCS] {0}：命中预载路径 {1}", scene, path));
                    return go;
                }
            }
            logger.LogError(string.Format(
                "[HKCS] {0} 里没有一条候选路径命中（{1}）—— 「打包测试」后看 ModLog 里 [HKCS][Dump] 打出来的真实路径，改 PrefabHolder 里的候选数组",
                scene, string.Join(" | ", paths)));
            return null;
        }
    }
}
