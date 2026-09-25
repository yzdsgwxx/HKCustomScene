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
        }

        public void Unhook()
        {
            On.GameManager.RefreshTilemapInfo -= OnRefreshTilemapInfo;
            On.GameManager.BeginSceneTransition -= OnBeginSceneTransition;
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
        /// 路由表：出发场景 + 原本要去哪 → 实际去哪。
        /// 返回 null 表示不改（放行原版行为）。
        /// </summary>
        private static string ResolveRedirect(string fromScene, string toScene, out string entryPoint)
        {
            entryPoint = null;
            if (Rooms.All.Count == 0) return null;

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
                        "[HKCS] 场景包里找不到房间 {0} —— Unity 里那个场景的 AssetBundle 名没设，或名字不一致",
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
            orig(self, targetScene);

            RoomDef room = Rooms.Get(targetScene);
            if (room == null) return;

            self.tilemap.width = (int)room.Width;
            self.tilemap.height = (int)room.Height;
            self.sceneWidth = room.Width;
            self.sceneHeight = room.Height;

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
