param([string]$Version)
$ErrorActionPreference = 'Stop'
if ($Version -and $Version -notmatch '^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?$') { throw 'Invalid version' }
$repo = Split-Path $PSScriptRoot -Parent
if (!$Version) { $Version = ([xml](Get-Content -LiteralPath (Join-Path $repo 'src/DesktopTidyLight/DesktopTidyLight.csproj') -Raw)).Project.PropertyGroup.Version }
$output = Join-Path $repo "dist/DesktopTidyLight-$Version-win-x64"
New-Item -ItemType Directory -Force -Path $output | Out-Null
& dotnet build (Join-Path $repo 'src/DesktopTidyLight/DesktopTidyLight.csproj') -c Release -o (Join-Path $repo 'build/package-current') "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
foreach ($name in @('DesktopTidyLight.exe', 'DesktopTidyLight.exe.config')) {
    Copy-Item -LiteralPath (Join-Path $repo "build/package-current/$name") -Destination $output -Force
}
[IO.File]::WriteAllText((Join-Path $output '使用说明.txt'), [IO.File]::ReadAllText((Join-Path $PSScriptRoot '使用说明.txt')).Replace('{VERSION}', $Version), (New-Object Text.UTF8Encoding($true)))
$archive = "$output.zip"
$files = @('DesktopTidyLight.exe', 'DesktopTidyLight.exe.config', '使用说明.txt') | ForEach-Object { Join-Path $output $_ }
Compress-Archive -LiteralPath $files -DestinationPath $archive -Force
Get-FileHash -LiteralPath $archive -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $([IO.Path]::GetFileName($archive))" } | Set-Content -LiteralPath "$archive.sha256" -Encoding ascii
Write-Output $archive
