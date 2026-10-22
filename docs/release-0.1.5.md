# DesktopTidyLight 0.1.5

本版修复桌面刷新时原图标短暂闪现，以及鼠标移入分组图标时变成拖动指针的问题。支持 Windows 11 x64，需要 .NET Framework 4.8。

- 修复图标区域继承分组移动、缩放指针的问题。
- 以统一显示替代屏幕外停放：运行时关闭原生图标绘制，未分组图标保持原坐标，由透明画布显示。空白桌面的系统菜单保持可用。
- 支持未分组图标的打开、拖动、跨显示器多选和框选、F5、Shell 项目右键菜单；拖入分组和移回桌面沿用身份核对与事务。
- 保存原始图标显示设置，正常退出、删除最后一个分组和 `--restore` 恢复。支持旧版异常退出后的布局迁移。
- 刷新期间暂时缺少但文件身份仍匹配的项目保留成员记录，等待重试。批量拖回桌面保留输入顺序。

## 验证

- Release 构建：0 警告、0 错误。
- 84 项默认回归检查通过；未修改真实桌面或用户布局。
- 原生显示检查：主程序停止和新版运行两种情况下，各连续执行三次 Shell 刷新，原生绘制保持关闭，35 个实际桌面项目仍可读取。
- 真实 Shell 集成检查：仅使用本次创建的临时文件、文件夹和快捷方式，验证批量加入、移动分组、拖回桌面、重新加入、退出、磁盘重载和重启；核对文件身份、无关图标位置及用户布局文件。临时项目已清理。
- 完整启动与正常退出核对：3 个分组、33 个成员保留；退出后恢复原生显示和原排列标志。

全程未使用 computer use 或模拟鼠标键盘。真实鼠标拖放、第三方 Shell 菜单扩展、显示器热插拔、Explorer 重启和长时间运行仍需交互验收。异常终止后需重新启动或运行 `--restore`，不会由独立后台进程自动恢复。

## 升级

先通过托盘退出旧版，完整解压新的 ZIP 后运行 `DesktopTidyLight.exe`。配置仍保存在 `%LOCALAPPDATA%\DesktopTidyLight`，无需重新创建分组。ZIP 附带 SHA256 校验文件。

## English

- Fix the move/resize cursor appearing over grouped icons.
- Replace off-screen native icon parking with managed rendering of grouped and ungrouped icons. Native icon drawing stays disabled during desktop refresh, and ungrouped icons retain their desktop positions.
- Restore the original icon visibility and positioning settings on normal exit or with `--restore`.
- Preserve group membership while Explorer temporarily rebuilds its item list, and preserve batch order when dragging icons back to the desktop.
- 84 regression checks and real Shell refresh/recovery checks passed. Physical mouse interactions, third-party context menus, monitor hot-plugging, Explorer restart, and long-running use still need hands-on verification.

Exit the old version before upgrading. After an unexpected shutdown, restart the app or run `DesktopTidyLight.exe --restore`.
