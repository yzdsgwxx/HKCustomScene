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
            return new List<ValueTuple<string, string>>
            {
                // 区域名 / 子区域名显示
                new ValueTuple<string, string>("White_Palace_18", "Area Title Controller"),
                // PlayMaker 名字显示（Dream Nail 名字等）依赖它
                new ValueTuple<string, string>("White_Palace_18", "_Managers/PlayMaker Unity 2D"),
                // 场景管理器（每间房都需要，SceneChanger 会给每个房间实例化一份）
                new ValueTuple<string, string>("White_Palace_18", "_SceneManager"),
            };
        }

        public override void Initialize(Dictionary<string, Dictionary<string, GameObject>> preloadedObjects)
        {
            SceneChanger = new SceneChanger(preloadedObjects, this);
            SceneChanger.Init();

            foreach (RoomDef r in Rooms.All)
            {
                Modding.Logger.Log(string.Format("[HKCS] 登记房间 {0} ({1}x{2})", r.Scene, r.Width, r.Height));
            }
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

        /// <summary>区域名三段。想改文字就改这里，不用碰 Unity。</summary>
        private string OnLanguageGetHook(string key, string sheet, string orig)
        {
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
