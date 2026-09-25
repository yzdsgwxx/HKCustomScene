// 编辑期小工具 · 来源：ModdingDocs «Creating custom Scenes with Unity» → Editor
// 作用：一键把场景里所有 Camera 的 transparency sort mode 切到 Orthographic。
// HK 的相机是 3D 相机 + Orthographic 排序模式，不切的话精灵前后关系会错。
using UnityEngine;
using UnityEditor;

public static class CameraModeSwitch
{
  [MenuItem("Camera/Orthographic")]
  static public void OrthographicCamera()
  {
    foreach (var cam in GameObject.FindObjectsOfType<Camera>())
      cam.transparencySortMode = TransparencySortMode.Orthographic;
  }
  [MenuItem("Camera/Perspective")]
  static public void PerspectiveCamera()
  {
    foreach (var cam in GameObject.FindObjectsOfType<Camera>())
      cam.transparencySortMode = TransparencySortMode.Default;
  }
}
