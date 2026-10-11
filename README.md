# DesktopTidyLight

一个计划开源的轻量 Windows 桌面图标分组工具。当前处于技术原型阶段，尚无可用的产品版本。

- [竞品调研](docs/competitive-analysis.md)
- [第一版（MVP）需求](docs/mvp.md)
- [第一版产品需求（PRD）](docs/prd.md)
- [技术选型与开发方式](docs/technical-approach.md)
- [技术实现与验证计划](docs/technical-design.md)
- [C# 主程序](src/DesktopTidyLight/DesktopTidyLight.csproj)
- [Windows 11 主右键菜单接入进度](docs/menu-integration.md)

设计目标：让桌面上的文件、文件夹和快捷方式容易分组、容易找到；退出或异常中断后仍能安全回到普通 Windows 桌面。

第一版以 Windows 11 为目标，主程序采用 C#、WinForms 和 .NET Framework 4.8。开源协议和发布方式稍后确定。

当前是**可运行原型，尚未通过 MVP 验收**。已写入分组框、原生图标位置读取与恢复、布局保存、托盘、设置、自启动开关和桌面右键的旧式菜单入口。Windows 11 主菜单的原生扩展和稀疏包清单已能离线构建，但未安装验收；真实拖放与多选、Win+D、Explorer 重启、异常恢复和不同缩放也仍待验收。[技术实现文档](docs/technical-design.md)记录了已验证的范围。

开发机运行 `dotnet build src/DesktopTidyLight/DesktopTidyLight.csproj -c Release`。首次使用前需在 Windows 桌面关闭“自动排列图标”，程序不会替用户改变该设置。`--restore` 可独立恢复已记录图标的位置；`--unregister` 清理本程序创建的桌面右键入口和自启动项。旧 C++ 探测工具已删除。
