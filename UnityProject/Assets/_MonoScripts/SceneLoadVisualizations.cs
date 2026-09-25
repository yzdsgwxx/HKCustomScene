// 空壳（仅嵌套枚举）· 来源：ModdingDocs «Creating custom Scenes with Unity» → _MonoScripts
// 文件名和类名不一致是**故意的**：HK 原版里 SceneLoadVisualizations 是嵌在 GameManager 里的枚举，
// TransitionPoint.sceneLoadVisualization 用的就是它。Unity 只需要能编译出这个嵌套类型即可。
using UnityEngine;

public class GameManager : MonoBehaviour
{
  public enum SceneLoadVisualizations
  {
    Default = 0,
    Custom = -1,
    Dream = 1,
    Colosseum = 2,
    GrimmDream = 3,
    ContinueFromSave = 4,
    GodsAndGlory = 5
  }
}
