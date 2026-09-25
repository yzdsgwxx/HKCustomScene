using UnityEngine;

namespace HKCustomSceneMod.Patchers
{
    /// <summary>
    /// Unity 侧空壳（字段必须和 FinalMod/Patchers/PatchVanillaProp.cs **逐字一致**）。
    ///
    /// 「把原版任意物体克隆到我的场景里」—— 针对光效/雕像/装饰这类**美术件**。
    /// 说明：原版美术**不能**直接拷进 Unity 工程（脚本/材质/粒子引用会断），
    /// 只能**运行时从原版场景预载 + 克隆**（和长椅/小怪/灵魂怪同一套办法）。
    ///
    /// 已经实测挖出来的光效（`GG_Workshop` = 众神殿堂，每个雕像都有一盏）：
    /// <code>
    ///   辐光雕像的聚光灯： GG_Workshop / GG_Statue_Radiance/Spotlight      ← 默认值，就是截图那种光效
    ///   别的雕像同理：     GG_Statue_MantisLords/Spotlight、GG_Statue_Hornet/Spotlight …
    /// </code>
    /// 用法：房间里放一个空物体 → 挂这个组件 → 填 <see cref="SourceScene"/> / <see cref="SourcePath"/>
    /// → 摆好位置（物体自己的 transform 就是锚点）→ 打包测试，进游戏就能看到那盏光。
    ///
    /// ⚠ 能用的路径必须先在 `PrefabHolder.VanillaPropPaths` 里登记（预载是启动时一次性做的）。
    /// 想加新的：在那边加一行 `("场景名", "层级路径")` 再重新编译即可（日志会提示哪条路径没命中）。
    /// </summary>
    public class PatchVanillaProp : MonoBehaviour
    {
        /// <summary>原版物体所在的**场景名**（例如 `GG_Workshop` = 众神殿堂）。</summary>
        public string SourceScene = "GG_Workshop";

        /// <summary>物体在场景里的**层级路径**（例如 `GG_Statue_Radiance/Spotlight`）。</summary>
        public string SourcePath = "GG_Statue_Radiance/Spotlight";

        /// <summary>位置微调（世界单位，正数=右/上）。</summary>
        public Vector2 Offset = Vector2.zero;

        /// <summary>整体缩放（1 = 原版大小）。</summary>
        public float Scale = 1f;

        /// <summary>深度 z。⚠ 道具 z 不能小于 0（骑士在 z=0）；0.02 这种小值 = 在骑士后面。</summary>
        public float Z = 0.02f;

        /// <summary>Scene 视图里标出锚点。</summary>
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.6f);
        }
    }
}
