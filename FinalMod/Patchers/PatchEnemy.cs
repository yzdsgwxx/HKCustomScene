using System.Collections;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace HKCustomSceneMod.Patchers
{
    /// <summary>
    /// 在自定义房间里放**原版小怪**。
    ///
    /// Unity 侧用法：房间里放一个空物体 → 挂上这个组件 → 摆在你要刷怪的位置（名字随便，比如 `Enemy01`）。
    ///   · <see cref="Kind"/> 选怪种；
    ///   · <see cref="Id"/>   一般**留空** —— 留空时自动用「摆放点的物体名」当唯一编号；
    ///   · **位置完全按摆放点的 transform 走**（含 z）。建议把你用来摆放的**预制体**的 z 设成 **-0.5**
    ///     （本工程的地形是 z=0 的实心 mesh、相机在 -z 方向看 ⇒ z 必须 &lt; 0 才不会被地形挡住；
    ///     从别的物体复制出来的空物体常带 -15 / -22 这种 z，那会让怪盖在骑士和长椅前面）。
    ///
    /// ── 落地：**别自己算高度，交给代码**（2026-09-26 加的，起因见下）────────────────
    /// 原版小怪的刚体属性差别很大（从游戏自己的场景数据里量出来的，layer 都是 11 Enemies）：
    /// <code>
    ///   Climber       bodyType = Kinematic,  gravityScale = 0   ⇒ **永远不掉**（贴墙/贴顶爬，靠射线找表面）
    ///   Crawler       bodyType = Dynamic,    gravityScale = 1   ⇒ 会掉
    ///   Zombie Runner bodyType = Dynamic,    gravityScale = 1   ⇒ 会掉
    ///   Fly           bodyType = Dynamic,    gravityScale = 0   ⇒ **不掉**（悬停，放多高飞多高）
    /// </code>
    /// 于是「把摆放点放高一点，反正会掉下来」这个直觉对 **Climber** 是错的：
    /// 它离地 1.9 格出生时不会落地，而是**在空气里按原方向爬**（用户 2026-09-26 报的现象：
    /// "这只怪怎么在爬空气"）。而且 Climber 的轴心几乎就在脚底（脚相对轴心 +0.016）。
    ///
    /// 现在 Awake 之后会（除了 Fly）**向下打一条射线找地形（layer Terrain=8），把怪的碰撞盒底边放到地面上**：
    ///   · 射线从**摆放点**往下打（距离 50 格）；摆放点要在地面上方，别埋进碰撞体里；
    ///   · 找不到地面（放在坑里/房间外）会打红字并保持原高度；
    ///   · 每只怪落地后都会打一行 `摆放点 y=… → 落地 y=…` 方便核对。
    /// 所以你现在只需要把摆放点放在**地面附近**就行，不用算每种怪的脚底偏移；**z 不会被改**。
    ///
    /// 为什么每只怪都要唯一编号（反编译 PersistentBoolItem.SetMyID 得到）：
    ///     if (string.IsNullOrEmpty(persistentBoolData.id)) persistentBoolData.id = name;
    /// 存档里的击杀记录用「物体名」当 id —— 两只同名的怪会共用一个 id，
    /// 杀掉一只，另一只在重进房间后也会一起消失。这里强制把 id 写成唯一名字，绕开这个坑。
    ///
    /// 注意：普通小怪本来就没有 `PersistentBoolItem`（原版就是每次进房间重刷），
    /// 日志里那句提示只说明"这只不记击杀状态"，不是错误。
    /// </summary>
    public class PatchEnemy : MonoBehaviour
    {
        /// <summary>
        /// 可放的怪种。
        ///
        /// ⚠ 这些**数字**会被 Unity 序列化进 `.unity`，所以改名/插值会让已经摆好的怪"换种"——
        /// 只能在末尾追加。括号里的宽×高是 2026-09-26 从游戏自己的场景数据里量的原版
        /// `BoxCollider2D`（摆放时当尺寸参照，不是美术尺寸）；"脚"= 碰撞盒底边相对轴心的 y（正=轴心在脚上方）。
        /// </summary>
        public enum EnemyKind
        {
            /// <summary>僵尸行者 —— `Crossroads_01` 的 `_Enemies/Zombie Runner`（1.22 × 1.63，脚 −1.44）：会冲锋，有重力。</summary>
            ZombieRunner = 0,
            /// <summary>复仇苍蝇 —— `Crossroads_07` 的 `Uninfected Parent/Fly`（0.89 × 0.84）：悬停 + 俯冲，零重力，**不落地**。</summary>
            Fly = 1,
            /// <summary>爬虫 —— `Crossroads_01` 的 `_Enemies/Crawler 1`（1.42 × 0.91，脚 −1.06）：贴地爬，有重力。</summary>
            Crawler = 2,
            /// <summary>攀爬虫 —— `Crossroads_01` 的 `_Enemies/Climber`（1.09 × 0.92，脚 +0.02）：贴墙/贴顶爬，**kinematic 零重力，不会落地**。</summary>
            Climber = 3,
        }

        /// <summary>要放哪种怪。</summary>
        public EnemyKind Kind = EnemyKind.ZombieRunner;

        /// <summary>唯一编号（同房间内不要重复）。留空 = 自动用本摆放点的物体名，一般不用填。</summary>
        public string Id = "";

        /// <summary>找地面的射线最长打多远（格）。</summary>
        private const float MaxGroundCast = 50f;

        /// <summary>地形所在的 layer（HK 的 Layer 8 = Terrain）。</summary>
        private const int TerrainLayerFallback = 8;

        /// <summary>已实例化出来的怪，留给 Start 里的落地协程用。</summary>
        private GameObject _enemy;

        public void Awake()
        {
            GameObject prefab = PrefabFor(Kind);
            if (prefab == null)
            {
                Debug.LogError("[HKCS] 没预载到 " + Kind + "，放不了。看上面 API 报的那行 " +
                               "could not load '<场景>/<路径>.prefab'，再到 PrefabHolder 里改那条路径。");
                return;
            }

            // 留空就用摆放点的名字：Enemy01 / Enemy02 … 天然唯一，也方便在存档里认出来
            string id = string.IsNullOrEmpty(Id) ? gameObject.name : Id;

            GameObject enemy = UObject.Instantiate(prefab);
            enemy.name = "HKCS_" + Kind + "_" + id;

            // 把击杀存档的 id / 场景名写死成我们自己的，避免和原版场景、或者同房间其它同名怪撞车
            PersistentBoolItem pbi = enemy.GetComponent<PersistentBoolItem>();
            if (pbi != null && pbi.persistentBoolData != null)
            {
                pbi.persistentBoolData.id = enemy.name;
                pbi.persistentBoolData.sceneName = GameManager.GetBaseSceneName(gameObject.scene.name);
            }

            Vector3 p = transform.position;                  // 位置完全按摆放点（含 z —— z 由你的预制体决定）
            enemy.transform.position = p;
            enemy.SetActive(true);   // 预载出来的 prefab 是 inactive 的，字段设完再开

            _enemy = enemy;

            Modding.Logger.Log(string.Format(
                "[HKCS] 放了小怪 {0}（{1}）@ ({2:0.##}, {3:0.##}, z={4:0.##}){5}",
                enemy.name, Kind, p.x, p.y, p.z,
                pbi == null ? " —— 普通小怪不记击杀状态（每次进房间重刷），正常" : ""));
        }

        private void Start()
        {
            // 苍蝇是悬停的（gravityScale = 0）：放在哪就飞在哪，不去找地面
            if (_enemy == null || Kind == EnemyKind.Fly) return;

            StartCoroutine(SnapToGround());
        }

        /// <summary>
        /// 把怪放到地面上。
        /// 等一个物理帧再打射线：Awake 阶段场景里的地形碰撞体**不一定**已经进物理世界，
        /// 那时射线会打空（这正是"必须早点做"和"不能太早做"之间的矛盾，等一帧最稳）。
        /// </summary>
        private IEnumerator SnapToGround()
        {
            yield return new WaitForFixedUpdate();
            if (_enemy == null) yield break;

            Vector3 spawn = transform.position;      // 摆放点（射线起点，别用怪当前的位置：动态刚体可能已经掉了）
            Vector3 cur = _enemy.transform.position;

            int mask = LayerMask.GetMask("Terrain");
            if (mask == 0) mask = 1 << TerrainLayerFallback;   // 万一层名字查不到，按 HK 约定用 8

            RaycastHit2D hit = Physics2D.Raycast(new Vector2(spawn.x, spawn.y), Vector2.down, MaxGroundCast, mask);
            if (hit.collider == null)
            {
                Modding.Logger.LogError(string.Format(
                    "[HKCS] {0} 落地失败：从摆放点 ({1:0.##}, {2:0.##}) 往下 {3:0} 格没打到地形（layer Terrain）。" +
                    "保持原高度 —— 把摆放点放到地面上方（别放进碰撞体里/房间外）即可。",
                    _enemy.name, spawn.x, spawn.y, MaxGroundCast));
                yield break;
            }

            // 脚 = 碰撞盒底边相对轴心的 y（Crawler −1.06 / ZombieRunner −1.44 / Climber +0.02）
            Collider2D col = _enemy.GetComponent<Collider2D>();
            float feetOffset = (col != null) ? (col.bounds.min.y - cur.y) : 0f;
            float newY = hit.point.y - feetOffset;

            _enemy.transform.position = new Vector3(cur.x, newY, cur.z);

            Modding.Logger.Log(string.Format(
                "[HKCS] {0} 落地：摆放点 y={1:0.##} → y={2:0.##}（地面 y={3:0.##}，脚偏移 {4:0.##}）",
                _enemy.name, spawn.y, newY, hit.point.y, feetOffset));
        }

        /// <summary>怪种 → 预载出来的 prefab。路径见 PrefabHolder。</summary>
        private static GameObject PrefabFor(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Fly: return PrefabHolder.FlyPrefab;
                case EnemyKind.Crawler: return PrefabHolder.CrawlerPrefab;
                case EnemyKind.Climber: return PrefabHolder.ClimberPrefab;
                default: return PrefabHolder.ZombieRunnerPrefab;
            }
        }
    }
}
