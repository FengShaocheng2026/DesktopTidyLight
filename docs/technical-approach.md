# 技术选型与开发方式

日期：2026-10-11。第一版只面向 Windows 11 x64。已选定 **C++17 + Windows 原生 Win32/Shell 接口**；目前不安排 macOS 或跨平台框架工作。选型依据是产品目标，与当前开发机是否安装工具链无关。

## 为什么选原生方案

- 不需要 .NET 或浏览器运行时。Win32 让程序直接使用 Windows 窗口、消息和系统接口，并能精细控制内存与耗电。[微软 Win32 概览](https://learn.microsoft.com/en-us/windows/win32/desktop-programming)、[C++ 桌面选型](https://learn.microsoft.com/en-us/cpp/windows/overview-of-windows-programming-in-cpp?view=msvc-170)。
- 一个进程完成分组框、托盘、设置与恢复；按系统消息响应变化，避免固定频率扫描和持续动画。实际内存、CPU 和启动时间以原型实测为准，不预先承诺“最低占用”。
- 使用 MSVC 和 Windows SDK 构建普通 Windows 桌面程序。界面只引入确有必要的系统组件，不引入 Qt、Electron、WebView、WinUI 或后台服务。

## 美术与 UI 风格

原生界面**可以自行决定外观**。Windows 支持[自绘控件](https://learn.microsoft.com/en-us/windows/win32/controls/user-controls-intro)、[Direct2D 二维绘制](https://learn.microsoft.com/en-us/windows/win32/direct2d/direct2d-overview)以及[分层窗口的半透明效果](https://learn.microsoft.com/en-us/windows/desktop/api/winuser/nf-winuser-updatelayeredwindow)。分组框颜色、边框、圆角、字体、图标间距和展开状态都可按设计实现；原生技术栈不会把外观锁死在系统默认控件风格。

第一版先做一套清晰、稳定的视觉方案。绘制代码与分组数据分开，今后可修改主题或增加风格；暂不建立完整皮肤/插件系统。自绘越多，键盘操作、缩放、辅助功能和不同主题的适配工作越多，复杂透明与动画也需要测量资源成本。

## 首个原型必须验证

1. 在隔离的 Windows 11 环境中读取一个测试桌面图标，放入一个分组框，并保持双击、右键、移出和退出恢复可用。
2. 验证强制结束程序、Explorer 重启、缩放变化后，原图标不会丢失、重复或跑到不可见位置。分组操作不移动或删除真实文件。
3. 微软公开的 [IFolderView](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifolderview) / [IFolderView2](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifolderview2) 有读取、选择和定位能力，但尚不能据此确认“可靠隐藏单个原图标并恢复”的做法。若无法通过这一关，应调整交互方案，再继续完整界面。
4. 记录冷启动、空闲私有内存和工作集、空闲 CPU、10/100 图标响应及发布包大小。优先优化多余窗口、轮询和图标缓存，而不是凭语言名称判断性能。

## 第一版实现顺序

桌面集成原型通过后，按 [MVP 需求](mvp.md)完成分组增删改、手动加入与移出、布局保存、系统托盘、异常恢复和可选登录自启动。布局配置只存本地，保存时保留可恢复版本；自启动由用户开启时写入当前用户的 [Run 键](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)，关闭或卸载时清理本软件的值。

开源许可证和发布包格式在首个可运行原型后确定。
