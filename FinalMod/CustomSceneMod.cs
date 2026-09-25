using System;
using System.Collections.Generic;
using SFCore;
using SFCore.Generics;   // SaveSettingsMod<T> 在这里（不在 SFCore 根命名空间）
using SFCore.Utils;
using UnityEngine;
// 不 using Modding：会同时把 Modding.Logger（静态类）拉进来，和 UnityEngine.Logger 撞名（CS0104）。
// 需要的两个类型各自取别名即可。注意 Modding.IMod 本身就继承 Modding.ILogger ——
// 也就是说这个 mod 类自己就是一个 ILogger，要传日志器就把 this 传出去。
using ModHooks = Modding.ModHooks;
using ReflectionHelper = Modding.ReflectionHelper;
using UnityEngine.SceneManagement;
using HKCustomSceneMod.Consts;
using UObject = UnityEngine.Object;

namespace HKCustomSceneMod
{
    /// <summary>
    /// 多个自定义房间的 Mod 主类。
    ///
    /// 与"单房间"教程的差别（这三条是多房间才需要的）：
    ///   1. 房间清单集中在 Consts/Rooms，场景切换时按房间名分派；
    ///   2. 每个房间都要**显式再调一次** GameManager.RefreshTilemapInfo(房间名)，
    ///      否则 HK 只认原版的 tilemap 尺寸 —— 这是官方教程没写的一步；
    ///   3. 房间之间的门用 SceneChanger.CreateGateway 在运行时造，
    ///      不用在 Unity 里预先摆（省掉 N 个房间互相引用场景名的手工活）。
    /// </summary>
    public class CustomSceneMod : SaveSettingsMod<SettingsClass>
    {
        internal static CustomSceneMod Instance;

        /// <summary>由 SceneChanger 持有，负责 AssetBundle 与每房间尺寸。</summary>
        public SceneChanger SceneChanger { get; private set; }

        private static readonly string[] AreaTitleKeys =
        {
            AreaInfo.AreaEvent + "_SUPER",
            AreaInfo.AreaEvent + "_MAIN",
            AreaInfo.AreaEvent + "_SUB",
        };

        public CustomSceneMod() : base("HK Custom Scene")
        {
            Instance = this;
            InitCallbacks();
        }

        public override string GetVersion() => "0.1.0";

        /// <summary>
        /// 预载原版组件。**尽量全从同一个场景拿**——预载是加载耗时的大头，
        /// 每多一个来源场景就多一次整场景加载。
        /// 多房间不需要为每个房间重复预载：这些 prefab 是跨房间共用的。
        /// </summary>
        public override List<ValueTuple<string, string>> GetPreloadNames()
        {
            List<ValueTuple<string, string>> list = new List<ValueTuple<string, string>>
            {
                // ── 原版组件（都来自同一个场景，预载是加载耗时大头，尽量少换场景）──
                // 区域名 / 子区域名显示
                new ValueTuple<string, string>("White_Palace_18", "Area Title Controller"),
                // PlayMaker 名字显示（Dream Nail 名字等）依赖它
                new ValueTuple<string, string>("White_Palace_18", "_Managers/PlayMaker Unity 2D"),
                // 场景管理器（每间房都需要，SceneChanger 会给每个房间实例化一份）
                new ValueTuple<string, string>("White_Palace_18", "_SceneManager"),

                // ── 长椅（存档点）──
                // Crossroads_47 是带长椅的最小场景（Benchwarp 的 ObjectCache 用的也是这一条）。
                // 长椅必须整只克隆：坐下/存档逻辑在它自带的 "Bench Control" FSM 里。
                new ValueTuple<string, string>("Crossroads_47", "RestBench"),

                // ── 原版小怪 ──
                // ⚠ 路径**不是猜的**：2026-09-26 用 UnityPy 直接读游戏自己的场景文件
                //    （level37 = Crossroads/Crossroads_01、level43 = Crossroads/Crossroads_07）得到。
                //    Crossroads_01 的 `_Enemies` 下只有这四只：Climber / Climber 1 / Crawler 1 / Zombie Runner。
                //    这里**一条都不多请求**：请求了不存在的路径，API 每次启动都会打一行红字
                //    `could not load '场景/路径.prefab'`（以前那一堆候选就是这样刷屏的）。
                new ValueTuple<string, string>("Crossroads_01", "_Enemies/Zombie Runner"),
                new ValueTuple<string, string>("Crossroads_01", "_Enemies/Crawler 1"),
                new ValueTuple<string, string>("Crossroads_01", "_Enemies/Climber"),

                // 苍蝇：Crossroads_01 里**没有**独立苍蝇物体（那里的 "Fly"/"Fly Left"/"Fly Right"
                // 只是爆裂僵尸 FSM 里的变量/状态名），所以只能换个场景拿 —— 多一个来源场景
                // = 启动时多一次整场景加载，这是苍蝇唯一的额外代价。
                new ValueTuple<string, string>("Crossroads_07", "Uninfected Parent/Fly"),
                // ⚠ 加新怪种要改三处：这里、PrefabHolder 的路径数组、PatchEnemy 的枚举（末尾追加）。
                //    枚举改了还要跑一次 `tools\更新壳工程.cmd`，否则 Unity 里选不到新怪种。

                // ── 原版灵魂怪（德特茅斯的"掘墓者"）──
                // 用 UnityPy 读 `level7`(= Town) 得到的路径；坐标实测 (211.64, 8.33, 0.00)。
                // 用途：PatchGhost 在**原版场景**里注入一只"长得跟他一样"的灵魂怪（迷宫入口）。
                new ValueTuple<string, string>("Town", "_NPCs/Gravedigger NPC"),
            };

            // ── 可克隆的原版美术件（光效/雕像/装饰）──
            // 清单在 PrefabHolder.VanillaPropPaths 里（单一来源），加新物体只改那一处
            list.AddRange(PrefabHolder.VanillaPropPaths);
            return list;
        }

