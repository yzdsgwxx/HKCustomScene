using System;
using UnityEngine;
using HKCustomSceneMod.Consts;
using ILogger = Modding.ILogger;

namespace HKCustomSceneMod
{
    /// <summary>
    /// 给自定义房间补一个「空但合法」的 <c>tk2dTileMap</c>。
    ///
    /// ── 为什么非有它不可（2026-09-26 实测，日志为证）────────────────────────
    /// 之前进 HKCS_Room01 时 Player-prev.log 里稳定报这几条：
    /// <code>
    /// Using fallback 1 to find tilemap. Scene HKCS_Room01 requires manual fixing.
    /// Using fallback 2 to find tilemap. Scene HKCS_Room01 requires manual fixing.
    /// Failed to find tilemap in HKCS_Room01 entirely.
    /// NullReferenceException
    ///   at CameraController.GetTilemapInfo ()
    ///   at CameraController.SceneInit ()
    ///   at GameCameras.StartScene () ← GameManager.BeginScene()
    /// NullReferenceException
    ///   at CameraController.GetTilemapInfo ()
    ///   at CameraController+&lt;DoPositionToHero&gt;d__74.MoveNext ()
    /// </code>
    /// 后果（就是用户看到的两个现象）：
    /// <list type="number">
    /// <item>SceneInit 在 GetTilemapInfo 抛异常 ⇒ <c>xLimit/yLimit</c> **从来没被赋值**，
    /// 相机右/上边界一直是上一个场景留下的旧值 ⇒ 边界不对、能看到房间外的黑。</item>
    /// <item>PositionToHero 协程同样在 GetTilemapInfo 抛异常 ⇒ 协程中断
    /// ⇒ 相机永远停在进场位置、不回正到骑士身上、也不跟随；
    /// 而且它末尾那句 <c>cameraFadeFSM "LEVEL LOADED"</c> 也发不出去（淡入淡出会卡住）。</item>
    /// </list>
    ///
    /// ── 原版是怎么找 tilemap 的（反编译 GameManager.cs:2007-2065）──────────
    /// 先扫「<b>场景根物体</b>」里 CompareTag("TileMap") 且挂 tk2dTileMap 的；
    /// 找不到 → <c>FindGameObjectsWithTag("TileMap")</c>；再找不到 → 找名字叫 "TileMap" 的。
    /// 所以替身必须满足：① 是**场景根物体**；② tag = "TileMap"；③ 挂 tk2dTileMap。
    /// 找到后原版会写 <c>GameManager.tilemap</c> 以及 <c>sceneWidth/sceneHeight = tilemap.width/height</c>，
    /// 相机再据此算 <c>xLimit = sceneWidth - 14.6f</c>、<c>yLimit = sceneHeight - 8.3f</c>。
    ///
    /// ── 为什么运行时 AddComponent 是安全的 ────────────────────────────────
    /// 反编译 <c>tk2dTileMap.Awake()</c>（tk2dTileMap.cs:132-163）：玩家端只有
    /// 「<c>spriteCollection != null &amp;&amp; data != null &amp;&amp; renderData == null</c>」才会 Build()，
    /// 我们三项都是 null ⇒ Awake 什么都不做；OnDestroy 也对 null 做了保护。
    /// （Unity 工程里没有 tk2d 脚本，所以在编辑器里加不了这个组件 —— 只能在运行时补。）
    /// </summary>
    public static class TileMapFix
    {
        /// <summary>替身物体的名字（原版 fallback 2 也是按这个名字找）。</summary>
        public const string ObjectName = "TileMap";

        /// <summary>原版 GameManager.GetTileMap() 要求这个 tag。</summary>
        public const string TagName = "TileMap";

