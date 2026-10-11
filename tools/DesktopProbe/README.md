# 桌面 Shell 只读探测工具（P0）

用途：确认 Windows 11 的 Explorer 桌面视图能否通过公开 Shell 接口枚举图标并读取位置。它不会移动图标、修改文件、写入设置或创建窗口。

在装有 Visual Studio C++ Build Tools 和 Windows SDK 的 PowerShell 中运行：

```powershell
.\tools\DesktopProbe\build.ps1
.\build\DesktopProbe\DesktopProbe.exe
```

默认只输出数量、位置读取错误数以及“自动排列”状态，不输出桌面文件名。`--list` 会输出图标名称和位置，仅在隔离测试账户中使用。P0 通过后仍需在隔离环境验证图标定位、分组框交互和恢复；此工具不证明那些功能可行。
