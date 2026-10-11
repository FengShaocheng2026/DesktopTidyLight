$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$sdk = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe'
if (-not (Test-Path $sdk)) { throw '需要 Windows SDK MakeAppx.exe。' }
$output = Join-Path $root 'build\DesktopTidyLight.identity.msix'
$stage = Join-Path $root 'build\package-input'
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'AppxManifest.xml') (Join-Path $stage 'AppxManifest.xml') -Force
& $sdk pack /o /d $stage /nv /p $output
if ($LASTEXITCODE -ne 0) { throw "稀疏包清单打包失败：$LASTEXITCODE" }
Write-Output $output
