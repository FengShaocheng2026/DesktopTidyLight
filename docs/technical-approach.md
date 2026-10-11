# 技术选型与开发方式

日期：2026-10-11。第一版只面向 Windows 11 x64。主程序采用 **C# + WinForms，目标 .NET Framework 4.8**；通过系统自带的 Win32/Shell 接口操作桌面。

## 为什么调整

- 主要工作是格子交互、布局保存、托盘和恢复。C# 与 WinForms 能更快完成界面和状态逻辑；应用是否轻量，以相同场景下的启动、空闲内存、CPU 和包大小实测为准，不凭语言判断。
- [微软的系统要求表](https://learn.microsoft.com/en-us/dotnet/framework/get-started/system-requirements)显示：Windows 11 初始版预装 .NET Framework 4.8，22H2 及以后预装 4.8.1。以 4.8 为目标，不要求用户额外安装较新的 .NET 桌面运行时；本机装有 4.8.1，可运行 4.8 应用。
- .NET Framework 4.8 作为 Windows 组件，支持期限跟随所安装的 Windows；它仍有安全和可靠性修复，但不再增加新功能。[微软生命周期说明](https://learn.microsoft.com/en-us/lifecycle/faq/dotnet-framework)、[WinForms 平台取舍](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/migration/)。
- 当前是 2026 年 10 月，[.NET 8 的支持将在 2026 年 11 月结束](https://dotnet.microsoft.com/en-us/platform/support/policy)。若选现代 .NET，应直接评估 .NET 10 LTS；为了更低的用户安装门槛和当前开发节奏，首版先用系统已有的 .NET Framework 4.8。

这里的“4.8”是**程序依赖的运行环境版本**，不要求把本机编译工具限制在同一版本。以后若需要现代 .NET 的界面、性能或新 API，再根据实测与维护成本迁移。

当前已用本机的 .NET 8 SDK 和构建时参考程序集成功编译 `net48` WinForms 原型。无分组的托盘状态一次测量为私有内存 24.1 MiB、工作集 52.8 MiB，后续 5 秒 CPU 时间增加 0.016 秒；有分组的负载尚未测量。

## 组件和依赖

- 主程序：一个 WinForms 进程，负责格子窗口、托盘、设置、布局和恢复；存在分组时以 800 毫秒定时回读图标位置，以识别用户在 Explorer 中的拖动。这个轮询成本需在大量图标场景实测。
- 系统接入：在 C# 中调用公开的 Win32/Shell COM 接口；旧 C++ 探测工具已按要求移除。
- Windows 11 主右键菜单：原生 `IExplorerCommand` 扩展和稀疏包清单已能离线构建，仍需安装和桌面实测。单独的 C++ DLL 只把菜单命令转给主程序，不承载分组逻辑，也不让 Explorer 加载托管运行时。[微软菜单扩展说明](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer)。
- 第一版不加入 Electron、WebView、Qt、额外后台服务或第三方 UI 库。构建时可用参考程序集包；它不是用户运行时依赖。

## 界面与后续风格

WinForms 支持自绘；格子的颜色、边框、字体和间距可继续迭代。首版只做一套清晰风格，绘制与分组数据分开。复杂透明、动画、深色模式及不同缩放下的细节以原型和资源测量决定，不提前建设皮肤系统。

## 当前技术关口

原生图标定位已用 C# 在本机通过临时测试图标验证，但“自动排列”开启时定位请求会被忽略。普通顶层分组窗会盖住 Chrome；当前原型改为挂在 Explorer 桌面视图下，短暂测试显示它位于桌面图标之上、普通应用之下，且不抢焦点。此方法依赖 Explorer 的内部窗口层级，Windows 更新后的兼容性尚未证明；拖放、多选、Win+D 和异常恢复仍未验收。继续按[技术实现计划](technical-design.md)验证。

开源许可证和发布包格式在首个可运行原型后确定。
