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

        /// <summary>高度补偿：把长椅从摆放点（=地面）往上抬这么多，脚才落在地面上。</summary>
        public float YOffset = 0.5f;

        /// <summary>深度 z：相机在 -z 看 ⇒ z 越小越靠前。</summary>
        public float Z = -0.5f;

        public Vector3 AdjustVector = Vector3.zero;

        public void Awake()
        {
            // 空：真正实现只在 FinalMod 里
        }
    }
}
