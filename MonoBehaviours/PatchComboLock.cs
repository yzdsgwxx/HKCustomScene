using UnityEngine;

namespace HKCustomSceneMod.Patchers
{
    /// <summary>
    /// Unity 侧空壳（字段/枚举必须和 FinalMod/Patchers/PatchComboLock.cs **逐字一致**）。
    ///
    /// 「密码锁」区域：按 <see cref="Sequence"/> 里**数组的顺序**依次做出动作就算解开
    /// （例如 `上 上 下 下 左 右 左 右`），然后走死亡式流程：**在目标椅子上醒来**。
    ///
    /// · 出手方向由原版状态位判定：上劈 / 下劈（**只算空中的那一下**）/ 横挥按朝向分左右；
    ///   跳按朝向分左右。
    /// · 顺序错了默认**只忽略**（勾 <see cref="ResetOnWrong"/> 才清零重来）。
    /// · <see cref="AnywhereInScene"/> = 不限区域（整场景任何地方做完都算，"后门"用）。
    /// </summary>
    public class PatchComboLock : MonoBehaviour
    {
        /// <summary>密码动作的六种。</summary>
        public enum ComboDir
        {
            /// <summary>向上攻击。</summary>
            AttackUp = 0,
            /// <summary>向下攻击（**只算玩家在空中的下劈**）。</summary>
            AttackDown = 1,
            /// <summary>向左攻击。</summary>
            AttackLeft = 2,
            /// <summary>向右攻击。</summary>
            AttackRight = 3,
            /// <summary>面向左跳。</summary>
            JumpLeft = 4,
            /// <summary>面向右跳。</summary>
            JumpRight = 5,
        }

        /// <summary>
        /// **密码（按顺序）**。例如左上角的 `+` 加号依次填：AttackUp, AttackUp, AttackDown, AttackDown,
        /// AttackLeft, AttackRight, AttackLeft, AttackRight（= 上上下下左右左右）。
        /// 空数组 = 没有密码（这个锁会被忽略）。
        /// </summary>
        public ComboDir[] Sequence = new ComboDir[0];

        /// <summary>判定范围（以本物体为中心的矩形宽高，单位=格）。选中时会画出来。</summary>
        public Vector2 ZoneSize = new Vector2(6f, 6f);

        /// <summary>顺序错了是否清零重来（默认 false = 只忽略）。</summary>
        public bool ResetOnWrong = false;

        /// <summary>离开范围是否清零（默认 true）。</summary>
        public bool ResetOnLeave = true;

        /// <summary>true = 不限区域，在这个场景任何地方做完都算（后门/保险用，ZoneSize 被忽略）。</summary>
        public bool AnywhereInScene = false;

        /// <summary>完成后等几秒再触发（留时间给动画）。</summary>
        public float Delay = 1.5f;

        /// <summary>送到哪个场景（默认 `Town` = 德特茅斯）。</summary>
        public string TargetScene = "Town";

        /// <summary>送到哪把椅子（德特茅斯 = `RestBench`）。</summary>
        public string TargetBench = "RestBench";

        /// <summary>解开一次后失效（默认 false = 可反复用）。</summary>
        public bool OneShot = false;

        /// <summary>把每次输入都打进 ModLog（排查密码时打开）。</summary>
        public bool Verbose = false;

        /// <summary>Scene 视图里把判定范围画出来（只在编辑器里跑）。</summary>
        private void OnDrawGizmosSelected()
        {
            if (AnywhereInScene)
            {
                Gizmos.color = new Color(0.5f, 1f, 0.6f, 0.5f);
                Gizmos.DrawWireCube(transform.position, new Vector3(40f, 20f, 0.1f));
                return;
            }
            Gizmos.color = new Color(1f, 0.9f, 0.4f, 0.9f);
            Gizmos.DrawWireCube(transform.position, new Vector3(ZoneSize.x, ZoneSize.y, 0.1f));
        }
    }
}
