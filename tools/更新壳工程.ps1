<#
=============================================================================
  更新壳工程.ps1 —— 改了壳工程（MonoBehaviours\）的代码/结构之后跑这个

  壳工程 = MonoBehaviours\HKCustomSceneMod.MonoBehaviours.csproj
  它只提供 MonoBehaviour 的「形状」（类名/字段），编译出的 HKCustomSceneMod.dll 要放进
  Unity 工程的 Assets\Assemblies\，Unity 才能把组件挂到物体上、把字段值序列化进 .unity。

  它按顺序做这 4 件事：
    1. dotnet build 编译壳工程，**先编译到临时暂存目录**（tools\_stage\Assemblies）
       —— 这样即使代码编不过，也不会去动 Unity 正在用的那个 dll
    2. 关掉 Unity（本工程的那个实例）：优先让它自己保存场景后正常退出，
       桥不通就提示你手动关，加 -Force 则强杀（未保存的改动会丢）
    3. 把新 dll 覆盖到 <Unity 工程>\Assets\Assemblies\，然后重新启动 Unity

  为什么必须先关 Unity 再覆盖：Unity 会锁定 Assets\Assemblies\ 里加载过的 dll，
  开着编辑器直接覆盖会得到「Access to the path ... is denied」。

  用法（在本目录下，用 Windows PowerShell 5.1）：
      powershell -ExecutionPolicy Bypass -File .\更新壳工程.ps1
  或者直接双击同目录的  更新壳工程.cmd

  常用开关：
      -Force               不等你手动关，直接强制结束 Unity 进程（未保存的改动会丢）
      -NoUnityRestart      只编译 + 覆盖 dll，不自动关/开 Unity
                           （这时 Unity 必须本来就没开着本工程，否则覆盖会失败）
      -DryRun              只检查路径、只打印计划，不动文件、不起/停进程
      -UnityStartTimeoutSec 等 Unity 启动并锁住工程的秒数，默认 420
      -UnityExe / -UnityProject  覆盖默认路径

  跑完这个脚本之后，接着跑 打包测试.ps1 就能把新壳打出的场景重新打包、装进游戏、
  重启游戏（本脚本不会自动替你跑，避免在你还没摆完场景时浪费时间）。
=============================================================================
#>
[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$NoUnityRestart,
    [switch]$DryRun,
    [int]$UnityStartTimeoutSec = 420,
    [string]$UnityExe,
    [string]$UnityProject
)

$ErrorActionPreference = 'Stop'
$scriptStart = Get-Date

. (Join-Path $PSScriptRoot 'HKCS.Common.ps1')

$paths = New-HkcsPaths -UnityExe $UnityExe -UnityProject $UnityProject

