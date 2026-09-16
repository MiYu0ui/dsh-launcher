# DSH 启动器 - 构建与安装脚本
#
#   pwsh -File build.ps1              编译 + 安装 + 创建快捷方式
#   pwsh -File build.ps1 -Icons       顺便重新生成图标资源（需要 sharp）
#   pwsh -File build.ps1 -NoShortcuts 不动快捷方式，只编译安装
#   pwsh -File build.ps1 -SelfTest    编译后跑一遍无黑窗自检
#
# 产物：%LOCALAPPDATA%\Programs\DSH Launcher\DSH Launcher.exe

[CmdletBinding()]
param(
    [string]$InstallDir = '',        # 留空 = 交互式询问（不再静默采用默认值）
    [switch]$Icons,
    [switch]$NoShortcuts,
    [switch]$BuildOnly,      # 只编译到 build\，不安装（用于验证"发现新版本"）
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$srcDir = Join-Path $root 'src'
$assets = Join-Path $root 'assets'
$buildDir = Join-Path $root 'build'
$exeName = 'DSH Launcher.exe'

# 安装位置不再静默取默认值：没显式给 -InstallDir 就在这里问一次。
# -BuildOnly 只编译不安装，不需要问。
if (-not $BuildOnly -and [string]::IsNullOrWhiteSpace($InstallDir)) {
    $suggested = Join-Path $env:LOCALAPPDATA 'Programs\DSH Launcher'
    Write-Host ''
    Write-Host '选择安装位置' -ForegroundColor Magenta
    Write-Host "  直接回车 = 建议位置：$suggested" -ForegroundColor Gray
    Write-Host '  也可输入任意目录，例如 D:\Apps\DSH Launcher' -ForegroundColor Gray
    $answer = Read-Host '安装位置'
    $InstallDir = if ([string]::IsNullOrWhiteSpace($answer)) { $suggested } else { $answer.Trim().Trim('"') }
    Write-Host "  -> $InstallDir" -ForegroundColor Green
}

function Step($text) { Write-Host "==> $text" -ForegroundColor Magenta }
function Ok($text)   { Write-Host "    $text" -ForegroundColor Green }
function Warn($text) { Write-Host "    $text" -ForegroundColor Yellow }

# ---------------------------------------------------------------- 编译器
Step '查找 C# 编译器'
$csc = $null
foreach ($candidate in @(
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'))) {
    if (Test-Path $candidate) { $csc = $candidate; break }
}
if (-not $csc) { throw '找不到 .NET Framework 的 csc.exe，无法编译。' }
Ok $csc

# ---------------------------------------------------------------- 图标
$needIcons = $Icons -or -not (Test-Path (Join-Path $assets 'app.ico')) -or -not (Test-Path (Join-Path $assets 'logo.png'))
if ($needIcons) {
    Step '生成图标资源（官方鲸鱼 → 粉白图标）'
    $sharpModules = $null
    $npxCache = Join-Path $env:LOCALAPPDATA 'npm-cache\_npx'
    if (Test-Path $npxCache) {
        foreach ($dir in Get-ChildItem $npxCache -Directory -ErrorAction SilentlyContinue) {
            $nm = Join-Path $dir.FullName 'node_modules'
            if (Test-Path (Join-Path $nm 'sharp')) { $sharpModules = $nm; break }
        }
    }
    if (-not $sharpModules) { throw '找不到 sharp（用于把官方 SVG 渲染成 ICO），请先运行一次 DSH 或加 -Icons 前手动安装。' }
    $env:NODE_PATH = $sharpModules
    & node (Join-Path $root 'tools\make-icon.cjs')
    if ($LASTEXITCODE -ne 0) { throw '图标生成失败。' }
    Ok 'app.ico / logo-white.png / logo-pink.png'
} else {
    Step '图标资源已存在（需要重建请加 -Icons）'
}

# ---------------------------------------------------------------- 编译
Step '编译 DSH Launcher.exe'
if (-not (Test-Path $buildDir)) { New-Item -ItemType Directory -Path $buildDir | Out-Null }
$outExe = Join-Path $buildDir $exeName
if (Test-Path $outExe) { Remove-Item $outExe -Force }

# ---------------------------------------------------------------- 版本信息
# 生成 BuildInfo.Generated.cs：版本号来自 version.txt，同时写进 exe 的文件版本，
# 这样启动器就能读取"候选新版本"的版本号来做更新比对。
$versionFile = Join-Path $root 'version.txt'
$version = if (Test-Path $versionFile) { (Get-Content $versionFile -Raw -Encoding UTF8).Trim() } else { '1.1.0' }
if ($version -notmatch '^\d+(\.\d+){1,3}$') { throw "version.txt 里的版本号不合法：$version" }
$stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
# 可选：repo.txt 里写 owner/repo，构建时注入，更新源就切到 GitHub Releases；
# 不写（或为空）则沿用"读取本地 build\ 产物"的老逻辑。
$repoFile = Join-Path $root 'repo.txt'
$repoSlug = if (Test-Path $repoFile) { (Get-Content $repoFile -Raw -Encoding UTF8).Trim() } else { '' }
if ($repoSlug -and $repoSlug -notmatch '^[\w.-]+/[\w.-]+$') { throw "repo.txt 格式应为 owner/repo，当前：$repoSlug" }
$genPath = Join-Path $srcDir 'BuildInfo.Generated.cs'
$gen = @"
// 本文件由 build.ps1 自动生成，请勿手工编辑。
using System.Reflection;

[assembly: AssemblyVersion("$version.0")]
[assembly: AssemblyFileVersion("$version.0")]
[assembly: AssemblyTitle("DSH 启动器")]
[assembly: AssemblyProduct("DSH Launcher")]

namespace DshLauncher
{
    internal static class BuildInfo
    {
        public const string Version = "$version";
        public const string BuildStamp = "$stamp";
        public const string SourceDir = @"$root";
        public const string RepoSlug = "$repoSlug";
    }
}
"@
[IO.File]::WriteAllText($genPath, $gen, (New-Object Text.UTF8Encoding($true)))
Ok ("版本 v{0}  构建时间 {1}" -f $version, $stamp)

$sources = Get-ChildItem (Join-Path $srcDir '*.cs') | ForEach-Object { $_.FullName }
$cscArgs = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/warn:4'
    '/codepage:65001'
    ('/win32icon:' + (Join-Path $assets 'app.ico'))
    ('/win32manifest:' + (Join-Path $root 'app.manifest'))
    ('/resource:' + (Join-Path $assets 'app.ico') + ',app.ico')
    ('/resource:' + (Join-Path $assets 'logo.png') + ',logo.png')
    ('/resource:' + (Join-Path $assets 'rhinelab-mark.png') + ',mark.png')
    '/reference:System.dll'
    '/reference:System.Management.dll'
    '/reference:System.Core.dll'
    '/reference:System.Drawing.dll'
    '/reference:System.Windows.Forms.dll'
    ('/out:' + $outExe)
) + $sources

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败（退出码 $LASTEXITCODE）" }
Ok ("{0:N0} KB" -f ((Get-Item $outExe).Length / 1KB))

# ---------------------------------------------------------------- 安装
if ([string]::IsNullOrWhiteSpace($InstallDir)) { $installedExe = '' } else { $installedExe = Join-Path $InstallDir $exeName }
if ($BuildOnly) {
    Step '仅编译模式：跳过安装与快捷方式'
    Ok $outExe
} else {
Step "安装到 $InstallDir"
if (-not (Test-Path $InstallDir)) { New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null }
Get-Process -Name 'DSH Launcher' -ErrorAction SilentlyContinue | ForEach-Object {
    Warn "关闭正在运行的旧启动器（PID $($_.Id)）"
    $_.Kill()
}
Start-Sleep -Milliseconds 400
Copy-Item $outExe (Join-Path $InstallDir $exeName) -Force
Ok $installedExe

# 把安装位置写进配置，首次运行的选择框就不会再弹（按键就地合并，不动其它设置）
$cfgPath = Join-Path $env:APPDATA 'DSH Launcher\config.ini'
$cfgDir = Split-Path $cfgPath -Parent
if (-not (Test-Path $cfgDir)) { New-Item -ItemType Directory -Path $cfgDir -Force | Out-Null }
$lines = New-Object 'System.Collections.Generic.List[string]'
if (Test-Path $cfgPath) { foreach ($l in (Get-Content $cfgPath -Encoding UTF8)) { $lines.Add($l) } }

function Set-IniKey($list, $key, $value) {
    for ($i = 0; $i -lt $list.Count; $i++) {
        if ($list[$i] -match ('^\s*' + [regex]::Escape($key) + '\s*=')) { $list[$i] = "$key=$value"; return }
    }
    $list.Add("$key=$value")
}
Set-IniKey $lines 'installdir' $InstallDir
Set-IniKey $lines 'installasked' '1'
[IO.File]::WriteAllLines($cfgPath, $lines, (New-Object Text.UTF8Encoding($false)))
Ok "安装位置已写入配置：$cfgPath"
}

# ---------------------------------------------------------------- 快捷方式
if (-not $NoShortcuts -and -not $BuildOnly) {
    Step '创建快捷方式'
    $shell = New-Object -ComObject WScript.Shell

    function New-Lnk($path, $target, $arguments, $workdir, $desc) {
        $lnk = $shell.CreateShortcut($path)
        $lnk.TargetPath = $target
        $lnk.Arguments = $arguments
        $lnk.WorkingDirectory = $workdir
        $lnk.IconLocation = "$target,0"
        $lnk.Description = $desc
        $lnk.WindowStyle = 1
        $lnk.Save()
        Write-Host "    $path" -ForegroundColor Green
    }

    New-Lnk (Join-Path ([Environment]::GetFolderPath('Desktop')) 'DSH 启动器.lnk') $installedExe '' $InstallDir 'DeepSeek Harness 一键启动'
    New-Lnk (Join-Path ([Environment]::GetFolderPath('Programs')) 'DSH 启动器.lnk') $installedExe '' $InstallDir 'DeepSeek Harness 一键启动'

    # 清理开始菜单里指向已卸载 Electron 版的死快捷方式
    $deadFolder = Join-Path ([Environment]::GetFolderPath('Programs')) 'DSH Launcher'
    if (Test-Path $deadFolder) {
        $deadTarget = $shell.CreateShortcut((Join-Path $deadFolder 'DSH Launcher.lnk')).TargetPath
        if (-not (Test-Path $deadTarget)) {
            $backup = Join-Path $env:LOCALAPPDATA 'DSH Launcher\backup'
            if (-not (Test-Path $backup)) { New-Item -ItemType Directory -Path $backup | Out-Null }
            Move-Item $deadFolder (Join-Path $backup ("DSH Launcher 死快捷方式-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))) -Force
            Warn '已移除开始菜单里失效的旧「DSH Launcher」文件夹（已备份到 %LOCALAPPDATA%\DSH Launcher\backup）'
        }
    }

    # 旧的 PowerShell 开机自启项：备份并停用（它每次开机都会闪一个黑窗）
    $legacyAuto = Join-Path ([Environment]::GetFolderPath('Startup')) 'DeepSeekHarness-AutoStart.lnk'
    if (Test-Path $legacyAuto) {
        Move-Item $legacyAuto "$legacyAuto.bak" -Force
        Warn '已停用旧的 PowerShell 开机自启项（备份：DeepSeekHarness-AutoStart.lnk.bak）'
    }
    New-Lnk (Join-Path ([Environment]::GetFolderPath('Startup')) 'DSH 启动器（后台自启）.lnk') $installedExe '--autostart' $InstallDir 'DeepSeek Harness 开机静默启动'
    Ok '开机自启已切换到本启动器（如需关闭：右键托盘图标 → 退出，再到设置里取消勾选）'

    # 装完自校验：三个快捷方式的目标必须真的存在
    #（这正是"安装目录被删 → 快捷方式全指向空气"那个坑，装的时候就要发现）
    Step '校验快捷方式'
    $bad = 0
    foreach ($lnkPath in @(
            (Join-Path ([Environment]::GetFolderPath('Desktop')) 'DSH 启动器.lnk'),
            (Join-Path ([Environment]::GetFolderPath('Programs')) 'DSH 启动器.lnk'),
            (Join-Path ([Environment]::GetFolderPath('Startup')) 'DSH 启动器（后台自启）.lnk'))) {
        if (-not (Test-Path $lnkPath)) { Warn "缺失：$lnkPath"; $bad++; continue }
        $target = $shell.CreateShortcut($lnkPath).TargetPath
        if (Test-Path $target) { Ok "OK   $lnkPath" } else { Warn "目标不存在：$lnkPath -> $target"; $bad++ }
    }
    if ($bad -gt 0) { throw "有 $bad 个快捷方式没有指向真实文件，安装不完整。" }
}

# ---------------------------------------------------------------- 自检
if ($SelfTest) {
    Step '运行无黑窗自检'
    $report = Join-Path $buildDir 'selftest-report.txt'
    # -BuildOnly 时程序只编译到 build\，就从那里跑自检
    $selftestExe = if ($installedExe -and (Test-Path $installedExe)) { $installedExe } else { $outExe }
    # 注意：GUI 子系统程序要用 Start-Process -Wait 才会等它跑完并拿到退出码
    $proc = Start-Process -FilePath $selftestExe -ArgumentList @('--selftest', $report) -PassThru -Wait
    if (Test-Path $report) { Get-Content $report | ForEach-Object { Write-Host "    $_" } }
    if ($proc.ExitCode -ne 0) { throw "自检未通过（退出码 $($proc.ExitCode)）" }
    Ok '自检通过：全程无黑色命令行窗口'
}

Write-Host ''
Write-Host '完成。' -ForegroundColor Magenta
if ($installedExe) { Write-Host "  程序：$installedExe" -ForegroundColor Gray } else { Write-Host "  程序：$outExe（仅编译，未安装）" -ForegroundColor Gray }
Write-Host "  配置：$env:APPDATA\DSH Launcher\config.ini" -ForegroundColor Gray
Write-Host "  日志：$env:LOCALAPPDATA\DSH Launcher\logs\" -ForegroundColor Gray