        public override void Initialize(Dictionary<string, Dictionary<string, GameObject>> preloadedObjects)
        {
            // 对话文本：先读 mod 目录下的 DialogueConfig.json（缺文件就照出厂模板建一份出来）
            DialogueConfig.Load();

            SceneChanger = new SceneChanger(preloadedObjects, this);
            SceneChanger.Init();

            foreach (RoomDef r in Rooms.All)
            {
                Modding.Logger.Log(string.Format("[HKCS] 登记房间 {0} ({1}x{2})", r.Scene, r.Width, r.Height));
            }

            // ── 写死的"入口灵魂怪"（2026-09-26 按用户要求：不需要在编辑器里配）──
            // 德特茅斯掘墓者（_NPCs/Gravedigger NPC，实测 (211.64, 8.33)）**右边 4 格**；
            // 克隆原版外观；梦钉抽它 → 在迷宫（Rooms.All[0]）的那把椅子上醒来。
            // ⚠ 文本全部来自 `DialogueConfig.json`（见文件顶部 DialogueConfig.Load()）：
            //    这里的 text = ① 交互文本；梦语 = ② entryDream（PatchGhost.EntryDreamText）。
            Patchers.PatchGhost.RegisterHardcoded(
                key: "HKCS_GHOST_Entry",
                scene: "Town",
                pos: new Vector2(215.64f, 8.33f),
                vanillaLook: true,
                text: DialogueConfig.EntryInteract,
                targetScene: Rooms.All[0].Scene,
                targetBench: "HKCS_Bench",
                delay: 2.5f);   // 梦钉会先弹梦语（entryDream）⇒ 留 2.5 秒再死
        }

        private void InitCallbacks()
        {
            ModHooks.GetPlayerBoolHook += OnGetPlayerBoolHook;
            ModHooks.SetPlayerBoolHook += OnSetPlayerBoolHook;
            ModHooks.LanguageGetHook += OnLanguageGetHook;
            // 全限定：Assembly-CSharp 里也有一个全局的 SceneManager（HK 自己的类），会遮蔽 UnityEngine 的
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnSceneChanged;
        }

