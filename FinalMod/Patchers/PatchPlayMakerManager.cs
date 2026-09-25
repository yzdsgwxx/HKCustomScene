using UnityEngine;
using UObject = UnityEngine.Object;

namespace HKCustomSceneMod.Patchers
{
    /// <summary>
    /// 每个房间都必须有一份 "PlayMaker Unity 2D" 管理器，否则 PlayMaker FSM 里
    /// 跟名字/文字相关的动作会静默失效（区域名、NPC 名、物品名都可能变空白）。
    ///
    /// 挂在房间的 __Initializer 上，ManagerTransform 指向场景里的 _Managers。
    /// </summary>
    public class PatchPlayMakerManager : MonoBehaviour
    {
        public Transform ManagerTransform;

        public void Awake()
        {
            if (PrefabHolder.PlayMakerUnity2D == null) return;

            Transform parent = ManagerTransform != null ? ManagerTransform : transform;
            GameObject go = UObject.Instantiate(PrefabHolder.PlayMakerUnity2D, parent);
            go.name = "PlayMaker Unity 2D";
            go.SetActive(true);
        }
    }
}
