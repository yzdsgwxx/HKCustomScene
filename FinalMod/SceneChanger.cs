using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using Modding;
using SFCore.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using HKCustomSceneMod.Consts;
using UObject = UnityEngine.Object;
// UnityEngine 也有 ILogger，而 Modding 的类型现在并进了 Assembly-CSharp.dll → 必须显式取别名，否则 CS0104
using ILogger = Modding.ILogger;

namespace HKCustomSceneMod
{
    /// <summary>
    /// 多房间自定义区域的核心：持有 AssetBundle、按房间设置尺寸、造/改门。
    ///
    /// 注意：这里刻意做成**普通类**而不是 MonoBehaviour。
    /// 参考实现把它写成 MonoBehaviour 却用 `new SceneChanger(...)` 构造 —— 那种
    /// 实例没有 GameObject，`transform` / `Start` 都会炸，只是恰好没用到而已。
    /// 我们只需要 On. 钩子和静态的 FindObjectOfType，普通类更诚实。
    /// </summary>
    public class SceneChanger
    {
        private readonly Dictionary<string, Dictionary<string, GameObject>> _preloaded;
        private readonly ILogger _logger;

        /// <summary>场景包：所有房间的 .unity 都在里面。</summary>
        public AssetBundle BundleScenes { get; private set; }

        /// <summary>素材包：材质/贴图。和场景分开放，方便单独重打。</summary>
        public AssetBundle BundleMaterials { get; private set; }

        public SceneChanger(Dictionary<string, Dictionary<string, GameObject>> preloaded,
                            ILogger logger)
        {
            _preloaded = preloaded;
            _logger = logger;
        }

        // ────────────────────────────────────────────────────────────────
        //  初始化
        // ────────────────────────────────────────────────────────────────
        public void Init()
        {
            PrefabHolder.Preloaded(_preloaded, _logger);

            Assembly asm = Assembly.GetExecutingAssembly();
            BundleScenes = LoadEmbedded(asm, "HKCustomSceneMod.Resources.hkcs_scenes")
                           ?? LoadEmbedded(asm, "HKCustomSceneMod.Resources.my_first_assetbundle");
            BundleMaterials = LoadEmbedded(asm, "HKCustomSceneMod.Resources.hkcs_materials");

            if (BundleScenes == null)
            {
                _logger.LogError("[HKCS] 场景 AssetBundle 没加载成功 —— 检查 csproj 里的 " +
                                 "EmbeddedResource LogicalName 和 Resources/ 下的文件名是否对得上");
            }
            else
            {
                string[] names = BundleScenes.GetAllScenePaths();
                _logger.Log(string.Format("[HKCS] 场景包已加载，含 {0} 个场景：{1}",
                    names.Length, string.Join(", ", names)));
                ValidateRooms(names);
            }

            On.GameManager.RefreshTilemapInfo += OnRefreshTilemapInfo;
            // 所有场景切换（不管触发者是 TransitionPoint 还是 PlayMaker FSM）最终都会走这一条
            On.GameManager.BeginSceneTransition += OnBeginSceneTransition;
            // 相机兜底：没补上 tilemap 时原版这个方法会 NRE，把相机彻底带坏（见 OnGetTilemapInfo 注释）
            On.CameraController.GetTilemapInfo += OnGetTilemapInfo;
            // 灵魂怪：梦钉抽到时原版会调 RecieveDreamImpact，我们借这一下触发"送回椅子"
            On.EnemyDreamnailReaction.RecieveDreamImpact += OnRecieveDreamImpact;
            // 全局后门：每帧看英雄状态（不用在场景里摆任何东西）
            On.HeroController.Update += OnHeroUpdate;
            On.DialogueBox.SetConversation += OnSetConversation;
            // 对话**结束**（对话框收起）时，如果刚才是我们的"出口灵魂怪"，就触发传送
            On.DialogueBox.HideText += OnHideText;
        }

