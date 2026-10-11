$ErrorActionPreference = 'Stop'
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'Visual Studio C++ Build Tools not found.' }

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$output = Join-Path $repo 'build\DesktopProbe'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$source = Join-Path $PSScriptRoot 'DesktopProbe.cpp'
$dev = Join-Path $vs 'Common7\Tools\VsDevCmd.bat'
Push-Location $output
try {
    $command = 'call "' + $dev + '" -arch=x64 -host_arch=x64 >nul && cl /nologo /std:c++17 /EHsc /W4 /DUNICODE /D_UNICODE "' + $source + '" /Fe:DesktopProbe.exe ole32.lib oleaut32.lib shell32.lib shlwapi.lib'
    & cmd.exe /d /s /c $command
    if ($LASTEXITCODE -ne 0) { throw "C++ build failed: $LASTEXITCODE" }
}
finally {
    Pop-Location
}
