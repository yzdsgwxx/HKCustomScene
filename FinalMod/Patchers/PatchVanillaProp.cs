using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace HKCustomSceneMod.Patchers
{
    /// <summary>
    /// 把**原版物体**（光效/雕像/装饰）克隆到我们的房间里。
    ///
    /// 为什么必须"预载 + 运行时克隆"：原版的美术件是 mesh + 材质 + 粒子 + PlayMaker FSM 的组合，
    /// 直接拷 .unity/.assets 进 Unity 工程脚本引用全断；只有从**原版场景**预载出来再 `Instantiate`
    /// 才能原样复现（长椅、小怪、灵魂怪都是这个套路）。
    ///
    /// 已挖出的关键光效（`GG_Workshop` = 众神殿堂 / Hall of Gods）：
    /// <code>
    ///   GG_Statue_Radiance/Spotlight        ← 辐光雕像的聚光灯（截图里那种：光束 + 光晕 + 粒子）
    ///   GG_Statue_&lt;任意雕像名&gt;/Spotlight     ← 其它雕像同款
    ///   GG_Atrium 里另有： light_beam、dream_beam_animation/dream_beam、
    ///                      gg_atrium_hidden_path/gg_roof open_effect/sun
    /// </code>
    /// 路径必须在 `PrefabHolder.VanillaPropPaths` 里登记过（预载是启动一次性的）。
    /// </summary>
    public class PatchVanillaProp : MonoBehaviour
    {
        /// <summary>原版物体所在的场景名（例如 `GG_Workshop`）。</summary>
        public string SourceScene = "GG_Workshop";

        /// <summary>物体在场景里的层级路径（例如 `GG_Statue_Radiance/Spotlight`）。</summary>
        public string SourcePath = "GG_Statue_Radiance/Spotlight";

        /// <summary>位置微调（世界单位）。</summary>
        public Vector2 Offset = Vector2.zero;

        /// <summary>整体缩放。</summary>
        public float Scale = 1f;

        /// <summary>深度 z（别小于 0，否则会盖住骑士）。</summary>
        public float Z = 0.02f;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.6f);
        }

        public void Awake()
        {
            GameObject template = PrefabHolder.GetVanillaProp(SourceScene, SourcePath);
            if (template == null)
            {
                Modding.Logger.LogError(string.Format(
                    "[HKCS] 没有预载 {0} / {1} —— 到 FinalMod/PrefabHolder.cs 的 VanillaPropPaths 里加一行。" +
                    "启动日志里 API 会打 could not load '<场景>/<路径>.prefab' 表示路径写错；" +
                    "当前**已经预载成功**的美术件有：{2}",
                    SourceScene, SourcePath, PrefabHolder.VanillaPropKeysText()));
                return;
            }

            GameObject prop = UObject.Instantiate(template);
            prop.name = "HKCS_Prop_" + gameObject.name;
            prop.SetActive(false);      // 预载出来的 prefab 是 inactive 的

            Vector3 p = transform.position;
            prop.transform.position = new Vector3(p.x + Offset.x, p.y + Offset.y, Z);
            if (Scale > 0f && !Mathf.Approximately(Scale, 1f))
            {
                prop.transform.localScale = prop.transform.localScale * Scale;
            }

            // 挪进**摆放点自己所在的场景** ⇒ 场景一卸载它跟着销毁（不会跟着玩家跑到别的场景）。
            // ⚠ 不要用 GetActiveScene()：场景切换时新场景的物体先 Awake，此刻"当前激活场景"还是
            //    **上一个**场景（例如刚离开的 Town）⇒ 物件会被塞进正在卸载的场景里，跟着一起消失。
            //    （2026-09-26 在 PatchGhost 上实测踩过这个坑，这里是同一类写法，一并改掉。）
            Scene here = gameObject.scene;
            if (!here.IsValid()) here = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (here.IsValid())
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(prop, here);
            }

            prop.SetActive(true);
            Modding.Logger.Log(string.Format("[HKCS] 放了原版物件 {0}（{1}/{2}）@ ({3:0.##}, {4:0.##}, z={5:0.##})，缩放 {6:0.##}",
                prop.name, SourceScene, SourcePath, prop.transform.position.x, prop.transform.position.y, Z, Scale));
        }
    }
}
