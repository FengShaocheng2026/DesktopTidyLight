# 技术选型与开发方式

日期：2026-10-11。第一版只发布 Windows 11 x64；为未来 macOS 版保留可复用的核心，但不承诺两平台的桌面图标交互完全相同。选型只依据产品目标，不依据当前开发机是否装了某套工具。

## 两个目标能否兼顾

| 目标 | 判断 |
| --- | --- |
| 极低常驻占用 | C++ 与 Rust 都可编译为原生程序，不需要 .NET 或浏览器运行时。实际内存、启动和响应还取决于窗口数量、图标缓存、绘制方式及是否轮询，不能仅凭语言推定。[微软 Win32 说明](https://learn.microsoft.com/en-us/windows/win32/desktop-programming)、[Rust 平台支持](https://doc.rust-lang.org/rustc/platform-support.html)。 |
| 未来 macOS 扩展 | 两种语言都能共享分组模型、布局规则、配置与恢复逻辑；窗口、文件图标、系统菜单、自启动和桌面集成必须分别实现。**共享代码不等于 Windows 版本直接能在 Mac 上运行**。[Apple AppKit](https://developer.apple.com/documentation/appkit)、[Objective-C++ 互操作](https://developer.apple.com/library/archive/documentation/Porting/Conceptual/PortingUnix/environments/environments.html)。 |

## 推荐路线：C++ 核心 + 各平台原生界面

- **共享核心（C++17）**：只放分组数据、布局计算、配置格式、异常恢复状态等与系统无关的逻辑。不让 Win32 类型、Windows 路径或注册表进入核心。
- **Windows 11 实现**：Win32 窗口、Shell 图标枚举与打开、系统托盘；按系统事件更新，不设持续绘制或固定频率扫描。第一版只实现这套平台层。
- **未来 macOS 实现**：用 Objective-C++ 接 AppKit 的窗口和菜单栏，使用 NSWorkspace 处理文件打开，登录自启动走 macOS 的 SMAppService；复用核心，但重新做 Finder 桌面集成和实机验收。[AppKit](https://developer.apple.com/documentation/appkit)、[NSWorkspace](https://developer.apple.com/documentation/appkit/nsworkspace)、[NSStatusItem](https://developer.apple.com/documentation/appkit/nsstatusitem)、[SMAppService](https://developer.apple.com/documentation/servicemanagement/smappservice)。
- **绘制**：先用系统原生控件/绘制；半透明分组框确有需要时再用 Windows 分层窗口，macOS 则用 AppKit 对应能力。不引入 Qt、Electron 或 WebView 作为整套界面运行环境。

这条路线不是说 C++ 必然比 Rust 快。选 C++ 的主要原因是 Windows 原生 API 可直接使用，未来 macOS 的 Cocoa 层有[官方说明的 Objective-C++ 桥接方式](https://developer.apple.com/library/archive/documentation/Porting/Conceptual/PortingUnix/environments/environments.html)，能保持一份原生核心而不过早引入另一套语言边界。代价是内存与资源所有权需要更仔细的实现和测试。[微软选型说明](https://learn.microsoft.com/en-us/cpp/windows/overview-of-windows-programming-in-cpp?view=msvc-170)把精确控制内存和耗电列为 C++ 的适用场景。

## Rust 值得保留为备选

Rust 同样没有垃圾回收器，官方支持 Windows 和 macOS 目标；微软的 [windows-rs](https://github.com/microsoft/windows-rs)提供 Win32/COM 接口。Rust 的所有权检查对共享核心和恢复逻辑有价值，但 Windows Shell 边界仍有不安全的系统调用，macOS AppKit 也需要绑定或桥接。它**不会自动带来更低的资源占用或现成的跨平台桌面界面**。如果原生原型表明 C++ 的安全性和维护成本难以控制，可以在核心尚小时改选 Rust；届时仍保持两个平台分别集成。

## 真正限制 macOS 版的地方

Windows 的公开 [IFolderView](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifolderview) / [IFolderView2](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifolderview2) 能读、选和定位项目，但尚未确认可靠地单独隐藏原桌面图标并在崩溃后恢复。Apple 的 [Finder Sync](https://developer.apple.com/documentation/findersync)官方用途是文件同步相关的徽标、菜单和工具栏，不能从文档推出它能接管 Finder 桌面图标。因此 macOS 版须单独验证原图标管理、权限和恢复；若做不到，不应把“只显示自绘分组框”包装成与 Windows 完全一样的功能。沙盒应用访问桌面文件还需考虑[用户授权和持久访问](https://developer.apple.com/documentation/security/accessing-files-from-the-macos-app-sandbox)。

## 开发顺序

1. 在隔离的 Windows 11 环境做一个原生小原型：一个分组、一个测试图标，验证显示、打开、右键、退出和强制结束后的恢复，以及 Explorer 重启。先解决文件安全问题。
2. 记录冷启动、空闲私有内存/工作集、空闲 CPU、10/100 图标响应和包大小。若不达标，先定位窗口、缓存和绘制造成的开销；切换 Rust 不应被当作自动降低占用的办法。不为语言之争提前维护双实现。
3. 完成 Windows MVP：手动分组、布局保存、托盘、恢复入口和可选登录自启动。各项按 [MVP 需求](mvp.md)验收。
4. 在共享核心和交互设计定型前，尽早用 Mac 实机做 Finder 桌面交互与权限原型，确认相同的“不移动文件、原图标可恢复”要求能否成立；这项调研不阻塞 Windows 第一版发布，但在验证前不承诺 macOS 有完全相同的体验。

配置保持本地、带版本、可恢复；分组本身不移动或删除真实文件。开源许可证和发布包格式在首个可运行原型后确定。
