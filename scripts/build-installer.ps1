param(
    [ValidateSet('win-x64','win-x86','win-arm64')][string]$Runtime = 'win-x64',
    [string]$Compiler = 'ISCC.exe',
    [string]$PackageDirectory,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
if (-not $PackageDirectory) { $PackageDirectory = Join-Path $projectRoot "artifacts\Mizu-$version-$Runtime" }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'artifacts\installers' }
if (-not (Test-Path (Join-Path $PackageDirectory 'app\Mizu.exe'))) { throw 'Publish the portable package first with scripts/publish.ps1.' }
$PackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
& $Compiler /Q "/DRuntime=$Runtime" "/DAppVersion=$version" "/DPackageDir=$PackageDirectory" "/DOutputDirPath=$OutputDirectory" (Join-Path $PSScriptRoot 'installer.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
