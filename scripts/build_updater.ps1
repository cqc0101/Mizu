param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$compilerArgs = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/codepage:65001',
    '/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll',
    '/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll',
    "/win32icon:$root/src/Mizu.Widget/Assets/mizu.ico","/out:$OutputDirectory/Mizu.Updater.exe",
    (Join-Path $root 'src/Mizu.Updater/PackageArchive.cs'),(Join-Path $root 'src/Mizu.Updater/InstallerJob.cs'),(Join-Path $root 'src/Mizu.Updater/Updater.cs'))
& (Join-Path $framework 'csc.exe') @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Updater build failed' }
