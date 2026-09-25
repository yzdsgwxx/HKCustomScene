# 空洞骑士 1.5.x · 自定义场景 Mod 骨架

目标：给 Hollow Knight **1.5.x**（当前最新 1.5.78.11833）做一个**自带场景**的 Mod。
文档依据：<https://prashantmohta.github.io/ModdingDocs/new-scene.html>
（你给的那篇 <https://radiance.synthagen.net/apidocs/_images/NewScene.html> 是 API 0.0.2 / Unity 2017.4.10f 的旧版，**别照着它的版本号开工**。）

---

## 一、要装的东西（只给链接，我不替你下）

| # | 软件 | 链接 | 说明 |
|---|---|---|---|
| 1 | **空洞骑士 PC 正版** | [Steam](https://store.steampowered.com/app/367520/Hollow_Knight/) / GoG / HumbleBundle | 主机版不行。确认版本是 **1.5.x**（右下角或主菜单看得到） |
| 2 | **空洞骑士 Modding API** | <https://github.com/hk-modding/api/releases> | 下与你游戏版本对应的那个 release，然后把 `Managed/` 里的文件覆盖进游戏的 `hollow_knight_Data/Managed/` |
| 3 | **Mod 安装器（推荐，省掉第 2 步手改）** | Lumafly <https://themulhima.github.io/Lumafly> · Scarab <https://github.com/fifty-six/Scarab> | 装 API + 依赖 Mod 都用它 |
| 4 | **Unity 2020.2.2** | 归档页 <https://unity.com/releases/editor/archive> · 版本页 <https://unity.com/releases/editor/whats-new/2020.2.2> | 用 **Unity Hub** 装 `2020.2.2f1`，**在 Windows 机器上不用勾任何模块**（列表里那个 **Universal Windows Platform Build Support = UWP，不是它**；写着 **(Mono)** 的那项只在 macOS/Linux 宿主上才出现，Windows 宿主的 Windows 平台支持是随编辑器自带的，本机已实测 `...\Editor\Data\PlaybackEngines\windowsstandalonesupport` 存在）。文档明确说"用最新 HK 版本对应的 Unity"，1.5.x 就是 2020.2.2 |
| 5 | **.NET Framework 4.7.2** | <https://dotnet.microsoft.com/en-us/download/dotnet-framework/net472> | 已装 .NET 4.8 Runtime 的话自带 4.7.2。**注意：旧文档写的 3.5 是过时的** |
| 6 | **Visual Studio 2022 Community** | <https://visualstudio.microsoft.com/vs/community/> | 安装时勾 **".NET 桌面开发"** 工作负载。Rider 也行 |
| 7 | **SFCore**（必需依赖 Mod） | <https://github.com/SFGrenade/SFCore/releases> | 提供场景里要用的 MonoBehaviour；它的 release 里同时有 `SFCore.dll` 和 **`SFCoreUnity.dll`** |
| 8 | *可选* Mod 模板 | `dotnet new -i HKModding.HKMod.Templates`（<https://www.nuget.org/packages/HKModding.HKMod.Templates>） | 只要 `hkmod`/`hksettings` 模板；本骨架已经把这些都写好了 |
| 9 | *可选* 反编译器 | ILSpy <https://github.com/icsharpcode/ILSpy> | 查 HK 的类/字段，做 Patch 时几乎离不开 |

**版本坑（唯一需要你自己核一下的）**：Unity 必须是 **2020.2.2f1**。错一个小版本通常还能出包，但
`Assembly-CSharp.dll` 的序列化布局差异会在运行时炸。确认不了就先按 2020.2.2f1 装。

---

## 二、目录结构

```
hkmod-custom-scene/
├─ FinalMod/                ← 工程③ 真 Mod（C#，net472）——产出放进 Mods/
│  ├─ HKCustomSceneMod.csproj
│  ├─ CustomSceneMod.cs          Mod 主类：加载 AssetBundle / preload / hooks / 劫持入口
│  ├─ SettingsClass.cs           存"是否到访过"的 bool
│  ├─ PrefabHolder.cs            持有预载物
│  └─ Patchers/
│     ├─ PatchAreaTitleController.cs
│     ├─ PatchPlayMakerManager.cs
│     └─ PatchTilemapSize.cs
├─ MonoBehaviours/          ← 工程① 壳工程（C#，只有成员变量、函数体全空）
│  └─ …                         编译出的 dll 要丢进 Unity 的 Assets/Assemblies/
└─ UnityProject/            ← 工程② Unity 2020.2.2（2D 模板，需要你自己在 Hub 里建）
   ├─ ProjectSettings/TagManager.asset   ← 直接覆盖，别手抄
   └─ Assets/
      ├─ _MonoScripts/       HK 原组件的"壳"，让 Unity 能正确序列化引用
      ├─ Editor/             4 个编辑期小工具
      ├─ Assemblies/         放 SFCore.dll(由 SFCoreUnity.dll 改名) + 壳 dll
      ├─ Meshes/             TutorialScene.obj（示例地形）
      ├─ Scenes/             在这里建 MyFirstCustomScene
      └─ Materials/          Physics Material 2D / 材质
```

> **硬约束**：`FinalMod` 与 `MonoBehaviours` 两个工程的 **AssemblyName 必须同名**（都叫 `HKCustomSceneMod`），
> 且 Patch 类必须**同 namespace 同名**。Unity 就是靠 `程序集名 + 命名空间 + 类名` 去反查脚本的，
> 差一个字，场景里的组件就变成 "Missing (Mono Script)"。

---

## 三、三步流程

### 步骤 A — 建 Unity 工程（只做一次）

1. Unity Hub 新建工程，模板选 **2D**，版本 2020.2.2f1，名字随意。
2. 关掉 Unity，把本骨架 `UnityProject/ProjectSettings/TagManager.asset`
   **覆盖**到新工程的 `ProjectSettings/TagManager.asset`（这一步不做，后面 layer/tag/sorting layer 全是错的）。
3. 重开 Unity，把本骨架 `UnityProject/Assets/` 下的 `_MonoScripts/`、`Editor/` 两个文件夹**整个拷进去**。
4. 按 `Assets/` 里的 README 把 `SFCore.dll` 和壳 dll 放进 `Assemblies/`。
5. 细节和必做项见 **`UnityProject/README-导入说明.md`**。

### 步骤 B — 编译壳 dll

```powershell
cd hkmod-custom-scene\MonoBehaviours
dotnet build -c Release
# 产物 HKCustomSceneMod.dll → 拷进 UnityProject/Assets/Assemblies/
```
csproj 里的 `<UnityManaged>` 属性指向 Unity 的 `Editor\Data\Managed\UnityEngine\`，路径不对就改它。

### 步骤 C — 编译真 Mod

```powershell
cd hkmod-custom-scene\FinalMod
dotnet build -c Release
```
csproj 里的 `<HollowKnightRefs>` 默认指向 Steam 默认安装路径，不是就改。
产物会自动拷进 `…\Hollow Knight\hollow_knight_Data\Managed\Mods\HKCustomSceneMod\`。
进游戏按 F1 打开 ModLog，看到 `HKCustomSceneMod loaded` 即成功。

---

## 四、这份骨架**没有**替你做的事

- 没建 Unity 工程本体（`Library/`、`ProjectSettings/*` 的其余文件、`.meta` 必须由 Unity 自己生成）。
- 没做场景内容：地形 mesh、装饰精灵、光照参数、BlurPlane、CameraLockArea，都要你在 Unity 里摆。
- 没写 `PatchSceneManager`：它由 **SFCore** 提供，直接用 SFCore 里那个组件即可。
- 没写打通"从别的场景走进去"以外的玩法（敌人、掉落、剧情），那属于后续工作。

## 五、已知会卡人的点（文档没强调或散落在各处）

1. **`TagManager.asset` 必须覆盖**，否则 `Terrain(8)`、sorting layer 全错。
2. **`_Managers/PlayMaker Unity 2D` 不是可选项**：NPC 名字显示（Dream Nail 名字等）依赖它。
3. **预载尽量全从同一个场景拿**（骨架用 `White_Palace_18` 一次拿两个），预载是加载耗时大头。
4. **门口的命名有约定**：`top1/top2…`、`left1…`、`right1…`、`bot1…`、`door1…`，`entryPoint` 要对上。
5. **`door` 类型的门**光改 `TransitionPoint` 不够，还要同步它的 `Door Control` FSM 的
   `New Scene` / `Entry Gate` 变量（骨架里写了）。
6. **地图**：`PatchTilemapSize` 的宽高不填对，游戏内地图和相机锁定区域会不对。
7. **自定义 shader 会打破跨平台**：`BuildTarget.StandaloneWindows64` 打出来的包只保证 Windows。
8. **中文**不在本文档范围内：`LanguageGetHook` 只解决文字内容，字形要另做（换字体/图集）。