        /// <summary>
        /// 相机半宽 / 半高。**这两个数字是原版 CameraController 里写死的**
        /// （KeepWithinSceneBounds / LateUpdate：x 限制在 [14.6, xLimit]，y 限制在 [8.3, yLimit]），
        /// 也正因为写死，才有「房间必须从 (0,0) 开始铺、宽度必须正好等于地形宽度」这条硬规矩。
        /// </summary>
        public const float CameraHalfWidth = 14.6f;
        public const float CameraHalfHeight = 8.3f;

        /// <summary>
        /// 保证 <paramref name="sceneName"/> 这间房里有可用的 tk2dTileMap，并把宽高设成房间表的值。
        /// 已经存在就只改宽高（幂等）；场景还没加载完就返回 null（调用方稍后还会再调一次）。
        /// </summary>
        public static tk2dTileMap Ensure(string sceneName, RoomDef room, ILogger log)
        {
            if (room == null) return null;

            UnityEngine.SceneManagement.Scene scene = FindLoadedScene(sceneName);
            if (!scene.IsValid())
            {
                // 正常：activeSceneChanged 里第一次调时新场景可能还没登记完，原版紧接着还会再调一次。
                return null;
            }

            tk2dTileMap tilemap = FindTileMap(scene, log);
            if (tilemap == null) tilemap = Create(scene, room, log);
            if (tilemap == null) return null;

            // 房间表是权威尺寸（tk2dTileMap.width/height 是 int，房间表是 float）。
            tilemap.width = (int)room.Width;
            tilemap.height = (int)room.Height;
            return tilemap;
        }

        public static UnityEngine.SceneManagement.Scene FindLoadedScene(string sceneName)
        {
            int count = UnityEngine.SceneManagement.SceneManager.sceneCount;
            for (int i = 0; i < count; i++)
            {
                UnityEngine.SceneManagement.Scene s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (s.IsValid() && s.name == sceneName) return s;
            }
            return default(UnityEngine.SceneManagement.Scene);
        }

