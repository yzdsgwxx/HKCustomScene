<#
=============================================================================
  打包测试.ps1 —— 改完 Unity 场景之后，一条命令走完全套「打包 → 安装 → 进游戏」

  它按顺序做这 5 件事（就是你平时手点的那 5 步）：
    1. 让 Unity 跑一遍打包（菜单 HKModCustomSceneTool → Build AssetBundles Compressed，等价于 HKModCustomSceneTool → 打包测试 的第一步）
       （不用你切窗口点菜单：脚本通过工程里的 Assets\Editor\HKCSBuildBridge.cs
         给编辑器发命令；桥要是还没生效，会退回「你点菜单、脚本盯着文件等」）
    2. 把 Assets\AssetBundles\ 里打出来的包拷进 FinalMod\Resources\
    3. dotnet build -c Release 编译 FinalMod
    4. 关掉空洞骑士，把编译出的 HKCustomSceneMod.dll 装进
       <游戏>\hollow_knight_Data\Managed\Mods\CustomScene\（覆盖前自动备份）
       ＋ 对面那份 DialogueConfig.json（对话文本）**只在缺失时**放过去，
         玩家自己改过的文本不会被覆盖（改文本不用重新打包，改完重启游戏即可）
    5. 重新启动空洞骑士（**请 Steam 启动**，原因见下）

  用法（在本目录下，用 Windows PowerShell 5.1）：
      powershell -ExecutionPolicy Bypass -File .\打包测试.ps1
  或者直接双击同目录的  打包测试.cmd

  常用开关：
      -SkipBundle        跳过第 1 步（直接用 Assets\AssetBundles 里现成的包）
      -SkipGameRestart   只部署，不重启游戏
      -NoUnityLaunch     Unity 没开着就报错退出（默认会自动帮你启动 Unity 并等它就绪）
      -DryRun            只做检查并打印每一步会干什么，不动任何文件、不起任何进程
      -BuildTimeoutSec   等 Unity 打包的超时秒数，默认 900
      -LaunchExe         直接启动 hollow_knight.exe。⚠ 默认不这么做：本机实测直接开 exe 会踩
                         Steam DRM（SteamAPI_RestartAppIfNecessary 让 Steam 又拉起一份），
                         两份抢 Unity 单实例锁 → 弹 "Fatal error: Another instance is already running"
      -GameStartTimeoutSec  等游戏进程出现的秒数，默认 90
      -UnityExe / -UnityProject / -GameRoot / -ModsFolder
                         覆盖默认路径（默认值见 HKCS.Common.ps1 顶部注释）
=============================================================================
#>
[CmdletBinding()]
param(
    [switch]$SkipBundle,
    [switch]$SkipGameRestart,
    [switch]$NoUnityLaunch,
    [switch]$DryRun,
    [int]$BuildTimeoutSec = 900,
    [int]$UnityStartTimeoutSec = 420,
    [switch]$LaunchExe,
    [int]$GameStartTimeoutSec = 90,
    [int]$ModLogWaitSec = 60,
    [string]$UnityExe,
    [string]$UnityProject,
    [string]$GameRoot,
    [string]$ModsFolder
)

$ErrorActionPreference = 'Stop'
$scriptStart = Get-Date

. (Join-Path $PSScriptRoot 'HKCS.Common.ps1')
Start-HkcsTranscript -Name '打包测试'

