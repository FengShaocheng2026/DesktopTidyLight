# Windows 11 桌面右键菜单

当前状态：代码和稀疏包清单已离线编译，尚未签名或注册，未在桌面实际点选。Windows 11 主菜单入口仍未通过验收。

主程序保持 C#。`src/DesktopTidyLight.Menu/DesktopMenuCommand.cpp` 是一个很小的原生 `IExplorerCommand` 扩展，只返回菜单文字，并在点击后启动同目录的 `DesktopTidyLight.exe --new-group`。它不在 Explorer 的菜单构建过程启动 .NET。`packaging/AppxManifest.xml` 把扩展声明为 `Directory\Background`，使用稀疏包给未打包的主程序提供包身份。这依据微软的[菜单扩展说明](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer)和[稀疏包制作说明](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps)。

离线构建：

```powershell
dotnet build src/DesktopTidyLight/DesktopTidyLight.csproj -c Release
./src/DesktopTidyLight.Menu/build.ps1
./packaging/build.ps1
```

生成的 DLL 在 `build/menu/`；生成的未签名包在 `build/`。最终安装目录需要放主程序、菜单 DLL、清单所引用的图像，以及签名后的包。清单中的 `Publisher` 目前是开发占位值，发布时须与签名证书一致。稀疏包需要可信证书签名后用 `Add-AppxPackage -Path <包路径> -ExternalLocation <安装目录>` 注册；卸载需移除同一包身份。构建脚本只编译和打包，不签名、不安装、不更改证书信任，也不重启 Explorer。
主程序的 `app.manifest` 中 `publisher`、`packageName` 和 `applicationId` 也必须与稀疏包同步修改，且要随主程序一起重新编译。

验收前要解决两件事：

1. `Directory\Background` 包含文件夹背景；当前 `GetState` 无法区分真正的桌面。需要实机检查 Windows 11 对桌面空白处是否调用该扩展，以及是否误出现在普通文件夹中，再决定过滤或改入口。
2. 当前 `--new-group` 以主程序读取的鼠标位置创建格子。需要实机验证菜单关闭后位置仍符合用户点击位置。

旧式 `DesktopBackground\Shell` 入口暂时保留为原型的备用入口，Windows 11 主菜单显示位置不作保证。不要在上述验收完成前把此包当作可发布安装包。
