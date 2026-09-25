# HKCS.Common.ps1 —— 「打包测试」「更新壳工程」两个脚本共用的零件
#
# 不要单独运行本文件，它由另外两个脚本 dot-source（. 路径）后使用。
# 目标运行时：Windows PowerShell 5.1（本机默认的 powershell.exe），不依赖 pwsh。
#
# 本文件里的路径默认值全部来自本机实测：
#   Unity 编辑器   D:\UnityEngine\2020.2.2f1\Editor\Unity.exe
#   Unity 工程     <仓库根>\UnityProject\HKModCustomScene
#   空洞骑士       D:\APP\steam.exe\steamapps\common\Hollow Knight
#   装 mod 的目录  <游戏>\hollow_knight_Data\Managed\Mods\CustomScene
# 换机器时改下面 New-HkcsPaths 里的默认值，或在命令行传 -UnityExe / -UnityProject / -GameRoot / -ModsFolder。

Set-StrictMode -Off

# =====================================================================
#  一、路径
# =====================================================================

function New-HkcsPaths {
    [CmdletBinding()]
    param(
        [string]$RepoRoot,
        [string]$UnityExe,
        [string]$UnityProject,
        [string]$GameRoot,
        [string]$ModsFolder
    )

    if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
        $RepoRoot = Split-Path -Parent $PSScriptRoot          # tools\ 的上一级 = 仓库根
    }
    $RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd('\')

    if ([string]::IsNullOrWhiteSpace($UnityExe)) {
        $UnityExe = 'D:\UnityEngine\2020.2.2f1\Editor\Unity.exe'
    }
    if ([string]::IsNullOrWhiteSpace($UnityProject)) {
        $UnityProject = Join-Path $RepoRoot 'UnityProject\HKModCustomScene'
    }
    if ([string]::IsNullOrWhiteSpace($GameRoot)) {
        $GameRoot = 'D:\APP\steam.exe\steamapps\common\Hollow Knight'
    }

    $p = @{}
    $p.RepoRoot        = $RepoRoot
    $p.ToolsDir        = Join-Path $RepoRoot 'tools'
    $p.BridgeDir       = Join-Path $RepoRoot 'tools\.bridge'      # 与编辑器内 HKCSBuildBridge.cs 的约定一致
    $p.StageDir        = Join-Path $RepoRoot 'tools\_stage'       # 壳工程先编到这里，避免锁住 Unity 正在用的 dll
    $p.BackupDir       = Join-Path $RepoRoot 'tools\_backup'      # 每次覆盖前留一份备份（只增不删）

    $p.UnityExe        = $UnityExe
    $p.UnityProject    = $UnityProject.TrimEnd('\')
    $p.UnityAssets     = Join-Path $p.UnityProject 'Assets'
    $p.UnityAssemblies = Join-Path $p.UnityAssets 'Assemblies'
    $p.BundleDir       = Join-Path $p.UnityAssets 'AssetBundles'
    $p.ShellDllInUnity = Join-Path $p.UnityAssemblies 'HKCustomSceneMod.dll'

    $p.FinalModDir     = Join-Path $RepoRoot 'FinalMod'
    $p.FinalModProj    = Join-Path $RepoRoot 'FinalMod\HKCustomSceneMod.csproj'
    $p.FinalModRes     = Join-Path $RepoRoot 'FinalMod\Resources'
    $p.FinalModDll     = Join-Path $RepoRoot 'FinalMod\bin\Release\HKCustomSceneMod.dll'

    $p.ShellDir        = Join-Path $RepoRoot 'MonoBehaviours'
    $p.ShellProj       = Join-Path $RepoRoot 'MonoBehaviours\HKCustomSceneMod.MonoBehaviours.csproj'
    $p.StageAssemblies = Join-Path $p.StageDir 'Assemblies'
    $p.StageShellDll   = Join-Path $p.StageAssemblies 'HKCustomSceneMod.dll'

    $p.GameRoot        = $GameRoot.TrimEnd('\')
    $p.GameExe         = Join-Path $p.GameRoot 'hollow_knight.exe'
    $p.ManagedDir      = Join-Path $p.GameRoot 'hollow_knight_Data\Managed'

    if ([string]::IsNullOrWhiteSpace($ModsFolder)) {
        $ModsFolder = Join-Path $p.ManagedDir 'Mods\CustomScene'
    }
    $p.ModsFolder      = $ModsFolder.TrimEnd('\')
    $p.ModDll          = Join-Path $p.ModsFolder 'HKCustomSceneMod.dll'
    $p.ModLog          = Join-Path $env:USERPROFILE 'AppData\LocalLow\Team Cherry\Hollow Knight\ModLog.txt'

    # Steam（空洞骑士必须由 Steam 启动，见 Start-HkcsGame 的注释）：
    #   GameRoot = <Steam>\steamapps\common\Hollow Knight  →  往上三层就是 Steam 安装目录
    $p.SteamAppId      = '367520'
    $p.SteamRoot       = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $p.GameRoot))
    $p.SteamExe        = Join-Path $p.SteamRoot 'steam.exe'
    $p.SteamManifest   = Join-Path $p.SteamRoot ('steamapps\appmanifest_' + $p.SteamAppId + '.acf')

    return $p
}

# =====================================================================
#  二、输出
# =====================================================================

function Write-HkcsBanner {
    param([string]$Title)
    Write-Host ''
    Write-Host ('=' * 68) -ForegroundColor DarkCyan
    Write-Host ("  " + $Title) -ForegroundColor Cyan
    Write-Host ('=' * 68) -ForegroundColor DarkCyan
}

function Write-HkcsStep   { param([string]$Text) Write-Host ''; Write-Host ("--- " + $Text) -ForegroundColor Cyan }
function Write-HkcsOk     { param([string]$Text) Write-Host ("  [完成] " + $Text) -ForegroundColor Green }
function Write-HkcsInfo   { param([string]$Text) Write-Host ("  " + $Text) }
function Write-HkcsWarn   { param([string]$Text) Write-Host ("  [注意] " + $Text) -ForegroundColor Yellow }
function Write-HkcsErr    { param([string]$Text) Write-Host ("  [错误] " + $Text) -ForegroundColor Red }
function Write-HkcsDryRun { param([string]$Text) Write-Host ("  [演练] " + $Text) -ForegroundColor Magenta }

# 出错就停：打印一行红色错误并以退出码 1 结束脚本（双击 .cmd 时窗口跑完就自动关，失败才留 20 秒）
function Stop-Hkcs {
    param([string]$Text)
    Write-Host ''
    Write-HkcsErr $Text
    Write-Host ''
    Write-Host '脚本已中止。' -ForegroundColor Red
    Stop-HkcsTranscript
    exit 1
}

# ---------------------------------------------------------------------
#  把整场输出同时写进 tools\_log\*.log
#  为什么要它：双击 .cmd 跑完会自动关窗口（不再堆一堆窗口），日志留一份随时能回看。
#  只保留最近 20 个 .log。
# ---------------------------------------------------------------------
function Start-HkcsTranscript {
    param([string]$Name)
    try {
        $dir = Join-Path $PSScriptRoot '_log'
        if (-not (Test-Path -LiteralPath $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
        Get-ChildItem -LiteralPath $dir -Filter '*.log' -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -Skip 20 |
            ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }
        $file = Join-Path $dir ($Name + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
        Start-Transcript -Path $file -Force | Out-Null
        Write-Host ('本次输出同时写入：' + $file) -ForegroundColor DarkGray
    } catch {
        # 转录失败不影响主流程
    }
}

function Stop-HkcsTranscript {
    try { Stop-Transcript | Out-Null } catch { }
}

# =====================================================================
#  三、Unity 进程 / 工程锁
# =====================================================================

function Get-HkcsUnityProcess {
    <# 所有 Unity.exe 进程 #>
    return @(Get-Process -Name 'Unity' -ErrorAction SilentlyContinue)
}

function Get-HkcsUnityProcessForProject {
    <#
      只取「命令行里带本工程路径」的 Unity 进程。
      用 CIM 读命令行；读不到就退回「所有 Unity 进程」。
    #>
    param($Paths)
    $result = @()
    try {
        $procs = @(Get-CimInstance -ClassName Win32_Process -Filter "Name='Unity.exe'" -ErrorAction Stop)
        # CIM 有时能列出进程、却读不到 CommandLine（权限受限）：
        # 这时绝不能返回空列表 —— 否则会被误判成「Unity 已退出」，然后在 Unity 还攥着
        # Assets\Assemblies 里 dll 的时候去覆盖它。宁可保守地按「所有 Unity 进程」处理。
        $withCmd = @($procs | Where-Object { $_.CommandLine })
        if ($procs.Count -gt 0 -and $withCmd.Count -eq 0) {
            return (Get-HkcsUnityProcess)
        }
        foreach ($pr in $procs) {
            if ($pr.CommandLine -and $pr.CommandLine -like ('*' + $Paths.UnityProject + '*')) {
                $found = Get-Process -Id $pr.ProcessId -ErrorAction SilentlyContinue
                if ($found) { $result += $found }
            }
        }
        return $result
    } catch {
        return (Get-HkcsUnityProcess)
    }
}

function Get-HkcsFileLockState {
    <#
      文件现在能不能写：返回 'ok' / 'locked'（被别的进程占用）/ 'denied'（权限/只读）/ 'missing'。
      「Unity 是否真的退干净了」用这个判断比查进程准 —— 进程还在关闭途中时，
      工程锁文件已经放开，但 dll 的句柄还攥着。
    #>
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return 'missing' }
    try {
        $fs = [System.IO.File]::Open($Path,
                                     [System.IO.FileMode]::Open,
                                     [System.IO.FileAccess]::ReadWrite,
                                     [System.IO.FileShare]::None)
        $fs.Close()
        return 'ok'
    } catch [System.IO.IOException] {
        return 'locked'
    } catch {
        return 'denied'
    }
}

function Test-HkcsUnityProjectOpen {
    <#
      判断「本工程是否正被 Unity 打开」。
      判据：Temp\UnityLockfile 存在，且无法以独占方式打开 —— Unity 会给它加独占锁。
    #>
    param($Paths)
    $lock = Join-Path $Paths.UnityProject 'Temp\UnityLockfile'
    if (-not (Test-Path -LiteralPath $lock)) { return $false }
    try {
        $fs = [System.IO.File]::Open($lock,
                                     [System.IO.FileMode]::Open,
                                     [System.IO.FileAccess]::ReadWrite,
                                     [System.IO.FileShare]::None)
        $fs.Close()
        return $false      # 能独占打开 ⇒ 没人锁着它 ⇒ 工程没被打开
    } catch {
        return $true
    }
}

function Wait-HkcsUnityReady {
    param($Paths, [int]$TimeoutSec = 420)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-HkcsUnityProjectOpen -Paths $Paths) { return $true }
        Start-Sleep -Seconds 2
    }
    return $false
}

