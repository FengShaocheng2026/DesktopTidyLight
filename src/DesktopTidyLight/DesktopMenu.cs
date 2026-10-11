using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DesktopTidyLight
{
    internal static class DesktopMenu
    {
        private const string KeyPath = @"Software\Classes\DesktopBackground\Shell\DesktopTidyLight";
        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

        internal static void Register()
        {
            using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                key.SetValue(null, "新建分组");
                key.SetValue("Icon", Application.ExecutablePath);
                using (var command = key.CreateSubKey("command"))
                    command.SetValue(null, "\"" + Application.ExecutablePath + "\" --new-group");
            }
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }

        internal static void Unregister()
        {
            Registry.CurrentUser.DeleteSubKeyTree(KeyPath, false);
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                key?.DeleteValue("DesktopTidyLight", false);
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
