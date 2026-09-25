using UnityEngine;

namespace HKCustomSceneMod.Patchers
{
    /// <summary>Unity 侧空壳。字段与 FinalMod 版本保持一致。</summary>
    public class SceneMapPatcher : MonoBehaviour
    {
        public Texture tex;

        public void Start()
        {
            // 空：真正实现只在 FinalMod 里
        }
    }
}