function Wait-HkcsUnityExit {
    param($Paths, [int]$TimeoutSec = 60)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $warned = $false
    while ((Get-Date) -lt $deadline) {
        $open = Test-HkcsUnityProjectOpen -Paths $Paths
        # 真正的"退干净"判据：工程锁放开 **且** 那个要被覆盖的 dll 不再被占用。
        # （权限拒绝不算"没退干净"，那种情况等多久都没用，直接返回让上层给出准确报错。）
        $state = Get-HkcsFileLockState -Path $Paths.ShellDllInUnity
        if (-not $open -and $state -ne 'locked') { return $true }
        if ($state -eq 'locked' -and -not $warned) {
            Write-HkcsInfo 'Unity 已放开工序锁，但 Assets\Assemblies 里的 dll 还被它攥着，再等一会儿 ...'
            $warned = $true
        }
        Start-Sleep -Seconds 1
    }
    return $false
}

function Start-HkcsUnity {
    param($Paths, [switch]$DryRun)
    if (-not (Test-Path -LiteralPath $Paths.UnityExe)) {
        Stop-Hkcs ("找不到 Unity 编辑器：" + $Paths.UnityExe + "（可用 -UnityExe 指定）")
    }
    if (-not (Test-Path -LiteralPath $Paths.UnityAssets)) {
        Stop-Hkcs ("找不到 Unity 工程：" + $Paths.UnityProject + "（可用 -UnityProject 指定）")
    }
    if ($DryRun) {
        Write-HkcsDryRun ('会执行：Unity.exe -projectPath "' + $Paths.UnityProject + '"')
        return
    }
    Write-HkcsInfo ('启动 Unity：' + $Paths.UnityExe)
    Start-Process -FilePath $Paths.UnityExe `
                  -ArgumentList @('-projectPath', ('"' + $Paths.UnityProject + '"')) `
                  -WorkingDirectory (Split-Path -Parent $Paths.UnityExe) | Out-Null
}

