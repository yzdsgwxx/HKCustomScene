using HutongGames.PlayMaker;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace HKCustomSceneMod.Patchers
{
    /// <summary>
    /// 把预载出来的 "Area Title Controller" 实例化并接到本房间上，用来弹区域名。
    /// 放在房间的 __Initializer 上；Awake 里做完就自毁，所以只会执行一次。
    ///
    /// 多房间注意：只有**整个区域的第一间房**把 SubArea 设 false，
    /// 其余房间设 true 并给不同的 AreaEvent，这样小标题会分别显示。
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
        /// <summary>场景里显示标题文字的物体名，通常就是 "Area Title"。</summary>
        public string AreaTitleObjectName = "Area Title";

        public void Awake()
        {
            if (PrefabHolder.AreaTitleController == null) return;

            GameObject atc = UObject.Instantiate(PrefabHolder.AreaTitleController);
            atc.SetActive(false);
            atc.transform.localPosition = transform.position;
            atc.transform.localEulerAngles = transform.eulerAngles;
            atc.transform.localScale = transform.lossyScale;

            PlayMakerFSM fsm = atc.GetComponent<PlayMakerFSM>();
            if (fsm != null)
            {
                FsmVariables v = fsm.FsmVariables;
                SetFloat(v, "Unvisited Pause", Pause);
                SetFloat(v, "Visited Pause", Pause);
                SetBool(v, "Always Visited", AlwaysVisited);
                SetBool(v, "Display Right", DisplayRight);
                SetBool(v, "Only On Revisit", OnlyOnRevisit);
                SetBool(v, "Sub Area", SubArea);
                SetBool(v, "Wait for Trigger", WaitForTrigger);
                // 上一行是"要不要等触发器"，这一行才是"玩家以前来过没有"
                SetBool(v, "Visited Area", PlayerData.instance.GetBool(VisitedBool));
                SetString(v, "Area Event", AreaEvent);
                SetString(v, "Visited Bool", VisitedBool);

                GameObject titleGo = GameObject.Find(AreaTitleObjectName);
                if (titleGo != null) SetGameObject(v, "Area Title", titleGo);
                else Debug.LogError("[HKCS] 找不到标题物体 " + AreaTitleObjectName);
            }

            // 标题控制器不能把玩家弹开
            atc.AddComponent<NonBouncer>();
            atc.SetActive(true);
            Destroy(gameObject);
        }

        private static void SetFloat(FsmVariables v, string n, float x)
        { FsmFloat f = v.GetFsmFloat(n); if (f != null) f.Value = x; }

        private static void SetBool(FsmVariables v, string n, bool x)
        { FsmBool f = v.GetFsmBool(n); if (f != null) f.Value = x; }

        private static void SetString(FsmVariables v, string n, string x)
        { FsmString f = v.GetFsmString(n); if (f != null) f.Value = x; }

        private static void SetGameObject(FsmVariables v, string n, GameObject x)
        { FsmGameObject f = v.GetFsmGameObject(n); if (f != null) f.Value = x; }
    }
}
