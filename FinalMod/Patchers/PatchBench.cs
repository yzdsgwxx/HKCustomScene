using HutongGames.PlayMaker;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace HKCustomSceneMod.Patchers
{
    /// <summary>
    /// 在自定义房间里放一把**原版长椅（存档点）**。
    ///
    /// Unity 侧用法：房间里放一个空物体，挂上这个组件，把空物体摆在**长椅要站的那块地面**上。
    ///
    /// 三个字段别搞混：
    ///   · BenchName —— 长椅物体名，同时是存档里的 respawnMarkerName；同房间多把椅子必须不同名。
    ///   · YOffset   —— **高度补偿**。原版长椅 prefab 的轴心不在脚底（在座位附近，约高 0.6，
    ///                  见 Benchwarp 的 triggerOffset.y ≈ -0.59），所以轴心放地面高度时**长椅会陷进地面**。
    ///                  这个值把它整体上抬，让脚落在地面上。摆放点 y = 地面 ⇒ 一般填 0.5~0.7。
    ///                  进游戏后日志会打长椅的实测上下边界，差多少照着微调即可。
    ///   · Z         —— **深度（前后）**，和高度无关。相机在 -z 方向看 ⇒ z 越小越靠前；
    ///                  房间地形若是 z=0 的实心 mesh（.obj），长椅要放负值才不会被地形挡住。
    ///
    /// 原理（反编译游戏 dll + 参考 Benchwarp 的 BenchMaker）：长椅必须靠预载克隆，Unity 里造不出来；
    /// 坐下/存档逻辑在它自带的 "Bench Control" FSM 里，用「当前场景名 + 长椅物体名」写
    /// PlayerData.respawnScene / respawnMarkerName，所以这里不用写任何存档代码。
    /// </summary>
    public class PatchBench : MonoBehaviour
    {
        /// <summary>长椅的物体名。同时是存档里的 respawnMarkerName，同一房间多把椅子要取不同名字。</summary>
        public string BenchName = "HKCS_Bench";

        /// <summary>高度补偿：把长椅从摆放点（=地面）往上抬这么多，脚才落在地面上。见类注释。</summary>
        public float YOffset = 0.5f;

        /// <summary>深度 z：相机在 -z 看 ⇒ z 越小越靠前。地形是 z=0 的实心 mesh 时用负值。</summary>
        public float Z = -0.5f;

        /// <summary>
        /// 坐下位置的微调（原版 FSM 里的 "Adjust Vector"）。留 (0,0,0) 表示不改动原值。
        /// 如果坐下后骑士位置明显偏了，就填一个小偏移量试。
        /// </summary>
        public Vector3 AdjustVector = Vector3.zero;

        public void Awake()
        {
            if (PrefabHolder.BenchPrefab == null)
            {
                Debug.LogError("[HKCS] 没预载到长椅，放不了。检查 GetPreloadNames 里有没有 Crossroads_47 / RestBench");
                return;
            }

            GameObject bench = UObject.Instantiate(PrefabHolder.BenchPrefab);
            bench.name = string.IsNullOrEmpty(BenchName) ? "HKCS_Bench" : BenchName;

            // 原版长椅就是这个 tag（Benchwarp 的 MakeDeployedBench 也是这么设的）
            bench.tag = "RespawnPoint";

            Vector3 p = transform.position;
            // y = 摆放点 + 高度补偿；z 由 Z 控制（深度，和高度无关）
            bench.transform.position = new Vector3(p.x, p.y + YOffset, Z);

            PlayMakerFSM fsm = FindBenchFsm(bench);
            if (fsm == null)
            {
                Debug.LogWarning("[HKCS] 长椅里没找到 \"Bench Control\" FSM —— 坐下/存档可能不会生效");
            }
            else if (AdjustVector != Vector3.zero)
            {
                FsmVector3 v = fsm.FsmVariables.GetFsmVector3("Adjust Vector");
                if (v != null) v.Value = AdjustVector;
                else Debug.LogWarning("[HKCS] 长椅 FSM 里没有 Adjust Vector 变量");
            }

            bench.SetActive(true);   // 预载出来的 prefab 是 inactive 的，字段设完再开

            Modding.Logger.Log(string.Format(
                "[HKCS] 放了长椅 {0} @ ({1}, {2}, {3})，FSM={4}",
                bench.name, bench.transform.position.x, bench.transform.position.y, bench.transform.position.z,
                (fsm != null) ? "OK" : "缺失"));

            LogBounds(bench, p);
        }

        /// <summary>
        /// 把长椅的真实画面上下边界打进日志 —— 用来把 YOffset 一次调准：
        /// 「脚比摆放点低 N」⇒ 把 YOffset 再加上 N 就贴地。
        /// </summary>
        private static void LogBounds(GameObject bench, Vector3 placement)
        {
            Renderer[] rs = bench.GetComponentsInChildren<Renderer>(true);
            if (rs == null || rs.Length == 0) return;

            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);

            Modding.Logger.Log(string.Format(
                "[HKCS] 长椅实测边界 y {0:0.###} ~ {1:0.###}，宽 {2:0.###}；摆放点 y={3:0.###} ⇒ 脚比摆放点低 {4:0.###}（把 YOffset 加上这个数就贴地）",
                b.min.y, b.max.y, b.size.x, placement.y, placement.y - b.min.y));
        }

        /// <summary>长椅的 FSM 在根物体或子物体上，且可能不是唯一一个，所以按名字找。</summary>
        private static PlayMakerFSM FindBenchFsm(GameObject bench)
        {
            PlayMakerFSM[] fsms = bench.GetComponentsInChildren<PlayMakerFSM>(true);
            if (fsms != null)
            {
                foreach (PlayMakerFSM f in fsms)
                {
                    if (f != null && f.FsmName == "Bench Control") return f;
                }
            }
            return null;
        }
    }
}