function Show-HkcsUnityWindow {
    <#
      把本工程的 Unity 窗口切到前台。
      为什么需要：Unity 的 Auto Refresh 是「重新获得焦点时才刷新资源」，
      新丢进 Assets 的 .cs（比如 HKCSBuildBridge.cs）不刷新就不会被编译，
      编辑器内的命令桥也就一直不响应。切一次前台 = 替用户点一下 Unity 窗口。
    #>
    param($Paths)

    $procs = @(Get-HkcsUnityProcessForProject -Paths $Paths)
    if ($procs.Count -eq 0) { $procs = @(Get-HkcsUnityProcess) }
    if ($procs.Count -eq 0) { return $false }

    $shell = $null
    try {
        $shell = New-Object -ComObject WScript.Shell
        foreach ($proc in $procs) {
            if ($shell.AppActivate($proc.Id)) {
                Write-HkcsInfo ('已把 Unity 窗口切到前台（PID ' + $proc.Id + '）以触发一次资源刷新。')
                return $true
            }
        }
        Write-HkcsInfo 'Unity 窗口没能切到前台（可能被最小化了）。'
    } catch {
        Write-HkcsInfo ('没法自动切 Unity 窗口：' + $_.Exception.Message + '（手动点一下 Unity 窗口也一样有效）。')
    } finally {
        if ($shell) { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
    }
    return $false
}

# =====================================================================
#  四、编辑器内命令桥
# =====================================================================
#
# 协议（纯文本 key=value，UTF-8 无 BOM）：
#   请求  <仓库根>\tools\.bridge\cmd.txt              id=<guid> / cmd=<命令> / ts=<ISO 时间>
#   应答  <仓库根>\tools\.bridge\result.<id>.txt      id / cmd / ok(1|0) / message / files / unity / project / pid / done
#
# 编辑器侧实现见 UnityProject\HKModCustomScene\Assets\Editor\HKCSBuildBridge.cs

function ConvertFrom-HkcsLines {
    param([string]$Text)
    $h = @{}
    foreach ($line in ($Text -split "`r?`n")) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $i = $line.IndexOf('=')
        if ($i -lt 1) { continue }
        $h[$line.Substring(0, $i).Trim()] = $line.Substring($i + 1)
    }
    return $h
}

