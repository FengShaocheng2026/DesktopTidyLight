using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DesktopTidyLight
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [STAThread]
        private static void Main(string[] args)
        {
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            if (args.Length == 2 && args[0] == "--diagnose")
            {
                try
                {
                    using (var desktop = new DesktopShell())
                        File.WriteAllText(args[1], "items=" + desktop.Items().Count + ", iconsVisible=" + desktop.IconsVisible + ", autoArrange=" + desktop.AutoArrange + ", positioningFlags=" + desktop.PositioningFlags + ", origin=" + desktop.Origin + ", spacing=" + desktop.Spacing);
                }
                catch (Exception error) { File.WriteAllText(args[1], error.ToString()); }
                return;
            }
            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\DesktopTidyLight", out createdNew))
            {
                if (!createdNew)
                {
                    string command = args.Length == 1 ? args[0] : "";
                    string eventName = command == "--new-group" ? "NewGroup" : command == "--restore" ? "Restore" : command == "--unregister" ? "Unregister" : command == "--settings" ? "Settings" : null;
                    if (eventName != null)
                    {
                        bool delivered = false;
                        for (int attempt = 0; attempt < 5; attempt++)
                        {
                            try
                            {
                                using (var signal = EventWaitHandle.OpenExisting(@"Local\DesktopTidyLight." + eventName)) signal.Set();
                                delivered = true;
                                break;
                            }
                            catch (WaitHandleCannotBeOpenedException) { Thread.Sleep(100); }
                        }
                        if (!delivered) Dialogs.Message("正在运行的程序尚未就绪，请稍后重试。", "DesktopTidyLight", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try
                {
                    Layout layout = LayoutStore.Load();
                    if (args.Length == 1 && (args[0] == "--restore" || args[0] == "--unregister"))
                    {
                        var controller = new LayoutController(layout, state => LayoutStore.Save(state));
                        using (var desktop = new DesktopShell())
                            controller.Restore(desktop, Screen.AllScreens.Select(s => s.WorkingArea).ToArray());
                        if (args[0] == "--unregister") DesktopMenu.Unregister();
                        return;
                    }
                    DesktopMenu.Register();
                    using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\DesktopTidyLight.NewGroup"))
                    using (var restore = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\DesktopTidyLight.Restore"))
                    using (var unregister = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\DesktopTidyLight.Unregister"))
                    using (var settings = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\DesktopTidyLight.Settings"))
                    {
                        if (args.Length == 1 && args[0] == "--settings") settings.Set();
                        Application.Run(new TidyContext(layout, signal, restore, unregister, settings, args.Length == 1 && args[0] == "--new-group"));
                    }
                }
                catch (Exception error) { Dialogs.Message(error.Message, "DesktopTidyLight", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

    }
}
