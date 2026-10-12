# Desktop Tidy Light

Windows 11 桌面分组工具。把已经显示在桌面上的文件、文件夹和快捷方式拖入分组，整理桌面而不移动或复制文件。当前为测试版，支持 Windows x64，需要 .NET Framework 4.8。

## 使用

获取便携包后完整解压，运行 `DesktopTidyLight.exe`。在桌面右键选择“新建分组”（部分 Windows 11 版本需先点“显示更多选项”），然后将桌面图标拖入分组。

- 支持批量拖入、分组间转移、组内排序和拖回桌面；Ctrl / Shift 多选，Ctrl+A 全选。
- 可缩放、移动、重叠和锁定分组；支持四种外观、背景透明度、标题自动隐藏和登录启动。
- 自动保存分组与成员顺序。正常退出时恢复普通桌面，再次启动后恢复分组。

运行时，原始桌面图标会临时停放到屏幕外，分组中的图标由程序绘制。正常退出时会恢复图标位置及 Windows 的自动排列、对齐网格设置。请通过托盘菜单退出程序；异常结束后可重新启动，或运行 `DesktopTidyLight.exe --restore` 恢复普通桌面。

配置保存在 `%LOCALAPPDATA%\DesktopTidyLight`，不包含在便携包中。升级前先退出旧版，再替换程序文件。卸载前运行 `DesktopTidyLight.exe --unregister`，清除本程序的桌面菜单和自启动项，然后删除程序目录；个人配置可按需保留。

## 命令

| 参数 | 用途 |
| --- | --- |
| `--settings` | 打开设置 |
| `--new-group` | 新建分组 |
| `--restore` | 恢复普通桌面并退出，保留分组记录 |
| `--unregister` | 恢复桌面并移除本程序菜单和自启动项 |

## 构建与检查

需要 Windows 和支持 .NET Framework 4.8 目标的 .NET SDK。引用程序集由 NuGet 恢复。

```powershell
dotnet build src/DesktopTidyLight/DesktopTidyLight.csproj -c Release
powershell -NoProfile -File tests/run-checks.ps1
powershell -NoProfile -File packaging/Package.ps1
```

默认检查不会修改真实桌面；打包脚本在 `dist/` 生成便携 ZIP 和 SHA256 文件。实机验收应从 Windows 桌面启动程序，避免开发终端的隔离环境影响配置与注册表读取。排查方法见[配置路径说明](docs/recovery-2026-10-12.md)。

## 项目文档

- [架构与恢复机制](docs/architecture.md)
- [当前版本验证范围](docs/release-0.1.2.md)
- [性能检查](docs/performance-2026-10-12.md)

## 已知限制

仅整理已出现在 Windows 桌面视图中的项目。与 Explorer 桌面窗口的集成依赖 Windows 行为；物理显示器热插拔、远程桌面、系统升级和长时间运行仍需要更多实机验证。部分 Shell 图标可能显示为通用图标，可在分组菜单中手动刷新。

## 许可证

本项目采用 [MIT 许可证](LICENSE)。