function Invoke-HkcsBridge {
    <#
      发一条命令给正在运行的 Unity 编辑器，等它的应答。
      返回 key=value 的哈希表；超时（桥不在）返回 $null。
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Command,
        [Parameter(Mandatory = $true)][string]$BridgeDir,
        [int]$TimeoutSec = 300
    )

    if (-not (Test-Path -LiteralPath $BridgeDir)) {
        New-Item -ItemType Directory -Path $BridgeDir -Force | Out-Null
    }

    $id      = [guid]::NewGuid().ToString('N')
    $cmdPath = Join-Path $BridgeDir 'cmd.txt'
    $resPath = Join-Path $BridgeDir ('result.' + $id + '.txt')
    $tmpPath = Join-Path $BridgeDir ('cmd.' + $id + '.tmp')

    $payload = 'id=' + $id + "`r`n" + 'cmd=' + $Command + "`r`n" + 'ts=' + (Get-Date).ToString('o') + "`r`n"
    [System.IO.File]::WriteAllText($tmpPath, $payload, (New-Object System.Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $tmpPath -Destination $cmdPath -Force

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $resPath) {
            Start-Sleep -Milliseconds 150
            $text = ''
            try { $text = [System.IO.File]::ReadAllText($resPath, [System.Text.Encoding]::UTF8) } catch { }
            Remove-Item -LiteralPath $resPath -Force -ErrorAction SilentlyContinue
            return (ConvertFrom-HkcsLines -Text $text)
        }
        Start-Sleep -Milliseconds 400
    }

    # 超时：把没人认领的命令文件收回来，免得 Unity 下次启动时执行一条过期命令
    try {
        if (Test-Path -LiteralPath $cmdPath) {
            $still = [System.IO.File]::ReadAllText($cmdPath, [System.Text.Encoding]::UTF8)
            if ($still -like ('id=' + $id + '*')) {
                Remove-Item -LiteralPath $cmdPath -Force -ErrorAction SilentlyContinue
            }
        }
    } catch { }
    return $null
}

function Wait-HkcsBridge {
    <# 反复探活，直到编辑器内的桥应答（Unity 刚启动时脚本可能还没编译完）。 #>
    param($Paths, [int]$TimeoutSec = 120, [int]$PingTimeoutSec = 4)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $last = $null
    while ((Get-Date) -lt $deadline) {
        $last = Invoke-HkcsBridge -Command 'ping' -BridgeDir $Paths.BridgeDir -TimeoutSec $PingTimeoutSec
        if ($last -and $last['ok'] -eq '1') { return $last }
        Start-Sleep -Seconds 2
    }
    return $null
}

# =====================================================================
#  五、AssetBundle
# =====================================================================

function Get-HkcsBundleFiles {
    <# Assets\AssetBundles 里真正的包（排除 *.manifest / *.meta / 顶层 AssetBundles 清单文件） #>
    param($Paths)
    if (-not (Test-Path -LiteralPath $Paths.BundleDir)) { return @() }
    return @(Get-ChildItem -LiteralPath $Paths.BundleDir -File -ErrorAction SilentlyContinue |
             Where-Object { $_.Name -notlike '*.manifest' -and
                            $_.Name -notlike '*.meta' -and
                            $_.Name -ne 'AssetBundles' })
}

function Get-HkcsBundleStamps {
    param($Paths)
    $h = @{}
    foreach ($f in (Get-HkcsBundleFiles -Paths $Paths)) {
        $h[$f.Name] = ($f.LastWriteTimeUtc.Ticks.ToString() + '|' + $f.Length)
    }
    return $h
}

function Test-HkcsStampsChanged {
    param($Before, $Now)
    if ($Now.Keys.Count -eq 0) { return $false }
    foreach ($k in $Now.Keys) {
        if (-not $Before.ContainsKey($k)) { return $true }
        if ($Before[$k] -ne $Now[$k])    { return $true }
    }
    return $false
}

