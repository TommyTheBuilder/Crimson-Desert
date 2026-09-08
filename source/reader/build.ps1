param([string]$OutputPath = (Join-Path $PSScriptRoot 'PywelReader.exe'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Der 64-Bit-.NET-Framework-Compiler wurde nicht gefunden.' }
$target = [IO.Path]::GetFullPath($OutputPath)
$targetDirectory = [IO.Path]::GetDirectoryName($target)
[IO.Directory]::CreateDirectory($targetDirectory) | Out-Null
& $compiler /nologo /target:exe /platform:x64 /optimize+ /checked+ /warnaserror+ /langversion:5 /reference:System.Web.Extensions.dll /out:$target (Join-Path $PSScriptRoot 'GameReader.cs')
if ($LASTEXITCODE -ne 0) { throw 'Der externe Leser konnte nicht erstellt werden.' }
Write-Output $target
