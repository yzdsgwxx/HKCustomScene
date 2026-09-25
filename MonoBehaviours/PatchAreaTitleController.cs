using UnityEngine;

namespace HKCustomSceneMod.Patchers
{
    /// <summary>
    /// Unity 侧空壳。字段必须和 FinalMod/Patchers/PatchAreaTitleController.cs 里
    /// **逐字一致**（名字、类型、顺序无所谓，但名字和类型必须一样），
    /// 因为 Unity 是按字段名序列化的。
    /// </summary>
    public class PatchAreaTitleController : MonoBehaviour
    {
        [Range(0f, 10f)] public float Pause = 3f;
        public bool AlwaysVisited = false;
        public bool DisplayRight = false;
        public bool OnlyOnRevisit = false;
        public bool SubArea = true;
        public bool WaitForTrigger = false;
        public string AreaEvent = "";
        public string VisitedBool = "";
        public string AreaTitleObjectName = "Area Title";

        public void Awake()
        {
            // 空：真正实现只在 FinalMod 里
        }
    }
}