function Invoke-HkcsUnityBundleBuild {
    <#
      在 Unity 里跑一次 Build AssetBundles Compressed。两种方式：
        A. 编辑器内的桥（HKCSBuildBridge.cs）—— 全自动，不用关 Unity、不用点菜单；
        B. 兜底：提示用户手动点菜单，脚本盯着包文件的修改时间，一变就继续（同时不停重试 A）。
    #>
    param(
        $Paths,
        [int]$TimeoutSec = 900,
        [int]$UnityStartTimeoutSec = 420,
        [switch]$NoUnityLaunch,
        [switch]$DryRun
    )

    # 1) 确保 Unity 打开着本工程
    if (Test-HkcsUnityProjectOpen -Paths $Paths) {
        Write-HkcsOk 'Unity 正在运行，而且打开的就是本工程。'
    } else {
        if ($NoUnityLaunch) {
            Stop-Hkcs 'Unity 没打开本工程（脚本被 -NoUnityLaunch 禁止自动启动）。请先打开 Unity 工程再运行。'
        }
        Write-HkcsWarn 'Unity 没在运行（或没打开本工程），先把它启动起来。'
        Start-HkcsUnity -Paths $Paths -DryRun:$DryRun
        if ($DryRun) { return }
        Write-HkcsInfo ('等 Unity 启动并锁住工程（最多 ' + $UnityStartTimeoutSec + ' 秒；首次导入会比较久）...')
        if (-not (Wait-HkcsUnityReady -Paths $Paths -TimeoutSec $UnityStartTimeoutSec)) {
            Stop-Hkcs ('等 Unity 启动超时（' + $UnityStartTimeoutSec + ' 秒）。')
        }
        Write-HkcsOk 'Unity 起来了。'
    }

    $beforeStamps = Get-HkcsBundleStamps -Paths $Paths
    $startedAt    = Get-Date

    if ($DryRun) {
        Write-HkcsDryRun '会通知编辑器执行 BuildPipeline.BuildAssetBundles(Assets/AssetBundles, None, StandaloneWindows64)'
        return
    }

    # 2) 先探一下桥（已经编译过的话，第一次就通）
    Write-HkcsInfo '连接 Unity 编辑器内的命令桥 ...'
    $ping = Wait-HkcsBridge -Paths $Paths -TimeoutSec 12 -PingTimeoutSec 4
    $fromBridge = $false

    if (-not $ping) {
        # 3) 兜底：先替用户把 Unity 窗口切到前台（逼它刷新一次、把桥脚本编译出来），
        #    再等；实在不行就提示用户手动点菜单，脚本盯着包文件的修改时间。
        Write-HkcsWarn '命令桥没应答 —— 多半是 HKCSBuildBridge.cs 刚放进工程，Unity 还没刷新编译它。'
        Show-HkcsUnityWindow -Paths $Paths
        Write-HkcsInfo ('等 Unity 刷新 + 编译（最多 ' + $TimeoutSec + ' 秒）；桥一旦生效就自动打包，不用你点菜单。')
        Write-HkcsWarn '如果一直没动静，就手动点一下 Unity 窗口，或者点菜单：'
        Write-HkcsWarn '    HKModCustomSceneTool → 打包测试（或 Build AssetBundles Compressed）'

        $deadline  = (Get-Date).AddSeconds($TimeoutSec)
        $lastPing  = Get-Date
        $built     = $false
        $dots      = 0
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 2
            $dots++
            if ($dots % 5 -eq 0) { Write-Host '.' -NoNewline }

            $nowStamps = Get-HkcsBundleStamps -Paths $Paths
            if (Test-HkcsStampsChanged -Before $beforeStamps -Now $nowStamps) {
                Write-Host ''
                Write-HkcsOk '检测到 AssetBundle 已重新生成。'
                $built = $true
                break
            }

            if (((Get-Date) - $lastPing).TotalSeconds -ge 12) {
                $lastPing = Get-Date
                $p2 = Wait-HkcsBridge -Paths $Paths -TimeoutSec 5 -PingTimeoutSec 4
                if ($p2) {
                    Write-Host ''
                    Write-HkcsOk '命令桥已生效，改由脚本自动打包。'
                    $res = Invoke-HkcsBridge -Command 'build_bundles' -BridgeDir $Paths.BridgeDir -TimeoutSec $TimeoutSec
                    if (-not $res) { Stop-Hkcs ('等 Unity 打包结果超时。') }
                    if ($res['ok'] -ne '1') { Stop-Hkcs ('Unity 打包失败：' + $res['message']) }
                    Write-HkcsOk ('Unity 回报：' + $res['message'])
                    $built = $true
                    $fromBridge = $true
                    break
                }
            }
        }
        if (-not $built) {
            Stop-Hkcs ('等待打包超时（' + $TimeoutSec + ' 秒）。提示：点一下 Unity 窗口让它编译一次 HKCSBuildBridge.cs，之后就不用再手动点了。' +
                       '（要是你已经手动点过菜单，可能只是包内容没变、Unity 沿用了旧文件 —— 那就直接重新跑一次本脚本。）')
        }
    } else {
        Write-HkcsOk ('命令桥已连接（' + $ping['unity'] + '）。命令 Unity 打包 ...')
        $res = Invoke-HkcsBridge -Command 'build_bundles' -BridgeDir $Paths.BridgeDir -TimeoutSec $TimeoutSec
        if (-not $res) { Stop-Hkcs '等 Unity 打包结果超时 —— Unity 可能正忙（编译中/弹了对话框）。' }
        if ($res['ok'] -ne '1') { Stop-Hkcs ('Unity 打包失败：' + $res['message']) }
        Write-HkcsOk ('Unity 回报：' + $res['message'])
        $fromBridge = $true
    }

    # 4) 校验产物
    $bundles = Get-HkcsBundleFiles -Paths $Paths
    if ($bundles.Count -eq 0) {
        Stop-Hkcs '打包结束了，但 Assets\AssetBundles 里一个包都没有。检查场景最底部的 AssetBundle 名是否填了 hkcs_scenes。'
    }
    $fresh = @($bundles | Where-Object { $_.LastWriteTime -ge $startedAt.AddSeconds(-5) })
    foreach ($b in $bundles) {
        if ($b.LastWriteTime -ge $startedAt.AddSeconds(-5)) {
            $tag = '（本次新生成）'
        } elseif ($fromBridge) {
            $tag = '（内容没变 ⇒ Unity 沿用了旧文件，时间戳不会变，属正常）'
        } else {
            $tag = '（时间戳没变）'
        }
        Write-HkcsInfo ('包：' + $b.Name + '  ' + $b.Length + ' 字节  ' + $b.LastWriteTime.ToString('HH:mm:ss') + ' ' + $tag)
    }
    if ($fresh.Count -eq 0 -and -not $fromBridge) {
        Write-HkcsWarn '所有包的时间戳都没有变新。'
        Write-HkcsWarn '如果你刚才是手动点菜单打的包，而且场景内容本来就没改，Unity 会沿用旧文件、时间戳不变 —— 这种情况属正常。'
    }
}

# =====================================================================
#  六、dotnet 编译
# =====================================================================

