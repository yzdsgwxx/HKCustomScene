using HutongGames.PlayMaker;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace HKCustomSceneMod.Patchers
{
    /// <summary>
    /// 在自定义房间里放一把**原版长椅（存档点）**。
    ///
    /// Unity 侧用法：在房间里放一个空物体，挂上这个组件，把空物体摆到你要放长椅的位置即可。
    ///
    /// 原理（反编译游戏 dll 得到，参考实现是 Benchwarp 的 BenchMaker）：
    ///   · 长椅必须靠预载克隆，Unity 里造不出来 —— 它的精灵、trigger、
    ///     以及名为 "Bench Control" 的 PlayMaker FSM（状态 Idle / In Range / Rest Burst）
    ///     全在原版 prefab 里。**坐下和存档的逻辑就在那个 FSM 的 Rest Burst 状态里**，
    ///     它会用「当前场景名」+「长椅物体名」写进 PlayerData.respawnScene / respawnMarkerName。
    ///   · 所以这里不需要写任何存档代码，只要：唯一名字 + tag=RespawnPoint + 位置 (z≈0.02)。
    ///   · 存档点 = 场景名 + 长椅物体名。同一间房放多把椅子时，BenchName 必须各不相同。
    /// </summary>
    public class PatchBench : MonoBehaviour
    {
        /// <summary>长椅的物体名。它同时是存档里的 respawnMarkerName，同一房间多把椅子要取不同名字。</summary>
        public string BenchName = "HKCS_Bench";

        /// <summary>
        /// 长椅的深度（z）。
        ///   · 原版长椅是 **0.02**（贴在地形精灵前面一点点）；
        ///   · 相机在 -z 方向看 ⇒ **z 越小越靠前**；
        ///   · ⚠ 如果房间地形是 z=0 的**实心 mesh**（比如 .obj 模型，Standard 材质不透明），
        ///     长椅放在 +z 会被地形挡在后面看不见 —— 这时要放**负值**（本工程默认 -0.5）。
        /// 想确认效果：进游戏看长椅有没有被地形吃掉，或看 Unity Scene 视图里图标和地形的前后关系。
        /// </summary>
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
            bench.transform.position = new Vector3(p.x, p.y, Z);   // z 由 Z 字段控制（见字段注释）

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

            // 注意：这个"摆放点"空物体**不销毁** —— 留在场景里方便你进游戏时用 DebugMod/UnityExplorer 找到它
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
