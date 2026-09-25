using UnityEngine;

namespace HKCustomSceneMod.Patchers
{
    /// <summary>
    /// Unity 侧空壳。字段/枚举必须和 FinalMod/Patchers/PatchEnemy.cs 里
    /// **逐字一致**（名字、类型、枚举值的数字都必须一样），因为 Unity 是按字段名/枚举值序列化进 .unity 的。
    /// 真正逻辑只在 FinalMod 里。
    ///
    /// ⚠ 枚举**只能往后追加**：已经摆好的组件把数字存进了 .unity，插值/改名会让它们换种。
    /// </summary>
    public class PatchEnemy : MonoBehaviour
    {
        public enum EnemyKind
        {
            /// <summary>僵尸行者 —— Crossroads_01 的 _Enemies/Zombie Runner（1.22 × 1.63）：会冲锋。</summary>
            ZombieRunner = 0,
            /// <summary>复仇苍蝇 —— Crossroads_07 的 Uninfected Parent/Fly（0.89 × 0.84）：悬停 + 俯冲。</summary>
            Fly = 1,
            /// <summary>爬虫 —— Crossroads_01 的 _Enemies/Crawler 1（1.42 × 0.91）：贴地爬。</summary>
            Crawler = 2,
            /// <summary>攀爬虫 —— Crossroads_01 的 _Enemies/Climber（1.09 × 0.92）：贴墙爬。</summary>
            Climber = 3,
        }

        public EnemyKind Kind = EnemyKind.ZombieRunner;

        /// <summary>唯一编号。留空 = 用摆放点的物体名（一般不用填）。</summary>
        public string Id = "";

        public void Awake()
        {
            // 空：真正实现只在 FinalMod 里
        }
    }
}