function Invoke-HkcsDotnetBuild {
    param(
        [Parameter(Mandatory = $true)][string]$Project,
        [string]$Label = '工程',
        [string[]]$ExtraArgs
    )

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) { Stop-Hkcs '找不到 dotnet 命令（需要 .NET SDK）。' }
    if (-not (Test-Path -LiteralPath $Project)) { Stop-Hkcs ('找不到工程文件：' + $Project) }

    $dotnetArgs = @('build', $Project, '-c', 'Release', '-nologo', '-v', 'minimal')
    if ($ExtraArgs) { $dotnetArgs += $ExtraArgs }

    Write-HkcsInfo ('dotnet ' + ($dotnetArgs -join ' '))

    # dotnet 会往 stderr 写正常的构建信息；EAP=Stop 时那会被当成终止性错误，所以这里临时放开
    $oldEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $dotnet.Source @dotnetArgs 2>&1 | ForEach-Object { Write-Host ('    ' + $_) }
    } finally {
        $ErrorActionPreference = $oldEap
    }
    if ($LASTEXITCODE -ne 0) {
        Stop-Hkcs ($Label + ' 编译失败（dotnet 退出码 ' + $LASTEXITCODE + '）。')
    }
}

# =====================================================================
#  七、文件与进程
# =====================================================================

function Get-HkcsFileStamp {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return 'missing' }
    $fi = Get-Item -LiteralPath $Path
    return ($fi.LastWriteTimeUtc.Ticks.ToString() + '|' + $fi.Length)
}

function Copy-HkcsFileWithBackup {
    <#
      覆盖式拷贝，先把目标位置已有的文件备份到 <仓库根>\tools\_backup\（只增不删）。
      目标被占用时重试（游戏/编辑器刚退出时文件可能还被锁着）。
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$DestinationDir,
        [Parameter(Mandatory = $true)][string]$BackupDir,
        [int]$RetrySeconds = 30
    )

    if (-not (Test-Path -LiteralPath $DestinationDir)) {
        New-Item -ItemType Directory -Path $DestinationDir -Force | Out-Null
        Write-HkcsInfo ('新建目录：' + $DestinationDir)
    }

    $name = Split-Path -Leaf $Source
    $dest = Join-Path $DestinationDir $name

    # 内容已经一样就什么都不做：既不拷贝也不留备份（避免每跑一次就堆一份没用的 .bak）
    if ((Test-Path -LiteralPath $dest) -and (Test-HkcsSameFile -A $Source -B $dest)) {
        Write-HkcsInfo ('目标已是最新（内容完全相同），跳过拷贝与备份：' + $dest)
        return $dest
    }

    if (Test-Path -LiteralPath $dest) {
        if (-not (Test-Path -LiteralPath $BackupDir)) {
            New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
        }
        $bak = Join-Path $BackupDir ($name + '.bak-' + (Get-Date).ToString('yyyyMMdd-HHmmss'))
        Copy-Item -LiteralPath $dest -Destination $bak -Force
        Write-HkcsInfo ('旧文件已备份 → ' + $bak)
    }

    $deadline = (Get-Date).AddSeconds($RetrySeconds)
    while ($true) {
        try {
            Copy-Item -LiteralPath $Source -Destination $dest -Force -ErrorAction Stop
            break
        } catch {
            $state = Get-HkcsFileLockState -Path $dest
            if ((Get-Date) -ge $deadline) {
                if ($state -eq 'denied') {
                    Stop-Hkcs ('拷贝被拒绝（不是"被占用"，是**权限/只读**）：' + $dest + "`r`n" +
                               '        · 如果你是在某个受限沙箱/自动化里跑本脚本 → 换成你自己的终端（双击 .cmd）就行；' + "`r`n" +
                               '        · 否则检查文件是否只读，或该目录的 ACL。' + "`r`n" +
                               '        原始报错：' + $_.Exception.Message)
                }
                Stop-Hkcs ('拷贝失败（目标文件被 Unity / 游戏占用）：' + $dest + ' —— ' + $_.Exception.Message)
            }
            Write-HkcsWarn '目标文件还被占用着，2 秒后重试 ...'
            Start-Sleep -Seconds 2
        }
    }
    return $dest
}

function Test-HkcsSameFile {
    param([string]$A, [string]$B)
    $ha = (Get-FileHash -LiteralPath $A -Algorithm SHA256).Hash
    $hb = (Get-FileHash -LiteralPath $B -Algorithm SHA256).Hash
    return ($ha -eq $hb)
}

