using UnityEngine;

namespace HKCustomSceneMod.Patchers
{
    /// <summary>
    /// Unity 侧空壳。字段必须和 FinalMod/Patchers/PatchGhost.cs 里
    /// **逐字一致**（名字和类型必须一样），因为 Unity 是按字段名序列化进 .unity 的。
    /// 真正逻辑只在 FinalMod 里。
    ///
    /// 用法：房间里放一个空物体 → 挂这个组件 → 摆在"灵魂怪"要站的位置。
    /// 运行时它会**自己造一只灵魂怪**（纯运行时对象，不进场景、不进 bundle）：
    ///   · 有精灵（<see cref="GhostSprite"/> 填了的话）+ 碰撞盒；
    ///   · 挂原版的 `EnemyDreamnailReaction` ⇒ **可以被梦钉抽**（原版梦钉的命中回调就靠这个组件）；
    ///   · 抽了**不消失**，而是走"从存档点醒来"的流程，把你送到 <see cref="TargetScene"/> 的
    ///     <see cref="TargetBench"/> 那把椅子上（黑屏 + 醒来动画，和死亡表现类似，但不会掉影子/掉钱）。
    /// </summary>
    public class PatchGhost : MonoBehaviour
    {
        /// <summary>灵魂的贴图（不填 = 看不见，只剩碰撞和梦钉反应）。</summary>
        public Sprite GhostSprite;

        /// <summary>
        /// **交互文本**（走到跟前按上/下说话时显示；**编辑器里可配**）。
        /// 默认那句是"出口灵魂怪"用的：对话一结束就传送。
        /// </summary>
        [TextArea(2, 6)]
        public string DialogueText = "虽然你亵渎了我，但你的勇敢征服了我，我原谅你了，回去吧";

        /// <summary>要送去的**房间场景名**（例如 HKCS_Room01）。</summary>
        public string TargetScene = "HKCS_Room01";

        /// <summary>要送去的**椅子名字**（那把长椅的 BenchName，例如 HKCS_Bench）。</summary>
        public string TargetBench = "HKCS_Bench";

        /// <summary>抽完等几秒再传送（留时间给黑屏/动画）。</summary>
        public float Delay = 1.5f;

        /// <summary>抽它的时候要不要给灵魂（默认不给，免得白送 33 点）。</summary>
        public bool GiveSoul = false;

        /// <summary>
        /// true = **注入到原版场景**（<see cref="SpawnScene"/> 那个场景）的 <see cref="SpawnPosition"/> 坐标；
        /// 本物体只当"配置卡"，摆在我们自己的场景里就行（原版场景的 .unity 不在工程里，只能运行时注入）。
        /// </summary>
        public bool SpawnInVanillaScene = false;

        /// <summary>要注入的原版场景名（默认 `Town` = 德特茅斯）。</summary>
        public string SpawnScene = "Town";

        /// <summary>注入坐标（世界坐标）。默认 = 原版掘墓者 (211.64, 8.33) 右边 4 格。</summary>
        public Vector2 SpawnPosition = new Vector2(215.64f, 8.33f);

        /// <summary>true = 克隆原版那只灵魂怪（掘墓者）的外观，长得一模一样；false = 用 <see cref="GhostSprite"/>。</summary>
        public bool UseVanillaLook = true;

        public void Awake()
        {
            // 空：真正实现只在 FinalMod 里
        }
    }
}
