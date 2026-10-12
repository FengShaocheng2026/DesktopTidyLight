$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    dotnet build src/DesktopTidyLight/DesktopTidyLight.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw '主程序构建失败。' }
    dotnet build tests/MvpChecks.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw '检查程序构建失败。' }
    & .\tests\bin\Release\net48\MvpChecks.exe
    if ($LASTEXITCODE -ne 0) { throw '后台回归检查失败。' }
}
finally { Pop-Location }
