using System.Collections;
using UnityEngine;
using HKCustomSceneMod.Consts;

namespace HKCustomSceneMod
{
    /// <summary>
    /// **全局后门**：不需要在场景里摆任何东西 —— 只要玩家在**本 Mod 自己的房间**里
    /// （`Consts/RoomNames.cs` 里登记过的场景）做出下面这串动作，就直接被送到**德特茅斯的椅子上**。
    /// 用途：防止玩家卡在迷宫里出不来（保险）；表现和正常出迷宫一样（黑屏 → 在椅子上醒来，不掉影子不掉钱）。
    ///
    /// **当前密码（按顺序做，见 <see cref="StepKinds"/>/<see cref="StepDirs"/>）**：
    ///   上攻击 ×2 → 左攻击 ×1 → 右攻击 ×1 → 左攻击 ×1 → 右攻击 ×1 → 左跳 ×1 → 右跳 ×1。
    ///
    /// 判定用的状态位和 <c>PatchComboLock</c> 完全一致（反编译 `HeroControllerStates`）：
    ///   上劈 `upAttacking`、下劈 `downAttacking` 且 `!onGround`（**只算空中下劈**）、
    ///   横挥 `attacking` + `facingRight`、跳 `jumping` 上升沿 + `facingRight`。
    ///
    /// ⚠ 想改密码/目的地就改下面 "配置" 那一段（改完重新编译 = 跑一次 `tools\打包测试.cmd`）。
    /// </summary>
    internal static class GlobalBackdoor
    {
        // ────────────────────────────────────────────────────────────────
        //  配置（改这里）
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// **密码 = 一个有顺序的动作表**：数组里**每一项就是一次动作**（同一个动作要做两次就写两项），
        /// 口径和 `PatchComboLock.Sequence` 完全一样，只是这里拆成"种类 + 方向"两个并行数组。
        ///
        /// 当前密码：上攻击、上攻击 → 左攻击、右攻击、左攻击、右攻击 → 左跳、右跳。
        /// </summary>
        private static readonly int[] StepKinds =
        {
            KindAttack, KindAttack,   // ① 上攻击 ×2
            KindAttack, KindAttack,   // ② 左攻击 → 右攻击
            KindAttack, KindAttack,   // ③ 左攻击 → 右攻击
            KindJump,   KindJump,     // ④ 左跳 → 右跳
        };

        /// <summary>密码：每一步的方向（与 <see cref="StepKinds"/> 一一对应，长度必须一致）。</summary>
        private static readonly int[] StepDirs =
        {
            DirUp,   DirUp,
            DirLeft, DirRight,
            DirLeft, DirRight,
            DirLeft, DirRight,
        };

        /// <summary>送到哪个场景（德特茅斯）。</summary>
        internal const string TargetScene = "Town";
        /// <summary>送到哪把椅子（德特茅斯长椅的物体名，实测）。</summary>
        internal const string TargetBench = "RestBench";
        /// <summary>完成后延迟几秒再传送（留时间给黑屏/动画）。</summary>
        internal const float Delay = 1.5f;
        /// <summary>把每次输入都打进 ModLog（排查用，玩家看不到）。</summary>
        internal const bool Verbose = true;

        // ────────────────────────────────────────────────────────────────
        //  状态
        // ────────────────────────────────────────────────────────────────
        private const int KindAttack = 0;
        private const int KindJump = 1;
        private const int DirUp = 0;
        private const int DirDown = 1;
        private const int DirLeft = 2;
        private const int DirRight = 3;

        private static int _stepIndex;         // 已经做对到密码的第几步
        private static bool _prevAnyAttack;
        private static bool _prevJumping;
        private static bool _enabled;     // 当前场景是不是"我们自己的房间"
        private static bool _busy;

        /// <summary>场景切换时调用：只在我们自己的房间里启用。</summary>
        internal static void SetScene(string sceneName)
        {
            bool was = _enabled;
            _enabled = Rooms.IsCustomRoom(sceneName);
            Reset();
            if (_enabled && !was)
            {
                Modding.Logger.Log("[HKCS] 全局后门已就绪（在" + sceneName + "里做出密码动作就能直接回德特茅斯）：" + OrderText());
            }
        }

