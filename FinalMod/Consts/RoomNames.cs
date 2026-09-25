using System.Collections.Generic;

namespace HKCustomSceneMod.Consts
{
    /// <summary>
    /// 一个房间的元数据。多房间的所有差异都收在这张表里，
    /// 加房间 = 加一行 + 在 Unity 里建一个同名场景。
    /// </summary>
    public class RoomDef
    {
        public string Scene;        // Unity 场景名，必须和 .unity 文件名一致
        public float Width;         // 场景宽度，单位是 HK 的 tile（= Unity unit）
        public float Height;
        public string FromPrev;     // 从上一个房间进来时落在哪个 entryPoint
        public string ToNext;       // 通往下一个房间的门（TransitionPoint 所在 GameObject 名）
        public bool ShowAreaTitle;  // 是否在这间房首次显示区域名

        public RoomDef(string scene, float width, float height,
                       string fromPrev, string toNext, bool showAreaTitle)
        {
            Scene = scene;
            Width = width;
            Height = height;
            FromPrev = fromPrev;
            ToNext = toNext;
            ShowAreaTitle = showAreaTitle;
        }
    }

    public static class Rooms
    {
        // ────────────────────────────────────────────────────────────────
        //  ① 这里是唯一需要改的地方：房间清单
        //     场景名建议统一前缀，避免和原版场景撞名。
        // ────────────────────────────────────────────────────────────────
        public const string Prefix = "HKCS_";

        public const string Room01 = Prefix + "Room01";
        public const string Room02 = Prefix + "Room02";
        public const string Room03 = Prefix + "Room03";

        // ── Width / Height 到底该填多少 ─────────────────────────────────
        // 相机边界**直接**由这两个数算出来（原版 CameraController）：
        //     xLimit = Width - 14.6   yLimit = Height - 8.3   （14.6 / 8.3 = 半个屏幕，写死的）
        // 相机中心被夹在 [14.6, xLimit] × [8.3, yLimit] ⇒ 相机视野恰好覆盖 x[0,Width] y[0,Height]。
        // ⇒ 硬规矩：**地形必须从 (0,0) 开始铺**，Width/Height 必须等于地形网格的实际跨度，
        //    否则走到边上就会看到房间外的黑（写大了）或看不全房间（写小了）。
        // 2026-09-26 实测（直接读 AssetBundle 里的网格 AABB + 场景里的 PolygonCollider2D）：
        //     HKCS_Room01 = 两块 30 宽的 mesh 拼起来 ⇒ x[0,60]、y[0,17]
        // ⇒ 之前填的 64x32 就是"相机右边界限制不对"的原因（右多 4 格、上多 15 格）。
        // 进游戏后日志里有 "[HKCS] <房间> 地形网格实测：x[…] y[…]"，照它校准即可。
        public static readonly List<RoomDef> All = new List<RoomDef>
        {
            // ⚠ 2026-09-26 02:3x 按日志实测更新：用户重做地图后，地形实测 x[-14.67 ~ 74.88]（宽 89.55）、
            //    y[-0.06 ~ 30.00]（高 30.06）⇒ 这里改成 90x30（否则相机会看不到大半张地图）。
            //    ⚠ 地形还**没从 (0,0) 开始铺**（左下角 -14.67, -0.06）：原版把相机左/下边界写死 14.6/8.3，
            //      x<0 / y<0 的部分玩家永远看不到 ⇒ 请在 Unity 里把地形整体挪到 x ≥ 0、y ≥ 0。
            //                                 scene    w    h   fromPrev  toNext    title
            new RoomDef(Room01, 90f, 30f, "left1",  "right1", true),
            new RoomDef(Room02, 48f, 48f, "left1",  "right1", false),
            new RoomDef(Room03, 32f, 96f, "left1",  "right1", false),
        };

        public static RoomDef Get(string sceneName)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].Scene == sceneName) return All[i];
            }
            return null;
        }

        public static bool IsCustomRoom(string sceneName)
        {
            return Get(sceneName) != null;
        }

        public static IEnumerable<string> SceneNames()
        {
            foreach (RoomDef r in All) yield return r.Scene;
        }
    }

    /// <summary>
    /// 区域（整个自定义区域共享一个区域名与一个 visited bool）。
    ///
    /// ⚠ 这两个字符串是**跨三处**的键名，必须完全一致：
    ///   1. Unity 场景里 PatchAreaTitleController 的 AreaEvent / VisitedBool 字段
    ///      （由 Assets/Editor/CreateInitializer.cs 自动填，值就是下面两个）；
    ///   2. 本类的 AreaEvent/VisitedBool（LanguageGetHook 用它拼 _SUPER/_MAIN/_SUB）；
    ///   3. SettingsClass 的字段名（Get/SetPlayerBoolHook 靠反射按名字找）。
    /// 曾经写的是 "HKCS_AreaTitle"/"HKCS_VisitedArea"，与 1、3 不一致 →
    /// 区域名会查不到文字、到访记录也存不下来。2026-09-25 已统一。
    /// </summary>
    public static class AreaInfo
    {
        public const string AreaEvent = "HKCustomSceneMod_AreaTitle";
        public const string VisitedBool = "HKCustomSceneMod_VisitedArea";
    }

    /// <summary>
    /// 原版场景里被我们"接管"的门。
    ///
    /// 关键：**每个进来的方向都要单独给落点**，否则两个方向会挤在同一侧。
    /// 本项目的路线（和官方教程一致）：
    ///   Town.bot1        （德特茅斯跳井）→ 房间 left1（西侧）
    ///   Crossroads.top2  （十字路井底往上爬）→ 房间 right1（东侧）
    /// 出房间的方向在 Unity 的 TransitionPoint 里各自配：
    ///   left1  → Town:bot1          （回到德特茅斯的井口）
    ///   right1 → Crossroads_01:top1 （回到井底）
    /// </summary>
    public static class VanillaGates
    {
        // 从 Dirtmouth（Town）的水井下去 → 进我们的第一间房（西侧 = Rooms[0].FromPrev）
        public const string TownScene = "Town";
        public const string TownGate = "bot1";

        // 从 Forgotten Crossroads 井底往上爬 → 也进第一间房，但落在**东侧**
        public const string CrossroadsScene = "Crossroads_01";
        public const string CrossroadsGate = "top2";
        /// <summary>爬上来时落在房间哪一侧的门。官方教程此处是 right1。</summary>
        public const string CrossroadsEntry = "right1";

        // 从自定义区域走出来时，回到哪个原版场景/入口（留档用，实际值配在 Unity 的 TransitionPoint 上）
        public const string ExitBackScene = TownScene;
        /// <summary>从房间 left1 出来时的落点。⚠ 是 bot1（井），不是 top1 ——
        /// 填 top1 会让骑士出现在德特茅斯上空然后掉下来。</summary>
        public const string ExitBackEntry = "bot1";
    }
}
