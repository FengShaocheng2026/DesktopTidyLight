using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopTidyLight
{
    internal static class DesktopDrop
    {
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
        private static string Class(IntPtr window) { var name = new StringBuilder(128); GetClassName(window, name, name.Capacity); return name.ToString(); }

        internal static bool ShouldReturn(bool escape, int keyState, bool overDesktop)
        { return !escape && (keyState & 3) == 0 && overDesktop; }

        internal static bool IsDesktop(Point point, bool includeManaged = true)
        {
            var window = WindowFromPoint(point); string child = Class(window), root = Class(GetAncestor(window, 2));
            var control = System.Windows.Forms.Control.FromHandle(GetAncestor(window, 2));
            var surface = control as GroupFrame ?? control?.Tag as GroupFrame;
            if (includeManaged && surface != null && surface.IsDesktopSurface) return true;
            return (root == "Progman" || root == "WorkerW") &&
                (child == "SysListView32" || child == "SHELLDLL_DefView" || child == "Progman" || child == "WorkerW");
        }

        internal static Dictionary<string, Point> Targets(Layout layout, IList<MemberData> members, IList<DesktopItem> items,
            Point origin, Size spacing, int iconSize, Rectangle[] screens, Point dropPoint)
        {
            var screen = screens.FirstOrDefault(s => s.Contains(dropPoint));
            if (screen.IsEmpty) throw new InvalidOperationException("请放到屏幕内的普通桌面上。");
            var selected = new HashSet<string>(members.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
            var grouped = new HashSet<string>(layout.Members.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
            var occupied = items.Where(i => !selected.Contains(i.Id) && !grouped.Contains(i.Id)).Select(i =>
            { var r = GridLayout.ItemBounds(i.Position, spacing, iconSize); r.Offset(origin); return r; }).ToList();
            occupied.AddRange(layout.Groups.Select(GridLayout.Bounds));
            var cells = GridLayout.Slots(screen, spacing).Select(p => new Rectangle(p, spacing))
                .OrderBy(r => Math.Pow((r.X + spacing.Width / 2d - dropPoint.X) / spacing.Width, 2) +
                              Math.Pow((r.Y + spacing.Height / 2d - dropPoint.Y) / spacing.Height, 2))
                .ThenBy(r => r.Y).ThenBy(r => r.X).ToArray();
            var result = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in members)
            {
                var cell = cells.FirstOrDefault(r => !occupied.Any(o => o.IntersectsWith(r)));
                if (cell.IsEmpty) throw new InvalidOperationException("这里放不下整批图标，请换一处空白桌面。");
                occupied.Add(cell);
                result.Add(member.Id, new Point(cell.X + GridLayout.LabelInset(spacing, iconSize) - origin.X, cell.Y - origin.Y));
            }
            return result;
        }
    }
}
