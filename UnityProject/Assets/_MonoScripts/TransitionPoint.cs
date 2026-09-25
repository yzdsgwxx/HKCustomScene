// 空壳（仅成员，无逻辑）· 来源：ModdingDocs «Creating custom Scenes with Unity» → _MonoScripts
// 这是场景里门的组件。字段名/类型必须和 HK 原版一致，否则 Unity 序列化进 .unity 的数据
// 在游戏里会读不出来（表现：门不生效 / 组件显示 Missing）。
// 逻辑全部由游戏本体的 Assembly-CSharp 提供，这里不许写任何实现。
using UnityEngine;

public class TransitionPoint : MonoBehaviour
{
  public bool isADoor = false;
  [HideInInspector]
  public bool dontWalkOutOfDoor = false;
  [HideInInspector]
  public float entryDelay = 0.0f;
  public bool alwaysEnterRight = false;
  public bool alwaysEnterLeft = false;
  public bool hardLandOnExit = false;
  public string targetScene;
  public string entryPoint;
  [HideInInspector]
  public Vector2 entryOffset = new Vector2(0.0f, 0.0f);
  [HideInInspector]
  public PlayMakerFSM customFadeFSM = null;
  [HideInInspector]
  public bool nonHazardGate = false;
  public HazardRespawnMarker respawnMarker;
  [HideInInspector]
  public AudioMixerSnapshot atmosSnapshot = null;
  [HideInInspector]
  public AudioMixerSnapshot enviroSnapshot = null;
  [HideInInspector]
  public AudioMixerSnapshot actorSnapshot = null;
  [HideInInspector]
  public AudioMixerSnapshot musicSnapshot = null;
  public GameManager.SceneLoadVisualizations sceneLoadVisualization = GameManager.SceneLoadVisualizations.Default;
  [HideInInspector]
  public bool customFade = false;
  [HideInInspector]
  public bool forceWaitFetch = false;
}
