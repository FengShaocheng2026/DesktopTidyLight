using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace DesktopTidyLight
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\DesktopTidyLight", out createdNew))
            {
                if (!createdNew)
                {
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (var menu = new ContextMenuStrip())
                using (var tray = new NotifyIcon())
                {
                    menu.Items.Add("退出", null, (sender, args) => Application.ExitThread());
                    tray.Icon = SystemIcons.Application;
                    tray.Text = "DesktopTidyLight";
                    tray.ContextMenuStrip = menu;
                    tray.Visible = true;
                    Application.Run();
                    tray.Visible = false;
                }
            }
        }
    }
}
