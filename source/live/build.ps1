param(
  [string]$RuntimeDirectory = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'runtime'),
  [string]$LicenseOutput = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'licenses\PywelLive-Trinity-MIT.txt')
)
$ErrorActionPreference='Stop'
$runtime=[IO.Path]::GetFullPath($RuntimeDirectory)
[IO.Directory]::CreateDirectory($runtime) | Out-Null
$commit='70c9a00dd6e10b2081d706a837756844c11f5c2b'
$root=Join-Path ([IO.Path]::GetTempPath()) 'pywel-trinity-live'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
New-Item -ItemType Directory -Force -Path $root | Out-Null
$repo=Join-Path $root 'repo'
& git clone --filter=blob:none --no-checkout https://github.com/XeTrinityz/Trinity.git $repo
if($LASTEXITCODE -ne 0){throw 'Trinity-Quelltext konnte nicht geladen werden.'}
Push-Location $repo
try{
  & git fetch --depth 1 origin $commit
  if($LASTEXITCODE -ne 0){throw 'Der gepinnte Trinity-Commit konnte nicht geladen werden.'}
  & git checkout --detach $commit
  if($LASTEXITCODE -ne 0){throw 'Trinity-Commit konnte nicht ausgecheckt werden.'}

  Copy-Item (Join-Path $PSScriptRoot 'bridge.cpp') (Join-Path $repo 'src\pywel_bridge.cpp') -Force

  $cmake=Get-Content -LiteralPath (Join-Path $repo 'CMakeLists.txt') -Raw -Encoding UTF8
  $needle='    src/game/pak.cpp'
  if(-not $cmake.Contains($needle)){throw 'Trinity CMake ist unerwartet geändert.'}
  $cmake=$cmake.Replace($needle,$needle+"`r`n    src/pywel_bridge.cpp")
  [IO.File]::WriteAllText((Join-Path $repo 'CMakeLists.txt'),$cmake,(New-Object Text.UTF8Encoding($false)))

  $modPath=Join-Path $repo 'src\core\mod.cpp'
  $mod=Get-Content -LiteralPath $modPath -Raw -Encoding UTF8
  $include='#include "../game/friendly.h"'
  if(-not $mod.Contains($include)){throw 'Trinity mod.cpp include anchor fehlt.'}
  $mod=$mod.Replace($include,$include+"`r`nnamespace trinity::bridge { bool Start(); void Stop(); }")

  $dx=@'
        if (!hooks::InstallDX12Hooks())
        {
            LOG("Failed to install DX12 hooks.");
            MH_Uninitialize();
            return;
        }
'@
  if(-not $mod.Contains($dx)){throw 'Trinity DX12-Initblock wurde nicht gefunden.'}
  $mod=$mod.Replace($dx,"        // Pywel live bridge runs headless; no overlay/DX12 hook is required.`r`n")
  $anchor='        game::Friendly::Install();  // Trust Multiplier (gift/feed/tame)'
  if(-not $mod.Contains($anchor)){throw 'Trinity Install-Anker fehlt.'}
  $mod=$mod.Replace($anchor,$anchor+"`r`n        bridge::Start();              // local named-pipe API for Pywel Trainer")
  $shutdown='        game::Player::Remove();'
  if(-not $mod.Contains($shutdown)){throw 'Trinity Shutdown-Anker fehlt.'}
  $mod=$mod.Replace($shutdown,"        bridge::Stop();`r`n"+$shutdown)
  $mod=$mod.Replace('        hooks::RemoveDX12Hooks();'+"`r`n",'')
  $mod=$mod.Replace('        hooks::RemoveDX12Hooks();'+"`n",'')
  [IO.File]::WriteAllText($modPath,$mod,(New-Object Text.UTF8Encoding($false)))

  $build=Join-Path $root 'build'
  & cmake -S $repo -B $build -A x64
  if($LASTEXITCODE -ne 0){throw 'PywelLive CMake-Konfiguration fehlgeschlagen.'}
  & cmake --build $build --config Release --target Trinity -- /m
  if($LASTEXITCODE -ne 0){throw 'PywelLive.dll konnte nicht kompiliert werden.'}
  $asi=Join-Path $build 'Release\Trinity.asi'
  if(-not (Test-Path -LiteralPath $asi)){throw 'Trinity.asi wurde nach dem Build nicht gefunden.'}
  Copy-Item $asi (Join-Path $runtime 'PywelLive.dll') -Force
  [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($LicenseOutput))) | Out-Null
  Copy-Item (Join-Path $repo 'LICENSE') ([IO.Path]::GetFullPath($LicenseOutput)) -Force
}
finally{Pop-Location}

$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc=Join-Path $framework 'csc.exe'
if(-not (Test-Path -LiteralPath $csc)){throw 'Der 64-Bit-.NET-Framework-Compiler wurde nicht gefunden.'}
& $csc /nologo /target:exe /platform:x64 /optimize+ /utf8output ('/out:'+(Join-Path $runtime 'PywelInjector.exe')) (Join-Path $PSScriptRoot 'Injector.cs')
if($LASTEXITCODE -ne 0){throw 'PywelInjector.exe konnte nicht kompiliert werden.'}
foreach($file in @((Join-Path $runtime 'PywelLive.dll'),(Join-Path $runtime 'PywelInjector.exe'))){if(-not(Test-Path -LiteralPath $file)){throw "Live-Release unvollständig: $file"}}
Write-Output (Join-Path $runtime 'PywelLive.dll')