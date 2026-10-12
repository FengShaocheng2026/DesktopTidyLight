using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace DesktopTidyLight
{
    internal static class GridLayout
    {
        internal const int HeaderHeight = 52;
        internal const int SidePadding = 32;
        internal const int TopPadding = 24;
        internal const int HorizontalInsets = SidePadding * 2;
        internal const int VerticalInsets = HeaderHeight + TopPadding + SidePadding;
        internal static Rectangle Bounds(GroupData group)
        {
            return new Rectangle(group.X, group.Y, group.Width, group.Height);
        }

        internal static Rectangle Interior(Rectangle bounds)
        {
            return new Rectangle(bounds.X + SidePadding, bounds.Y + HeaderHeight + TopPadding,
                Math.Max(0, bounds.Width - HorizontalInsets), Math.Max(0, bounds.Height - VerticalInsets));
        }

        internal static Rectangle SnapBounds(Rectangle bounds, Rectangle[] others, Rectangle[] screens, int edges = 0)
        {
            const int gap = 16;
            Func<int, IEnumerable<int>, int> nearest = (value, candidates) => candidates
                .Where(n => Math.Abs((long)n - value) <= 12).OrderBy(n => Math.Abs(n - value)).DefaultIfEmpty(value).First();
            var result = bounds;
            if (edges == 0)
            {
                result.X = nearest(bounds.X, others.SelectMany(r => new[] { r.Left, r.Right - bounds.Width, r.Right + gap, r.Left - gap - bounds.Width })
                    .Concat(screens.SelectMany(r => new[] { r.Left, r.Right - bounds.Width })));
                result.Y = nearest(bounds.Y, others.SelectMany(r => new[] { r.Top, r.Bottom - bounds.Height, r.Bottom + gap, r.Top - gap - bounds.Height })
                    .Concat(screens.SelectMany(r => new[] { r.Top, r.Bottom - bounds.Height })));
            }
            else
            {
                var xs = others.SelectMany(r => new[] { r.Left, r.Right }).Concat(screens.SelectMany(r => new[] { r.Left, r.Right }));
                var ys = others.SelectMany(r => new[] { r.Top, r.Bottom }).Concat(screens.SelectMany(r => new[] { r.Top, r.Bottom }));
                result = Rectangle.FromLTRB((edges & 1) != 0 ? nearest(bounds.Left, xs) : bounds.Left,
                    (edges & 2) != 0 ? nearest(bounds.Top, ys) : bounds.Top,
                    (edges & 4) != 0 ? nearest(bounds.Right, xs) : bounds.Right,
                    (edges & 8) != 0 ? nearest(bounds.Bottom, ys) : bounds.Bottom);
            }
            return result.Width >= 240 && result.Height >= 180 && screens.Any(r => r.Contains(result)) ? result : bounds;
        }

        internal static int LabelInset(Size spacing, int iconSize) { return Math.Max(0, (spacing.Width - iconSize) / 2); }

        internal static Rectangle ItemBounds(Point position, Size spacing, int iconSize)
        {
            return new Rectangle(position.X - LabelInset(spacing, iconSize), position.Y, spacing.Width, spacing.Height);
        }

        internal static IEnumerable<Point> Slots(Rectangle area, Size spacing)
        {
            if (spacing.Width < 1 || spacing.Height < 1) throw new InvalidOperationException("图标间距无效。");
            for (int y = area.Top; y <= area.Bottom - spacing.Height; y += spacing.Height)
                for (int x = area.Left; x <= area.Right - spacing.Width; x += spacing.Width)
                    yield return new Point(x, y);
        }

        internal static Dictionary<string, Point> Arrange(Layout layout, IList<DesktopItem> items,
            IEnumerable<string> groupIds, Point origin, Size spacing, int iconSize)
        {
            var selected = new HashSet<string>(groupIds);
            var moving = new HashSet<string>(layout.Members.Where(m => selected.Contains(m.GroupId)).Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
            var occupied = items.Where(i => !moving.Contains(i.Id)).Select(i => ItemBounds(i.Position, spacing, iconSize)).ToList();
            var result = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in layout.Groups.Where(g => selected.Contains(g.Id)))
            {
                var members = layout.Members.Where(m => m.GroupId == group.Id).ToList();
                var inner = Interior(Bounds(group));
                inner.Offset(-origin.X, -origin.Y);
                int index = 0;
                var placed = new List<Rectangle>();
                foreach (var position in Slots(inner, spacing))
                {
                    if (index == members.Count) break;
                    var cell = new Rectangle(position, spacing);
                    if (occupied.Any(r => r.IntersectsWith(cell))) continue;
                    result.Add(members[index++].Id, new Point(position.X + LabelInset(spacing, iconSize), position.Y));
                    placed.Add(cell);

                }
                occupied.AddRange(placed);
                if (index != members.Count)
                    throw new InvalidOperationException("“" + group.Name + "”放不下这些图标，请扩大格子或移开附近未分组的图标。此次操作已取消。");
            }
            return result;
        }

        internal static void FitGroups(Layout layout, Rectangle[] screens, Size spacing, IList<DesktopItem> items, Point origin, int iconSize)
        {
            if (screens.Length == 0) throw new InvalidOperationException("没有可用的显示器。");
            var placed = new List<Rectangle>();
            var members = new HashSet<string>(layout.Members.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
            var occupied = items.Where(i => !members.Contains(i.Id)).Select(i => ItemBounds(i.Position, spacing, iconSize)).ToArray();
            foreach (var group in layout.Groups)
            {
                Rectangle original = Bounds(group);
                var candidates = screens.OrderByDescending(s => Rectangle.Intersect(s, original).Width * (long)Rectangle.Intersect(s, original).Height).ToArray();
                bool found = false;
                foreach (var screen in candidates)
                {
                    int count = layout.Members.Count(m => m.GroupId == group.Id);
                    int width = Math.Min(Math.Max(group.Width, spacing.Width + HorizontalInsets), screen.Width);
                    int columns = (width - HorizontalInsets) / spacing.Width;
                    if (columns < 1) continue;
                    int height = Math.Min(Math.Max(group.Height, Math.Max(1, (count + columns - 1) / columns) * spacing.Height + VerticalInsets), screen.Height);
                    if ((height - VerticalInsets) / spacing.Height * columns < count) continue;
                    var preferred = new Rectangle(Math.Max(screen.Left, Math.Min(group.X, screen.Right - width)),
                        Math.Max(screen.Top, Math.Min(group.Y, screen.Bottom - height)), width, height);
                    var positions = new[] { preferred.Location }.Concat(Slots(screen, new Size(24, 24)));
                    foreach (var point in positions)
                    {
                        var bounds = new Rectangle(point, preferred.Size);
                        if (!screen.Contains(bounds) || placed.Any(p => p.IntersectsWith(bounds))) continue;
                        var inner = Interior(bounds);
                        inner.Offset(-origin.X, -origin.Y);
                        if (count > 0 && Slots(inner, spacing).Count(p => !occupied.Any(r => r.IntersectsWith(new Rectangle(p, spacing)))) < count) continue;
                        group.X = point.X; group.Y = point.Y; group.Width = width; group.Height = height;
                        placed.Add(bounds);
                        found = true;
                        break;
                    }
                    if (found) break;
                }
                if (!found) throw new InvalidOperationException("当前屏幕放不下所有分组。请连接原显示器，或先恢复普通桌面后删除部分分组。");
            }
        }

        internal static Dictionary<string, Point> RestoreTargets(IList<MemberData> members, IList<DesktopItem> items,
            Point origin, Size spacing, Rectangle[] screens, int iconSize, IEnumerable<Rectangle> excludedScreenAreas = null)
        {
            var moving = new HashSet<string>(members.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
            var occupied = items.Where(i => !moving.Contains(i.Id)).Select(i => ItemBounds(i.Position, spacing, iconSize)).ToList();
            if (excludedScreenAreas != null)
                occupied.AddRange(excludedScreenAreas.Select(r => new Rectangle(r.X - origin.X, r.Y - origin.Y, r.Width, r.Height)));
            var areas = screens.Select(s => new Rectangle(s.X - origin.X, s.Y - origin.Y, s.Width, s.Height)).ToArray();
            var result = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in members)
            {
                var preferred = new Point(member.OriginalX - origin.X, member.OriginalY - origin.Y);
                var candidates = new[] { preferred }.Concat(areas.SelectMany(s => Slots(s, spacing)).Select(p => new Point(p.X + LabelInset(spacing, iconSize), p.Y)));
                bool found = false;
                foreach (var point in candidates)
                {
                    var cell = ItemBounds(point, spacing, iconSize);
                    if (!areas.Any(s => s.Contains(cell)) || occupied.Any(r => r.IntersectsWith(cell))) continue;
                    result.Add(member.Id, point);
                    occupied.Add(cell);

                    found = true;
                    break;
                }
                if (!found) throw new InvalidOperationException("普通桌面没有足够的空位置；恢复记录已保留，请先腾出空间。");
            }
            return result;
        }
    }
}
