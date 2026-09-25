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

        public static void Preloaded(
            Dictionary<string, Dictionary<string, GameObject>> preloadedObjects,
            ILogger logger)
        {
            AreaTitleController = Grab(preloadedObjects, "White_Palace_18", "Area Title Controller", logger);
            PlayMakerUnity2D = Grab(preloadedObjects, "White_Palace_18", "_Managers/PlayMaker Unity 2D", logger);
            SceneManagerPrefab = Grab(preloadedObjects, "White_Palace_18", "_SceneManager", logger);
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