        // ────────────────────────────────────────────────────────────────
        //  场景切换：多房间分派的唯一入口
        // ────────────────────────────────────────────────────────────────
        private void OnSceneChanged(Scene from, Scene to)
        {
            string scene = to.name;

            // 0. 往**原版场景**里注入灵魂怪（PatchGhost 里勾了"注入原版场景"的那些配置卡）
            //    原版场景每次加载都重造一次：场景卸载它就跟着销毁，不会跑到别的场景去
            //    ⚠ 把 `to`（新场景）传给 SpawnForScene：它自己 GetActiveScene() 会拿到**上一个**场景，
            //      导致怪被塞进正在卸载的场景里、跟着一起销毁（2026-09-26 出口怪"看不见"的真凶）。
            Patchers.PatchGhost.SpawnForScene(to);

            // 0.5 全局后门：只在我们自己的房间里启用（防卡死，不用在场景里摆东西）
            GlobalBackdoor.SetScene(scene);

            // A. 玩家进了我们自己的一间房 → 让 HK 重新读这个房间的尺寸
            RoomDef room = Rooms.Get(scene);
            if (room != null)
            {
                GameManager.instance.RefreshTilemapInfo(scene);

                if (room.ShowAreaTitle)
                {
                    // 区域名由 Unity 场景里预先摆好的 PatchAreaTitleController 负责，
                    // 这里只记一条日志方便排查"区域名没弹出来"。
                    Modding.Logger.Log(string.Format("[HKCS] 进入 {0}，到访 bool = {1}",
                        scene, PlayerData.instance.GetBool(AreaInfo.VisitedBool)));
                }
                return;
            }

            // B. 玩家在原版场景里 → 把该场景的入口门改指向我们的第一间房
            //    ⚠ 两个方向的落点**不一样**：Town 侧落 left1（西），Crossroads 侧落 right1（东）。

            // ⚠ 名字带 HKCS_ 前缀却没登记进 Rooms.All 的场景：尺寸/相机边界/地图/TileMap
            //    全都不会被接管 ⇒ 会原样重现 2026-09-26 那串 CameraController.GetTilemapInfo NRE
            //    （相机锁死、不居中、不跟随）。这种情况必须大声报出来，别让它静默失败。
            if (scene.StartsWith(Rooms.Prefix, StringComparison.Ordinal))
            {
                Modding.Logger.LogError(string.Format(
                    "[HKCS] 场景 {0} 有 '{1}' 前缀，但不在 Consts/RoomNames.cs 的 Rooms.All 里 —— " +
                    "尺寸/相机边界/地图/TileMap 都没人接管（相机会锁死、不跟随）。加一行 RoomDef 即可。",
                    scene, Rooms.Prefix));
            }

            // （调试）把关键物体的层级路径打进日志，用来找 GetPreloadNames 要写的路径字符串
            ScenePathDump.TryDump(to);

            // ⚠ 2026-09-26 按用户要求**恢复原版联通**：不再改原版场景里的门，
            //    德特茅斯井口 ↔ 井底（Crossroads_01）照原版走；
            //    进迷宫改由"德特茅斯的灵魂怪"负责（见 Initialize 里写死的入口灵魂怪）。
            //    想恢复旧的"跳井直接进房间"，把 SceneChanger.EnableVanillaGateRedirect 改成 true。
            if (!SceneChanger.EnableVanillaGateRedirect) return;

            if (scene == VanillaGates.TownScene)
            {
                SceneChanger.RedirectVanillaGate(to, VanillaGates.TownGate,
                    Rooms.All[0].Scene, Rooms.All[0].FromPrev);
            }
            else if (scene == VanillaGates.CrossroadsScene)
            {
                // ⚠ 不按门名改：实测 Crossroads_01 里有**两道**门的目标都是 Town:bot1
                //    （扫场景数据得到），只改 top2 会导致"爬上来照样回德特茅斯"。
                //    按"目标场景"扫，把通向 Town 的门全部接管。
                SceneChanger.RedirectAllGatesTo(to, VanillaGates.TownScene,
                    Rooms.All[0].Scene, VanillaGates.CrossroadsEntry);
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  存档 / 语言
        // ────────────────────────────────────────────────────────────────
        private bool OnGetPlayerBoolHook(string target, bool orig)
        {
            var field = ReflectionHelper.GetFieldInfo(typeof(SettingsClass), target);
            if (field != null) return (bool)field.GetValue(SaveSettings);
            return orig;
        }

        private bool OnSetPlayerBoolHook(string target, bool orig)
        {
            var field = ReflectionHelper.GetFieldInfo(typeof(SettingsClass), target);
            if (field != null) field.SetValue(SaveSettings, orig);
            return orig;
        }

        /// <summary>区域名三段 + 灵魂怪的梦钉文本。想改文字就改这里/改组件上的字段，不用碰 Unity。</summary>
        private string OnLanguageGetHook(string key, string sheet, string orig)
        {
            // 灵魂怪（PatchGhost）的梦钉文本：键 = HKCS_GHOST_<摆放点名>，后缀由原版 UI 自己拼
            string ghostText = Patchers.PatchGhost.GetConvo(key);
            if (ghostText != null)
            {
                // ⚠ 2026-09-26：梦钉那条路的 sheet 实测就叫 "Enemy Dreams"
                //    · 入口怪 → 显示「既然你这么不知好歹，那就去地狱吧！」
                //    · 出口怪 → 空串（梦钉不弹字）
                if (sheet == "Enemy Dreams")
                {
                    string dream = Patchers.PatchGhost.GetDreamConvo(key);
                    Modding.Logger.Log(string.Format("[HKCS] 梦语：key={0} → 「{1}」", key, dream ?? "(放行原版)"));
                    if (dream != null) return dream;
                }
                else
                {
                    // 交互那条路：把真实文本给他
                    Modding.Logger.Log(string.Format("[HKCS] 交互文本命中：key={0} sheet={1} → 「{2}」",
                        key, sheet, ghostText));
                    return ghostText;
                }
            }

            if (sheet == "Titles")
            {
                if (key == AreaTitleKeys[0]) return "被遗忘的";
                if (key == AreaTitleKeys[1]) return "塔楼群";
                if (key == AreaTitleKeys[2]) return "下层";
            }
            return orig;
        }

    }
}
