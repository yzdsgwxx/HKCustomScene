# tools —— 一键脚本（打包测试 / 更新壳工程）

## 最简用法

| 什么时候 | 做什么 |
|---|---|
| 只是改了 Unity 场景 | 双击 **`打包测试.cmd`** |
| 改了壳工程（`MonoBehaviours\`） | 先双击 **`更新壳工程.cmd`**（等 Unity 起来），再双击 **`打包测试.cmd`** |
| 只想看看会干什么 | 加 `-DryRun`；只部署不重启游戏加 `-SkipGameRestart` |

下面是细节。

把「改完 Unity 场景 → 打包 → 编译 → 装进游戏 → 重启游戏」这条链路固化成两条命令，
省掉每次手动点菜单、手动拷文件、手动开关编辑器/游戏。

| 脚本 | 干什么 | 什么时候跑 |
|---|---|---|
| **打包测试** | 打包 AssetBundle → 拷进 `FinalMod\Resources\` → 编译 FinalMod → 把 dll 装进空洞骑士 → 重启空洞骑士 | 每次改完 Unity 场景 |
| **更新壳工程** | 编译壳工程（`MonoBehaviours\`）→ 覆盖 Unity 工程的 `Assets\Assemblies\HKCustomSceneMod.dll` → 重启 Unity | 改了壳工程代码/结构之后（要在「打包测试」之前跑） |

跑完「更新壳工程」不会自动接着跑「打包测试」—— 壳更新完你还得进 Unity 摆场景，
摆完再跑「打包测试」收尾。

## 怎么用

双击同名 `.cmd` 就行（会 pause，报错也看得见）。或者在命令行：

```powershell
cd D:\HKModding\hkmod-custom-scene\tools
powershell -ExecutionPolicy Bypass -File .\打包测试.ps1
powershell -ExecutionPolicy Bypass -File .\更新壳工程.ps1
```

常用开关（完整列表见脚本头部注释）：

```
打包测试.ps1   -SkipBundle        跳过打包，直接用 Assets\AssetBundles 里现成的包
               -SkipGameRestart   只部署，不重启游戏
               -NoUnityLaunch     Unity 没开着就报错（默认会自动启动 Unity 并等它）
               -DryRun            只检查路径、只打印计划，不动任何文件
               -BuildTimeoutSec   等 Unity 打包的超时秒数（默认 900）

更新壳工程.ps1 -Force             不等你手动关，直接强杀 Unity（未保存的改动会丢）
               -NoUnityRestart    只编译 + 覆盖 dll，不自动关/开 Unity
               -DryRun            只检查路径、只打印计划
```

换机器/换安装位置时，改 `HKCS.Common.ps1` 里 `New-HkcsPaths` 的默认值，
或每次用 `-UnityExe` / `-UnityProject` / `-GameRoot` / `-ModsFolder` 覆盖。

本机默认路径（实测值）：

```
Unity 编辑器   D:\UnityEngine\2020.2.2f1\Editor\Unity.exe
Unity 工程     <仓库根>\UnityProject\HKModCustomScene
空洞骑士       D:\APP\steam.exe\steamapps\common\Hollow Knight
装 mod 到      <游戏>\hollow_knight_Data\Managed\Mods\CustomScene
```

## 目录里都是什么

| 文件 | 说明 |
|---|---|
| `打包测试.ps1` / `打包测试.cmd` | 主流程脚本 + 双击启动器 |
| `更新壳工程.ps1` / `更新壳工程.cmd` | 壳工程脚本 + 双击启动器 |
| `HKCS.Common.ps1` | 两个脚本共用的零件（路径、进程、命令桥、编译、拷贝备份）。不要单独运行 |
| `.bridge\` | 脚本和 Unity 编辑器之间的命令/应答文件（临时，跑完自动清空） |
| `_stage\` | 「更新壳工程」编译壳工程的暂存目录（先编到这里，避免锁住 Unity 正在用的 dll） |
| `_backup\` | 每次**内容真的变了**才自动备份的旧文件（只增不删，文件名带时间戳） |

编辑器那一侧的配对文件在 Unity 工程里：

```
UnityProject\HKModCustomScene\Assets\Editor\HKCSBuildBridge.cs    ← 真工程（Unity 用的就是它）
UnityProject\Assets\Editor\HKCSBuildBridge.cs                     ← 骨架拷贝源（重建工程时用）
```

## 原理：脚本怎么让 Unity「自己打包」

Unity 不允许第二个实例打开同一个工程，所以不能在开着编辑器的时候用 `-batchmode` 打包；
Unity 也没给外部程序留「点菜单」的接口。于是反过来做：让编辑器**自己轮询命令文件**。

1. `打包测试.ps1` 往 `tools\.bridge\cmd.txt` 写一行 `cmd=build_bundles`；
2. 编辑器里的 `HKCSBuildBridge.cs`（`[InitializeOnLoad]` + `EditorApplication.update`，每 0.4 秒看一眼）
   读到命令 → 保存打开的场景 → `BuildPipeline.BuildAssetBundles(..., BuildAssetBundlesOptions.None, StandaloneWindows64)`
   （和菜单 **Build AssetBundles → Build AssetBundles Compressed** 完全等价）；
3. 结果写到 `tools\.bridge\result.<id>.txt`，脚本读它并继续后面的步骤。

实测：Unity 在前台/后台都能应答（后台约 0.6 秒）。**不用切窗口、不用点菜单、不用关 Unity。**

⚠ 唯一的例外：`HKCSBuildBridge.cs` 刚放进工程时，Unity 要「重新获得焦点」才会刷新并编译它。
这时脚本会**自动把 Unity 窗口切到前台一次**（`Show-HkcsUnityWindow`）逼它刷新；
万一失败，脚本会提示你手动点一下 Unity 窗口，或直接手点一次菜单 —— 之后就一直全自动了。

## 排查

| 症状 | 原因 / 处理 |
|---|---|
| `命令桥没应答` 然后一直超时 | 多半是 Unity 没刷新编译 `HKCSBuildBridge.cs`。点一下 Unity 窗口；确认 Console 没有红字；`tools\.bridge` 里不该长期堆着 `cmd.txt` |
| `Access to the path ... HKCustomSceneMod.dll is denied` | Unity 正锁着 `Assets\Assemblies` 里的 dll。这就是「更新壳工程」必须先关 Unity 的原因：去掉 `-NoUnityRestart`，或用 `-Force` |
| 游戏里还是旧行为 | 看 `%USERPROFILE%\AppData\LocalLow\Team Cherry\Hollow Knight\ModLog.txt`；确认 `打包测试` 第 4 步打印的 SHA256 和 `FinalMod\bin\Release\HKCustomSceneMod.dll` 一致 |
| `dll 比 Resources 里的文件还旧` | 场景包可能没被嵌进 dll。删掉 `FinalMod\obj\Release` 再跑一次 |
| 打包成功但游戏里场景没变 | 检查场景最底部的 AssetBundle 名是不是 `hkcs_scenes`，以及 `FinalMod\Resources\hkcs_scenes` 的时间戳 |
| 想回到某个旧 dll | `tools\_backup\` 里按时间戳找，复制回 `Mods\CustomScene\` 即可（游戏要先关掉） |