        public void Unhook()
        {
            On.GameManager.RefreshTilemapInfo -= OnRefreshTilemapInfo;
            On.GameManager.BeginSceneTransition -= OnBeginSceneTransition;
            On.CameraController.GetTilemapInfo -= OnGetTilemapInfo;
            On.EnemyDreamnailReaction.RecieveDreamImpact -= OnRecieveDreamImpact;
            On.HeroController.Update -= OnHeroUpdate;
            On.DialogueBox.SetConversation -= OnSetConversation;
            On.DialogueBox.HideText -= OnHideText;
        }

        /// <summary>正在对话的"出口灵魂怪"（对话一结束就让它传送）。</summary>
        private Patchers.GhostMarker _pendingExit;

        /// <summary>
        /// 交互文本替换：我们的灵魂怪是**克隆原版掘墓者**，它被"按上/下交互"触发的对话用的还是原版对话键
        /// （`PatchGhost` 会把克隆体 FSM 里的那些键抄下来）。这里把那些键换成我们的键，
        /// 再由 `CustomSceneMod` 的语言钩子返回我们写的文本 ⇒ **交互时显示的就是我们的文本**。
        /// 另外：如果这只是"出口灵魂怪"（就地生成的那种），记下来，等对话结束就传送。
        /// </summary>
        private void OnSetConversation(On.DialogueBox.orig_SetConversation orig,
                                       DialogueBox self, string convName, string sheetName)
        {
            // 1) **先按距离**判断：英雄正站在我们的哪只灵魂怪旁边（2.5 格内）⇒ 这次对话就是它的。
            //    必须放最前面：`SwapConvo` 只知道"这是原版 GRAVEDIGGER_TALK"这种通用键，
            //    分不清是入口那只还是迷宫里的出口那只（出口那只的文本和"对话完就传送"都会错）。
            string ours = null;
            {
                HeroController hero = HeroController.instance;
                if (hero != null)
                {
                    // 半径 2.5 格：只有**紧挨着**我们的灵魂怪说话才算（避免把旁边 4 格外的原版掘墓者也算进来）
                    Patchers.GhostMarker near =
                        Patchers.PatchGhost.FindMarkerNear(hero.transform.position, 2.5f);
                    if (near != null) ours = near.ConvoKey;
                }
            }

            // 2) 距离没命中 → 退回按"原版对话键"判断（键藏在 FSM 动作字面量里，`PatchGhost` 会把它们抄下来）
            if (ours == null) ours = Patchers.PatchGhost.SwapConvo(convName);

            if (ours != null)
            {
                _logger.Log(string.Format("[HKCS] 交互文本替换：{0} → {1}（sheet={2}）", convName, ours, sheetName));
                Patchers.GhostMarker marker = Patchers.PatchGhost.GetMarker(ours);
                if (marker != null && marker.ExitOnDialogueEnd)
                {
                    _pendingExit = marker;
                    _logger.Log("[HKCS] 出口灵魂怪开始对话，等对话结束后传送");
                }
                orig(self, ours, sheetName);
                return;
            }
            orig(self, convName, sheetName);
        }

        /// <summary>对话框收起 = 交互结束 ⇒ 出口灵魂怪触发**不死亡**传送（黑屏后在德特茅斯长椅上醒来）。</summary>
        private void OnHideText(On.DialogueBox.orig_HideText orig, DialogueBox self)
        {
            orig(self);

            if (_pendingExit == null) return;
            Patchers.GhostMarker marker = _pendingExit;
            _pendingExit = null;
            _logger.Log("[HKCS] 交互结束 → 触发出口传送（不杀死骑士）");
            marker.ExitWithoutDying();
        }

        /// <summary>全局后门的每帧驱动（见 <see cref="GlobalBackdoor"/>）。</summary>
        private void OnHeroUpdate(On.HeroController.orig_Update orig, HeroController self)
        {
            orig(self);
            GlobalBackdoor.Tick(self);
        }

