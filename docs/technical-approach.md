# 技术选型与开发方式

日期：2026-10-11。第一版只面向 Windows 11 x64。主程序改为 **C# + WinForms，目标 .NET Framework 4.8**；通过系统自带的 Win32/Shell 接口操作桌面。现有 C++ 探测工具继续用于验证底层行为，不作为主程序技术栈。

## 为什么调整

- 主要工作是格子交互、布局保存、托盘和恢复。C# 与 WinForms 能更快完成界面和状态逻辑；应用是否轻量，以相同场景下的启动、空闲内存、CPU 和包大小实测为准，不凭语言判断。
- [微软的系统要求表](https://learn.microsoft.com/en-us/dotnet/framework/get-started/system-requirements)显示：Windows 11 初始版预装 .NET Framework 4.8，22H2 及以后预装 4.8.1。以 4.8 为目标，不要求用户额外安装较新的 .NET 桌面运行时；本机装有 4.8.1，可运行 4.8 应用。
- .NET Framework 4.8 作为 Windows 组件，支持期限跟随所安装的 Windows；它仍有安全和可靠性修复，但不再增加新功能。[微软生命周期说明](https://learn.microsoft.com/en-us/lifecycle/faq/dotnet-framework)、[WinForms 平台取舍](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/migration/)。
- 当前是 2026 年 10 月，[.NET 8 的支持将在 2026 年 11 月结束](https://dotnet.microsoft.com/en-us/platform/support/policy)。若选现代 .NET，应直接评估 .NET 10 LTS；为了更低的用户安装门槛和当前开发节奏，首版先用系统已有的 .NET Framework 4.8。

这里的“4.8”是**程序依赖的运行环境版本**，不要求把本机编译工具限制在同一版本。以后若需要现代 .NET 的界面、性能或新 API，再根据实测与维护成本迁移。

当前已用本机的 .NET 8 SDK 和构建时参考程序集成功编译 `net48` WinForms 骨架；这只验证编译流程，没有测量资源占用或完成桌面交互。

## 组件和依赖

- 主程序：一个 WinForms 进程，负责格子窗口、托盘、设置、布局和恢复；仅在事件发生时更新，不固定频率扫描桌面。
- 系统接入：在 C# 中调用公开的 Win32/Shell COM 接口；现有 [C++ 桌面探测工具](../tools/DesktopProbe/README.md)保留为独立验证工具，不把它作为常驻进程。
- Windows 11 主右键菜单：仍需验证原生 `IExplorerCommand` 扩展。若确实需要单独的 C++ DLL，只把菜单命令转给主程序，不把业务逻辑移进去，也不让 Explorer 加载托管运行时。[微软菜单扩展说明](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer)。
- 第一版不加入 Electron、WebView、Qt、额外后台服务或第三方 UI 库。构建时可用参考程序集包；它不是用户运行时依赖。

## 界面与后续风格

WinForms 支持自绘；格子的颜色、边框、字体和间距可继续迭代。首版只做一套清晰风格，绘制与分组数据分开。复杂透明、动画、深色模式及不同缩放下的细节以原型和资源测量决定，不提前建设皮肤系统。

## 当前技术关口

原生图标定位已在本机通过临时测试图标验证，但“自动排列”开启时定位请求会被忽略；完整格子叠放、拖放、多选、Win+D 和异常恢复仍未验证。主程序换成 C# 不改变这些 Windows 桌面集成难点。继续按[技术实现计划](technical-design.md)逐项验证，不能把技术栈切换当作交互可行的证明。

开源许可证和发布包格式在首个可运行原型后确定。