        private static tk2dTileMap FindTileMap(UnityEngine.SceneManagement.Scene scene, ILogger log)
        {
            GameObject[] roots = scene.GetRootGameObjects();

            // ① 完全符合原版规则的
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].CompareTag(TagName))
                {
                    tk2dTileMap tm = roots[i].GetComponent<tk2dTileMap>();
                    if (tm != null) return tm;
                }
            }

            // ② 挂了组件但 tag 不对（原版会因此找不到）→ 顺手把 tag 补上
            for (int i = 0; i < roots.Length; i++)
            {
                tk2dTileMap tm = roots[i].GetComponent<tk2dTileMap>();
                if (tm == null) continue;
                try
                {
                    roots[i].tag = TagName;
                    log.Log(string.Format("[HKCS] {0} 里的 '{1}' 缺 tag '{2}'，已补上",
                        scene.name, roots[i].name, TagName));
                }
                catch (Exception e)
                {
                    log.LogError(string.Format("[HKCS] 给 '{0}' 设 tag 失败：{1}",
                        roots[i].name, e.Message));
                }
                return tm;
            }

            return null;
        }

        private static tk2dTileMap Create(UnityEngine.SceneManagement.Scene scene, RoomDef room, ILogger log)
        {
            GameObject go = null;
            try
            {
                go = new GameObject(ObjectName);
                if (go.scene != scene)
                {
                    // 必须落到**这间房**的场景里，否则 RefreshTilemapInfo 扫不到它
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                }
                go.tag = TagName;

                tk2dTileMap tm = go.AddComponent<tk2dTileMap>();
                tm.width = (int)room.Width;
                tm.height = (int)room.Height;

                log.Log(string.Format(
                    "[HKCS] {0} 里没有 tk2dTileMap → 已补一个空替身（tag={1}，{2}x{3}）。" +
                    "没有它原版 GameManager.tilemap == null，相机会 NRE（不居中、不跟随、边界错）。",
                    scene.name, TagName, tm.width, tm.height));

                LogGeometry(scene, room, log);
                return tm;
            }
            catch (Exception e)
            {
                log.LogError(string.Format("[HKCS] 给 {0} 补 TileMap 失败：{1} {2}",
                    scene.name, e.GetType().Name, e.Message));
                if (go != null) UnityEngine.Object.Destroy(go);
                return null;
            }
        }

        /// <summary>
        /// 把「这间房实际有多大」打进日志 —— 因为相机边界完全由房间表决定，
        /// 而房间表写错了在游戏里只表现为「相机能看到房间外的黑」，很难反推。
        /// 量的是 MeshRenderer（地形）和 Collider2D（碰撞）的并集包围盒，都是世界坐标。
        /// </summary>
        private static void LogGeometry(UnityEngine.SceneManagement.Scene scene, RoomDef room, ILogger log)
        {
            try
            {
                bool hasMesh = false;
                Bounds mesh = default(Bounds);
                bool hasCol = false;
                Bounds col = default(Bounds);

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (!hasMesh) { mesh = r.bounds; hasMesh = true; }
                        else mesh.Encapsulate(r.bounds);
                    }
                    foreach (Collider2D c in root.GetComponentsInChildren<Collider2D>(true))
                    {
                        if (!hasCol) { col = c.bounds; hasCol = true; }
                        else col.Encapsulate(c.bounds);
                    }
                }

                if (hasMesh)
                {
                    log.Log(string.Format(
                        "[HKCS] {0} 地形网格实测：x[{1:F2} ~ {2:F2}]（宽 {3:F2}）、y[{4:F2} ~ {5:F2}]（高 {6:F2}）",
                        scene.name, mesh.min.x, mesh.max.x, mesh.size.x,
                        mesh.min.y, mesh.max.y, mesh.size.y));
                }
                if (hasCol)
                {
                    log.Log(string.Format("[HKCS] {0} 碰撞体实测：x[{1:F2} ~ {2:F2}]、y[{3:F2} ~ {4:F2}]",
                        scene.name, col.min.x, col.max.x, col.min.y, col.max.y));
                }

                log.Log(string.Format(
                    "[HKCS] 房间表登记 {0}x{1} ⇒ 相机 xLimit={2:F1}、yLimit={3:F1}" +
                    "（原版把左边界写死成 x=14.6、下边界写死成 y=8.3 ⇒ 地形要从 (0,0) 开始铺，" +
                    "Width 要等于地形实际宽度，否则右/上边会露出房间外的黑）",
                    room.Width, room.Height,
                    room.Width - CameraHalfWidth, room.Height - CameraHalfHeight));

                // ── 两条"新建房间最容易踩"的自动体检（都是相机边界的直接来源）──
                if (hasMesh && (mesh.min.x < -0.5f || mesh.min.y < -0.5f))
                {
                    log.LogError(string.Format(
                        "[HKCS] ⚠ {0} 的地形没有从 (0,0) 开始铺：实测左下角 ({1:F2}, {2:F2})。" +
                        "原版把相机左边界写死 x=14.6、下边界写死 y=8.3 ⇒ 视野只覆盖 x[0,Width] y[0,Height]，" +
                        "x<0 / y<0 那部分玩家永远看不到。把地形整体挪到 ≥0（或改网格顶点）后重新打包。",
                        scene.name, mesh.min.x, mesh.min.y));
                }
                if (hasMesh && (Mathf.Abs(mesh.size.x - room.Width) > 1f || Mathf.Abs(mesh.size.y - room.Height) > 1f))
                {
                    log.LogError(string.Format(
                        "[HKCS] ⚠ {0} 房间表登记 {1}x{2}，地形实测却是 {3:F2}x{4:F2} —— " +
                        "宽度/高度对不上，相机边界就会多跑或少跑（右/上边露出房间外的黑，或看不全房间）。" +
                        "把 Consts/RoomNames.cs 里这一行改成实测值。",
                        scene.name, room.Width, room.Height, mesh.size.x, mesh.size.y));
                }
            }
            catch (Exception e)
            {
                log.Log("[HKCS] 实测几何失败（不影响运行）：" + e.Message);
            }
        }
    }
}