        /// <summary>
        /// 梦钉命中回调的钩子。反编译 `SendDreamImpact` 得到：梦钉命中后原版会
        /// `GetComponent/GetComponentInParent&lt;EnemyDreamnailReaction&gt;()` 再调这个方法。
        /// 我们先放行原版（这样灵魂怪自己的文本/特效照常出），再看看被抽的是不是我们放的灵魂怪
        /// （身上有 <see cref="HKCustomSceneMod.Patchers.GhostMarker"/>）。
        /// </summary>
        private void OnRecieveDreamImpact(On.EnemyDreamnailReaction.orig_RecieveDreamImpact orig,
                                         EnemyDreamnailReaction self)
        {
            // 我们的灵魂怪分两种：
            //  · **出口**（迷宫里那只）→ 用户要求：**梦钉抽它不应该有任何反应**
            //    ⇒ 把这一下**整个吞掉**：不放行原版（不弹梦语、不播梦钉特效、不给灵魂），也不传送。
            //      另外它的 `ghost_npc_dreamnail` FSM 也已经被关掉、convoAmount=0，双保险。
            //  · 入口（德特茅斯那只）→ 用户要求梦钉**要**弹「既然你这么不知好歹，那就去地狱吧！」
            //    ⇒ 放行原版（它会走全局 FSM `Enemy Dream Msg`，文本由我们的语言钩子按 `Enemy Dreams` 表给），
            //      然后照样死亡式传送回迷宫。
            if (self != null)
            {
                Patchers.GhostMarker marker = self.GetComponent<Patchers.GhostMarker>();
                if (marker != null)
                {
                    if (marker.ExitOnDialogueEnd)
                    {
                        _logger.Log("[HKCS] 梦钉命中**出口**灵魂怪 → 按用户要求：**毫无反应**（不放行、不弹字、不传送）");
                        return;
                    }

                    _logger.Log("[HKCS] 梦钉命中入口灵魂怪 → 放行原版梦语（显示我们的梦语文本），随后死亡式传送");
                    orig(self);
                    marker.OnDreamNailed();
                    return;
                }
            }

            orig(self);   // 原版的怪：行为不变
        }

        // ────────────────────────────────────────────────────────────────
        //  场景切换的唯一咽喉
        //
        //  为什么要有这一层：改 TransitionPoint 只能抓到"用门走出去"的情况。
        //  实测 Crossroads_01 的"从井底往上爬"**不是** TransitionPoint 驱动的
        //  （把该场景里两道通向 Town 的门全改了，玩家照样回到德特茅斯），
        //  所以改在 GameManager.BeginSceneTransition 上 —— 谁都绕不过它。
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⚠ **总开关**（2026-09-26 按用户要求关掉）：
        /// true  = 旧行为：德特茅斯跳井 → 直接进我们第一间房；井底爬上来 → 也进房间。
        /// false = **恢复原版联通**：德特茅斯井口 ↔ 井底（Crossroads_01）照原版走，
        ///         进迷宫改由**德特茅斯的灵魂怪**负责（梦钉抽它 → 在迷宫椅子上醒来）。
        /// </summary>
        internal static readonly bool EnableVanillaGateRedirect = false;

        /// <summary>
        /// 路由表：出发场景 + 原本要去哪 → 实际去哪。
        /// 返回 null 表示不改（放行原版行为）。
        /// </summary>
        private static string ResolveRedirect(string fromScene, string toScene, out string entryPoint)
        {
            entryPoint = null;
            if (Rooms.All.Count == 0) return null;

            // 已按用户要求停用（见 EnableVanillaGateRedirect 的注释）：要恢复"跳井直接进房间"就改成 true
            if (!EnableVanillaGateRedirect) return null;

            // 德特茅斯跳井（原本 → Crossroads_01）→ 第一间房西侧
            if (fromScene == VanillaGates.TownScene && toScene == VanillaGates.CrossroadsScene)
            {
                entryPoint = Rooms.All[0].FromPrev;          // "left1"
                return Rooms.All[0].Scene;
            }

            // 十字路井底往上爬（原本 → Town）→ 第一间房东侧
            if (fromScene == VanillaGates.CrossroadsScene && toScene == VanillaGates.TownScene)
            {
                entryPoint = VanillaGates.CrossroadsEntry;   // "right1"
                return Rooms.All[0].Scene;
            }

            return null;
        }

