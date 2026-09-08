param([string]$OutputDirectory = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$runtime = Join-Path $output 'runtime'
New-Item -ItemType Directory -Force -Path $output,$runtime | Out-Null

# Build both external helpers first. A release is considered incomplete without
# the read-only live reader and the offline save editor used for Add Item.
& (Join-Path $PSScriptRoot 'reader\build.ps1') -OutputPath (Join-Path $runtime 'PywelReader.exe')
if ($LASTEXITCODE -ne 0) { throw 'PywelReader.exe konnte nicht erstellt werden.' }
& (Join-Path $PSScriptRoot 'save-editor\build.ps1') -OutputPath (Join-Path $runtime 'PywelSaveEditor.exe') -LicenseOutput (Join-Path $output 'licenses\CrimsonSaveEditor-MPL-2.0.txt')
if ($LASTEXITCODE -ne 0) { throw 'PywelSaveEditor.exe konnte nicht erstellt werden.' }

$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Der 64-Bit-.NET-Framework-Compiler wurde nicht gefunden.' }
$references = @('System.dll','System.Core.dll','System.Web.Extensions.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output ('/out:' + (Join-Path $output 'PywelTrainer.exe')) ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')) ('/resource:' + (Join-Path $PSScriptRoot 'MainWindow.xaml') + ',PywelTrainer.MainWindow.xaml') @references (Join-Path $PSScriptRoot 'TrainerApp.cs')
if ($LASTEXITCODE -ne 0) { throw 'Trainer konnte nicht kompiliert werden.' }

foreach ($required in @(
  (Join-Path $output 'PywelTrainer.exe'),
  (Join-Path $runtime 'PywelReader.exe'),
  (Join-Path $runtime 'PywelSaveEditor.exe')
)) {
  if (-not (Test-Path -LiteralPath $required)) { throw "Release unvollständig: $required fehlt." }
}
Write-Output (Join-Path $output 'PywelTrainer.exe')