# ---------------------------------------------------------------------
#  让本工程的 Unity 编辑器优雅退出（桥 → 手动 → 强杀 三级兜底）
# ---------------------------------------------------------------------
function Stop-HkcsUnityEditor {
    param($Paths, [switch]$Force, [switch]$DryRun)

    if (-not (Test-HkcsUnityProjectOpen -Paths $Paths)) {
        $others = @(Get-HkcsUnityProcess)
        if ($others.Count -gt 0) {
            Write-HkcsInfo ('检测到 ' + $others.Count + ' 个 Unity 进程，但它们没打开本工程，不动它们。')
        } else {
            Write-HkcsInfo 'Unity 没在运行。'
        }
        return
    }

    Write-HkcsInfo '本工程正被 Unity 打开，先让它退出（退出前会自动保存打开的场景）。'
    if ($DryRun) {
        Write-HkcsDryRun '会命令 Unity 保存场景并退出，然后重启它。'
        return
    }

    $quit = Invoke-HkcsBridge -Command 'quit' -BridgeDir $Paths.BridgeDir -TimeoutSec 20
    if ($quit -and $quit['ok'] -eq '1') {
        Write-HkcsOk ('已发出退出命令（' + $quit['message'] + '）。')
    } else {
        Write-HkcsWarn '编辑器内的命令桥没应答（HKCSBuildBridge.cs 可能还没被 Unity 编译过），没法让 Unity 自己保存退出。'
    }

    if (Wait-HkcsUnityExit -Paths $Paths -TimeoutSec 60) {
        Write-HkcsOk 'Unity 已退出。'
        return
    }

    if ($Force) {
        Write-HkcsWarn '强制结束 Unity 进程（未保存的改动会丢）。'
        $procs = @(Get-HkcsUnityProcessForProject -Paths $Paths)
        if ($procs.Count -eq 0) { $procs = @(Get-HkcsUnityProcess) }
        foreach ($proc in $procs) {
            Write-HkcsInfo ('结束 Unity.exe（PID ' + $proc.Id + '）')
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        }
        Start-Sleep -Seconds 3
        if (Wait-HkcsUnityExit -Paths $Paths -TimeoutSec 30) {
            Write-HkcsOk 'Unity 已结束。'
            return
        }
        Stop-Hkcs 'Unity 进程还在，没法覆盖 Assets\Assemblies 里的 dll。'
    }

    Write-HkcsWarn '需要你手动关闭 Unity：请切到 Unity 窗口，保存场景后正常退出。'
    Read-Host '关掉 Unity 之后按回车继续（想取消就按 Ctrl+C）' | Out-Null
    if (-not (Wait-HkcsUnityExit -Paths $Paths -TimeoutSec 180)) {
        Stop-Hkcs 'Unity 还在运行（或还锁着本工程）。请关掉它，或用 -Force 强杀。'
    }
    Write-HkcsOk 'Unity 已退出。'
}

