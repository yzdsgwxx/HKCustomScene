# Assets/ 使用说明（骨架自带）

## Assemblies/ —— 两个 dll，名字一个都不能错

| 放什么 | 从哪来 | 必须叫什么 |
|---|---|---|
| 壳工程 dll | `MonoBehaviours/` 里 `dotnet build -c Release` 的产物 | `HKCustomSceneMod.dll` |
| SFCore 的 Unity 侧 | SFCore release 里的 `SFCoreUnity.dll` | **改名为 `SFCore.dll`** |

> **为什么必须叫 `SFCore.dll`**：Unity 靠「程序集名 + 命名空间 + 类名」反查脚本。
> SFCore 的组件（`SFCore.MonoBehaviours.SceneManagerPatcher` 等）原程序集名就是 `SFCore`，
> 不改名的话场景里的组件会全部变成 `Missing (Mono Script)`。
>
> 同理，壳 dll 必须是 `HKCustomSceneMod.dll` —— 它和 `FinalMod/` 的 AssemblyName 故意同名。

## 各文件夹

- `_MonoScripts/` — HK 原版组件（`TransitionPoint`、`CameraLockArea`…）的**空壳**，只有字段没有逻辑。
  作用只是让 Unity 能把组件挂到物体上、能把字段值序列化进 `.unity`。
  **不要在这里写任何逻辑，也不要引用空洞骑士的程序集。**
- `Editor/` — 编辑期工具，只在编辑器里跑，不会进游戏包。
  - `CreateAssetBundles.cs`：菜单 `Build AssetBundles/*` 打场景包 → 输出到 `Assets/AssetBundles/`
  - `MeshCollisionCreator.cs`：MeshFilter 组件右上角 `⋮` → `Create Collision` 自动生成 `PolygonCollider2D`
  - `CameraLockAreaEditor.cs`：在 Scene 视图画出相机锁定区域
  - `CameraModeSwitch.cs`：菜单 `Camera/Orthographic` 切排序模式
  - `CreateInitializer.cs`：菜单 `Tools/HKCS/生成 __Initializer` 一键建 `_Managers` + `__Initializer`
  - `SceneManagerPatcherEditor.cs`：**依赖 `SFCore.dll`**，必须等 dll 进了 `Assemblies/` 之后再拷进来，
    否则整个 Editor 程序集编译不过。
- `Meshes/` — `TutorialScene.obj`（教学用示例地形，纯文本，可直接记事本改数值）
- `Scenes/` — 你建房间场景的地方（第 11 步：`HKCS_Room01`）
- `Materials/` — 材质与 `Physics Material 2D`（Friction 0.2 / Bounciness 0）
- `Assemblies/` — 见上表

## 顺序很重要

1. 先把 `SFCore.dll` 和 `HKCustomSceneMod.dll` 放进 `Assemblies/`
2. 再把 `_MonoScripts/`、`Editor/` 拷进来
3. 最后重开 Unity，等它编译完，确认 Console 里**没有红色报错**

反过来做（先 Editor/ 后 dll）会因为 `SceneManagerPatcherEditor.cs` 找不到 `SFCore` 而满屏红字。
