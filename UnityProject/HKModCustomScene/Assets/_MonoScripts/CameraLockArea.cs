// 空壳（仅成员，无逻辑）· 来源：ModdingDocs «Creating custom Scenes with Unity» → _MonoScripts
using UnityEngine;

public class CameraLockArea : MonoBehaviour
{
  public float cameraXMin;
  public float cameraYMin;
  public float cameraXMax;
  public float cameraYMax;
  public bool preventLookUp;
  public bool preventLookDown;
  public bool maxPriority;
}
