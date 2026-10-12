# Desktop Tidy Light

English | [简体中文](README.zh-CN.md)

A lightweight Windows 11 desktop organizer. Drag files, folders, and shortcuts already shown on your desktop into groups without moving or copying the underlying files. This is a preview release for Windows x64 and requires .NET Framework 4.8.

## Get started

Download the latest portable ZIP from [Releases](https://github.com/FengShaocheng2026/DesktopTidyLight/releases), extract it completely, and run `DesktopTidyLight.exe`. Right-click the desktop and select **New group** (`新建分组`); on some Windows 11 versions, first select **Show more options**. Drag desktop icons into the group.

- Drag multiple icons at once, move them between groups, reorder them, or drag them back to the desktop. Use Ctrl / Shift for selection and Ctrl+A to select all.
- Resize, move, overlap, and lock groups. Choose from four styles and adjust background transparency, title visibility, and startup at sign-in.
- Groups and icon order are saved automatically. A normal exit restores the regular desktop; the groups return when you start the app again.

While the app runs, it draws the icons inside each group and temporarily parks their original desktop icons off-screen. A normal exit restores their positions and the Windows auto-arrange and grid-alignment settings. Exit through the tray menu. After an unexpected shutdown, restart the app or run `DesktopTidyLight.exe --restore` to restore the regular desktop.

Configuration is stored in `%LOCALAPPDATA%\DesktopTidyLight` and is not included in the portable ZIP. Exit the old version before replacing its files. Before uninstalling, run `DesktopTidyLight.exe --unregister` to remove the app's desktop menu and startup entry, then delete the program folder. You may keep or remove your personal configuration.

## Commands

| Option | Purpose |
| --- | --- |
| `--settings` | Open settings |
| `--new-group` | Create a group |
| `--restore` | Restore the regular desktop and exit, keeping group data |
| `--unregister` | Restore the desktop and remove the app's menu and startup entry |

## Build and check

You need Windows and a .NET SDK that supports targeting .NET Framework 4.8. NuGet restores the reference assemblies.

```powershell
dotnet build src/DesktopTidyLight/DesktopTidyLight.csproj -c Release
powershell -NoProfile -File tests/run-checks.ps1
powershell -NoProfile -File packaging/Package.ps1
```

The default checks do not modify your real desktop. The packaging script writes a portable ZIP and its SHA256 checksum to `dist/`. For hands-on checks, launch the app from the Windows desktop; a sandboxed development terminal may redirect configuration and registry access. See the [configuration recovery notes](docs/recovery-2026-10-12.md).

## Documentation

- [Architecture and recovery](docs/architecture.md) (Chinese)
- [0.1.2 verification scope](docs/release-0.1.2.md) (Chinese)
- [Performance checks](docs/performance-2026-10-12.md) (Chinese)

## Known limitations

The app only organizes items already visible in the Windows desktop view. Integration with the Explorer desktop depends on Windows behavior. Physical monitor hot-plugging, Remote Desktop, Windows updates, and long-running use need more hands-on testing. Some Shell icons may appear generic; you can refresh them from the group menu.

## License

[MIT](LICENSE).
