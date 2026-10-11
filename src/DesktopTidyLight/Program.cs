using System;
using System.IO;
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
                        File.WriteAllText(args[1], "items=" + desktop.Items().Count + ", autoArrange=" + desktop.AutoArrange + ", positioningFlags=" + desktop.PositioningFlags);
                }
                catch (Exception error) { File.WriteAllText(args[1], error.ToString()); }
                return;
            }
            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\DesktopTidyLight", out createdNew))
            {
                if (!createdNew)
                {
                    if (args.Length == 1 && args[0] == "--new-group")
                    {
                        for (int attempt = 0; attempt < 5; attempt++)
                        {
                            try
                            {
                                using (var signal = EventWaitHandle.OpenExisting(@"Local\DesktopTidyLight.NewGroup")) signal.Set();
                                break;
                            }
                            catch (WaitHandleCannotBeOpenedException) { Thread.Sleep(100); }
                        }
                    }
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try
                {
                    if (args.Length == 1 && args[0] == "--unregister")
                    {
                        DesktopMenu.Unregister();
                        return;
                    }
                    Layout layout = LayoutStore.Load();
                    if (args.Length == 1 && args[0] == "--restore")
                    {
                        using (var desktop = new DesktopShell())
                        {
                            if (layout.Members.Count > 0 && desktop.AutoArrange) desktop.SetPositioningFlags(0);
                            TidyContext.RestoreMembers(layout.Members);
                            if (layout.OriginalFolderFlags.HasValue) desktop.SetPositioningFlags(layout.OriginalFolderFlags.Value);
                            layout.Snapshots = desktop.Items().ConvertAll(item => new PositionData { Id = item.Id, X = item.Position.X, Y = item.Position.Y });
                        }
                        layout.Members.Clear();
                        layout.OriginalFolderFlags = null;
                        LayoutStore.Save(layout);
                        return;
                    }
                    DesktopMenu.Register();
                    using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\DesktopTidyLight.NewGroup"))
                        Application.Run(new TidyContext(layout, signal, args.Length == 1 && args[0] == "--new-group"));
                }
                catch (Exception error) { Dialogs.Message(error.Message, "DesktopTidyLight", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

    }
}
