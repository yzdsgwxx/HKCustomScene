using UnityEngine;

namespace HKCustomSceneMod.Patchers
{
    /// <summary>
    /// Unity 侧空壳。字段必须和 FinalMod/Patchers/PatchBench.cs 里
    /// **逐字一致**（名字和类型必须一样），因为 Unity 是按字段名序列化进 .unity 的。
    /// 真正逻辑只在 FinalMod 里。
    /// </summary>
    public class PatchBench : MonoBehaviour
    {
        public string BenchName = "HKCS_Bench";

        /// <summary>长椅深度 z。原版是 0.02；相机在 -z 看 ⇒ z 越小越靠前。
        /// 地形若是 z=0 的实心 mesh，长椅放 +z 会被挡住 ⇒ 用负值（本工程默认 -0.5）。</summary>
        public float Z = -0.5f;

        public Vector3 AdjustVector = Vector3.zero;

        public void Awake()
        {
            // 空：真正实现只在 FinalMod 里
        }
    }
}
