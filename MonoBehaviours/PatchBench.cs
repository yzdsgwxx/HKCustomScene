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

        /// <summary>手动微调（正数=往上抬）。高度默认会自动对齐到长椅自带的 trigger，一般不用填。</summary>
        public float YOffset = 0f;

        public Vector3 AdjustVector = Vector3.zero;

        public void Awake()
        {
            // 空：真正实现只在 FinalMod 里
        }
    }
}
