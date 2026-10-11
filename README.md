# DesktopTidyLight

一个计划开源的轻量 Windows 桌面图标分组工具。当前处于技术原型阶段，尚无可用的产品版本。

- [竞品调研](docs/competitive-analysis.md)
- [第一版（MVP）需求](docs/mvp.md)
- [第一版产品需求（PRD）](docs/prd.md)
- [技术选型与开发方式](docs/technical-approach.md)
- [技术实现与验证计划](docs/technical-design.md)
- [C# 主程序骨架](src/DesktopTidyLight/DesktopTidyLight.csproj)
- [桌面接口探测工具](tools/DesktopProbe/README.md)

设计目标：让桌面上的文件、文件夹和快捷方式容易分组、容易找到；退出或异常中断后仍能安全回到普通 Windows 桌面。

第一版以 Windows 11 为目标，主程序采用 C#、WinForms 和 .NET Framework 4.8；现有 C++ 工具用于验证 Windows 桌面接口。开源协议和发布方式稍后确定。

主程序目前只有单实例和托盘退出的基础骨架，还没有桌面分组功能。开发机可运行 `dotnet build src/DesktopTidyLight/DesktopTidyLight.csproj -c Release` 编译；构建时会还原 .NET Framework 4.8 参考程序集，不要求用户安装该构建包。
