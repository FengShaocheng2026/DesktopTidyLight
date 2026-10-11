# DesktopTidyLight

一个计划开源的轻量 Windows 桌面图标分组工具。当前处于技术原型阶段，尚无可用的产品版本。

- [竞品调研](docs/competitive-analysis.md)
- [第一版（MVP）需求](docs/mvp.md)
- [第一版产品需求（PRD）](docs/prd.md)
- [技术选型与开发方式](docs/technical-approach.md)
- [技术实现与验证计划](docs/technical-design.md)
- [C# 主程序](src/DesktopTidyLight/DesktopTidyLight.csproj)

设计目标：让桌面上的文件、文件夹和快捷方式容易分组、容易找到；退出或异常中断后仍能安全回到普通 Windows 桌面。

第一版以 Windows 11 为目标，主程序采用 C#、WinForms 和 .NET Framework 4.8。开源协议和发布方式稍后确定。

当前是**可运行原型，尚未通过 MVP 验收**。本机已验证桌面右键新建分组、临时文件的拖入和拖出、多选和跨组移动、组内双击及系统右键菜单、格子改名与删除、移动和缩放、异常结束后的恢复。Explorer 重启、不同缩放及长期稳定性仍待验收。[技术实现文档](docs/technical-design.md)记录了已验证的范围。

开发机运行 `dotnet build src/DesktopTidyLight/DesktopTidyLight.csproj -c Release`。新建分组时程序会记录原来的“自动排列图标”和“对齐到网格”设置，并暂时关闭它们；退出恢复时还原。当前技术路径**不能在自动排列持续开启时保持分组布局**。`--restore` 可独立恢复已记录图标的位置；`--unregister` 清理本程序创建的桌面右键入口和自启动项。
