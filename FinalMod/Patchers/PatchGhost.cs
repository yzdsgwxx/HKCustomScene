using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace HKCustomSceneMod.Patchers
{
    /// <summary>
    /// 放一只**可以梦钉抽的灵魂怪**。两种用法：
    ///
    /// **A. 就地生成**（<see cref="SpawnInVanillaScene"/> = false）
    ///   空物体 + 本组件摆在**我们自己的场景**里，进这个场景时在摆放点生成。
    ///
    /// **B. 注入到原版场景**（<see cref="SpawnInVanillaScene"/> = true）★ 本轮新增
    ///   本组件摆在**我们自己的场景**里当"配置卡"，运行时往 <see cref="SpawnScene"/> 那个**原版场景**
    ///   的 <see cref="SpawnPosition"/> 坐标注入一只灵魂怪。原版场景每次加载都会重新生成
    ///   （场景一卸载它跟着销毁，不会跑到别的场景去）。
    ///   默认值就是**德特茅斯掘墓者旁边**：`Town`、`(215.64, 8.33)`
    ///   —— 原版那只 `_NPCs/Gravedigger NPC` 实测在 `(211.64, 8.33, 0.00)`。
    ///   <see cref="UseVanillaLook"/> = true 时**克隆原版那只的外观**（精灵/光晕/粒子/飘动，长得一模一样）。
    ///
    /// ── 机制（全部反编译实测）──────────────────────────────────────────────
    /// · 梦钉命中谁：英雄 FSM 的 `SendDreamImpact` → `GetComponent/GetComponentInParent&lt;EnemyDreamnailReaction&gt;()`
    ///   → `RecieveDreamImpact()`；命中盒在**第 19 层（Interactive Object）**、trigger
    ///   （原版灵魂怪就是用一个子物体 `Dreamnail Hit` 干这个）。
    /// · 文本：`RecieveDreamImpact` → 全局 FSM `Enemy Dream Msg → Display`，文本走语言表
    ///   ⇒ 由本 mod 的 `LanguageGetHook` 按 `HKCS_GHOST_&lt;摆放点名&gt;` 前缀提供，编辑器里填什么就显示什么。
    /// · 抽了**不消失**（原版那段"消失"逻辑被我们拆掉了），而是把长椅重生点改成
    ///   <see cref="TargetScene"/> / <see cref="TargetBench"/> 后走 `GameManager.ReadyForRespawn(false)`
    ///   = 淡出 + 在椅子上醒来（**不掉影子、不掉钱**）。
    /// </summary>
    public class PatchGhost : MonoBehaviour
    {
        /// <summary>灵魂的贴图（就地生成且 <see cref="UseVanillaLook"/> = false 时才用；不填 = 隐形）。</summary>
        public Sprite GhostSprite;

        /// <summary>梦钉抽出来的文本（编辑器里随便填；留空给一句占位）。</summary>
        public string DialogueText = "";

        /// <summary>要送去的房间场景名（例如 HKCS_Room01）。</summary>
        public string TargetScene = "HKCS_Room01";

        /// <summary>要送去的椅子名字（长椅的 BenchName，例如 HKCS_Bench）。</summary>
        public string TargetBench = "HKCS_Bench";

        /// <summary>抽完等几秒再传送（留时间给黑屏/动画）。</summary>
        public float Delay = 1.5f;

        /// <summary>抽它的时候要不要给灵魂（默认不给）。</summary>
        public bool GiveSoul = false;

        /// <summary>true = 注入到 <see cref="SpawnScene"/> 那个**原版场景**里（本物体只当配置卡）。</summary>
        public bool SpawnInVanillaScene = false;

        /// <summary>要注入的原版场景名（默认 `Town` = 德特茅斯）。</summary>
        public string SpawnScene = "Town";

        /// <summary>注入坐标（世界坐标）。默认 = 原版掘墓者 (211.64, 8.33) 右边 4 格。</summary>
        public Vector2 SpawnPosition = new Vector2(215.64f, 8.33f);

        /// <summary>true = 克隆原版灵魂怪（掘墓者）的外观，长得一模一样；false = 用 <see cref="GhostSprite"/>。</summary>
        public bool UseVanillaLook = true;

        // ────────────────────────────────────────────────────────────────
        //  内部
        // ────────────────────────────────────────────────────────────────

        /// <summary>梦钉文本的键前缀：<c>HKCS_GHOST_&lt;摆放点物体名&gt;</c>。</summary>
        internal const string KeyPrefix = "HKCS_GHOST_";

        /// <summary>原版梦钉命中盒所在的层（TagManager 第 19 层 = Interactive Object）。</summary>
        private const int DreamnailHitLayer = 19;

        /// <summary>原版灵魂怪根物体所在的层（第 13 层 = Hero Detector）。</summary>
        private const int GhostRootLayer = 13;

        private static readonly Dictionary<string, string> Convos = new Dictionary<string, string>();

        /// <summary>从克隆体 FSM 里抄下来的原版对话键（交互时会被换成我们的键）。</summary>
        private static readonly HashSet<string> _convoKeys = new HashSet<string>();

        /// <summary>入口灵魂怪的键（交互文本换成它）。</summary>
        internal static string EntryGhostKey = KeyPrefix + "Entry";

        /// <summary>要注入到原版场景的清单（由本组件的 Awake 登记）。</summary>
        private static readonly List<Request> Requests = new List<Request>();

        internal sealed class Request
        {
            public string Key;
            public string Scene;
            public Vector2 Pos;
            public bool VanillaLook;
            public Sprite Sprite;
            public string TargetScene;
            public string TargetBench;
            public float Delay;
            public bool GiveSoul;
        }

        /// <summary>
        /// **写死**一只灵魂怪（不需要在 Unity 里摆任何东西）—— 由 <c>CustomSceneMod.Initialize()</c> 调用。
        /// 用于"迷宫入口"：德特茅斯掘墓者旁边那只，文本固定。
        /// </summary>
        internal static void RegisterHardcoded(string key, string scene, Vector2 pos, bool vanillaLook,
                                               string text, string targetScene, string targetBench, float delay)
        {
            Convos[key] = string.IsNullOrEmpty(text) ? "……" : text;
            Requests.Add(new Request
            {
                Key = key,
                Scene = scene,
                Pos = pos,
                VanillaLook = vanillaLook,
                Sprite = null,
                TargetScene = targetScene,
                TargetBench = targetBench,
                Delay = delay,
                GiveSoul = false,
            });
            Modding.Logger.Log(string.Format(
                "[HKCS] 已写死入口灵魂怪：{0} @ ({1:0.##}, {2:0.##})，文本「{3}」，抽了送去 {4}:{5}，克隆原版外观={6}",
                scene, pos.x, pos.y, Convos[key], targetScene, targetBench, vanillaLook));
        }

        /// <summary>入口灵魂怪（写死那只）的**梦钉文本** —— 来自 `DialogueConfig.json` 的 `entryDream`。</summary>
        internal static string EntryDreamText
        {
            get { return DialogueConfig.EntryDream; }
        }

        /// <summary>
        /// 梦钉那条路的文本（sheet = `Enemy Dreams`）：
        /// · 入口怪 → 返回 <see cref="EntryDreamText"/>；
        /// · 就地生成（出口）那种 → 返回空串（梦钉不弹字）。
        /// 返回 null = 不是我们的键（放行原版）。
        /// </summary>
        internal static string GetDreamConvo(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (key.StartsWith(EntryGhostKey)) return EntryDreamText;
            if (GetConvo(key) != null) return "";   // 是我们的怪，但梦钉不弹字
            return null;
        }

        /// <summary>语言钩子用：键命中我们的前缀就返回编辑器里填的文本，否则 null（放行原版）。</summary>
        internal static string GetConvo(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            string text;
            if (Convos.TryGetValue(key, out text)) return text;
            foreach (KeyValuePair<string, string> kv in Convos)
            {
                if (key.StartsWith(kv.Key)) return kv.Value;
            }
            return null;
        }

        public void Awake()
        {
            string key = KeyPrefix + gameObject.name;
            string text = ResolveDialogueText();
            Convos[key] = text;
            StripPlacementVisuals();

            // ⚠⚠ 从这里开始一律用**摆放点自己所在的场景**（gameObject.scene），**绝不要**用
            //    SceneManager.GetActiveScene()。原因（2026-09-26 03:2x 实测出来的真凶）：
            //    场景切换时，新场景里的物体**先** Awake，而此刻"当前激活场景"还是**上一个**场景
            //    （进迷宫前是 Town）。用 GetActiveScene() 会把克隆体/看门人塞进**正在卸载的 Town**，
            //    Town 一卸载它们跟着被销毁 —— 日志明明打了"生成后 active=True"、一秒后什么都没有，
            //    连挂在独立物体上的看门人都不执行（它也被塞进 Town 了）。
            //    证据：本文件下面那条"目标场景被配成了它自己所在的房间（Town）"的兜底日志 ——
            //    它读的就是 GetActiveScene()，读到 Town 才误判成"原地传送"。
            Scene here = gameObject.scene;
            if (!here.IsValid()) here = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

            // ⚠ 2026-09-26 自动纠正（用户踩过）：摆在我们**自己的房间**里的 PatchGhost 一定是"出口"，
            //    不是"注入原版场景的入口"（入口那只是代码写死的，不需要任何物体）。
            //    勾错了会导致：迷宫里什么都不生成，反而往原版场景里多塞一只。
            if (SpawnInVanillaScene && HKCustomSceneMod.Consts.Rooms.IsCustomRoom(here.name))
            {
                Modding.Logger.LogError("[HKCS] " + gameObject.name +
                    " 摆在我们自己的房间里，却勾了「注入原版场景」—— 已自动改成就地生成（出口模式）。" +
                    "要让它出现在原版场景（入口）才需要勾那个，而入口已经写死在代码里了，不用摆。");
                SpawnInVanillaScene = false;
            }

            Request r = new Request
            {
                Key = key,
                Pos = SpawnInVanillaScene ? SpawnPosition : (Vector2)transform.position,
                VanillaLook = UseVanillaLook,
                Sprite = GhostSprite,
                TargetScene = TargetScene,
                TargetBench = TargetBench,
                Delay = Delay,
                GiveSoul = GiveSoul,
            };

            if (SpawnInVanillaScene)
            {
                if (string.IsNullOrEmpty(SpawnScene))
                {
                    Modding.Logger.LogError("[HKCS] PatchGhost 勾了「注入原版场景」但 SpawnScene 是空的");
                    return;
                }
                r.Scene = SpawnScene;
                Requests.Add(r);
                Modding.Logger.Log(string.Format(
                    "[HKCS] 登记灵魂怪注入：{0} @ ({1:0.##}, {2:0.##})，文本「{3}」，抽了送去 {4}:{5}，克隆原版外观={6}",
                    SpawnScene, SpawnPosition.x, SpawnPosition.y, text, TargetScene, TargetBench, UseVanillaLook));
                return;   // 本场景不生成，等进到 SpawnScene 时由 SpawnForScene 生成
            }

            // 出口（迷宫里的那只）"送去哪"的兜底：
            // 留空、或者目标被配成了**它自己所在的房间**（那就是原地传送，肯定是配错了）⇒ 改成德特茅斯的长椅。
            if (!SpawnInVanillaScene)
            {
                if (string.IsNullOrEmpty(r.TargetScene)) r.TargetScene = "Town";
                if (string.IsNullOrEmpty(r.TargetBench)) r.TargetBench = "RestBench";
                if (r.TargetScene == here.name)
                {
                    Modding.Logger.Log(string.Format(
                        "[HKCS] {0} 的「目标场景」被配成了它自己所在的房间（{1}）⇒ 那是原地传送，已改成 Town:RestBench",
                        gameObject.name, r.TargetScene));
                    r.TargetScene = "Town";
                    r.TargetBench = "RestBench";
                }
            }

            // z：注入原版场景时用 0（和原版灵魂怪一致）；
            //    就地生成（出口）时**强制 0.02**（道具统一 z ≈ 0.02，和长椅一样）。
            //    用户那只曾经摆在 z=-13.56（多半是从别的物体上复制来的）——那种 z 容易和地形/相机出怪事。
            if (!SpawnInVanillaScene && Mathf.Abs(transform.position.z - 0.02f) > 0.5f)
            {
                Modding.Logger.Log(string.Format(
                    "[HKCS] {0} 的 z={1:0.##} 不像道具摆放（道具都接近 0.02）⇒ 灵魂怪已按 z=0.02 生成",
                    gameObject.name, transform.position.z));
            }
            // ★ 场景用**摆放点自己所在的场景**（here）—— 见 Awake 开头那段注释（用 GetActiveScene()
            //   会把怪塞进正在卸载的上一个场景，一起被销毁 = 出口怪"生成过但看不见"的真凶）。
            SpawnOne(new Vector3(r.Pos.x, r.Pos.y, SpawnInVanillaScene ? 0f : 0.02f), r, here);
        }

        /// <summary>
        /// 这只怪的交互文本：`DialogueConfig.json` 的 **exitInteract** 优先；
        /// 它在文件里留空 ⇒ 用**场景里这个组件上填的** <see cref="DialogueText"/>；再空 ⇒ 占位「……」。
        /// （默认值 = 出厂那句「虽然你亵渎了我…」，见 `DialogueDefaults`。）
        /// </summary>
        private string ResolveDialogueText()
        {
            string text, src;
            if (!string.IsNullOrEmpty(DialogueConfig.ExitInteract))
            {
                text = DialogueConfig.ExitInteract;
                src = "DialogueConfig.json 的 exitInteract";
            }
            else if (!string.IsNullOrEmpty(DialogueText))
            {
                text = DialogueText;
                src = "场景里这个 PatchGhost 组件上填的 DialogueText";
            }
            else
            {
                text = "……";
                src = "占位（配置和组件都没填）";
            }
            Modding.Logger.Log(string.Format("[HKCS] {0} 的交互文本：「{1}」（来源：{2}）", gameObject.name, text, src));
            return text;
        }

        /// <summary>
        /// 把**摆放点自己身上**的美术件拆掉。
        ///
        /// 为什么必须拆（2026-09-26 实证）：摆放点是个**真的场景物体**，游戏里照样存在。
        /// 很自然会往它上面挂一张精灵当「图标」好认位置 —— 那张图在游戏里会被**真的画出来**，
        /// 而它的贴图/材质并不在我们的 AssetBundle 里 ⇒ **渲染成一块洋红色方块**，
        /// 正好盖在生成的灵魂怪身上，看起来就像"怪没生成、只有一个坏图标"。
        /// 摆放点只需要「位置 + 配置字段」，不需要任何渲染件 ⇒ 运行时一律删掉。
        /// 要换灵魂怪的样子请用组件上的 `GhostSprite` 字段（或勾 `UseVanillaLook` 克隆原版外观）。
        /// </summary>
        private void StripPlacementVisuals()
        {
            int n = 0;

            SpriteRenderer[] srs = GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < srs.Length; i++)
            {
                if (srs[i] == null) continue;
                srs[i].enabled = false;      // 先关，保证这一帧就不画
                UObject.Destroy(srs[i]);     // 再删干净
                n++;
            }

            tk2dBaseSprite[] tks = GetComponentsInChildren<tk2dBaseSprite>(true);
            for (int i = 0; i < tks.Length; i++)
            {
                if (tks[i] == null) continue;
                tks[i].color = new Color(1f, 1f, 1f, 0f);
                UObject.Destroy(tks[i]);
                n++;
            }

            if (n > 0)
            {
                Modding.Logger.Log("[HKCS] " + gameObject.name + " 是「摆放点」——已拆掉它身上 " + n +
                    " 个美术件（编辑器里你看到的那个图标，游戏里会渲染成洋红方块，所以不放它出来）。" +
                    "想换灵魂怪外观：用组件上的 GhostSprite 字段，或勾 UseVanillaLook 克隆原版。");
            }
        }

        /// <summary>
        /// 场景切换时由 <c>CustomSceneMod.OnSceneChanged</c> 调用：把要注入原版场景的灵魂怪造出来。
        /// ⚠ 场景**由调用方传进来**（就是 `activeSceneChanged` 的 `to`）—— 这里**不能**自己
        /// `GetActiveScene()`：那正是"怪被塞进上一个场景、跟着一起销毁"的坑（见 <c>Awake</c> 里的注释）。
        /// </summary>
        internal static void SpawnForScene(Scene scene)
        {
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.name)) return;
            string sceneName = scene.name;
            for (int i = 0; i < Requests.Count; i++)
            {
                Request r = Requests[i];
                if (r.Scene != sceneName) continue;
                SpawnOne(new Vector3(r.Pos.x, r.Pos.y, 0f), r, scene);
            }
        }

        /// <summary>键 → 已经生成出来的灵魂怪（交互对话结束时要用它触发传送）。</summary>
        private static readonly Dictionary<string, GhostMarker> _byKey = new Dictionary<string, GhostMarker>();

        /// <summary>按键找已生成的灵魂怪（`SceneChanger` 的对话钩子用）。</summary>
        internal static GhostMarker GetMarker(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            GhostMarker m;
            return _byKey.TryGetValue(key, out m) ? m : null;
        }

        /// <summary>
        /// 找"离某点最近的**我们放出来的**灵魂怪"（用于**按距离**替换交互文本：
        /// 原版对话键藏在 FSM 动作的字面量里，抓不到，所以改成"站在我们那只怪跟前说话的就是它的"）。
        /// 入口那只（德特茅斯）也算，这样跟它说话也会显示我们写的文本。
        /// </summary>
        internal static GhostMarker FindMarkerNear(Vector3 pos, float radius)
        {
            GhostMarker best = null;
            float bestD = radius;
            foreach (KeyValuePair<string, GhostMarker> kv in _byKey)
            {
                GhostMarker m = kv.Value;
                if (m == null) continue;
                float d = Vector3.Distance(m.transform.position, pos);
                if (d <= bestD) { bestD = d; best = m; }
            }
            return best;
        }

        /// <summary>
        /// **强制让克隆出来的原版灵魂怪永久现形**（原因见 <see cref="SpawnOne"/> 里的说明）。
        /// 三步：① 打开视觉子物体；② 把颜色/透明度拉满（tk2d 的 sprite 靠**网格顶点色**画，
        /// 原版的"淡出/藏起来"就是把它 alpha 淡到 0）；③ **只保留交互 FSM，其余全部关掉**（原版那套
        /// `Appear`/`fader`/`ghost_npc_death` 会按距离把怪藏起来甚至关掉整个物体）。
        /// <paramref name="dumpFsm"/> = true 时顺便把 FSM 的状态与动作类型打进日志（以后要精确定位有据可查）。
        /// </summary>
        internal static void ForceAppear(GameObject ghost, bool killNonInteractionFsm, bool dumpFsm)
        {
            // ① + ② 视觉件打开、颜色/alpha 拉满
            string fixedWhat = RestoreVisibility(ghost);

            // ③ FSM：出口怪要的是"永远站在那里的普通 NPC"，所以**只留交互（说话）那两套**，
            //    其余全部关掉 —— 实测原版这套掘墓者身上带着：
            //      `Appear`（英雄靠近才现形，里面有状态「Inert：FindChild + ActivateGameObject」= 把子物体关掉）
            //      `fader`（按距离把 alpha 淡到 0）、`FSM`×2（靠近检测）、`Bob`（上下飘）
            //      `ghost_npc_death`（里面有「Destroy：ActivateGameObject」「Remove：ActivateAllChildren」）
            //      `ghost_npc_dreamnail`（梦钉通路，我们已经有自己的 EnemyDreamnailReaction）
            //    这些任何一套在我们房间里跑起来，怪就会"看不见"（2026-09-26 实证：日志里连看门狗都没跑，
            //    说明怪在生成后立刻被某套原版逻辑关掉了）。关掉它们就彻底安静了。
            PlayMakerFSM[] fsms = ghost.GetComponentsInChildren<PlayMakerFSM>(true);
            string killed = "";
            string kept = "";
            for (int i = 0; i < fsms.Length; i++)
            {
                PlayMakerFSM f = fsms[i];
                if (f == null) continue;
                string nm = f.FsmName;
                if (dumpFsm)
                {
                    Modding.Logger.Log(string.Format("[HKCS] 灵魂怪 FSM「{0}」当前状态「{1}」", nm, f.ActiveStateName));
                    FsmState[] states = f.FsmStates;
                    if (states != null)
                    {
                        for (int s = 0; s < states.Length && s < 12; s++)
                        {
                            if (states[s] == null) continue;
                            string acts = "";
                            FsmStateAction[] aa = states[s].Actions;
                            if (aa != null)
                            {
                                for (int a = 0; a < aa.Length && a < 6; a++)
                                {
                                    if (aa[a] == null) continue;
                                    acts += (acts.Length > 0 ? "," : "") + aa[a].GetType().Name;
                                }
                            }
                            Modding.Logger.Log(string.Format("[HKCS]   · 状态「{0}」：{1}", states[s].Name, acts));
                        }
                    }
                }

                if (!killNonInteractionFsm || string.IsNullOrEmpty(nm)) continue;

                // ⚠ 2026-09-26 第二轮修正（用户反馈"不能交互"）：**只关"梦钉"那一套，别的全留**。
                //    上一版把 Appear / fader / FSM(靠近检测) / ghost_npc_death 全关了，结果**按上/下毫无反应**：
                //    原版"英雄靠近 → 显示 ↑↓ 提示球 → 按上/下 → 弹对话"这条链是好几套 FSM 串起来的
                //    （`FSM` 的 Enter/Exit 会 `SetFsmBool` 给 `npc_control`/`Conversation Control`），
                //    关掉其中一环，整条链就断在"没人告诉 npc_control 英雄在跟前"。
                //    依据：德特茅斯那只**入口怪所有 FSM 都是开的**，它交互得好好的 ⇒ 出口怪照原样留全。
                //    "被藏起来/被淡掉"这类风险由 <see cref="GhostKeeper"/> 兜底（被关掉就打开）。
                if (IsDreamnailFsm(nm))
                {
                    f.enabled = false;
                    killed += (killed.Length > 0 ? "、" : "") + nm;
                }
                else
                {
                    kept += (kept.Length > 0 ? "、" : "") + nm;
                }
            }

            Modding.Logger.Log(string.Format("[HKCS] 强制现形：{0}；关掉的 FSM：{1}；保留的 FSM：{2}",
                fixedWhat.Length > 0 ? fixedWhat : "视觉件本来就是好的",
                killed.Length > 0 ? killed : "（没有）",
                kept.Length > 0 ? kept : "（没有 —— 那原版交互链就断了，要检查！）"));
        }

        /// <summary>
        /// 把「被关掉的视觉件 / 被淡成透明的颜色」恢复原样，返回**都修了什么**（人话，直接进日志）。
        /// 生成时调一次；之后由 <see cref="GhostKeeper"/> 每 0.25 秒复查一次
        /// —— 那时返回值非空就说明原版逻辑又动手脚了（看门人会打红字）。
        /// </summary>
        internal static string RestoreVisibility(GameObject ghost)
        {
            if (ghost == null) return "";
            string what = "";

            // ① 视觉子物体
            Transform[] kids = ghost.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < kids.Length; i++)
            {
                if (kids[i] == null || kids[i] == ghost.transform) continue;
                string n = kids[i].name;
                if ((n == "Character Sprite" || n == "Base Glow" || n == "Rush" || n == "Idle Pt" || n == "Away Pt")
                    && !kids[i].gameObject.activeSelf)
                {
                    kids[i].gameObject.SetActive(true);
                    what += (what.Length > 0 ? "、" : "") + "打开了 " + n;
                }
            }

            // ② 颜色（含 alpha）拉满：tk2d 的 sprite 靠**网格顶点色**画，原版的"淡出"就是 alpha 淡到 0
            tk2dBaseSprite[] sprites = ghost.GetComponentsInChildren<tk2dBaseSprite>(true);
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] == null) continue;
                Color c = sprites[i].color;
                if (c.r < 0.999f || c.g < 0.999f || c.b < 0.999f || c.a < 0.999f)
                {
                    what += (what.Length > 0 ? "、" : "") + sprites[i].name + " 颜色 a=" + c.a.ToString("0.##") + "→1";
                    sprites[i].color = new Color(1f, 1f, 1f, 1f);
                }
            }
            SpriteRenderer[] srs = ghost.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < srs.Length; i++)
            {
                if (srs[i] == null) continue;
                if (!srs[i].enabled)
                {
                    srs[i].enabled = true;
                    what += (what.Length > 0 ? "、" : "") + "打开了 " + srs[i].name;
                }
                Color c = srs[i].color;
                if (c.r < 0.999f || c.g < 0.999f || c.b < 0.999f || c.a < 0.999f)
                {
                    srs[i].color = new Color(1f, 1f, 1f, 1f);
                    what += (what.Length > 0 ? "、" : "") + srs[i].name + " 颜色→1";
                }
            }
            return what;
        }

        /// <summary>出口灵魂怪身上要关掉的 FSM：**只有"梦钉"那套**（用户要求：梦钉抽它不能有任何反应）。</summary>
        private static bool IsDreamnailFsm(string name)
        {
            return !string.IsNullOrEmpty(name) && name.ToLower().Contains("dreamnail");
        }

        internal static GameObject SpawnOne(Vector3 pos, Request r, Scene scene, bool withKeeper = true)
        {
            // 原版命中盒的尺寸（克隆时从原版那个子物体上抄下来）
            Vector2 hitSize = new Vector2(1.5f, 2f);
            Vector2 hitOffset = Vector2.zero;

            GameObject ghost;
            if (r.VanillaLook && PrefabHolder.GravediggerPrefab != null)
            {
                ghost = UObject.Instantiate(PrefabHolder.GravediggerPrefab);
                ghost.name = "HKCS_Ghost_" + r.Key;
                ghost.SetActive(false);
                ghost.transform.position = pos;
                StripVanillaDreamnailPath(ghost, ref hitSize, ref hitOffset);
            }
            else
            {
                ghost = new GameObject("HKCS_Ghost_" + r.Key);
                ghost.layer = GhostRootLayer;
                ghost.transform.position = pos;
                if (r.Sprite != null)
                {
                    SpriteRenderer sr = ghost.AddComponent<SpriteRenderer>();
                    sr.sprite = r.Sprite;
                    sr.sortingLayerName = "Default";
                    hitSize = r.Sprite.bounds.size;
                    if (hitSize.x < 0.2f || hitSize.y < 0.2f) hitSize = new Vector2(2f, 2f);
                }
            }

            // 我们的梦钉命中盒（照抄原版：子物体 "Dreamnail Hit"、第 19 层、trigger）
            GameObject hit = new GameObject("Dreamnail Hit");
            hit.transform.SetParent(ghost.transform, false);
            hit.transform.localPosition = hitOffset;
            hit.layer = DreamnailHitLayer;
            BoxCollider2D col = hit.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = hitSize;

            // 能被梦钉抽：原版梦钉就是找这个组件。
            // ⚠ 梦钉文本分两种：
            //   · 入口（注入到原版场景的那只）→ 用户要求弹「既然你这么不知好歹，那就去地狱吧！」
            //     ⇒ convoAmount 必须 ≥ 1（否则全局 FSM 没有页可显示，对话框根本不出来）
            //   · 出口（迷宫里的那只）→ 不要梦钉文本 ⇒ convoAmount 0
            bool isEntry = !string.IsNullOrEmpty(r.Scene);
            EnemyDreamnailReaction reaction = ghost.AddComponent<EnemyDreamnailReaction>();
            reaction.allowUseChildColliders = true;
            reaction.SetConvoTitle(r.Key);
            SetPrivateField(reaction, "convoAmount", isEntry ? 1 : 0);
            SetPrivateField(reaction, "noSoul", !r.GiveSoul);

            GhostMarker marker = ghost.AddComponent<GhostMarker>();
            marker.Setup(r.Key, r.TargetScene, r.TargetBench, r.Delay);
            // 就地生成（r.Scene 为空）= 放在**我们自己的地图**里当"出口"：交互对话一结束就传送；
            // 注入到原版场景（r.Scene 非空）= "入口"：梦钉抽它才传送。
            marker.ExitOnDialogueEnd = string.IsNullOrEmpty(r.Scene);
            _byKey[r.Key] = marker;
            Modding.Logger.Log(string.Format("[HKCS] 灵魂怪模式：{0}",
                marker.ExitOnDialogueEnd ? "出口（对话结束→传送）" : "入口（梦钉→传送）"));

            // 只有"贴图自建"的幽灵才需要我们自己补飘动（克隆原版的待机动画是它 FSM 驱动的，别抢）
            if (!r.VanillaLook) ghost.AddComponent<GhostBob>();
            // 挪进目标场景（原版场景一卸载，它跟着销毁；下次进这个场景再生成）
            if (scene.IsValid())
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(ghost, scene);
            }

            ghost.SetActive(true);

            // ⚠⚠ 2026-09-26 出口怪"看不见"的真凶 ⚠⚠
            //   克隆体自带一套 `Appear` FSM：**英雄靠近 Appear Range 才现形**，走远了还会 Unappear（淡出/收起）。
            //   在德特茅斯玩家总会走到它跟前，所以看着正常；在我们自己的迷宫里它就一直停在"没现形"的状态
            //   （日志实证：怪确实生成在 (54.69, 3.21, z=0)、子物体全"开"、网格 1.34x3.45 —— 什么都没错，就是没现形）。
            //   ⇒ 出口怪直接**永久现形**：强制视觉件可见 + 颜色拉满 + 关掉 Appear 那套 FSM。
            if (!isEntry)
            {
                ForceAppear(ghost, true, true);
                // ⚠ 原版那套 `Bob` FSM 现在是**开着**的（它自己会飘），所以**不要**再加 GhostBob，
                //    否则两个上下浮动打架（一抖一抖）。贴图自建的幽灵没有 FSM，才需要我们补。
                if (withKeeper) SpawnKeeper(ghost, r, pos, scene);   // ★ 独立看门人（见 GhostKeeper 注释）
                Modding.Logger.Log(string.Format(
                    "[HKCS] 出口灵魂怪目标：{0}:{1}（**交互对话结束就传送，不杀死骑士**，延迟 {2:0.##} 秒；" +
                    "梦钉抽它没有任何反应）",
                    r.TargetScene, r.TargetBench, r.Delay));
            }

            // 克隆原版那套有 Appear/Unappear 逻辑（英雄靠近才现形），子物体常常一开始是关的。
            // 这里把每个子物体的 activeSelf 打进日志，并把"看得到的东西"强制打开 + 放大感应范围 —— 排查"怪看不见"用。
            if (r.VanillaLook)
            {
                string states = "";
                Transform[] kids = ghost.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < kids.Length; i++)
                {
                    if (kids[i] == null || kids[i] == ghost.transform) continue;
                    string n = kids[i].name;
                    states += (states.Length > 0 ? "、" : "") + n + (kids[i].gameObject.activeSelf ? "=开" : "=关");

                    // 视觉相关的子物体强制打开（粒子/光晕/精灵）
                    if (n == "Character Sprite" || n == "Base Glow" || n == "Rush" || n == "Burst"
                        || n == "Idle Pt" || n == "Away Pt")
                    {
                        kids[i].gameObject.SetActive(true);
                    }

                    // 原版靠这两个范围判定"英雄在不在附近" ⇒ 放大到永远在范围内，逼它现形
                    if (n == "Appear Range" || n == "Unappear Range")
                    {
                        BoxCollider2D bc = kids[i].GetComponent<BoxCollider2D>();
                        if (bc != null)
                        {
                            bc.size = new Vector2(200f, 120f);
                            bc.offset = Vector2.zero;
                            states += "(范围已放大)";
                        }
                    }
                }

                // 视觉件到底有没有网格/有多大（bounds 接近 0 就说明网格是空的 ⇒ 那是 tk2d 的问题）
                MeshRenderer mr = ghost.GetComponentInChildren<MeshRenderer>();
                string meshInfo = "无 MeshRenderer";
                if (mr != null)
                {
                    Bounds b = mr.bounds;
                    meshInfo = string.Format("网格 {0}=({1:0.##},{2:0.##}) enabled={3}",
                        mr.gameObject.name, b.size.x, b.size.y, mr.enabled);
                }

                Modding.Logger.Log("[HKCS] 灵魂怪子物体状态（强制打开视觉件后）：" + states + "；" + meshInfo);
            }

            Modding.Logger.Log(string.Format(
                "[HKCS] 放了灵魂怪 {0} @ ({1:0.##}, {2:0.##}, z={3:0.##})（外观={4}，命中盒 {5:0.##}x{6:0.##}，" +
                "生成后 active={7}，所在场景={8}）",
                ghost.name, pos.x, pos.y, pos.z, r.VanillaLook ? "克隆原版掘墓者" : "贴图", hitSize.x, hitSize.y,
                ghost.activeSelf, ghost.scene.name));
            return ghost;
        }

        /// <summary>
        /// 给出口怪造一个**独立**的看门人（见 <see cref="GhostKeeper"/>）。
        /// 必须是独立根物体：怪自己被关掉/销毁时，挂在怪身上的任何组件都不会再执行。
        /// </summary>
        private static void SpawnKeeper(GameObject ghost, Request r, Vector3 pos, Scene scene)
        {
            GameObject keeper = new GameObject("HKCS_GhostKeeper_" + r.Key);
            keeper.layer = GhostRootLayer;
            keeper.transform.position = new Vector3(pos.x, pos.y, 0f);
            if (scene.IsValid())
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(keeper, scene);
            }
            GhostKeeper k = keeper.AddComponent<GhostKeeper>();
            k.Setup(ghost, r, pos, scene);
        }

        /// <summary>看门人专用：怪被销毁后照着**同一份配置**再造一只（不再重复造看门人，免得无限套娃）。
        /// 场景用**看门人自己所在的场景**（= 房间），不要 GetActiveScene()。</summary>
        internal static GameObject RespawnForKeeper(Request r, Scene scene)
        {
            if (r == null) return null;
            if (!scene.IsValid()) scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            Vector3 pos = new Vector3(r.Pos.x, r.Pos.y, 0.02f);
            return SpawnOne(pos, r, scene, false);
        }

        /// <summary>
        /// 把克隆体自带的**梦钉**通路拆掉（但不碰它的对话 FSM！）：
        /// ① 删掉它自己的 `Dreamnail Hit` 子物体、② 删掉它身上的 `EnemyDreamnailReaction`
        /// ⇒ 梦钉不会再触发原版那套"弹台词"逻辑，只剩我们自己的组件（弹不弹文本由我们控制）。
        /// ③ **顺手把它 FSM 里像"对话键"的字符串变量抄下来**：交互（按 A 说话）走的就是这些键，
        ///    我们的 `SwapConvo` 会把它们换成我们的键 ⇒ **交互文本变成我们写的**（原版那只不受影响）。
        /// ⚠ 不要禁用它的 FSM：待机动画和"按 A 说话"都是 FSM 驱动的，禁了就既不动也不能对话了。
        /// </summary>
        private static void StripVanillaDreamnailPath(GameObject ghost, ref Vector2 hitSize, ref Vector2 hitOffset)
        {
            Transform[] all = ghost.GetComponentsInChildren<Transform>(true);
            List<GameObject> kill = new List<GameObject>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                if (all[i].name == "Dreamnail Hit")
                {
                    BoxCollider2D bc = all[i].GetComponent<BoxCollider2D>();
                    if (bc != null) { hitSize = bc.size; hitOffset = bc.offset; }
                    kill.Add(all[i].gameObject);
                }
            }
            for (int i = 0; i < kill.Count; i++) UObject.Destroy(kill[i]);

            EnemyDreamnailReaction[] old = ghost.GetComponentsInChildren<EnemyDreamnailReaction>(true);
            for (int i = 0; i < old.Length; i++) UObject.Destroy(old[i]);

            // ③ 抄下 FSM 里的"对话键"（FSM 变量名/值都在这里，顺便全部打进日志方便以后精确定位）
            PlayMakerFSM[] fsms = ghost.GetComponentsInChildren<PlayMakerFSM>(true);
            for (int i = 0; i < fsms.Length; i++)
            {
                if (fsms[i] == null || fsms[i].FsmVariables == null) continue;
                FsmString[] strs = fsms[i].FsmVariables.StringVariables;
                if (strs == null) continue;
                for (int j = 0; j < strs.Length; j++)
                {
                    if (strs[j] == null) continue;
                    string v = strs[j].Value;
                    if (string.IsNullOrEmpty(v)) continue;
                    Modding.Logger.Log(string.Format("[HKCS] 原版灵魂怪 FSM 字符串：{0}.{1} = 「{2}」",
                        fsms[i].FsmName, strs[j].Name, v));
                    if (LooksLikeConvoKey(v))
                    {
                        _convoKeys.Add(v);
                    }
                }
            }
            Modding.Logger.Log(string.Format(
                "[HKCS] 已拆掉原版梦钉通路：删 Dreamnail Hit ×{0}、删 EnemyDreamnailReaction ×{1}；" +
                "抄到对话键 {2} 个（这些键的文本会被换成我们写的，交互时生效）",
                kill.Count, old.Length, _convoKeys.Count));
        }

        /// <summary>像不像"对话键"：全大写/数字/下划线，长度 ≥ 4（HK 的对话键就长这样，例如 `GRAVEDIGGER_1`）。</summary>
        private static bool LooksLikeConvoKey(string v)
        {
            if (v.Length < 4 || v.Length > 60) return false;
            for (int i = 0; i < v.Length; i++)
            {
                char c = v[i];
                bool ok = (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok) return false;
            }
            return v.IndexOf('_') >= 0 || v.Length >= 6;   // 至少得像 "GRAVEDIGGER" 这种词
        }

        /// <summary>把原版对话键换成我们的键（交互文本替换就靠它）。返回 null = 不改。</summary>
        internal static string SwapConvo(string convName)
        {
            if (string.IsNullOrEmpty(convName) || _convoKeys.Count == 0) return null;
            if (!_convoKeys.Contains(convName)) return null;
            return EntryGhostKey;
        }

        private static void SetPrivateField(object target, string field, object value)
        {
            try
            {
                FieldInfo f = target.GetType().GetField(field,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f != null) f.SetValue(target, value);
                else Modding.Logger.Log("[HKCS] EnemyDreamnailReaction 上没有字段 " + field);
            }
            catch (System.Exception e)
            {
                Modding.Logger.Log("[HKCS] 设置 " + field + " 失败：" + e.Message);
            }
        }
    }

    /// <summary>
    /// 出口灵魂怪的**看门人**（挂在**另一个独立根物体**上，不是怪身上）。
    ///
    /// 为什么必须独立：怪被原版逻辑 `SetActive(false)` 关掉、甚至直接销毁之后，
    /// **挂在怪身上的组件一个都不会再执行**（物体不激活 ⇒ 没有 Update；被销毁 ⇒ 组件没了），
    /// 所以"自己救自己"是不可能的 —— 2026-09-26 实证：怪生成出来了、日志里说视觉件全开，
    /// 但之前那个挂在怪身上的看门狗**一行都没打** ⇒ 怪在生成后立刻被某套原版逻辑关掉了。
    ///
    /// 它每 0.25 秒扫一次，做四件事：
    ///   ① 怪被关掉 → 立刻打开（打红字，说明还有原版逻辑在藏它）；
    ///   ② 怪被销毁 → 照同一份配置**重建**（最多 <see cref="MaxRevives"/> 次，每次都打红字）；
    ///   ③ 视觉件被关 / 颜色被淡掉 → 立刻恢复（<see cref="PatchGhost.RestoreVisibility"/>）；
    ///   ④ 怪"自己跑掉"（离摆放点超过 2 格）→ 拉回摆放点并冻住刚体。
    ///
    /// 另外头 <see cref="DiagSeconds"/> 秒每秒打一行**体检日志**：位置/激活/每个渲染件的开关、
    /// 材质名、**贴图名**、世界大小、tk2d 颜色 alpha、FSM 状态 —— 其中
    /// `贴图=**贴图=null（这样就是洋红块）**` 或 `材质=null` 就是"渲染成洋红方块"的直接证据。
    /// 之后安静地继续看着，只在**动手修了**的时候才打日志。
    /// </summary>
    public class GhostKeeper : MonoBehaviour
    {
        /// <summary>日志里显示的名字（= 摆放点的键，例如 HKCS_GHOST_ExitGhost）。</summary>
        internal string Tag = "";

        /// <summary>体检日志持续几秒。</summary>
        public float DiagSeconds = 8f;

        /// <summary>怪被销毁后最多重建几次。</summary>
        public int MaxRevives = 3;

        private GameObject _ghost;
        private PatchGhost.Request _req;
        private Vector3 _home;
        private Scene _scene;

        private float _nextScan;
        private float _nextDiag;
        private float _diagEnd;
        private int _fixes;
        private int _revives;
        private bool _gaveUp;
        private bool _loggedCam;

        internal void Setup(GameObject ghost, PatchGhost.Request req, Vector3 home, Scene scene)
        {
            _ghost = ghost;
            _req = req;
            _home = home;
            _scene = scene;
            Tag = req != null ? req.Key : "";
        }

        private void Start()
        {
            _nextScan = Time.time;
            _nextDiag = Time.time;
            _diagEnd = Time.time + DiagSeconds;
            Modding.Logger.Log("[HKCS][看门人] 已就位，看着 " + Tag +
                "（它被关掉/被销毁/跑掉都会自动救回来；头 " + DiagSeconds.ToString("0") + " 秒每秒一行体检）");
        }

        private void Update()
        {
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + 0.25f;

            // ② 被销毁了 → 重建（放最前面：怪都没了，别的都白说）
            if (_ghost == null)
            {
                Revive();
                return;
            }

            // ① 被关掉了 → 打开
            if (!_ghost.activeSelf)
            {
                _fixes++;
                _ghost.SetActive(true);
                Modding.Logger.LogError("[HKCS][看门人] " + Tag + " 被某处 SetActive(false) 关掉了 ⇒ 已重新打开" +
                    "（累计修复 " + _fixes + " 次；这说明还有原版逻辑在藏它）");
            }

            // ③ 视觉件：**故意不再每 0.25 秒强行掰回纯白**。
            //    原版那套 Appear/fader FSM 现在是**开着**的（交互链要靠它们），它会按距离正常淡入淡出 ——
            //    那是我们要的原版表现。以前那种"发现颜色不是纯白就改回去"的做法会跟 fader 打架（一明一暗地闪）。
            //    万一真的被永久藏起来，体检日志里 `可见=False` / `tk2d 颜色 a=0` 会显示出来，再针对性处理。

            // ④ 跑了 → 拉回摆放点 + 冻住
            Vector3 p = _ghost.transform.position;
            if ((p - _home).sqrMagnitude > 4f)
            {
                Rigidbody2D rb = _ghost.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.velocity = Vector2.zero;
                    rb.bodyType = RigidbodyType2D.Kinematic;
                    rb.gravityScale = 0f;
                }
                GhostBob bob = _ghost.GetComponent<GhostBob>();
                if (bob != null) bob.enabled = false;
                _ghost.transform.position = _home;
                Modding.Logger.Log(string.Format(
                    "[HKCS][看门人] {0} 从 ({1:0.##},{2:0.##}) 跑到了 ({3:0.##},{4:0.##}) ⇒ 已拉回摆放点并冻住",
                    Tag, _home.x, _home.y, p.x, p.y));
            }

            // 体检日志（头几秒每秒一行；之后只在上面动手修的时候才出声）
            if (Time.time <= _diagEnd && Time.time >= _nextDiag)
            {
                _nextDiag = Time.time + 1f;

                if (!_loggedCam)
                {
                    _loggedCam = true;
                    Camera cam = Camera.main;
                    if (cam != null)
                    {
                        Vector3 cp = cam.transform.position;
                        Modding.Logger.Log(string.Format(
                            "[HKCS][看门人] 主相机 @ ({0:0.##},{1:0.##},{2:0.##}) 正交={3} size={4:0.##}",
                            cp.x, cp.y, cp.z, cam.orthographic, cam.orthographicSize));
                    }
                }

                Modding.Logger.Log("[HKCS][看门人] " + Describe());
            }
        }

        /// <summary>怪被销毁了：照同一份配置再造一只（<c>withKeeper: false</c>，免得看门人套娃）。</summary>
        private void Revive()
        {
            if (_gaveUp) return;

            if (_revives >= MaxRevives)
            {
                _gaveUp = true;
                Modding.Logger.LogError("[HKCS][看门人] " + Tag + " 已被销毁 " + _revives +
                    " 次，达到上限，不再重建（说明有个原版逻辑在销毁它；把 ModLog 里 [看门人] 那几行发出来）");
                return;
            }

            _revives++;
            Modding.Logger.LogError("[HKCS][看门人] " + Tag + " 被**销毁**了 ⇒ 第 " + _revives + " 次重建");
            _ghost = PatchGhost.RespawnForKeeper(_req, _scene);
            if (_ghost == null)
            {
                _gaveUp = true;
                Modding.Logger.LogError("[HKCS][看门人] 重建失败（配置没了？）");
            }
        }

        /// <summary>
        /// 一行体检：激活/位置/每个渲染件的开关、材质名、贴图名、世界大小、排序 + tk2d 颜色 alpha + FSM 状态。
        /// 「贴图=null」= 渲染出来就是洋红块；「激活=False」= 根本没画。
        /// </summary>
        private string Describe()
        {
            if (_ghost == null) return Tag + " = 已被销毁（等重建）";

            Transform t = _ghost.transform;
            Vector3 p = t.position;

            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("{0} pos=({1:0.##},{2:0.##},{3:0.##}) 激活={4} 在层级里={5} 缩放={6:0.##}",
                Tag, p.x, p.y, p.z, _ghost.activeSelf, _ghost.activeInHierarchy, t.localScale.x);

            sb.Append(" | 渲染件：");
            Renderer[] all = _ghost.GetComponentsInChildren<Renderer>(true);
            if (all.Length == 0) sb.Append("(一个都没有！)");
            for (int i = 0; i < all.Length; i++)
            {
                Renderer rd = all[i];
                if (rd == null) continue;

                string matName = "(材质=null)";
                string texName = "(贴图=null)";
                if (rd.sharedMaterial != null)
                {
                    matName = rd.sharedMaterial.name;
                    texName = rd.sharedMaterial.mainTexture != null
                        ? rd.sharedMaterial.mainTexture.name
                        : "**贴图=null ⇒ 画出来就是洋红块**";
                }

                Bounds b = rd.bounds;
                sb.AppendFormat("{0}[{1} enabled={2} 可见={3} 材质={4} 贴图={5} 世界大小={6:0.##}x{7:0.##} 排序={8},{9}]",
                    i > 0 ? "；" : "", rd.gameObject.name, rd.enabled, rd.isVisible, matName, texName,
                    b.size.x, b.size.y, rd.sortingLayerName, rd.sortingOrder);
            }

            sb.Append(" | tk2d 颜色：");
            tk2dBaseSprite[] sp = _ghost.GetComponentsInChildren<tk2dBaseSprite>(true);
            if (sp.Length == 0) sb.Append("(无)");
            for (int i = 0; i < sp.Length; i++)
            {
                if (sp[i] == null) continue;
                sb.AppendFormat("{0}{1} a={2:0.##}", i > 0 ? "；" : "", sp[i].name, sp[i].color.a);
            }

            sb.Append(" | FSM：");
            PlayMakerFSM[] fsms = _ghost.GetComponentsInChildren<PlayMakerFSM>(true);
            if (fsms.Length == 0) sb.Append("(无)");
            for (int i = 0; i < fsms.Length; i++)
            {
                if (fsms[i] == null) continue;
                sb.AppendFormat("{0}{1}={2}{3}", i > 0 ? "；" : "", fsms[i].FsmName,
                    fsms[i].ActiveStateName, fsms[i].enabled ? "" : "(已关)");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// 让灵魂怪"飘着"的小动画（因为原版那套 FSM 被我们关了，待机动画要自己补）。
    /// 只是上下浮动 + 轻微呼吸感，不碰任何逻辑。
    /// </summary>
    public class GhostBob : MonoBehaviour
    {
        /// <summary>浮动幅度（格）。</summary>
        public float Amplitude = 0.18f;
        /// <summary>一个来回的秒数。</summary>
        public float Period = 2.4f;

        private Vector3 _base;
        private float _t;

        private void Start()
        {
            _base = transform.localPosition;
            _t = Random.Range(0f, Period);   // 错开相位，多个灵魂不会整齐划一
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float y = Mathf.Sin(_t / Mathf.Max(0.1f, Period) * Mathf.PI * 2f) * Amplitude;
            transform.localPosition = new Vector3(_base.x, _base.y + y, _base.z);
        }
    }

    /// <summary>
    /// 运行时挂在灵魂怪身上的小组件（**纯运行时对象，不需要壳工程里有对应类型**）。
    /// `SceneChanger` 的 `On.EnemyDreamnailReaction.RecieveDreamImpact` 钩子会调用
    /// <see cref="OnDreamNailed"/>。
    /// </summary>
    public class GhostMarker : MonoBehaviour
    {
        internal string ConvoKey;
        internal string TargetScene;
        internal string TargetBench;
        internal float Delay = 1.5f;

        /// <summary>true = 出口模式：**交互对话结束**时传送（否则等梦钉命中）。</summary>
        internal bool ExitOnDialogueEnd;

        private bool _busy;

        internal void Setup(string key, string scene, string bench, float delay)
        {
            ConvoKey = key;
            TargetScene = scene;
            TargetBench = bench;
            Delay = delay;
        }

        /// <summary>被梦钉抽到了：延迟一会儿后把玩家送回目标房间的椅子上（**入口怪走这条**：真死亡流程）。</summary>
        internal void OnDreamNailed()
        {
            if (_busy) return;
            _busy = true;
            StartCoroutine(TeleportRoutine());
        }

        /// <summary>
        /// **出口怪走这条**：交互对话结束后，**不杀死骑士**地把他送回目标椅子。
        ///
        /// 做法（2026-09-26 用户要求"不要用杀死的方式传送"）：
        ///   ① `PlayerData.SetBenchRespawn(椅子, 场景, true)` —— 把重生点写到德特茅斯那把长椅；
        ///   ② `GameManager.ReadyForRespawn(false)` —— 原版"在椅子上醒来"的流程：**黑屏淡出 → 直接出现在长椅上**，
        ///      没有死亡动画、不掉影子、不掉钱（这是本 mod 0.37 那版用过的同一条路，实测可用）。
        /// </summary>
        internal void ExitWithoutDying()
        {
            if (_busy) return;
            _busy = true;
            StartCoroutine(ExitRoutine());
        }

        private IEnumerator ExitRoutine()
        {
            Modding.Logger.Log(string.Format(
                "[HKCS] 出口灵魂怪：交互结束 → **不死亡**传送（{0:0.##} 秒后 → {1}:{2}，黑屏后在长椅上醒来）",
                Delay, TargetScene, TargetBench));

            if (Delay > 0f) yield return new WaitForSeconds(Delay);

            if (PlayerData.instance == null || GameManager.instance == null)
            {
                Modding.Logger.LogError("[HKCS] PlayerData/GameManager 为空，传送取消");
                _busy = false;
                yield break;
            }

            PlayerData.instance.SetBenchRespawn(TargetBench, TargetScene, true);
            Modding.Logger.Log("[HKCS] 走原版「在长椅上醒来」流程（ReadyForRespawn），不杀骑士、不掉影子");

            try
            {
                GameManager.instance.ReadyForRespawn(false);
            }
            catch (System.Exception e)
            {
                Modding.Logger.LogError("[HKCS] ReadyForRespawn 抛异常，改用原版换场景：" + e.Message);
                try
                {
                    // 兜底：直接换场景到目标场景的入口（也能回去，只是不保证正好落在长椅上）
                    GameManager.instance.BeginSceneTransition(new GameManager.SceneLoadInfo
                    {
                        SceneName = TargetScene,
                        EntryGateName = TargetBench,
                        EntryDelay = 0.5f,
                        Visualization = GameManager.SceneLoadVisualizations.Default,
                        WaitForSceneTransitionCameraFade = true,
                        AlwaysUnloadUnusedAssets = true,
                    });
                }
                catch (System.Exception e2)
                {
                    Modding.Logger.LogError("[HKCS] BeginSceneTransition 也失败了：" + e2.Message);
                }
            }

            yield return new WaitForSeconds(2f);
            _busy = false;
        }

        private IEnumerator TeleportRoutine()
        {
            Modding.Logger.Log(string.Format("[HKCS] 灵魂怪被梦钉抽到（{0}）→ {1:0.##} 秒后送往 {2}:{3}",
                name, Delay, TargetScene, TargetBench));

            if (Delay > 0f) yield return new WaitForSeconds(Delay);

            if (PlayerData.instance == null || GameManager.instance == null)
            {
                Modding.Logger.LogError("[HKCS] PlayerData/GameManager 为空，传送取消");
                _busy = false;
                yield break;
            }

            // 1) 先把"长椅重生点"改成目标房间的那把椅子（死亡后就在那里醒来）
            PlayerData.instance.SetBenchRespawn(TargetBench, TargetScene, true);

            // 2) **真正让他死**：`HeroController.Die()` 是 private 协程，用反射直接调
            //    （用户要求："直接让他真正死亡就好了，只不过要传送到迷宫里的椅子"）
            //    ⇒ 会播死亡动画、掉影子，然后原版自己会重生到我们刚设的那把椅子。
            if (KillHeroForReal()) yield break;   // 成功触发真死亡就交给原版流程，不再自己传送

            // 3) 兜底（反射失败时）：至少播个死亡动画 + 走原版的死亡流程
            PlayKnightDeathAnim();
            yield return new WaitForSeconds(1.2f);
            Modding.Logger.Log("[HKCS] 反射调 Die() 失败，退回 PlayerDead 流程");
            GameManager.instance.StartCoroutine(GameManager.instance.PlayerDead(0.5f));

            yield return new WaitForSeconds(2f);
            _busy = false;   // 允许以后再用（这只怪一直留着）
        }

        /// <summary>
        /// 调 `HeroController.Die()`（private 协程）让骑士**真死亡**。
        /// 返回 true = 已成功触发（后续由原版接管：死亡动画 → 影子 → 在我们设的椅子上重生）。
        /// </summary>
        private bool KillHeroForReal()
        {
            try
            {
                HeroController hero = HeroController.instance;
                if (hero == null) return false;

                System.Reflection.MethodInfo mi = typeof(HeroController).GetMethod(
                    "Die", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (mi == null)
                {
                    Modding.Logger.LogError("[HKCS] 找不到 HeroController.Die()，改用兜底流程");
                    return false;
                }

                System.Collections.IEnumerator co = mi.Invoke(hero, null) as System.Collections.IEnumerator;
                if (co == null)
                {
                    Modding.Logger.LogError("[HKCS] Die() 没返回协程，改用兜底流程");
                    return false;
                }

                Modding.Logger.Log("[HKCS] 触发真死亡 HeroController.Die()（会在椅子上醒来）");
                GameManager.instance.StartCoroutine(co);
                return true;
            }
            catch (System.Exception e)
            {
                Modding.Logger.LogError("[HKCS] 调 Die() 抛异常：" + e.Message);
                return false;
            }
        }

        /// <summary>让骑士播"死亡"动画（best-effort：找不到动画就只打一条日志）。</summary>
        private void PlayKnightDeathAnim()
        {
            try
            {
                HeroController hero = HeroController.instance;
                if (hero == null) return;

                tk2dSpriteAnimator anim = hero.GetComponent<tk2dSpriteAnimator>();
                if (anim == null) anim = hero.GetComponentInChildren<tk2dSpriteAnimator>();
                if (anim == null)
                {
                    Modding.Logger.Log("[HKCS] 找不到骑士的 tk2dSpriteAnimator，跳过死亡动画");
                    return;
                }

                // 动画名兼容几种写法（HK 的叫 "Death"）
                string[] names = { "Death", "Death Alt", "Death 2" };
                for (int i = 0; i < names.Length; i++)
                {
                    if (anim.GetClipByName(names[i]) != null)
                    {
                        anim.Play(names[i]);
                        Modding.Logger.Log("[HKCS] 播放骑士死亡动画：" + names[i]);
                        return;
                    }
                }
                Modding.Logger.Log("[HKCS] 没找到死亡动画片（试着列出可用名字）");
                foreach (tk2dSpriteAnimationClip c in anim.Library.clips)
                {
                    if (c != null && c.name != null && c.name.ToLower().Contains("death"))
                    {
                        anim.Play(c.name);
                        Modding.Logger.Log("[HKCS] 播放骑士死亡动画：" + c.name);
                        return;
                    }
                }
            }
            catch (System.Exception e)
            {
                Modding.Logger.Log("[HKCS] 播死亡动画失败（不影响传送）：" + e.Message);
            }
        }
    }
}