function Get-HkcsGameProcesses {
    <#
      游戏进程。先按进程名找；找不到再扫一遍所有进程的可执行文件路径
      （覆盖「exe 被改名 / 装在别处」的情况）。
    #>
    param($Paths)

    $found = @(Get-Process -Name 'hollow_knight' -ErrorAction SilentlyContinue)
    if ($found.Count -gt 0) { return $found }

    $root = $Paths.GameRoot.TrimEnd('\') + '\'
    $byPath = @()
    foreach ($proc in @(Get-Process -ErrorAction SilentlyContinue)) {
        try {
            $path = $proc.Path
            if ($path -and $path.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) { $byPath += $proc }
        } catch { }
    }
    return $byPath
}

function Show-HkcsProcessWindow {
    <# 把某些进程的主窗口切到前台（失败就静默跳过）。 #>
    param([int[]]$Ids)
    if (-not $Ids -or $Ids.Count -eq 0) { return }
    $shell = $null
    try {
        $shell = New-Object -ComObject WScript.Shell
        foreach ($id in $Ids) { [void]$shell.AppActivate($id) }
    } catch { } finally {
        if ($shell) { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
    }
}

function Test-HkcsSteamAvailable {
    <# 这台机器上的空洞骑士是不是 Steam 装的（有 appmanifest 就是）。 #>
    param($Paths)
    return (Test-Path -LiteralPath $Paths.SteamManifest)
}

function Stop-HkcsGame {
    param($Paths)
    $procs = @(Get-HkcsGameProcesses -Paths $Paths)
    if ($procs.Count -eq 0) {
        Write-HkcsInfo '空洞骑士没在运行。'
        return
    }

    foreach ($proc in $procs) {
        Write-HkcsInfo ('请求关闭 hollow_knight.exe（PID ' + $proc.Id + '）...')
        try { $proc.CloseMainWindow() | Out-Null } catch { }
    }

    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        $now = @(Get-HkcsGameProcesses -Paths $Paths)
        if ($now.Count -eq 0) {
            Write-HkcsOk '游戏已退出。'
            Start-Sleep -Seconds 1
            return
        }
        Start-Sleep -Milliseconds 500
    }

    Write-HkcsWarn '游戏 20 秒内没自己退出，强制结束它。'
    Get-HkcsGameProcesses -Paths $Paths | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
    $still = @(Get-HkcsGameProcesses -Paths $Paths)
    if ($still.Count -gt 0) {
        Stop-Hkcs ('游戏进程还在（PID ' + (($still | ForEach-Object { $_.Id }) -join ',') + '），无法覆盖 dll。')
    }
    Write-HkcsOk '游戏已强制退出。'
}

function Start-HkcsGame {
    <#
      启动空洞骑士。**默认必须走 Steam**，原因是本机实测出来的：
      2026-09-25 22:24 直接 Start-Process hollow_knight.exe 时，游戏起来后立刻通过
      SteamAPI_RestartAppIfNecessary 让 Steam 又拉起一份（Steam 的 console_log.txt 里写着
      "Game process added : AppID 367520 ... ProcID 6864"），两份抢 Unity 的单实例互斥锁，
      Steam 拉起的那份弹出 "Fatal error: Another instance is already running"。
      所以这里：① 已经有实例在跑就绝不重复启动；② 一律请 Steam 启动并等进程出现；
      ③ 起来后看窗口标题，是报错框就关掉重试一次。
      想强行直接启动 exe 就加 -LaunchExe（本机不推荐，见上）。
    #>
    param(
        $Paths,
        [switch]$LaunchExe,
        [int]$TimeoutSec = 90,
        [int]$ModLogWaitSec = 60,
        [switch]$DryRun
    )

    if (-not (Test-Path -LiteralPath $Paths.GameExe)) {
        Stop-Hkcs ('找不到游戏可执行文件：' + $Paths.GameExe + '（可用 -GameRoot 指定）')
    }

    # ① 已经有实例在跑 → 不要启动第二份
    $existing = @(Get-HkcsGameProcesses -Paths $Paths)
    if ($existing.Count -gt 0) {
        Write-HkcsWarn ('检测到空洞骑士已经在运行（PID ' + (($existing | ForEach-Object { $_.Id }) -join ',') + '），不再启动第二份。')
        Write-HkcsInfo '重复启动会弹 "Another instance is already running"；而且它是在第 4 步装好 dll 之后启动的，已经带着新 dll 了。'
        Show-HkcsProcessWindow -Ids @($existing | ForEach-Object { $_.Id })
        return
    }

    $useSteam = -not $LaunchExe
    if ($useSteam -and -not (Test-HkcsSteamAvailable -Paths $Paths)) {
        Write-HkcsWarn ('没找到 Steam 清单：' + $Paths.SteamManifest + '，改成直接启动 exe。')
        $useSteam = $false
    }

    if ($DryRun) {
        if ($useSteam) { Write-HkcsDryRun ('会请 Steam 启动（appid ' + $Paths.SteamAppId + '）并等游戏进程出现') }
        else           { Write-HkcsDryRun ('会直接启动：' + $Paths.GameExe) }
        return
    }

    for ($attempt = 1; $attempt -le 2; $attempt++) {
        if ($attempt -gt 1) { Start-Sleep -Seconds 5 }

        if ($useSteam) {
            if (Test-Path -LiteralPath $Paths.SteamExe) {
                Write-HkcsInfo ('请 Steam 启动：steam.exe -applaunch ' + $Paths.SteamAppId)
                Start-Process -FilePath $Paths.SteamExe -ArgumentList '-applaunch', $Paths.SteamAppId | Out-Null
            } else {
                Write-HkcsInfo ('请 Steam 启动：steam://rungameid/' + $Paths.SteamAppId)
                Start-Process ('steam://rungameid/' + $Paths.SteamAppId) | Out-Null
            }
        } else {
            Write-HkcsWarn ('直接启动 exe（Steam 在跑时会被 DRM 重新拉起，可能撞出单实例报错）：' + $Paths.GameExe)
            Start-Process -FilePath $Paths.GameExe -WorkingDirectory $Paths.GameRoot | Out-Null
        }

        Write-HkcsInfo ('等游戏进程出现（最多 ' + $TimeoutSec + ' 秒）...')
        $deadline = (Get-Date).AddSeconds($TimeoutSec)
        $proc  = $null
        $fatal = $false
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 1
            $procs = @(Get-HkcsGameProcesses -Paths $Paths)
            if ($procs.Count -eq 0) { continue }
            $proc = $procs[0]
            $title = ''
            try { $title = $proc.MainWindowTitle } catch { }
            if ($title -like '*Fatal*') { $fatal = $true; break }
            # 活过 8 秒、而且没有报错框 ⇒ 认为起来了
            if (((Get-Date) - $proc.StartTime).TotalSeconds -ge 8) { break }
        }

        if ($fatal) {
            Write-HkcsWarn '启动的这份弹了报错框（标题 "Fatal error"）—— 说明已经有一个空洞骑士在跑。'
            Write-HkcsInfo ('关掉它（PID ' + $proc.Id + '），等 5 秒再试一次 ...')
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 5
            continue
        }
        if (-not $proc) {
            Write-HkcsWarn ('等了 ' + $TimeoutSec + ' 秒也没看到 hollow_knight 进程。')
            if ($useSteam) {
                Write-HkcsWarn 'Steam 没把游戏拉起来。请：① 看一眼 Steam 窗口有没有弹着对话框（或 Steam 认为游戏还在运行，点一下"停止"）；'
                Write-HkcsWarn '② 必要时重启 Steam 客户端；③ 确认这个脚本是在你自己的终端 / 双击 .cmd 里跑的 ——'
                Write-HkcsWarn '   在受限沙箱里运行时 Steam 的进程间通信会被拦，-applaunch 根本传不到 Steam。'
                Write-HkcsWarn '（脚本加 -LaunchExe 会直接启动 exe，但那同样要靠 Steam DRM 重新拉起，且可能撞出单实例报错。）'
            }
            continue
        }

        Write-HkcsOk ('游戏已启动（PID ' + $proc.Id + '）。')
        Show-HkcsProcessWindow -Ids @($proc.Id)

        # 再确认一步：游戏启动时会重写 ModLog.txt，时间戳一变就说明它进了托管代码、mod 真的载入了
        $logBefore = Get-HkcsFileStamp -Path $Paths.ModLog
        Write-HkcsInfo ('等 mod 日志刷新（最多 ' + $ModLogWaitSec + ' 秒）...')
        $logDeadline = (Get-Date).AddSeconds($ModLogWaitSec)
        $logFresh = $false
        while ((Get-Date) -lt $logDeadline) {
            Start-Sleep -Seconds 2
            if ((Get-HkcsFileStamp -Path $Paths.ModLog) -ne $logBefore) { $logFresh = $true; break }
            if (@(Get-HkcsGameProcesses -Paths $Paths).Count -eq 0) { break }
        }
        if ($logFresh) {
            Write-HkcsOk 'mod 日志已刷新 ⇒ 游戏进了托管代码，mod 载入成功。'
        } else {
            Write-HkcsWarn '还没看到 mod 日志刷新（游戏可能还在加载，也可能又退出了）。'
        }
        Write-HkcsInfo ('游戏日志：' + $Paths.ModLog)
        return
    }

    Stop-Hkcs '两次都没能把空洞骑士启动起来（mod 本身已经装好了，只差启动游戏）。请手动启动一次看看报什么错；若是 Steam 卡住，重启 Steam 客户端再跑一次本脚本。'
}