$paths = New-HkcsPaths -UnityExe $UnityExe `
                       -UnityProject $UnityProject `
                       -GameRoot $GameRoot `
                       -ModsFolder $ModsFolder

Write-HkcsBanner '打包测试：改完场景 → 打包 → 编译 → 装进游戏 → 重启游戏'
if ($DryRun) { Write-HkcsDryRun '演练模式：只检查路径、只打印计划，不改文件、不起进程。' }

try {
    # ------------------------------------------------------------------
    Write-HkcsStep '0/5 预检'
    Test-HkcsPrerequisites -Paths $paths -NeedBundleToolchain -NeedGame -NeedUnity:(-not $SkipBundle)
    Write-HkcsPathSummary -Paths $paths

    if ($SkipBundle) {
        Write-HkcsWarn '跳过「打包」步骤（-SkipBundle），直接用 Assets\AssetBundles 里现成的包。'
        $existing = Get-HkcsBundleFiles -Paths $paths
        if ($existing.Count -eq 0) {
            Stop-Hkcs ('Assets\AssetBundles 里没有现成的包：' + $paths.BundleDir + '。去掉 -SkipBundle 重新跑。')
        }
        foreach ($b in $existing) {
            Write-HkcsInfo ('现有包：' + $b.Name + '  ' + $b.Length + ' 字节  ' + $b.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))
        }
    }

    # ------------------------------------------------------------------
    Write-HkcsStep '1/5 打包 AssetBundle（HKModCustomSceneTool → Build AssetBundles Compressed）'
    if ($SkipBundle) {
        Write-HkcsInfo '已跳过。'
    } else {
        Invoke-HkcsUnityBundleBuild -Paths $paths `
                                    -TimeoutSec $BuildTimeoutSec `
                                    -UnityStartTimeoutSec $UnityStartTimeoutSec `
                                    -NoUnityLaunch:$NoUnityLaunch `
                                    -DryRun:$DryRun
    }

    # ------------------------------------------------------------------
    Write-HkcsStep '2/5 把 AssetBundle 拷进 FinalMod\Resources'
    $bundles = Get-HkcsBundleFiles -Paths $paths
    if ($bundles.Count -eq 0) {
        Stop-Hkcs ('没找到任何 AssetBundle：' + $paths.BundleDir + '。检查场景最底部的 AssetBundle 名是否填了 hkcs_scenes。')
    }
    if (-not $DryRun -and -not (Test-Path -LiteralPath $paths.FinalModRes)) {
        New-Item -ItemType Directory -Path $paths.FinalModRes -Force | Out-Null
        Write-HkcsInfo ('新建目录：' + $paths.FinalModRes)
    }
    foreach ($b in $bundles) {
        $dest = Join-Path $paths.FinalModRes $b.Name
        if ($DryRun) {
            Write-HkcsDryRun ('会拷贝：' + $b.FullName + '  →  ' + $dest)
            continue
        }
        Copy-Item -LiteralPath $b.FullName -Destination $dest -Force
        if (-not (Test-HkcsSameFile -A $b.FullName -B $dest)) {
            Stop-Hkcs ('拷贝后内容不一致：' + $dest)
        }
        Write-HkcsOk ($b.Name + '  ' + $b.Length + ' 字节  →  ' + $dest)
    }
    Write-HkcsInfo 'FinalMod\Resources 当前内容（这些会被嵌进 dll）：'
    if (Test-Path -LiteralPath $paths.FinalModRes) {
        foreach ($f in (Get-ChildItem -LiteralPath $paths.FinalModRes -File | Sort-Object Name)) {
            Write-HkcsInfo ('    ' + $f.Name + '  ' + $f.Length + ' 字节  ' + $f.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))
        }
    }

    # ------------------------------------------------------------------
    Write-HkcsStep '3/5 编译 FinalMod'
    if ($DryRun) {
        Write-HkcsDryRun ('会执行：dotnet build "' + $paths.FinalModProj + '" -c Release')
    } else {
        Invoke-HkcsDotnetBuild -Project $paths.FinalModProj -Label 'FinalMod'
        if (-not (Test-Path -LiteralPath $paths.FinalModDll)) {
            Stop-Hkcs ('编译结束但没找到产物：' + $paths.FinalModDll)
        }
        $dllItem = Get-Item -LiteralPath $paths.FinalModDll
        Write-HkcsOk ('产物：' + $paths.FinalModDll + '  ' + $dllItem.Length + ' 字节  ' + $dllItem.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))

        $newestRes = Get-ChildItem -LiteralPath $paths.FinalModRes -File -ErrorAction SilentlyContinue |
                     Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($newestRes -and $dllItem.LastWriteTime -lt $newestRes.LastWriteTime) {
            Write-HkcsWarn ('dll 比 Resources 里的文件还旧（' + $dllItem.LastWriteTime.ToString('HH:mm:ss') +
                            ' < ' + $newestRes.LastWriteTime.ToString('HH:mm:ss') + '），场景包可能没被嵌进去。')
            Write-HkcsWarn '可以删掉 FinalMod\obj\Release 后再跑一次。'
        }
    }

    # ------------------------------------------------------------------
    Write-HkcsStep '4/5 把 dll 装进空洞骑士'
    if ($DryRun) {
        Write-HkcsDryRun ('会先关掉正在运行的游戏，然后把 ' + $paths.FinalModDll + ' 拷到 ' + $paths.ModsFolder)
        Write-HkcsDryRun ('对话文本配置只在缺失时放一份过去：' + $paths.ModDialogueConfig + '（已存在就不动）')
    } else {
        if (-not (Test-Path -LiteralPath $paths.FinalModDll)) {
            Stop-Hkcs ('没有可安装的 dll：' + $paths.FinalModDll)
        }
        Stop-HkcsGame -Paths $paths
        $installed = Copy-HkcsFileWithBackup -Source $paths.FinalModDll `
                                             -DestinationDir $paths.ModsFolder `
                                             -BackupDir $paths.BackupDir
        if (-not (Test-HkcsSameFile -A $paths.FinalModDll -B $installed)) {
            Stop-Hkcs ('装过去的 dll 和编译产物不一致：' + $installed)
        }
        $installedItem = Get-Item -LiteralPath $installed
        $hash = (Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash
        Write-HkcsOk ('已安装：' + $installed)
        Write-HkcsInfo ('大小 ' + $installedItem.Length + ' 字节，SHA256 ' + $hash.Substring(0, 12) + '…')
        Write-HkcsInfo ('其中嵌了 HKCustomSceneMod.Resources.hkcs_scenes（场景包），游戏读的就是它。')

        # 对话文本配置：**只在没有的时候**放一份过去 —— 绝不覆盖玩家自己改过的文本。
        # 文本改了不用重新打包：直接编辑 mod 目录里那个 json，重启游戏即可。
        if (Test-Path -LiteralPath $paths.RepoDialogueConfig) {
            if (Test-Path -LiteralPath $paths.ModDialogueConfig) {
                Write-HkcsInfo ('对话文本配置已存在，保留不动（不会覆盖你的改动）：' + $paths.ModDialogueConfig)
            } else {
                Copy-Item -LiteralPath $paths.RepoDialogueConfig -Destination $paths.ModDialogueConfig -Force
                Write-HkcsOk ('已放入对话文本配置：' + $paths.ModDialogueConfig)
                Write-HkcsInfo '改里面的字 → 存盘 → 重启游戏生效（不用重新打包）。'
            }
        }
    }

    # ------------------------------------------------------------------
    Write-HkcsStep '5/5 重启空洞骑士'
    if ($SkipGameRestart) {
        Write-HkcsInfo '已跳过（-SkipGameRestart）。'
    } elseif ($DryRun) {
        if ($LaunchExe) { Write-HkcsDryRun ('会直接启动：' + $paths.GameExe) }
        else            { Write-HkcsDryRun ('会请 Steam 启动（appid ' + $paths.SteamAppId + '）并等游戏进程出现') }
    } else {
        # 注意：一律走 Steam 启动。直接开 exe 会踩 Steam DRM，被重新拉起一份、撞出
        # "Another instance is already running"（2026-09-25 22:24 实测）。
        if (@(Get-HkcsGameProcesses -Paths $paths).Count -gt 0) {
            Stop-HkcsGame -Paths $paths
        }
        Start-HkcsGame -Paths $paths -LaunchExe:$LaunchExe -TimeoutSec $GameStartTimeoutSec -ModLogWaitSec $ModLogWaitSec
    }

    # ------------------------------------------------------------------
    Write-HkcsBanner '打包测试 —— 完成'
    Write-HkcsOk ('全部步骤结束，用时 ' + [int]((Get-Date) - $scriptStart).TotalSeconds + ' 秒。')
    if (-not $SkipGameRestart -and -not $DryRun) {
        Write-HkcsInfo '进游戏后在日志里找这几行确认包已生效：'
        Write-HkcsInfo '    [CustomSceneMod] - 场景包已加载，含 1 个场景：Assets/Scenes/HKCS_Room01.unity'
        Write-HkcsInfo ('日志文件：' + $paths.ModLog)
    }
    Stop-HkcsTranscript
    exit 0
} catch {
    Stop-Hkcs $_.Exception.Message
}
