using HutongGames.PlayMaker;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace HKCustomSceneMod.Patchers
{
    /// <summary>
    /// 在自定义房间里放一把**原版长椅（存档点）**。
    ///
    /// Unity 侧用法：房间里放一个空物体，挂上这个组件，把空物体摆在**长椅要站的那块地面**上。
    /// 高度会**自动对齐**（见下），一般不用手调。
    ///
    /// 为什么需要对齐：原版长椅 prefab 的**轴心不在脚底**，而是在座位附近
    /// （实测 trigger 的 offset.y ≈ -0.59，也就是"骑士站的那块地"在轴心下方约 0.6）。
    /// 直接把轴心放在地面高度，长椅就会陷进地面约 0.6。所以这里读长椅自带的 BoxCollider2D
    /// （就是那个 trigger）的 offset.y，把**trigger 中心**对齐到摆放点 ⇒ 脚自然落在地面上。
    ///
    /// 深度 z **固定用原版长椅的 0.02**，不自己改显示层级。
    ///
    /// 原理（反编译游戏 dll + 参考 Benchwarp 的 BenchMaker）：长椅必须靠预载克隆，Unity 里造不出来；
    /// 坐下/存档逻辑在它自带的 "Bench Control" FSM 里，用「当前场景名 + 长椅物体名」写
    /// PlayerData.respawnScene / respawnMarkerName，所以这里不用写任何存档代码。
    /// </summary>
    public class PatchBench : MonoBehaviour
    {
        /// <summary>长椅的物体名。同时是存档里的 respawnMarkerName，同一房间多把椅子要取不同名字。</summary>
        public string BenchName = "HKCS_Bench";

        /// <summary>
        /// 手动微调（世界单位，正数=往上抬）。自动对齐之后如果还差一点点才用；
        /// 进游戏看 ModLog 里"长椅本体…世界边界 y …；摆放点 y=…"那行来定。
        /// </summary>
        public float YOffset = 0f;

        /// <summary>
        /// 坐下位置的微调（原版 FSM 里的 "Adjust Vector"）。留 (0,0,0) 表示不改动原值。
        /// 如果坐下后骑士位置明显偏了，就填一个小偏移量试。
        /// </summary>
        public Vector3 AdjustVector = Vector3.zero;

        /// <summary>原版长椅的深度（显示层级），照抄不动。</summary>
        private const float BenchZ = 0.02f;

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

            // ── 高度：把长椅自带的 trigger（骑士站的那块地）中心对齐到摆放点 ──
            Vector3 p = transform.position;
            float triggerOffsetY = 0f;
            BoxCollider2D trigger = bench.GetComponent<BoxCollider2D>();
            if (trigger != null) triggerOffsetY = trigger.offset.y;

            bench.transform.position = new Vector3(p.x, p.y - triggerOffsetY + YOffset, BenchZ);

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
                "[HKCS] 放了长椅 {0} @ ({1:0.###}, {2:0.###}, {3:0.###})，FSM={4}，triggerOffsetY={5:0.###}（已按它对齐到摆放点，YOffset={6:0.###}）",
                bench.name, bench.transform.position.x, bench.transform.position.y, bench.transform.position.z,
                (fsm != null) ? "OK" : "缺失", triggerOffsetY, YOffset));

            LogBodyBounds(bench, p);
        }

        /// <summary>
        /// 只测**长椅本体**（根物体上的 SpriteRenderer）的世界边界 —— 不要用
        /// GetComponentsInChildren 求并集：长椅的子物体里有光晕(Lit)之类的大 sprite，
        /// 会把边界撑到十几单位宽（上一版就是这么打出假数据的）。这里顺便把宽度报出来，
        /// 编辑器里 Gizmo 用的 BenchWidth 可以照着改成实测值。
        /// </summary>
        private static void LogBodyBounds(GameObject bench, Vector3 placement)
        {
            SpriteRenderer sr = bench.GetComponent<SpriteRenderer>();
            if (sr == null) return;

            Bounds b = sr.bounds;
            Modding.Logger.Log(string.Format(
                "[HKCS] 长椅本体(sprite={0}) 世界边界 x {1:0.###}~{2:0.###}（宽 {3:0.###}），y {4:0.###}~{5:0.###}；摆放点 y={6:0.###} ⇒ 脚相对摆放点 {7:0.###}（负数=陷下去）",
                (sr.sprite != null) ? sr.sprite.name : "null",
                b.min.x, b.max.x, b.size.x, b.min.y, b.max.y, placement.y, b.min.y - placement.y));

            WriteMetricsForEditor(b, placement);
        }

        /// <summary>
        /// 把游戏里**实测**的长椅尺寸/轴心偏移写给 Unity 编辑器，
        /// 让 `HKCSPlacementPreview` 的预览物体和游戏里完全一致（位置和大小都不用猜）。
        /// 路径：%USERPROFILE%\AppData\LocalLow\Team Cherry\Hollow Knight\HKCS_bench_metrics.txt
        /// </summary>
        private static void WriteMetricsForEditor(Bounds b, Vector3 placement)
        {
            try
            {
                string dir = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    "Low", "Team Cherry", "Hollow Knight");
                if (!System.IO.Directory.Exists(dir)) return;

                string path = System.IO.Path.Combine(dir, "HKCS_bench_metrics.txt");
                string text = string.Format(
                    "# 由 CustomSceneMod 写入，供 Unity 编辑器预览用（HKCSPlacementPreview 每 2 秒读一次）\n" +
                    "sprite={0}\n" +
                    "width={1:0.#####}\n" +
                    "height={2:0.#####}\n" +
                    "originLift={3:0.#####}\n" +
                    "scene={4}\n",
                    (b.size.x > 0f) ? "measured" : "measured",
                    b.size.x, b.size.y,
                    (b.center.y - placement.y),   // 精灵中心相对摆放点的高度差 = 轴心偏移
                    GameManager.instance != null ? GameManager.instance.sceneName : "?");

                System.IO.File.WriteAllText(path, text);
            }
            catch (System.Exception e)
            {
                Modding.Logger.Log("[HKCS] 写预览数据失败（不影响游戏）：" + e.Message);
            }
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