        internal static void Reset()
        {
            _stepIndex = 0;
            _prevAnyAttack = false;
            _prevJumping = false;
        }

        /// <summary>每帧由 HeroController.Update 的钩子调用。</summary>
        internal static void Tick(HeroController hero)
        {
            if (!_enabled || _busy || hero == null) return;

            HeroControllerStates st = hero.cState;
            if (st == null) return;

            // 攻击：上升沿 + 四分方向（下劈只算空中的）
            bool anyAttack = st.attacking || st.upAttacking || st.downAttacking;
            if (anyAttack && !_prevAnyAttack)
            {
                int dir;
                bool valid = true;
                if (st.upAttacking) dir = DirUp;
                else if (st.downAttacking) { dir = DirDown; valid = !st.onGround; }
                else dir = st.facingRight ? DirRight : DirLeft;

                if (valid) Register(KindAttack, dir);
                else if (Verbose) Log("地面下劈不计入（下攻击只算空中的那一下）");
            }
            _prevAnyAttack = anyAttack;

            // 跳：上升沿 + 左右
            if (st.jumping && !_prevJumping) Register(KindJump, st.facingRight ? DirRight : DirLeft);
            _prevJumping = st.jumping;
        }

        private static string DirName(int kind, int dir)
        {
            if (kind == KindJump) return dir == DirRight ? "右跳" : "左跳";
            switch (dir)
            {
                case DirUp: return "上攻击";
                case DirDown: return "下攻击";
                case DirRight: return "右攻击";
                default: return "左攻击";
            }
        }

        /// <summary>把整串密码打成一行（例如 `上攻击 → 上攻击 → 左攻击 → 右攻击`），方便核对。</summary>
        private static string OrderText()
        {
            string s = "";
            for (int i = 0; i < StepKinds.Length && i < StepDirs.Length; i++)
            {
                if (i > 0) s += " → ";
                s += DirName(StepKinds[i], StepDirs[i]);
            }
            return s;
        }

        private static void Register(int kind, int dir)
        {
            if (Verbose) Log("输入：" + DirName(kind, dir));

            if (_stepIndex >= StepKinds.Length) return;   // 做完会 Reset，这里只是兜底

            if (kind != StepKinds[_stepIndex] || dir != StepDirs[_stepIndex])
            {
                // 顺序不对就忽略（后门不惩罚玩家：不清零、不重来）
                if (Verbose) Log("顺序不对（这一步应该是 " + DirName(StepKinds[_stepIndex], StepDirs[_stepIndex]) + "），已忽略");
                return;
            }

            _stepIndex++;
            if (Verbose) Log(string.Format("正确 {0}/{1}（{2}）", _stepIndex, StepKinds.Length, DirName(kind, dir)));

            if (_stepIndex < StepKinds.Length)
            {
                if (Verbose) Log("下一个要做：" + DirName(StepKinds[_stepIndex], StepDirs[_stepIndex]));
                return;
            }

            Fire();
        }

        private static void Fire()
        {
            _busy = true;
            Log("后门触发！" + Delay.ToString("0.##") + " 秒后送往 " + TargetScene + ":" + TargetBench);
            if (GameManager.instance != null) GameManager.instance.StartCoroutine(TeleportRoutine());
        }

        private static IEnumerator TeleportRoutine()
        {
            if (Delay > 0f) yield return new WaitForSeconds(Delay);

            if (PlayerData.instance != null && GameManager.instance != null)
            {
                PlayerData.instance.SetBenchRespawn(TargetBench, TargetScene, true);
                // 原版死亡流程（死亡动画 → 黑屏 → 在椅子上醒来）；PlayerDead 是 public，Die() 是 private
                GameManager.instance.StartCoroutine(GameManager.instance.PlayerDead(0.5f));
            }

            yield return new WaitForSeconds(1f);
            _busy = false;
            Reset();
        }

        private static void Log(string msg)
        {
            Modding.Logger.Log("[HKCS][后门] " + msg);
        }
    }
}
