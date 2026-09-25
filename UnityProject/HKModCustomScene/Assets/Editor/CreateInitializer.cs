// 编辑期小工具 · 本项目自加（教学文档第 6 章阶段三第 14 步承诺的"一键生成 __Initializer"）
//
// 用法：打开你的房间场景 → 菜单 Tools/HKCS/生成 __Initializer
// 它会建好：
//   _Managers        （空物体，挂 PlayMaker Unity 2D 的父级）
//   __Initializer    （挂 PatchAreaTitleController + PatchPlayMakerManager [+ SFCore 的 SceneManagerPatcher]）
// 并自动把 PatchPlayMakerManager.ManagerTransform 指到 _Managers。
//
// 设计上**故意用反射找类型**，不 using 壳 dll / SFCore.dll：
// 这样即使 Assets/Assemblies/ 里还没有 dll，本文件也能编译，不会连累整个 Editor 程序集。
// 找不到类型时只在 Console 给一条警告，不会抛异常。
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CreateInitializer
{
  const string ShellAssembly = "HKCustomSceneMod";

  [MenuItem("Tools/HKCS/生成 __Initializer")]
  static void Generate()
  {
    Scene scene = SceneManager.GetActiveScene();
    if (!scene.IsValid())
    {
      EditorUtility.DisplayDialog("HKCS", "没有打开任何场景。先新建/打开你的房间场景。", "好");
      return;
    }

    // _Managers（已存在就复用）
    GameObject managers = GameObject.Find("_Managers");
    if (managers == null)
    {
      managers = new GameObject("_Managers");
      Undo.RegisterCreatedObjectUndo(managers, "Create _Managers");
    }

    GameObject initializer = GameObject.Find("__Initializer");
    if (initializer == null)
    {
      initializer = new GameObject("__Initializer");
      Undo.RegisterCreatedObjectUndo(initializer, "Create __Initializer");
    }

    int added = 0;
    added += AddIfMissing(initializer, "HKCustomSceneMod.Patchers.PatchAreaTitleController", comp =>
    {
      // 和 FinalMod 里的默认值对齐；AreaEvent / VisitedBool 是语言表与存档的键名
      SetField(comp, "SubArea", false);
      SetField(comp, "AreaEvent", "HKCustomSceneMod_AreaTitle");
      SetField(comp, "VisitedBool", "HKCustomSceneMod_VisitedArea");
    });
    added += AddIfMissing(initializer, "HKCustomSceneMod.Patchers.PatchPlayMakerManager", comp =>
    {
      SetField(comp, "ManagerTransform", managers.transform);
    });
    added += AddIfMissing(initializer, "SFCore.MonoBehaviours.SceneManagerPatcher", null);

    EditorSceneManager.MarkSceneDirty(scene);
    Selection.activeGameObject = initializer;

    Debug.Log($"[HKCS] __Initializer 生成完毕，新挂上 {added} 个组件。" +
              (added < 3 ? "（少于 3 个说明有类型没找到，看下面的警告）" : ""));
  }

  static int AddIfMissing(GameObject go, string fullTypeName, Action<Component> configure)
  {
    Type t = FindType(fullTypeName);
    if (t == null)
    {
      Debug.LogWarning($"[HKCS] 找不到类型 {fullTypeName}。" +
                       "若是 HKCustomSceneMod.* → 壳 dll 还没进 Assets/Assemblies/；" +
                       "若是 SFCore.* → SFCore.dll 还没进 Assets/Assemblies/。");
      return 0;
    }
    if (go.GetComponent(t) != null)
    {
      Debug.Log($"[HKCS] {t.Name} 已经在了，跳过。");
      return 0;
    }
    var comp = Undo.AddComponent(go, t);
    configure?.Invoke(comp);
    return 1;
  }

  static void SetField(object target, string fieldName, object value)
  {
    FieldInfo f = target.GetType().GetField(fieldName,
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
    if (f == null)
    {
      Debug.LogWarning($"[HKCS] {target.GetType().Name} 上没有字段 {fieldName}，跳过赋值。");
      return;
    }
    f.SetValue(target, value);
  }

  static Type FindType(string fullTypeName)
  {
    // 先按壳工程/依赖 dll 的程序集名找
    Type t = Type.GetType(fullTypeName + ", " + ShellAssembly);
    if (t != null) return t;

    // 再在已加载的程序集里全局找（SFCore.dll 走这条）
    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
    {
      try
      {
        t = asm.GetType(fullTypeName);
        if (t != null) return t;
      }
      catch (Exception)
      {
        // 个别程序集反射会抛，忽略继续找
      }
    }
    return null;
  }
}
