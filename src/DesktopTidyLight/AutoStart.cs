using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DesktopTidyLight
{
    internal static class AutoStart
    {
        internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal static bool Read()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                return string.Equals(key?.GetValue("DesktopTidyLight") as string, "\"" + Application.ExecutablePath + "\"", StringComparison.OrdinalIgnoreCase);
        }

        internal static void Write(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue("DesktopTidyLight", "\"" + Application.ExecutablePath + "\"");
                else key.DeleteValue("DesktopTidyLight", false);
            }
        }
    }
}