# =====================================================================
#  八、预检
# =====================================================================

function Test-HkcsPrerequisites {
    param($Paths, [switch]$NeedBundleToolchain, [switch]$NeedGame, [switch]$NeedUnity)

    $bad = $false
    if ($NeedUnity) {
        if (-not (Test-Path -LiteralPath $Paths.UnityExe)) {
            Write-HkcsErr ('Unity 编辑器不存在：' + $Paths.UnityExe); $bad = $true
        }
        if (-not (Test-Path -LiteralPath $Paths.UnityAssets)) {
            Write-HkcsErr ('Unity 工程不存在：' + $Paths.UnityProject); $bad = $true
        }
    }
    if ($NeedBundleToolchain) {
        if (-not (Test-Path -LiteralPath $Paths.FinalModProj)) {
            Write-HkcsErr ('FinalMod 工程不存在：' + $Paths.FinalModProj); $bad = $true
        }
    }
    if ($NeedGame) {
        if (-not (Test-Path -LiteralPath $Paths.GameExe)) {
            Write-HkcsErr ('游戏本体不存在：' + $Paths.GameExe + '（可用 -GameRoot 指定）'); $bad = $true
        }
        if (-not (Test-Path -LiteralPath $Paths.ManagedDir)) {
            Write-HkcsErr ('游戏 Managed 目录不存在：' + $Paths.ManagedDir); $bad = $true
        }
    }
    if ($bad) { Stop-Hkcs '上面这些路径不对，先用参数覆盖再跑。' }
}

function Write-HkcsPathSummary {
    param($Paths)
    Write-HkcsInfo ('仓库根      ' + $Paths.RepoRoot)
    Write-HkcsInfo ('Unity 工程  ' + $Paths.UnityProject)
    Write-HkcsInfo ('AssetBundle ' + $Paths.BundleDir)
    Write-HkcsInfo ('FinalMod    ' + $Paths.FinalModProj)
    Write-HkcsInfo ('装 mod 到   ' + $Paths.ModsFolder)
    Write-HkcsInfo ('游戏        ' + $Paths.GameExe)
}
