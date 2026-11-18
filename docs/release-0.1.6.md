# 0.1.6

修复显示变化处理中的循环重建路径，减少切换分辨率或缩放后的闪烁。

- 显示环境通知只唤醒检查；实际显示器身份、工作区或 DPI 改变时才调整布局，避免窗口自身的 DPI 通知反复触发重置。
- 调整显示布局时复用现有窗口，只有 Explorer 重启或窗口脱离桌面时才重建。
- 保留布局文件格式及首选显示器记录。

验证：Release 构建成功，85 项后台回归检查通过，包括显示器重排、分辨率变化、仅 DPI 变化和断开重连的检测。不修改真实桌面图标或用户布局。

限制：原版本的持续闪烁未在隐藏窗口检查中直接复现；本次修复针对源码中确认的循环重建路径。尚未执行真实分辨率切换后的启动和运行验收。

升级：先退出旧版，完整解压新的 ZIP 后运行 `DesktopTidyLight.exe`。现有分组配置继续保留，无需重新创建。ZIP 附带 SHA256 校验文件。

## English

- Prevent DPI notifications raised during window creation or movement from repeatedly resetting the desktop layout. Check the actual monitor identity, work area and DPI instead.
- Reuse existing group and desktop windows when updating the display layout; rebuild them only after Explorer restarts or a window loses its desktop attachment.
- Release builds and 85 background regression checks passed. The original continuous flicker was not directly reproduced with hidden windows; startup and runtime verification after a physical resolution change remain pending.

Exit the old version before upgrading, then extract the complete ZIP and run `DesktopTidyLight.exe`. Existing groups are preserved. A SHA256 checksum is included.
