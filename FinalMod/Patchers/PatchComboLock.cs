using System.Collections;
using UnityEngine;

namespace HKCustomSceneMod.Patchers
{
    /// <summary>
    /// 「密码锁」区域（逻辑）。**密码 = 一个有序数组** <see cref="Sequence"/>（例如 上上下下左右左右），
    /// 玩家必须在范围内（或 <see cref="AnywhereInScene"/> 时全场景）**按这个顺序**做完每一个动作。
    ///
    /// 判定用原版状态位（反编译 `HeroControllerStates`）：
    ///   上劈 `upAttacking`｜下劈 `downAttacking` **且 `!onGround`**（只算空中下劈）｜
    ///   横挥 `attacking` + `facingRight`（分左右）｜跳 `jumping` 上升沿 + `facingRight`（分左右）。
    /// 每个动作都是**上升沿**（一次挥砍/一次起跳只记一次）。
    ///
    /// 解完 ⇒ 先写好长椅重生点，然后**走真正的死亡流程** `HeroController.Die()`
    /// ⇒ 播死亡动画 → 黑屏 → **在目标椅子上醒来**（默认德特茅斯 `Town`/`RestBench`）。
    /// ⚠ 这是真死亡：会掉影子、掉钱（影子留在原地）。用户明确要"死亡动画"，所以走这条路。
    /// </summary>
    public class PatchComboLock : MonoBehaviour
    {
        /// <summary>密码动作的六种（数值会存进 .unity，**只能往后追加**）。</summary>
        public enum ComboDir
        {
            /// <summary>向上攻击。</summary>
            AttackUp = 0,
            /// <summary>向下攻击（只算空中下劈）。</summary>
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

        /// <summary>密码（按数组顺序）。空 = 忽略这个锁。</summary>
        public ComboDir[] Sequence = new ComboDir[0];

        /// <summary>判定范围（以本物体为中心的矩形宽高）。</summary>
        public Vector2 ZoneSize = new Vector2(6f, 6f);

        /// <summary>顺序错了是否清零重来。</summary>
        public bool ResetOnWrong = false;

        /// <summary>离开范围是否清零。</summary>
        public bool ResetOnLeave = true;

        /// <summary>true = 不限区域（整场景任何地方做完都算，后门用）。</summary>
        public bool AnywhereInScene = false;

        /// <summary>完成后延迟几秒再触发。</summary>
        public float Delay = 1.5f;

        /// <summary>目的地场景（默认德特茅斯）。</summary>
        public string TargetScene = "Town";

        /// <summary>目的地椅子名（德特茅斯 = RestBench）。</summary>
        public string TargetBench = "RestBench";

        /// <summary>解开一次后失效。</summary>
        public bool OneShot = false;

        /// <summary>每次输入都打日志。</summary>
        public bool Verbose = false;

        // ── 内部 ──
        private int _index;              // 已经匹配到密码的第几个
        private bool _prevAnyAttack;
        private bool _prevJumping;
        private bool _inZoneLastFrame;
        private bool _done;
        private bool _busy;
        private bool _warnedEmpty;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.9f, 0.4f, 0.9f);
            Gizmos.DrawWireCube(transform.position, new Vector3(ZoneSize.x, ZoneSize.y, 0.1f));
        }

        private void Update()
        {
            if (_busy) return;
            if (_done && OneShot) return;

            if (Sequence == null || Sequence.Length == 0)
            {
                if (!_warnedEmpty)
                {
                    _warnedEmpty = true;
                    Modding.Logger.LogError("[HKCS] 密码锁的 Sequence 是空的 —— 没有密码，已忽略这个锁" +
                                            "（防止玩家随便动一下就触发）");
                }
                return;
            }

            HeroController hero = HeroController.instance;
            if (hero == null) return;

            bool inside = AnywhereInScene || Inside(hero.transform.position);
            if (!inside)
            {
                if (_inZoneLastFrame && ResetOnLeave && !_done) ResetProgress("离开区域");
                _inZoneLastFrame = false;
                _prevAnyAttack = false;
                _prevJumping = false;
                return;
            }
            _inZoneLastFrame = true;

            HeroControllerStates st = hero.cState;
            if (st == null) return;

            // 攻击：上升沿，四方向
            bool anyAttack = st.attacking || st.upAttacking || st.downAttacking;
            if (anyAttack && !_prevAnyAttack)
            {
                ComboDir dir;
                bool valid = true;
                if (st.upAttacking) dir = ComboDir.AttackUp;
                else if (st.downAttacking) { dir = ComboDir.AttackDown; valid = !st.onGround; }   // 只算空中下劈
                else dir = st.facingRight ? ComboDir.AttackRight : ComboDir.AttackLeft;

                if (valid) Register(dir);
                else if (Verbose) Modding.Logger.Log("[HKCS] 密码锁：地面下劈不计入（下攻击只算空中的那一下）");
            }
            _prevAnyAttack = anyAttack;

            // 跳：上升沿，两方向
            if (st.jumping && !_prevJumping)
            {
                Register(st.facingRight ? ComboDir.JumpRight : ComboDir.JumpLeft);
            }
            _prevJumping = st.jumping;
        }

