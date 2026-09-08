param(
  [string]$OutputPath = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'runtime\PywelSaveEditor.exe'),
  [string]$LicenseOutput = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'licenses\CrimsonSaveEditor-MPL-2.0.txt')
)
$ErrorActionPreference = 'Stop'

$repository = 'https://github.com/NattKh/CRIMSON-DESERT-SAVE-EDITOR-AND-GAME-MODS.git'
$commit = '96e7f78fcb00d6cb5615171a7f359f0c2de5b114'
$git = (Get-Command git.exe -ErrorAction SilentlyContinue)
if (-not $git) { $git = Get-Command git -ErrorAction SilentlyContinue }
$cmake = (Get-Command cmake.exe -ErrorAction SilentlyContinue)
if (-not $cmake) { $cmake = Get-Command cmake -ErrorAction SilentlyContinue }
if (-not $git) { throw 'Git wurde nicht gefunden. Git for Windows wird zum Bauen des Save-Editor-Helfers benötigt.' }
if (-not $cmake) { throw 'CMake wurde nicht gefunden. Installiere CMake oder die Visual-Studio-C++-Buildtools mit CMake-Unterstützung.' }

$cacheRoot = Join-Path ([IO.Path]::GetTempPath()) 'pywel-save-editor-src'
$sourceRoot = Join-Path $cacheRoot 'repo'
$buildRoot = Join-Path $cacheRoot 'build'
New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null

function Invoke-Checked([string]$File, [string[]]$Arguments) {
  & $File @Arguments
  if ($LASTEXITCODE -ne 0) { throw "Befehl fehlgeschlagen ($LASTEXITCODE): $File $($Arguments -join ' ')" }
}

if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot '.git'))) {
  if (Test-Path -LiteralPath $sourceRoot) { Remove-Item -LiteralPath $sourceRoot -Recurse -Force }
  Invoke-Checked $git.Source @('clone','--filter=blob:none','--no-checkout','--sparse',$repository,$sourceRoot)
}
Invoke-Checked $git.Source @('-C',$sourceRoot,'sparse-checkout','set','CrimsonSaveEditorCpp','LICENSE.txt')
Invoke-Checked $git.Source @('-C',$sourceRoot,'fetch','--depth','1','origin',$commit)
Invoke-Checked $git.Source @('-C',$sourceRoot,'checkout','--detach','FETCH_HEAD')

if (Test-Path -LiteralPath $buildRoot) { Remove-Item -LiteralPath $buildRoot -Recurse -Force }
$project = Join-Path $sourceRoot 'CrimsonSaveEditorCpp'
Invoke-Checked $cmake.Source @(
  '-S',$project,
  '-B',$buildRoot,
  '-A','x64',
  '-DBUILD_GUI=OFF',
  '-DCMAKE_POLICY_DEFAULT_CMP0091=NEW',
  '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded'
)
Invoke-Checked $cmake.Source @('--build',$buildRoot,'--config','Release','--target','parc_engine_cli','--parallel')

$built = Join-Path $buildRoot 'Release\parc_engine_cli.exe'
if (-not (Test-Path -LiteralPath $built)) {
  $built = Get-ChildItem -LiteralPath $buildRoot -Filter 'parc_engine_cli.exe' -File -Recurse | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $built -or -not (Test-Path -LiteralPath $built)) { throw 'parc_engine_cli.exe wurde nicht erzeugt.' }

$target = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($target)) | Out-Null
Copy-Item -LiteralPath $built -Destination $target -Force
if ((Get-Item -LiteralPath $target).Length -lt 1024) { throw 'PywelSaveEditor.exe ist unerwartet klein.' }

$license = Join-Path $sourceRoot 'LICENSE.txt'
if (-not (Test-Path -LiteralPath $license)) { throw 'MPL-2.0-Lizenzdatei des Save-Editor-Helfers fehlt.' }
$licenseTarget = [IO.Path]::GetFullPath($LicenseOutput)
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($licenseTarget)) | Out-Null
Copy-Item -LiteralPath $license -Destination $licenseTarget -Force

Write-Output $target
