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
        public Vector3 AdjustVector = Vector3.zero;

        public void Awake()
        {
            // 空：真正实现只在 FinalMod 里
        }
    }
}
