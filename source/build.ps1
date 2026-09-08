param([string]$OutputDirectory = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$references = @('System.dll','System.Core.dll','System.Web.Extensions.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output ('/out:' + (Join-Path $OutputDirectory 'PywelTrainer.exe')) ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')) ('/resource:' + (Join-Path $PSScriptRoot 'MainWindow.xaml') + ',PywelTrainer.MainWindow.xaml') @references (Join-Path $PSScriptRoot 'TrainerApp.cs')
if ($LASTEXITCODE -ne 0) { throw 'Trainer konnte nicht kompiliert werden.' }
Write-Output (Join-Path $OutputDirectory 'PywelTrainer.exe')
