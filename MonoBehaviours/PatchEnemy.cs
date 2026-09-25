using UnityEngine;

namespace HKCustomSceneMod.Patchers
{
    /// <summary>
    /// Unity 侧空壳。字段/枚举必须和 FinalMod/Patchers/PatchEnemy.cs 里
    /// **逐字一致**（名字和类型必须一样），因为 Unity 是按字段名序列化进 .unity 的。
    /// 真正逻辑只在 FinalMod 里。
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

        public EnemyKind Kind = EnemyKind.ZombieRunner;
        public string Id = "";

        public void Awake()
        {
            // 空：真正实现只在 FinalMod 里
        }
    }
}
