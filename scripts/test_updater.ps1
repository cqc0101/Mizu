$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$folder = Join-Path $root ('artifacts/updater-integration-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$refs = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll')
foreach ($version in @('0.7.11','0.7.12')) {
    $info = Join-Path $folder ("version-$version.cs")
    [System.IO.File]::WriteAllText($info,('[assembly:System.Reflection.AssemblyVersion("' + $version + '.0")][assembly:System.Reflection.AssemblyFileVersion("' + $version + '.0")]'))
    & $compiler /nologo /target:winexe /platform:x64 /codepage:65001 /reference:System.Web.Extensions.dll "/out:$folder/fixture-$version.exe" $info (Join-Path $root 'tests/Mizu.Updater.Checks/FixtureApp.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed' }
}
$iscc = Join-Path (Split-Path $root -Parent) 'tooling/inno-portable/tools/ISCC.exe'
if (-not (Test-Path -LiteralPath $iscc)) { $iscc = (Get-Command ISCC.exe -ErrorAction Stop).Source }
& $iscc /Q "/DFixtureExe=$folder/fixture-0.7.12.exe" "/DOutputFolder=$folder" (Join-Path $root 'tests/Mizu.Updater.Checks/FixtureInstaller.iss')
if ($LASTEXITCODE -ne 0) { throw 'Isolated installer fixture build failed' }
& (Join-Path $PSScriptRoot 'build_updater.ps1') -OutputDirectory $folder
& $compiler /nologo /target:winexe /platform:anycpu /codepage:65001 /main:HelperEntry @refs "/out:$folder/test-helper.exe" (Join-Path $root 'src/Mizu.Updater/PackageArchive.cs') (Join-Path $root 'src/Mizu.Updater/InstallerJob.cs') (Join-Path $root 'src/Mizu.Updater/Updater.cs') (Join-Path $root 'tests/Mizu.Updater.Checks/HelperEntry.cs')
if ($LASTEXITCODE -ne 0) { throw 'Isolated protocol helper build failed' }
& $compiler /nologo /target:winexe /platform:anycpu /codepage:65001 /main:JobEntry @refs "/out:$folder/job-entry.exe" (Join-Path $root 'src/Mizu.Updater/PackageArchive.cs') (Join-Path $root 'src/Mizu.Updater/InstallerJob.cs') (Join-Path $root 'src/Mizu.Updater/Updater.cs') (Join-Path $root 'tests/Mizu.Updater.Checks/HelperEntry.cs')
if ($LASTEXITCODE -ne 0) { throw 'Job owner fixture build failed' }
& $compiler /nologo /target:winexe /platform:anycpu "/out:$folder/job-tree.exe" (Join-Path $root 'tests/Mizu.Updater.Checks/JobTreeFixture.cs')
if ($LASTEXITCODE -ne 0) { throw 'Job tree fixture build failed' }
& $compiler /nologo /target:exe /platform:x64 /codepage:65001 /main:UpdaterChecks @refs "/out:$folder/checks.exe" (Join-Path $root 'src/Mizu.Updater/PackageArchive.cs') (Join-Path $root 'src/Mizu.Updater/InstallerJob.cs') (Join-Path $root 'src/Mizu.Updater/Updater.cs') (Join-Path $root 'tests/Mizu.Updater.Checks/Checks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Updater checks build failed' }
& "$folder/checks.exe" "$folder/fixture-0.7.11.exe" "$folder/fixture-0.7.12.exe" "$folder/Mizu.Updater.exe" $folder "$folder/fixture-setup.exe" "$folder/test-helper.exe" "$folder/job-entry.exe" "$folder/job-tree.exe"
if ($LASTEXITCODE -ne 0) { throw "Updater transaction checks failed; retained $folder" }
