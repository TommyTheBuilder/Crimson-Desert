param([string]$OutputDirectory = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$runtime = Join-Path $output 'runtime'
New-Item -ItemType Directory -Force -Path $output,$runtime | Out-Null

# Generate the compiled UI source. The reviewed base source stays readable; the
# transform only switches to the live supervisor and adjusts Add-Item gating.
$generatedTrainer = & (Join-Path $PSScriptRoot 'prepare-trainer-source.ps1') -Source (Join-Path $PSScriptRoot 'TrainerApp.cs') -Output (Join-Path ([IO.Path]::GetTempPath()) 'PywelTrainer.TrainerApp.generated.cs')
if (-not $generatedTrainer -or -not (Test-Path -LiteralPath $generatedTrainer)) { throw 'Der Trainer-Quelltext konnte nicht vorbereitet werden.' }

# Build read-only reader, offline save editor, and live single-player bridge.
& (Join-Path $PSScriptRoot 'reader\build.ps1') -OutputPath (Join-Path $runtime 'PywelReader.exe')
if ($LASTEXITCODE -ne 0) { throw 'PywelReader.exe konnte nicht erstellt werden.' }
& (Join-Path $PSScriptRoot 'save-editor\build.ps1') -OutputPath (Join-Path $runtime 'PywelSaveEditor.exe') -LicenseOutput (Join-Path $output 'licenses\CrimsonSaveEditor-MPL-2.0.txt')
if ($LASTEXITCODE -ne 0) { throw 'PywelSaveEditor.exe konnte nicht erstellt werden.' }
& (Join-Path $PSScriptRoot 'live\build.ps1') -RuntimeDirectory $runtime -LicenseOutput (Join-Path $output 'licenses\PywelLive-Trinity-MIT.txt')
if ($LASTEXITCODE -ne 0) { throw 'Live-Spawner konnte nicht erstellt werden.' }

$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Der 64-Bit-.NET-Framework-Compiler wurde nicht gefunden.' }
$references = @('System.dll','System.Core.dll','System.Web.Extensions.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$resource = ('/resource:' + (Join-Path $PSScriptRoot 'MainWindow.xaml') + ',PywelTrainer.MainWindow.xaml')

& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output ('/out:' + (Join-Path $output 'PywelTrainer.exe')) ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')) $resource @references $generatedTrainer
if ($LASTEXITCODE -ne 0) { throw 'Trainer konnte nicht kompiliert werden.' }

# Catch startup regressions (missing x:Name, bad embedded XAML, constructor NRE)
# before a portable release is published.
$smoke = Join-Path ([IO.Path]::GetTempPath()) 'PywelTrainer.StartupSmoke.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /utf8output /main:StartupSmoke ('/out:' + $smoke) $resource @references $generatedTrainer (Join-Path $PSScriptRoot 'StartupSmoke.cs')
if ($LASTEXITCODE -ne 0) { throw 'Startup-Smoke-Test konnte nicht kompiliert werden.' }
& $smoke
if ($LASTEXITCODE -ne 0) { throw 'Trainer-Starttest ist fehlgeschlagen.' }

foreach ($required in @(
  (Join-Path $output 'PywelTrainer.exe'),
  (Join-Path $runtime 'PywelReader.exe'),
  (Join-Path $runtime 'PywelSaveEditor.exe'),
  (Join-Path $runtime 'PywelInjector.exe'),
  (Join-Path $runtime 'PywelLive.dll')
)) {
  if (-not (Test-Path -LiteralPath $required)) { throw "Release unvollständig: $required fehlt." }
}
Write-Output (Join-Path $output 'PywelTrainer.exe')