# DesktopTidyLight 0.1.4

## 更新内容

- 修正分组窗口自动抬高时的处理：重新定位到桌面层级，避免保留已经错误的层级。新分组先绑定桌面再显示。
- 前台应用切换或 Explorer 重排时及时检查窗口层级；图标同步忙碌或拖动时也继续维护。普通资源管理器窗口不再被误当成桌面排除。
- 监听桌面图标变化，刷新、重排或位置变化后提前扫描，减少原图标重新出现在桌面上的等待时间。保留约 3 秒一次的空闲检查作为兜底。
- 扫描期间桌面再次发生变化时丢弃旧结果；沿用文件身份核对、恢复记录和失败回退机制。

## 验证与限制

- 本地 Release 构建通过，0 警告、0 错误；73 项自动化回归检查通过。
- 新增检查覆盖自动抬高请求、隐藏窗口的真实置顶请求、显示前定位以及桌面变化通知筛选。
- 验证未操作鼠标键盘、切换前台应用或修改真实桌面图标、用户布局。
- 尚未对用户使用的具体全屏应用进行现场验收。原图标仍采用屏幕外停放，不能保证 Explorer 重绘时完全没有瞬间闪现。

## 使用与升级

适用于 Windows x64，需要 .NET Framework 4.8。下载并完整解压 `DesktopTidyLight-0.1.4-win-x64.zip`，退出旧版后运行新版 `DesktopTidyLight.exe`。配置沿用 `%LOCALAPPDATA%\DesktopTidyLight` 中的现有数据，无需重新建立分组。

## English

- Re-anchor automatic window raises to the desktop and attach new groups before showing them.
- Repair window order promptly on foreground changes, independently of icon synchronization or dragging; ordinary Explorer windows are no longer excluded.
- React to desktop icon changes instead of waiting for the next periodic scan, and discard snapshots invalidated by later changes.
- Local Release builds and all 73 automated checks passed. Tests did not manipulate the user's desktop or foreground applications.
- Specific fullscreen applications still require real-world validation. Brief icon flashes during Explorer repainting may remain because native icons are still parked off-screen.

Exit the old version, extract the complete portable ZIP, and run the new executable. Existing configuration is preserved.
