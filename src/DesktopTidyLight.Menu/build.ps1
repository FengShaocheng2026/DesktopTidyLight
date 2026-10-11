$ErrorActionPreference = 'Stop'
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$installation = & $vswhere -latest -products * -property installationPath
if (-not $installation) { throw '需要安装 Visual Studio C++ Build Tools。' }
$vcvars = Join-Path $installation 'VC\Auxiliary\Build\vcvars64.bat'
$output = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')) 'build\menu'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$source = Join-Path $PSScriptRoot 'DesktopMenuCommand.cpp'
$command = 'call "' + $vcvars + '" >nul && cl /nologo /std:c++17 /EHsc /W4 /LD /DUNICODE /D_UNICODE /Fe:"' + (Join-Path $output 'DesktopTidyLight.Menu.dll') + '" "' + $source + '" /link shlwapi.lib ole32.lib /EXPORT:DllGetClassObject,PRIVATE /EXPORT:DllCanUnloadNow,PRIVATE'
Push-Location $output
try {
    & cmd.exe /d /c $command
    if ($LASTEXITCODE -ne 0) { throw "菜单扩展编译失败：$LASTEXITCODE" }
} finally { Pop-Location }
