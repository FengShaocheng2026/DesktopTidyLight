# 技术选型与开发方式

日期：2026-10-11。目标：Windows 11 x64、普通用户权限、本地运行、常驻时尽量少占资源。此处区分“验证用工具”和“最终发布技术栈”；后者目前尚未确定。

## 先纠正一个误区

- SDK 是**开发和编译工具**，用户运行程序需要的是相应运行时，或者应用自带运行时。把 SDK 从 10 降到 8，本身不会让程序少占内存；改变目标运行时后，也不能仅凭版本号判断哪个更省资源。[微软的安装说明](https://learn.microsoft.com/en-us/dotnet/core/install/windows)区分了 SDK 与运行时。
- 最初选 .NET 10 是考虑维护期，不是已有性能测量。[微软支持页](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support)显示 .NET 8 到 2026 年 11 月结束支持，.NET 10 到 2028 年 11 月。不能因为 8 的数字小就把即将停止支持的版本定为长期发布目标。

## 原生优先方案

若将**最低常驻占用**作为首要目标，优先用 **C++17 + Win32/Shell API** 做产品原型：一个进程、普通窗口与托盘，不引入 .NET、WinUI、浏览器内核或后台服务。[微软的 Win32 说明](https://learn.microsoft.com/en-us/windows/win32/desktop-programming)确认它不依赖托管运行时；[C++ 桌面选型说明](https://learn.microsoft.com/en-us/cpp/windows/overview-of-windows-programming-in-cpp?view=msvc-170)也把精确控制内存和省电列为选择原生 C++ 的理由。这是更合适的**轻量化方向**，仍不能代替实际测量。

- 界面先用 Win32 窗口和系统绘制能力，只在需要半透明效果时使用[分层窗口](https://learn.microsoft.com/en-us/windows/desktop/api/winuser/nf-winuser-updatelayeredwindow)；不做持续动画或固定频率刷新。
- 托盘使用 Windows 的 [`Shell_NotifyIcon`](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area)。桌面项目读取和操作优先使用公开的 Shell 接口。
- 先只实现一个分组框、一个测试图标、打开与恢复。原生路线仍需验证能否安全处理**原图标的显示与恢复**。微软公开的 [`IFolderView`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifolderview) 与 [`IFolderView2`](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifolderview2)列出读取、选择、定位等操作，但从这些接口列表**不能直接得出可靠隐藏单个原图标的办法**；这是原型要解决的风险，而不是语言选择能自动解决的问题。
- 本机已有 Visual Studio C++ 编译工具，但 Windows SDK 头文件尚未安装；原生原型开始前需补齐 SDK。保留 C# WinForms 作为开发成本较低的备选路线。

## 备选路线及版本取舍

| 路线 | 资源与发布 | 开发成本 | 当前判断 |
| --- | --- | --- | --- |
| C# + WinForms + .NET 8 | 需要 .NET 8 桌面运行时或自带运行时；占用须实测；支持即将结束 | 本机已有 .NET 8 SDK，可直接验证 | 若原生开发明显拖慢进度，可用于**一次性对照原型**；不作为正式发布版本 |
| C# + WinForms + .NET 10 | 同样需要对应桌面运行时或自带运行时；没有证据表明它一定比 8 更占资源 | 开发与调试路径直接，但本机尚缺 .NET 10 SDK | 若 C# 原型满足资源目标，正式开发时再选这个长期支持版 |
| C# + WinForms + .NET Framework 4.8.1 | [Windows 11 自带](https://learn.microsoft.com/en-us/dotnet/framework/install/)，本机也已安装，可减少额外运行时部署；内存仍须实测 | 旧框架，未来开发受限；微软建议新项目用现代 .NET | 不因其版本号小就默认选择，只有部署或实测显示明显收益时再考虑 |

目前以 C++/Win32 为首选，但仍以桌面集成安全性和代表性资源测量决定最终方案。WinForms 提供控件、事件与自绘能力，若原生实现成本超出收益，可退回 C# 路线。[微软概览](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/overview/)。

## 与技术栈无关的实现原则

- **只在必要处直接调用 Windows 原生接口**：桌面图标、托盘、文件图标与右键菜单若所选框架现成能力不足，再封装对应 Win32/Shell API。优先使用公开、受支持的接口，不把依赖资源管理器内部窗口结构的技巧当成已可靠的方案。
- **本地 JSON 配置**：保存组名、位置、大小、成员和设置；避免为了少量布局数据增加数据库。写入需采用临时文件加替换，并保留可恢复的上一个版本，防止异常退出破坏布局。
- **自启动**：用户开启时写入当前用户的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，关闭或卸载时只删除本软件的值，不使用系统服务或管理员权限。Windows 会在用户登录时执行，且用户可在系统启动应用设置中管理；它不表示“尚未登录时运行”。[微软 Run 键说明](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)、[启动应用说明](https://learn.microsoft.com/en-us/windows/win32/w8cookbook/startup-apps)。

## 其他界面方案

| 方案 | 当前取舍 |
| --- | --- |
| Rust + 原生 Windows API | 同样适合系统工具，但桌面 Shell 交互仍需处理，且本机是否具备完整 Windows 编译工具链尚未确认；此阶段不能保证更快交付。 |
| WPF | 视觉定制较方便；分组框第一版界面简单，暂时没有足够理由引入另一套绘制与布局方式。 |
| WinUI 3 | 现代控件较好，但[微软文档](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app)说明未打包应用需处理 Windows App SDK 运行时，独立部署会增加输出体积；当前功能不需要这些额外条件。 |
| Electron / 网页界面 | 没有网页功能需求；额外运行环境对常驻小工具的收益有限，暂不引入。 |

这些是工程取舍，不是跨框架性能排行榜。发布形态先比较依赖已安装运行时与自包含包的体积、启动和内存；不预设“单文件”必然更轻。

## 开发顺序与停靠点

1. **小原型**：补齐 Windows SDK 后，在隔离的 Windows 11 测试环境中用原生 C++ 只实现一个分组和一个测试图标。验证显示、打开、右键、退出恢复、强制结束、资源管理器重启。不能安全恢复就先调整设计，不继续做完整界面。
2. **资源测量与定栈**：同一机器、同一场景下记录冷启动时间、空闲私有内存与工作集、空闲 CPU、10/100 个图标时的响应，以及发布包大小。先排除轮询和资源泄漏；若原生实现成本明显高于收益，再用现有 .NET 8 SDK 做同功能对照原型，记录两条路线的开发与维护成本。
3. **最小可用版本**：分组增删改、手动加入/移出项目、本地布局保存、托盘、恢复入口、自启动开关。每完成一条需求，按 `mvp.md` 的验收结果手工验证。
4. **发布准备**：确定许可证、发布方式、卸载恢复流程和干净机器验证，再公开可下载版本。

## 开发习惯

- 用 Git 小步提交；自动化检查集中在布局读写、恢复状态和自启动开关等有实际风险的逻辑。桌面交互用隔离环境实测。
- 开发和测试时不触碰日常桌面的真实文件。测试图标放在专门的测试账户或虚拟机。
- 不用固定频率轮询桌面；优先响应 Windows 事件。若必须轮询，测量其空闲 CPU 和异常恢复行为。
- 原型通过前，不增加自动更新、遥测、账号、云服务或插件系统。
