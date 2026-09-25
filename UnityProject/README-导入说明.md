# Unity 工程导入说明（阶段二：Unity 工程，一次性）

> 这份文件对应教学《第 6 章 · 阶段二》。**先读完第 0 节再动手**，否则很容易把文件拷到错的地方。

## 0. 先分清"两个 UnityProject"（最容易踩的坑）

```
hkmod-custom-scene\
├─ UnityProject\                    ← 【骨架】本文件所在的位置，是"拷贝源"
│   ├─ ProjectSettings\TagManager.asset
│   ├─ Assets\{_MonoScripts, Editor, Meshes, Materials, Scenes, Assemblies}
│   └─ HKModCustomScene\            ← 【你的真工程】你在 Unity Hub 里新建的那个
│        ├─ Assets\
│        └─ ProjectSettings\
└─ MonoBehaviours\                  ← 壳工程（要编译的那个）
```

教学里说的"把**骨架**的 X 覆盖过去"，意思是：**从外层 `UnityProject\` 拷 → 到内层 `HKModCustomScene\`**。
方向反了（或拷到骨架自己身上）等于什么都没做。

## 1. 顺序（很重要，别按教学原文的顺序做）

教学原文的顺序是「先拷 `_MonoScripts`/`Editor` → 再放 `SFCore.dll`」。
但 `Editor/SceneManagerPatcherEditor.cs` 里 `using SFCore.MonoBehaviours;`，
**SFCore.dll 不在时会报 `The type or namespace name 'SFCore' could not be found`，整个 Editor 程序集都编译不过。**

所以正确顺序是：

| 步 | 做什么 | 备注 |
|---|---|---|
| 1 | **关掉 Unity** | 覆盖 ProjectSettings 必须在编辑器关闭时做，否则退出时会被它写回去 |
| 2 | 覆盖 `ProjectSettings/TagManager.asset` | 目标：`HKModCustomScene\ProjectSettings\TagManager.asset` |
| 3 | 建 `HKModCustomScene\Assets\Assemblies\` | 目录名就叫 `Assemblies` |
| 4 | 放 `SFCoreUnity.dll` 并**改名 `SFCore.dll`** | 从 SFCore 的 GitHub release 下载 |
| 5 | 编译壳工程，放 `HKCustomSceneMod.dll` | 见 `MonoBehaviours/`，`dotnet build -c Release` |
| 6 | 重开 Unity | 等它导入完，Console **不应有红色报错** |
| 7 | 拷 `Assets\_MonoScripts\`、`Assets\Editor\`、`Assets\Meshes\` | 目标：`HKModCustomScene\Assets\` |
| 8 | 再等一次编译 | 仍然不应该有红色报错 |

> 第 7 步里 `Editor/SceneManagerPatcherEditor.cs` 依赖 SFCore.dll；如果你第 4 步还没做，
> 就**先不要拷这个文件**，等 SFCore.dll 到位再拷。

## 2. 覆盖后的自检

| 检查 | 期望 |
|---|---|
| `ProjectSettings/TagManager.asset` | `tags` 不再是 `[]`，里面能看到 `Terrain`、`HeroBox`、`TileMap`… |
| 任意物体 Inspector 的 **Layer** 下拉 | 第 8 项是 `Terrain` |
| 任意 Sprite Renderer 的 **Sorting Layer** | 能选到 `Far BG 2` / `Mid BG` / `Actors` / `Tiles` / `HUD`… |
| Console | 无红色报错 |

## 3. 验收（阶段二的验收标准）

1. Hierarchy 里 `Create Empty`
2. Inspector → `Add Component`
3. 搜索 `PatchAreaTitleController` → **应该能搜到**

搜不到说明壳 dll（`HKCustomSceneMod.dll`）没在 `Assets/Assemblies/` 里，或者它有编译错误。
搜到了但挂上去显示 `Missing (Mono Script)` → 说明 dll 的 **AssemblyName** 不是 `HKCustomSceneMod`。

## 4. 各文件来源

`_MonoScripts/`、`Editor/`、`TagManager.asset`、`Meshes/TutorialScene.obj` 的内容都来自官方教程
<https://prashantmohta.github.io/ModdingDocs/new-scene.html>（HK 1.5.x / Unity 2020.2.2）。
`Editor/CreateInitializer.cs` 是本项目自加的一键工具。