try {
    # ------------------------------------------------------------------
    Write-HkcsStep '0/4 预检'
    Test-HkcsPrerequisites -Paths $paths -NeedUnity
    if (-not (Test-Path -LiteralPath $paths.ShellProj)) {
        Stop-Hkcs ('找不到壳工程：' + $paths.ShellProj)
    }
    if (-not (Test-Path -LiteralPath $paths.UnityAssemblies)) {
        Stop-Hkcs ('找不到 Unity 工程的 Assets\Assemblies：' + $paths.UnityAssemblies)
    }
    Write-HkcsInfo ('仓库根      ' + $paths.RepoRoot)
    Write-HkcsInfo ('壳工程      ' + $paths.ShellProj)
    Write-HkcsInfo ('暂存目录    ' + $paths.StageAssemblies)
    Write-HkcsInfo ('目标 dll    ' + $paths.ShellDllInUnity)
    Write-HkcsInfo ('Unity 工程  ' + $paths.UnityProject)

    if ($NoUnityRestart -and (Test-HkcsUnityProjectOpen -Paths $paths)) {
        Stop-Hkcs ('Unity 正开着本工程，Assets\Assemblies 里的 dll 被锁着，覆盖不了。' +
                   '去掉 -NoUnityRestart 让脚本重启 Unity，或自己先关掉 Unity。')
    }

    # ------------------------------------------------------------------
    Write-HkcsStep '1/4 编译壳工程（先编到暂存目录）'
    if ($DryRun) {
        Write-HkcsDryRun ('会执行：dotnet build "' + $paths.ShellProj + '" -c Release -p:UnityProjectAssets=' +
                          $paths.StageDir)
    } else {
        if (Test-Path -LiteralPath $paths.StageDir) {
            Remove-Item -LiteralPath $paths.StageDir -Recurse -Force -ErrorAction SilentlyContinue
        }
        $buildStart = Get-Date
        Invoke-HkcsDotnetBuild -Project $paths.ShellProj `
                               -Label '壳工程' `
                               -ExtraArgs @('-p:UnityProjectAssets=' + $paths.StageDir)
        if (-not (Test-Path -LiteralPath $paths.StageShellDll)) {
            Stop-Hkcs ('编译结束但暂存目录里没有 dll：' + $paths.StageShellDll +
                       '（壳工程的 CopyToUnity 目标可能被改过）')
        }
        $stageItem = Get-Item -LiteralPath $paths.StageShellDll
        if ($stageItem.LastWriteTime -lt $buildStart.AddSeconds(-5)) {
            Write-HkcsWarn '暂存 dll 的时间戳比本次编译还旧 —— 可能什么都没重新编（源码没变）。'
        }
        Write-HkcsOk ('新壳 dll：' + $paths.StageShellDll + '  ' + $stageItem.Length + ' 字节  ' +
                      $stageItem.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))
        Write-HkcsInfo '（这一步没动 Unity 里的 dll，所以编译失败也不会影响你现在开着的编辑器。）'
    }

    # ------------------------------------------------------------------
    Write-HkcsStep '2/4 关闭 Unity'
    if ($NoUnityRestart) {
        Write-HkcsInfo '已指定 -NoUnityRestart：不关 Unity（前面已确认它没开着本工程）。'
    } else {
        Stop-HkcsUnityEditor -Paths $paths -Force:$Force -DryRun:$DryRun
    }

    # ------------------------------------------------------------------
    Write-HkcsStep '3/4 把新 dll 覆盖到 Assets\Assemblies'
    if ($DryRun) {
        Write-HkcsDryRun ('会备份旧文件到 ' + $paths.BackupDir + '，然后把 ' + $paths.StageShellDll +
                          ' 覆盖到 ' + $paths.ShellDllInUnity)
    } else {
        if (-not (Test-Path -LiteralPath $paths.StageShellDll)) {
            Stop-Hkcs ('没有可安装的壳 dll：' + $paths.StageShellDll)
        }
        $target = Copy-HkcsFileWithBackup -Source $paths.StageShellDll `
                                          -DestinationDir $paths.UnityAssemblies `
                                          -BackupDir $paths.BackupDir
        if (-not (Test-HkcsSameFile -A $paths.StageShellDll -B $target)) {
            Stop-Hkcs ('覆盖后内容不一致：' + $target)
        }
        $ti = Get-Item -LiteralPath $target
        Write-HkcsOk ('已更新：' + $target + '  ' + $ti.Length + ' 字节')
        Write-HkcsInfo '没有动同目录的 .meta 文件，Unity 会继续认这个 dll。'
    }

    # ------------------------------------------------------------------
    Write-HkcsStep '4/4 启动 Unity'
    if ($NoUnityRestart) {
        Write-HkcsInfo '已指定 -NoUnityRestart：不启动 Unity。'
    } elseif ($DryRun) {
        Write-HkcsDryRun ('会启动：' + $paths.UnityExe + ' -projectPath "' + $paths.UnityProject + '"')
    } else {
        Start-HkcsUnity -Paths $paths
        Write-HkcsInfo ('等 Unity 启动并锁住工程（最多 ' + $UnityStartTimeoutSec + ' 秒）...')
        if (Wait-HkcsUnityReady -Paths $paths -TimeoutSec $UnityStartTimeoutSec) {
            Write-HkcsOk 'Unity 已启动。'
            Write-HkcsInfo '等 Unity 把 Assets\Assemblies 里的新 dll 导入完（Console 里出现编译结束），'
            Write-HkcsInfo '再去摆场景；摆完运行 打包测试.ps1 收尾。'
        } else {
            Write-HkcsWarn ('等 Unity 启动超时（' + $UnityStartTimeoutSec + ' 秒）。它可能还在导入，自己看一眼窗口吧。')
        }
    }

    Write-HkcsBanner '更新壳工程 —— 完成'
    Write-HkcsOk ('用时 ' + [int]((Get-Date) - $scriptStart).TotalSeconds + ' 秒。')
    if (-not $DryRun) {
        Write-HkcsInfo '下一步：在 Unity 里改/摆好场景，然后运行 打包测试.ps1（打包 → 编译 FinalMod → 装进游戏 → 重启游戏）。'
        Write-HkcsInfo ('备份目录：' + $paths.BackupDir)
    }
    exit 0
} catch {
    Stop-Hkcs $_.Exception.Message
}
