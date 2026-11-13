param(
    [ValidateSet('win-x64', 'win-x86', 'win-arm64')][string]$Runtime = 'win-x64',
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$releaseVersion = ([xml](Get-Content (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot "artifacts\Mizu-$releaseVersion-$Runtime" }
$appDirectory = Join-Path $OutputDirectory 'app'
$project = Join-Path $projectRoot 'src\Mizu.Widget\Mizu.Widget.csproj'
dotnet restore $project -r $Runtime --configfile (Join-Path $projectRoot 'NuGet.Config') --disable-parallel --nologo
if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
dotnet publish $project -c Release -r $Runtime --self-contained true --no-restore -p:PublishSingleFile=false -p:PublishTrimmed=false -o $appDirectory --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /reference:System.Windows.Forms.dll "/out:$OutputDirectory\Mizu.exe" "/win32icon:$projectRoot\src\Mizu.Widget\Assets\mizu.ico" (Join-Path $projectRoot 'src\Mizu.Launcher\Launcher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed' }
$infoDirectory = Join-Path $OutputDirectory '说明与许可'
New-Item -ItemType Directory $infoDirectory -Force | Out-Null
foreach ($name in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md','licenses','docs')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $infoDirectory -Recurse -Force
}
@'
Mizu 便携版

双击 Mizu.exe 启动。app 文件夹存放程序与依赖，请保留整个目录。
说明、第三方许可和验证范围见“说明与许可”文件夹。
更新前先从托盘退出旧版，再解压新版到新文件夹运行。
设置和待办位于 %LOCALAPPDATA%\Mizu，会自动保留。
设置 → 外观可选择雾白、墨绿、深蓝、浅粉、浅紫。
外设区域滚轮到达第一页/末页即停止，不循环跳转。
根目录启动器使用 Windows 10/11 内置的 .NET Framework 4.x。
app 内主程序自带 .NET 10 运行时，无需另装 Python 或 .NET 10。
'@ | Set-Content (Join-Path $OutputDirectory '使用说明.txt') -Encoding utf8
Write-Output "Published $Runtime to $OutputDirectory"