        private void OnBeginSceneTransition(On.GameManager.orig_BeginSceneTransition orig,
                                            GameManager self, GameManager.SceneLoadInfo info)
        {
            string fromScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            string entryPoint;
            string newScene = ResolveRedirect(fromScene, info.SceneName, out entryPoint);

            if (newScene != null)
            {
                _logger.Log(string.Format("[HKCS] 拦截切换 {0} → {1}:{2} ⇒ 改为 {3}:{4}",
                    fromScene, info.SceneName, info.EntryGateName, newScene, entryPoint));
                info.SceneName = newScene;
                info.EntryGateName = entryPoint;
            }

            orig(self, info);
        }

        private AssetBundle LoadEmbedded(Assembly asm, string logicalName)
        {
            using (Stream s = asm.GetManifestResourceStream(logicalName))
            {
                return s == null ? null : AssetBundle.LoadFromStream(s);
            }
        }

        /// <summary>
        /// 启动时自检：RoomNames 里登记的每个房间，场景包里都得真有。
        /// 多房间最容易犯的错就是改完房间名忘了同步，这一步让它一进游戏就报出来。
        /// </summary>
        private void ValidateRooms(string[] bundleScenePaths)
        {
            foreach (RoomDef room in Rooms.All)
            {
                bool found = false;
                foreach (string path in bundleScenePaths)
                {
                    // GetAllScenePaths 返回 "assets/scenes/hkcs_room01.unity"
                    if (path.ToLowerInvariant().EndsWith("/" + room.Scene.ToLowerInvariant() + ".unity"))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    _logger.LogError(string.Format(
                        "[HKCS] 场景包里找不到房间 {0} —— 三种可能：① 这间房还没做（RoomNames 里先登记了，忽略这条）；" +
                        "② Unity 里那个场景的 AssetBundle 名没填 hkcs_scenes；③ 场景名不一致",
                        room.Scene));
                }
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  每个房间的尺寸
        //
        //  这是官方教程漏掉的一步。HK 的 GameManager 只认原版场景的尺寸表，
        //  自定义房间必须让 RefreshTilemapInfo 按**房间名**重新走一遍，
        //  否则相机边界、场景边缘的黑幕、地图 全是错的。
        //  多房间就是把这段从"一个 if"扩成"一张表"。
        // ────────────────────────────────────────────────────────────────
        private void OnRefreshTilemapInfo(On.GameManager.orig_RefreshTilemapInfo orig,
                                          GameManager self, string targetScene)
        {
            RoomDef room = Rooms.Get(targetScene);

            // ★ 顺序是关键：必须在 orig **之前**把替身 tk2dTileMap 准备好。
            //   原版 orig 会去场景根物体里找「tag = TileMap 且挂 tk2dTileMap」的物体，
            //   找到才会写 GameManager.tilemap 以及 sceneWidth/sceneHeight。
            //   没有它的后果（Player-prev.log 实测）：gm.tilemap == null
            //   → CameraController.GetTilemapInfo() 抛 NRE → 相机不居中、不跟随、
            //   xLimit/yLimit 永远停留在上一个场景的旧值。详见 TileMapFix.cs。
            tk2dTileMap ensured = (room == null)
                ? null
                : TileMapFix.Ensure(targetScene, room, _logger);

            orig(self, targetScene);

            if (room == null) return;

            // ⚠ 只有「确认 self.tilemap 就是我们刚补的那个（= 属于这间房）」才动它。
            //   拿不到时它可能是**别的原版场景残留的 tilemap**（fallback），
            //   改它的 width/height 会把那个原版房间的地图/相机尺寸一起改坏。
            if (ensured != null && self.tilemap == ensured)
            {
                self.tilemap.width = (int)room.Width;
                self.tilemap.height = (int)room.Height;
            }
            else if (self.tilemap == null)
            {
                _logger.LogError("[HKCS] " + targetScene +
                    " 还是没能拿到 tilemap（相机只能靠 OnGetTilemapInfo 兜底，地图会 NRE）");
            }

            self.sceneWidth = room.Width;
            self.sceneHeight = room.Height;

            // ★ 相机范围必须我们自己设：
            //   CameraController.GetTilemapInfo() 是 `xLimit = tilemap.width - 14.6f; yLimit = tilemap.height - 8.3f;`
            //   而它的 xLockMin/Max/yLockMin/Max 只在 Start() 里由 xLimit/yLimit 初始化一次，
            //   之后要靠原版房间里的 CameraLockArea 触发器才会刷新 —— 我们的房间没有，
            //   于是锁定框一直是最初的退化值 ⇒ 相机进场定位一次后就**不再跟随**骑士。
            //   这里按 HK 自己的公式补上，并把锁定框设成整间房。
            FixCameraLimits(room);

            GameMap map = UObject.FindObjectOfType<GameMap>();
            if (map != null)
            {
                // TODO(多房间地图)：每间房都从 (0,0) 画，地图界面上会互相压住。
                //  想做真正拼起来的地图，要么给每间房算一个全局偏移，
                //  要么走 SceneMapPatcher + 自定义 map texture 的路子。
                map.SetManualTilemap(0, 0, room.Width, room.Height);
            }

            _logger.Log(string.Format("[HKCS] {0} 尺寸设为 {1}x{2}",
                targetScene, room.Width, room.Height));
        }

        /// <summary>
        /// 把相机范围/锁定框设成这间房的尺寸（照抄 HK 自己的算法）。
        /// HK 原版：xLimit = 房宽 - 14.6，yLimit = 房高 - 8.3（14.6 / 8.3 是半个屏幕），
        /// 相机中心被夹在 [14.6, xLimit] × [8.3, yLimit] 之间，锁定框默认就是整间房。
        /// </summary>
        private void FixCameraLimits(RoomDef room)
        {
            try
            {
                if (GameCameras.instance == null) return;
                CameraController cam = GameCameras.instance.cameraController;
                if (cam == null) return;

                cam.sceneWidth = room.Width;
                cam.sceneHeight = room.Height;
                cam.xLimit = room.Width - 14.6f;
                cam.yLimit = room.Height - 8.3f;
                cam.xLockMin = 0f;
                cam.xLockMax = cam.xLimit;
                cam.yLockMin = 0f;
                cam.yLockMax = cam.yLimit;

                _logger.Log(string.Format("[HKCS] 相机范围设为 xLimit={0:0.#} yLimit={1:0.#}（锁定框 0~{0:0.#} / 0~{1:0.#}）",
                    cam.xLimit, cam.yLimit));
            }
            catch (System.Exception e)
            {
                _logger.LogError("[HKCS] 设相机范围失败：" + e.Message);
            }
        }

        /// <summary>
        /// 相机兜底。
        ///
        /// 原版 <c>CameraController.GetTilemapInfo()</c> 只有四行：
        /// <code>
        /// tilemap = gm.tilemap;  sceneWidth = tilemap.width;  sceneHeight = tilemap.height;
        /// xLimit = sceneWidth - 14.6f;  yLimit = sceneHeight - 8.3f;
        /// </code>
        /// <c>gm.tilemap</c> 为 null 时它在第一行就抛 NRE。而这个方法被两处调用：
        /// <list type="bullet">
        /// <item><c>CameraController.SceneInit()</c>（进场瞬间，由 GameCameras.StartScene 调）
        /// ⇒ 抛异常后 <c>xLimit/yLimit</c> 保持上一个场景的旧值（边界错、能看到房间外的黑）；</item>
        /// <item><c>CameraController.&lt;DoPositionToHero&gt;()</c>（落点/重生时）
        /// ⇒ **协程直接中断**：相机不回正到骑士身上、mode 也不会切回 FOLLOWING
        /// （就是"坐上长椅退到菜单重进后相机锁住、不跟随"的直接原因），
        /// 连它末尾那句 <c>cameraFadeFSM "LEVEL LOADED"</c> 都发不出去。</item>
        /// </list>
        /// TileMapFix 补上 TileMap 之后这里本该什么都不用做；这层兜底是为了
        /// 「万一 tilemap 还是没补上」时相机也一定对（我们的房间尺寸来自房间表，比 tilemap 更权威）。
        /// </summary>
        private void OnGetTilemapInfo(On.CameraController.orig_GetTilemapInfo orig, CameraController self)
        {
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            RoomDef room = Rooms.Get(scene);
            if (room == null)
            {
                orig(self);          // 原版场景：一个字都不改，走原逻辑
                return;
            }

            float oldX = self.xLimit;
            try
            {
                orig(self);
            }
            catch (NullReferenceException)
            {
                // gm.tilemap == null —— 由下面的兜底负责
            }

            self.sceneWidth = room.Width;
            self.sceneHeight = room.Height;
            self.xLimit = room.Width - TileMapFix.CameraHalfWidth;
            self.yLimit = room.Height - TileMapFix.CameraHalfHeight;

            if (self.xLimit != oldX)
            {
                _logger.Log(string.Format(
                    "[HKCS] 相机边界兜底：{0} → xLimit {1:0.#} → {2:0.#}（房间 {3}x{4}）",
                    scene, oldX, self.xLimit, room.Width, room.Height));
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  门
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 把原版场景里已有的门改指向我们的房间（推荐：不去动原场景结构）。
        /// `door#` 类型的门还要同步它门控 FSM 的变量，否则开门逻辑会把你送回原目标。
        /// </summary>
        public void RedirectVanillaGate(Scene scene, string gateName,
                                       string targetScene, string entryPoint)
        {
            GameObject gateGo = FindInScene(scene, gateName);
            if (gateGo == null)
            {
                _logger.LogError(string.Format("[HKCS] {0} 里找不到门 {1}",
                    scene.name, gateName));
                return;
            }

            TransitionPoint tp = gateGo.GetComponent<TransitionPoint>();
            if (tp == null)
            {
                _logger.LogError(string.Format("[HKCS] {0} 上有同名物体但没有 TransitionPoint",
                    gateName));
                return;
            }

            tp.SetTargetScene(targetScene);
            tp.entryPoint = entryPoint;

            // `door#` 的门由 "Door Control" FSM 驱动，光改 TransitionPoint 不生效
            if (gateName.StartsWith("door"))
            {
                PlayMakerFSM fsm = gateGo.LocateMyFSM("Door Control");
                if (fsm != null)
                {
                    fsm.FsmVariables.GetFsmString("New Scene").Value = targetScene;
                    fsm.FsmVariables.GetFsmString("Entry Gate").Value = entryPoint;
                }
            }

            _logger.Log(string.Format("[HKCS] {0}.{1} → {2}:{3}",
                scene.name, gateName, targetScene, entryPoint));
        }

        /// <summary>
        /// 把场景里**所有**"原本指向 <paramref name="fromTargetScene"/> 的门"改指向我们的房间。
        ///
        /// 为什么不按门名改一道就完事：原版一个房间里**可能有好几道门通向同一个场景**。
        /// 实测 Crossroads_01 里就有两道门的目标都是 `Town`:`bot1`
        /// （直接扫 level37 的场景数据得到的：(Town,bot1)、(Town,bot1)、(Crossroads_02,left1)、(Crossroads_07,right1)），
        /// 只改其中一道（比如 top2）就会出现"一个方向能进、另一个方向照样回原版场景"。
        /// 按目标场景扫，名字叫什么都不影响。
        /// </summary>
        public int RedirectAllGatesTo(Scene scene, string fromTargetScene,
                                      string newScene, string newEntryPoint)
        {
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (TransitionPoint tp in root.GetComponentsInChildren<TransitionPoint>(true))
                {
                    if (tp.targetScene != fromTargetScene) continue;

                    string gateName = tp.gameObject.name;
                    tp.SetTargetScene(newScene);
                    tp.entryPoint = newEntryPoint;

                    // door# 的门由 "Door Control" FSM 驱动，要一起同步
                    if (gateName.StartsWith("door"))
                    {
                        PlayMakerFSM fsm = tp.gameObject.LocateMyFSM("Door Control");
                        if (fsm != null)
                        {
                            fsm.FsmVariables.GetFsmString("New Scene").Value = newScene;
                            fsm.FsmVariables.GetFsmString("Entry Gate").Value = newEntryPoint;
                        }
                    }

                    _logger.Log(string.Format("[HKCS] {0}: 接管门 '{1}'（原本 → {2}）现指向 {3}:{4}",
                        scene.name, gateName, fromTargetScene, newScene, newEntryPoint));
                    count++;
                }
            }

            if (count == 0)
            {
                _logger.LogError(string.Format(
                    "[HKCS] {0} 里没有任何门指向 {1} —— 目标场景名写错了，或该房间的出口不是 TransitionPoint",
                    scene.name, fromTargetScene));
            }
            return count;
        }

        /// <summary>
        /// 在原版场景里**新建**一道门（原版没有现成的门可改时用）。
        /// pos 是场景坐标；size 是触发区像素尺寸；entryGate 是对方场景里的落点名。
        /// </summary>
        public GameObject CreateGateway(string gateName, Scene scene, Vector2 pos, Vector2 size,
                                        string toScene, string entryGate, Vector2 respawnOffset,
                                        bool enterFromRight, bool enterFromLeft, bool onlyOut,
                                        GameManager.SceneLoadVisualizations vis)
        {
            GameObject gate = new GameObject(gateName);
            // 全限定：Assembly-CSharp 里也有一个全局的 SceneManager（HK 自己的类），会遮蔽 UnityEngine 的
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(gate, scene);
            gate.transform.SetPosition2D(pos);

            TransitionPoint tp = gate.AddComponent<TransitionPoint>();
            if (!onlyOut)
            {
                BoxCollider2D bc = gate.AddComponent<BoxCollider2D>();
                bc.size = size;
                bc.isTrigger = true;
                tp.SetTargetScene(toScene);
                tp.entryPoint = entryGate;
            }
            tp.alwaysEnterLeft = enterFromLeft;
            tp.alwaysEnterRight = enterFromRight;
            tp.sceneLoadVisualization = vis;

            // 死后重生的落点：TransitionPoint 必须挂一个 HazardRespawnMarker，
            // 否则从这间房摔死会回到很奇怪的地方。
            GameObject rm = new GameObject("Hazard Respawn Marker");
            rm.transform.SetParent(gate.transform);
            rm.transform.SetPosition2D(pos.x + respawnOffset.x, pos.y + respawnOffset.y);
            HazardRespawnMarker marker = rm.AddComponent<HazardRespawnMarker>();
            marker.respawnFacingRight = enterFromRight;
            tp.respawnMarker = marker;

            return gate;
        }

        /// <summary>
        /// 房间之间互相连起来：第 i 间的 toNext 门 → 第 i+1 间的 fromPrev 落点。
        /// 在 Unity 里手摆门也可以，但房间一多就容易连错；这张表改起来更省事。
        /// </summary>
        public void BuildRoomLinks(Scene scene)
        {
            RoomDef room = Rooms.Get(scene.name);
            if (room == null) return;

            int index = Rooms.All.IndexOf(room);
            if (index < 0 || index + 1 >= Rooms.All.Count) return;

            RoomDef next = Rooms.All[index + 1];
            GameObject gateGo = FindInScene(scene, room.ToNext);
            if (gateGo == null)
            {
                _logger.LogError(string.Format("[HKCS] {0} 里找不到通往下一间的门 {1}",
                    room.Scene, room.ToNext));
                return;
            }
            TransitionPoint tp = gateGo.GetComponent<TransitionPoint>();
            if (tp != null)
            {
                tp.SetTargetScene(next.Scene);
                tp.entryPoint = next.FromPrev;
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  工具
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 在场景里按名字找物体（**包含未激活的**）。
        /// 不用 SFCore 的 scene.Find，因为它的行为在活跃/非活跃上有版本差异，
        /// 这里自己走一遍更可控 —— 门和 _Managers 常常是未激活状态。
        /// </summary>
        public static GameObject FindInScene(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                GameObject hit = FindRecursive(root.transform, name);
                if (hit != null) return hit;
            }
            return null;
        }

        private static GameObject FindRecursive(Transform t, string name)
        {
            if (t.name == name) return t.gameObject;
            for (int i = 0; i < t.childCount; i++)
            {
                GameObject hit = FindRecursive(t.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
