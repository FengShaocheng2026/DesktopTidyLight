# Desktop Tidy Light

轻量 Windows 桌面分组工具。**MVP 0.1.2**，适用于 Windows 11 x64 / .NET Framework 4.8。

## 使用

解压发布包后运行 `DesktopTidyLight.exe`。桌面右键“新建分组”，将桌面上的文件、文件夹或快捷方式拖入即可收纳。部分 Windows 版本可能需要展开“显示更多选项”。

- 支持整批拖入、分组间转移、组内排序，以及拖回普通桌面；不移动或复制实际文件。
- Ctrl / Shift 多选，Ctrl+A 全选，双击或 Enter 打开。长名称悬停查看，内容过多时组内滚动。
- 分组支持缩放、吸附、重叠和锁定；Alt 临时取消吸附。分组限制在屏幕工作区内。
- 托盘右键仅有“设置”和“退出”。设置提供四种外观、背景透明度、标题自动隐藏和登录启动。
- 自动保存分组、成员顺序和显示器位置。退出恢复普通桌面，再启动恢复分组。

运行时，组内图标由应用绘制，原始图标停放在整个虚拟桌面之外。程序暂时关闭 Windows 自动排列和对齐网格，正常退出时恢复原设置。

配置保存在 `%LOCALAPPDATA%\DesktopTidyLight`。升级时先退出旧版，再替换程序文件。不要将个人配置放入发布包。

## 命令

| 参数 | 用途 |
| --- | --- |
| `--settings` | 打开设置 |
| `--new-group` | 新建分组 |
| `--restore` | 恢复普通桌面并退出，保留分组记录 |
| `--unregister` | 恢复桌面、退出，移除本程序菜单和自启动项 |

异常结束后可重新启动或使用 `--restore` 恢复。卸载前运行 `--unregister`，然后删除程序目录；个人配置按需保留。

## 开发与打包

开发需要 .NET SDK（支持构建 net48）；引用程序集由 NuGet 恢复。

```powershell
powershell -NoProfile -File tests/run-checks.ps1
powershell -NoProfile -File packaging/Package.ps1
```

测试默认不操作真实桌面。打包版本来自主项目文件，在 `dist/` 输出便携 ZIP 和 SHA256；只包含程序、运行配置和使用说明。

- [当前架构](docs/architecture.md)
- [MVP 封版与验证记录](docs/release-0.1.2.md)
- [性能记录](docs/performance-2026-10-12.md)

## 已知边界

仅收纳已出现在 Windows 桌面视图中的项目；不从其他目录复制文件。Explorer 桌面窗口集成依赖 Windows 行为，系统升级、远程桌面和硬件热插拔仍需更多实机覆盖。Shell 有时会返回通用图标，可在分组菜单中“刷新图标”。

当前为 MVP，不代表所有显示器组合和长期运行场景均已验证。仓库尚未选择开源许可证。
