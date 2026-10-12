using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopTidyLight
{
    internal sealed class DisplayInfo
    {
        internal string Id;
        internal Rectangle Work;
        internal int Dpi = 96;
    }

    internal static class DisplayLayout
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayDevice
        {
            internal int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Description;
            internal int Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Id;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Key;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayDevices(string name, uint index, ref DisplayDevice device, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);

        internal static DisplayInfo[] Capture()
        {
            return Screen.AllScreens.Select(screen =>
            {
                var device = new DisplayDevice { Size = Marshal.SizeOf(typeof(DisplayDevice)) };
                string id = EnumDisplayDevices(screen.DeviceName, 0, ref device, 1) && !string.IsNullOrEmpty(device.Id) ? device.Id : screen.DeviceName;
                uint x, y;
                int dpi = GetDpiForMonitor(MonitorFromPoint(new Point(screen.Bounds.Left + 1, screen.Bounds.Top + 1), 2), 0, out x, out y) == 0 ? (int)x : 96;
                return new DisplayInfo { Id = id, Work = screen.WorkingArea, Dpi = Math.Max(96, dpi) };
            }).ToArray();
        }

        internal static Rectangle Clamp(Rectangle bounds, Rectangle work)
        {
            int w = Math.Min(bounds.Width, work.Width), h = Math.Min(bounds.Height, work.Height);
            return new Rectangle(Math.Max(work.Left, Math.Min(bounds.X, work.Right - w)),
                Math.Max(work.Top, Math.Min(bounds.Y, work.Bottom - h)), w, h);
        }

        internal static DisplayInfo Nearest(Rectangle bounds, DisplayInfo[] monitors)
        {
            return monitors.OrderByDescending(m => { var r = Rectangle.Intersect(bounds, m.Work); return (long)r.Width * r.Height; })
                .ThenBy(m => Math.Abs((long)bounds.X - m.Work.X) + Math.Abs((long)bounds.Y - m.Work.Y)).First();
        }

        internal static void Remember(GroupData group, DisplayInfo[] monitors)
        {
            var display = Nearest(GridLayout.Bounds(group), monitors);
            var r = Clamp(GridLayout.Bounds(group), display.Work);
            group.MonitorId = display.Id; group.MonitorDpi = display.Dpi;
            group.MonitorX = display.Work.X; group.MonitorY = display.Work.Y;
            group.MonitorWidth = display.Work.Width; group.MonitorHeight = display.Work.Height;
            group.SavedX = r.X; group.SavedY = r.Y; group.SavedWidth = r.Width; group.SavedHeight = r.Height;
        }

        internal static void Resolve(GroupData group, DisplayInfo[] monitors)
        {
            var preferred = monitors.FirstOrDefault(m => m.Id == group.MonitorId);
            var display = preferred ?? Nearest(GridLayout.Bounds(group), monitors);
            double scale = (double)display.Dpi / Math.Max(96, group.MonitorDpi);
            if (preferred != null && group.MonitorWidth > 0 && group.MonitorHeight > 0)
            {
                double widthRatio = (double)display.Work.Width / group.MonitorWidth;
                double heightRatio = (double)display.Work.Height / group.MonitorHeight;
                if (Math.Abs(widthRatio - 1) > 0.2 || Math.Abs(heightRatio - 1) > 0.2)
                    scale = Math.Min(widthRatio, heightRatio);
            }
            int w = Math.Min(display.Work.Width, Math.Max(240, (int)Math.Round(group.SavedWidth * scale)));
            int h = Math.Min(display.Work.Height, Math.Max(180, (int)Math.Round(group.SavedHeight * scale)));
            double x = (double)(group.SavedX - group.MonitorX) / Math.Max(1, group.MonitorWidth - group.SavedWidth);
            double y = (double)(group.SavedY - group.MonitorY) / Math.Max(1, group.MonitorHeight - group.SavedHeight);
            var bounds = Clamp(new Rectangle(display.Work.X + (int)Math.Round(x * (display.Work.Width - w)),
                display.Work.Y + (int)Math.Round(y * (display.Work.Height - h)), w, h), display.Work);
            group.X = bounds.X; group.Y = bounds.Y; group.Width = bounds.Width; group.Height = bounds.Height;
            // Keep preferred monitor / geometry intact during unplug, scaling, and temporary fallback.
        }

        internal static void ResolveAll(Layout layout, DisplayInfo[] monitors)
        {
            foreach (var group in layout.Groups) Resolve(group, monitors);
            // Preserve intentional overlap and preferred display geometry.
        }
    }
}
