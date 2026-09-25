using UnityEngine;
using UObject = UnityEngine.Object;

namespace HKCustomSceneMod.Patchers
{
    /// <summary>
    /// 在自定义房间里放**原版小怪**。
    ///
    /// Unity 侧用法：房间里放一个空物体，挂上这个组件，摆在你要刷怪的位置（脚下要有地面）。
    ///   · Kind 选怪种（爬虫 / 复仇苍蝇）；
    ///   · Id 填一个**唯一**的编号（1、2、3…），同一间房每只怪都不一样。
    ///
    /// 为什么必须唯一（反编译 PersistentBoolItem.SetMyID 得到）：
    ///     if (string.IsNullOrEmpty(persistentBoolData.id)) persistentBoolData.id = name;
    /// 存档里的击杀记录用「物体名」当 id —— 两只同名的怪会共用一个 id，
    /// 杀掉一只，另一只在重进房间后也会一起消失。这里强制把 id 写成唯一名字，绕开这个坑。
    ///
    /// 注意：怪需要地面/碰撞体才走得动；2D 触发器依赖场景里的 `_Managers/PlayMaker Unity 2D`（本工程已有）。
    /// </summary>
    public class PatchEnemy : MonoBehaviour
    {
        public enum EnemyKind
        {
            /// <summary>爬虫：Crossroads_01 里的 "Zombie Runner"</summary>
            ZombieRunner = 0,
            /// <summary>复仇苍蝇：Crossroads_01 里的 "Fly"</summary>
            Fly = 1,
        }

        /// <summary>要放哪种怪。</summary>
        public EnemyKind Kind = EnemyKind.ZombieRunner;

        /// <summary>唯一编号（同房间内不要重复）。留空则用序号自动生成一个名字。</summary>
        public string Id = "";

        public void Awake()
        {
            GameObject prefab = (Kind == EnemyKind.Fly) ? PrefabHolder.FlyPrefab : PrefabHolder.ZombieRunnerPrefab;
            if (prefab == null)
            {
                Debug.LogError("[HKCS] 没预载到这只怪，放不了（Kind=" + Kind +
                               "）。检查 GetPreloadNames 里 Crossroads_01 的那两条 —— 路径不对时 API 会打 " +
                               "\"could not load 'Crossroads_01/<路径>.prefab'\"，把红字发出来换名字即可。");
                return;
            }

            GameObject enemy = UObject.Instantiate(prefab);
            enemy.name = "HKCS_" + Kind + "_" + (string.IsNullOrEmpty(Id) ? "0" : Id);

            // 把击杀存档的 id / 场景名写死成我们自己的，避免和原版场景、或者同房间其它同名怪撞车
            PersistentBoolItem pbi = enemy.GetComponent<PersistentBoolItem>();
            if (pbi != null && pbi.persistentBoolData != null)
            {
                pbi.persistentBoolData.id = enemy.name;
                pbi.persistentBoolData.sceneName = GameManager.GetBaseSceneName(gameObject.scene.name);
            }

            enemy.transform.position = transform.position;
            enemy.SetActive(true);   // 预载出来的 prefab 是 inactive 的，字段设完再开

            Modding.Logger.Log(string.Format("[HKCS] 放了小怪 {0} @ ({1}, {2})",
                enemy.name, enemy.transform.position.x, enemy.transform.position.y));
        }
    }
}