        private bool Inside(Vector3 p)
        {
            Vector3 c = transform.position;
            return Mathf.Abs(p.x - c.x) <= ZoneSize.x * 0.5f
                && Mathf.Abs(p.y - c.y) <= ZoneSize.y * 0.5f;
        }

        private static string DirName(ComboDir d)
        {
            switch (d)
            {
                case ComboDir.AttackUp: return "上攻击";
                case ComboDir.AttackDown: return "下攻击";
                case ComboDir.AttackLeft: return "左攻击";
                case ComboDir.AttackRight: return "右攻击";
                case ComboDir.JumpLeft: return "左跳";
                default: return "右跳";
            }
        }

        /// <summary>把整串密码打成一行，方便核对（例如 `上攻击 上攻击 下攻击 下攻击 左攻击 右攻击`）。</summary>
        private string SequenceText()
        {
            string s = "";
            for (int i = 0; i < Sequence.Length; i++)
            {
                if (i > 0) s += " ";
                s += DirName(Sequence[i]);
            }
            return s;
        }

        private void Register(ComboDir dir)
        {
            if (Verbose) Modding.Logger.Log(string.Format("[HKCS] 密码锁输入：{0}（进度 {1}/{2}）",
                DirName(dir), _index, Sequence.Length));

            if (dir == Sequence[_index])
            {
                _index++;
                Modding.Logger.Log(string.Format("[HKCS] 密码锁 正确 {0}/{1}（{2}）",
                    _index, Sequence.Length, DirName(dir)));

                if (_index >= Sequence.Length)
                {
                    Modding.Logger.Log("[HKCS] 密码锁 全串完成：" + SequenceText());
                    Complete();
                }
                else if (Verbose)
                {
                    Modding.Logger.Log("[HKCS] 下一个要做的动作：" + DirName(Sequence[_index]));
                }
                return;
            }

            // 顺序不对
            if (ResetOnWrong)
            {
                ResetProgress("做错了");
            }
            else if (Verbose)
            {
                Modding.Logger.Log("[HKCS] 密码锁 顺序不对（这一步应该是 " + DirName(Sequence[_index]) + "），已忽略");
            }
        }

        private void ResetProgress(string why)
        {
            _index = 0;
            Modding.Logger.Log("[HKCS] 密码锁 进度清零（" + why + "）");
        }

        private void Complete()
        {
            if (_done) return;
            _done = true;
            _busy = true;
            Modding.Logger.Log(string.Format("[HKCS] 密码锁打开！{0:0.##} 秒后死亡并送往 {1}:{2}",
                Delay, TargetScene, TargetBench));
            StartCoroutine(TeleportRoutine());
        }

        private IEnumerator TeleportRoutine()
        {
            if (Delay > 0f) yield return new WaitForSeconds(Delay);

            if (PlayerData.instance == null || GameManager.instance == null || HeroController.instance == null)
            {
                Modding.Logger.LogError("[HKCS] PlayerData/GameManager/HeroController 为空，取消");
                _busy = false;
                yield break;
            }

            // 先把"长椅重生点"指到目标椅子，再走**原版的死亡流程**（会播死亡动画、黑屏）
            //   Cecil 实测：`GameManager.PlayerDead(float)` 是 public；`HeroController.Die()` 是 private 拿不到
            PlayerData.instance.SetBenchRespawn(TargetBench, TargetScene, true);
            Modding.Logger.Log("[HKCS] 触发死亡流程（播死亡动画 → 在椅子上醒来）");
            GameManager.instance.StartCoroutine(GameManager.instance.PlayerDead(0.5f));

            yield return new WaitForSeconds(2f);
            _busy = false;
            if (OneShot) enabled = false;
        }
    }
}
