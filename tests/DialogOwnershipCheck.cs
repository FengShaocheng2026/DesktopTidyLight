// Compile with Dialogs.cs using the .NET Framework csc compiler; run interactively.
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DesktopTidyLight;

internal static class DialogOwnershipCheck
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetOwner(IntPtr window, int index, IntPtr owner);
    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);

    [STAThread]
    private static int Main()
    {
        IntPtr desktop = FindWindow("Progman", null);
        if (desktop == IntPtr.Zero || !IsWindowEnabled(desktop)) return 2;
        bool passed = true;
        using (var group = new Form { Text = "Dialog ownership check", ShowInTaskbar = false, Width = 180, Height = 80 })
        using (var timer = new Timer { Interval = 200 })
        {
            group.Show();
            SetOwner(group.Handle, -8, desktop);
            group.Activate();
            using (var dialog = new Form { Text = "Form check", ShowInTaskbar = false })
            {
                EventHandler close = (s, e) => { passed &= IsWindowEnabled(desktop); timer.Stop(); dialog.Close(); };
                timer.Tick += close;
                timer.Start();
                Dialogs.Show(dialog);
                timer.Tick -= close;
            }
            passed &= IsWindowEnabled(desktop);
            group.Activate();
            timer.Tick += (s, e) =>
            {
                passed &= IsWindowEnabled(desktop);
                timer.Stop();
                PostMessage(GetActiveWindow(), 0x10, IntPtr.Zero, IntPtr.Zero);
            };
            timer.Start();
            Dialogs.Message("Closes automatically", "Message check", MessageBoxButtons.OK, MessageBoxIcon.Information);
            passed &= IsWindowEnabled(desktop);
        }
        Console.WriteLine("Desktop enabled during and after both modal dialogs: " + passed);
        return passed ? 0 : 1;
    }
}
