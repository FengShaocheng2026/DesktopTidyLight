using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DesktopTidyLight
{
    internal sealed class TidyContext : ApplicationContext
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private readonly Layout layout;
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly Timer timer = new Timer();
        private readonly List<GroupFrame> frames = new List<GroupFrame>();
        private readonly Dictionary<string, Point> previous = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        private readonly System.Threading.EventWaitHandle createEvent;
        private bool closing;
        private bool pendingSave;
        private bool firstSync = true;
        private int syncFailures;

        internal TidyContext(Layout layout, System.Threading.EventWaitHandle createEvent, bool createNow)
        {
            this.layout = layout;
            this.createEvent = createEvent;
            var menu = new ContextMenuStrip();
            menu.Items.Add("新建分组", null, (sender, args) => CreateGroup());
            menu.Items.Add("检测桌面设置", null, (sender, args) => ShowDesktopStatus());
            menu.Items.Add("设置", null, (sender, args) => ShowSettings());
            menu.Items.Add("退出并恢复图标", null, (sender, args) => ExitAndRestore());
            tray.Icon = SystemIcons.Application;
            tray.Text = "DesktopTidyLight";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;

            try
            {
                using (var desktop = new DesktopShell())
                {
                    foreach (var saved in layout.Snapshots) previous[saved.Id] = new Point(saved.X, saved.Y);
                    foreach (var item in desktop.Items())
                        if (!previous.ContainsKey(item.Id) || (layout.Groups.Count == 0 && layout.Members.Count == 0))
                            previous[item.Id] = item.Position;
                    if (layout.Snapshots.Count == 0 || (layout.Groups.Count == 0 && layout.Members.Count == 0)) SavePositions();
                    if (!desktop.AutoArrange)
                        foreach (var group in layout.Groups) ShowFrame(group);
                }
            }
            catch (Exception error) { Alert("无法读取桌面，分组暂未显示：" + error.Message); }

            timer.Interval = 800;
            timer.Tick += (sender, args) => Tick();
            timer.Start();
            if (createNow) CreateGroup();
        }

        private void Tick()
        {
            if (closing) return;
            if (createEvent.WaitOne(0)) CreateGroup();
            if (layout.Groups.Count == 0 || frames.Any(frame => frame.IsDragging)) return;
            try
            {
                if (frames.Any(frame => frame.IsDisposed || !frame.IsHandleCreated || !frame.IsAttachedToDesktop()))
                {
                    foreach (var frame in frames.Where(frame => !frame.IsDisposed)) frame.Dispose();
                    frames.Clear();
                }
                using (var desktop = new DesktopShell())
                {
                    if (desktop.AutoArrange)
                    {
                        foreach (var frame in frames.ToArray()) { frame.Close(); frames.Remove(frame); }
                        bool moved = false;
                        foreach (var item in desktop.Items())
                        {
                            Point old;
                            if (!previous.TryGetValue(item.Id, out old) || old != item.Position) moved = true;
                            previous[item.Id] = item.Position;
                        }
                        if (moved || pendingSave) SavePositions();
                        return;
                    }
                    if (frames.Count < layout.Groups.Count)
                    {
                        foreach (var group in layout.Groups.Where(group => frames.All(frame => frame.Data.Id != group.Id))) ShowFrame(group);
                    }
                    bool changed = false;
                    foreach (var item in desktop.Items())
                    {
                        MemberData member = layout.Members.FirstOrDefault(value => string.Equals(value.Id, item.Id, StringComparison.OrdinalIgnoreCase));
                        Point screenPosition = new Point(item.Position.X + SystemInformation.VirtualScreen.Left,
                            item.Position.Y + SystemInformation.VirtualScreen.Top);
                        GroupFrame target = frames.FirstOrDefault(frame => Interior(frame).Contains(screenPosition));
                        Point old;
                        if (member == null && target != null && previous.TryGetValue(item.Id, out old) && old != item.Position)
                        {
                            layout.Members.Add(new MemberData { Id = item.Id, GroupId = target.Data.Id, OriginalX = old.X, OriginalY = old.Y });
                            changed = true;
                        }
                        else if (!firstSync && member != null && previous.TryGetValue(item.Id, out old) && old != item.Position)
                        {
                            if (target == null) layout.Members.Remove(member);
                            else member.GroupId = target.Data.Id;
                            changed = true;
                        }
                        if (!previous.TryGetValue(item.Id, out old) || old != item.Position) changed = true;
                        previous[item.Id] = item.Position;
                    }
                    if (changed || pendingSave) SavePositions();
                    firstSync = false;
                    syncFailures = 0;
                }
            }
            catch (Exception error)
            {
                if (++syncFailures == 3)
                {
                    tray.BalloonTipTitle = "桌面暂时不可用";
                    tray.BalloonTipText = "分组正在等待 Explorer 恢复：" + error.Message;
                    tray.ShowBalloonTip(3000);
                }
            }
        }

        private void SavePositions()
        {
            pendingSave = true;
            layout.Snapshots = previous.Select(value => new PositionData { Id = value.Key, X = value.Value.X, Y = value.Value.Y }).ToList();
            LayoutStore.Save(layout);
            pendingSave = false;
        }

        private static Rectangle Interior(GroupFrame frame)
        {
            Point screen = frame.PointToScreen(Point.Empty);
            return new Rectangle(screen.X + 6, screen.Y + 34, frame.Width - 12, frame.Height - 40);
        }

        private void CreateGroup()
        {
            try
            {
                using (var desktop = new DesktopShell())
                {
                    if (desktop.AutoArrange) { Alert("请先在 Windows 桌面右键菜单中关闭“自动排列图标”，再新建分组。"); return; }
                    foreach (var item in desktop.Items()) previous[item.Id] = item.Position;
                }
                Point cursor = Cursor.Position;
                Rectangle area = Screen.FromPoint(cursor).WorkingArea;
                var group = new GroupData
                {
                    Id = Guid.NewGuid().ToString("N"), Name = "新建分组",
                    X = Math.Max(area.Left, Math.Min(cursor.X, area.Right - 260)),
                    Y = Math.Max(area.Top, Math.Min(cursor.Y, area.Bottom - 210)),
                    Width = 260, Height = 210
                };
                layout.Groups.Add(group);
                SavePositions();
                ShowFrame(group);
            }
            catch (Exception error) { Alert("无法新建分组：" + error.Message); }
        }

        private void ShowFrame(GroupData group)
        {
            Rectangle area = Screen.FromPoint(new Point(group.X, group.Y)).WorkingArea;
            group.X = Math.Max(area.Left, Math.Min(group.X, area.Right - Math.Max(160, group.Width)));
            group.Y = Math.Max(area.Top, Math.Min(group.Y, area.Bottom - Math.Max(120, group.Height)));
            var frame = new GroupFrame(group);
            frame.LayoutChanged += ChangeFrame;
            frame.DeleteRequested += DeleteGroup;
            frames.Add(frame);
            try { frame.Show(); frame.AttachToDesktop(); }
            catch { frames.Remove(frame); frame.Close(); throw; }
        }

        private void ChangeFrame(GroupFrame frame, Rectangle oldBounds)
        {
            Rectangle inner = Interior(frame);
            var members = layout.Members.Where(value => value.GroupId == frame.Data.Id).ToList();
            var originals = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var desktop = new DesktopShell())
                {
                    var items = desktop.Items().ToDictionary(value => value.Id, StringComparer.OrdinalIgnoreCase);
                    int dx = frame.Left - oldBounds.Left;
                    int dy = frame.Top - oldBounds.Top;
                    var targets = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
                    foreach (var member in members)
                    {
                        DesktopItem item;
                        if (!items.TryGetValue(member.Id, out item)) continue;
                        originals.Add(member.Id, item.Position);
                        Point target = new Point(item.Position.X + dx, item.Position.Y + dy);
                        if (!inner.Contains(new Point(target.X + SystemInformation.VirtualScreen.Left, target.Y + SystemInformation.VirtualScreen.Top)))
                            throw new InvalidOperationException("格子太小，成员会被遮住。");
                        if (dx != 0 || dy != 0) targets.Add(member.Id, target);
                    }
                    SavePositions();
                    if (targets.Count > 0)
                    {
                        desktop.SetPositions(targets);
                        var after = desktop.Items().ToDictionary(value => value.Id, StringComparer.OrdinalIgnoreCase);
                        if (targets.Any(value => after[value.Key].Position != value.Value))
                            throw new InvalidOperationException("Explorer 没有按要求移动全部图标。");
                        foreach (var value in targets) previous[value.Key] = value.Value;
                    }
                }
                frame.StoreBounds();
                SavePositions();
            }
            catch (Exception error)
            {
                frame.Bounds = oldBounds;
                try
                {
                    if (originals.Count > 0)
                    {
                        using (var desktop = new DesktopShell()) desktop.SetPositions(originals);
                        foreach (var value in originals) previous[value.Key] = value.Value;
                    }
                    Alert("分组位置没有保存，图标已回到调整前：" + error.Message);
                }
                catch (Exception restoreError)
                {
                    timer.Stop();
                    Alert("分组移动失败，自动同步已暂停。请用“退出并恢复图标”处理：" + restoreError.Message);
                }
            }
        }

        private void DeleteGroup(GroupFrame frame)
        {
            if (MessageBox.Show("删除分组后，其图标将回到加入前的位置。", "删除分组", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            var removed = layout.Members.Where(value => value.GroupId == frame.Data.Id).ToList();
            try
            {
                RestoreMembers(removed);
                using (var desktop = new DesktopShell())
                    foreach (var item in desktop.Items()) previous[item.Id] = item.Position;
                layout.Members.RemoveAll(value => value.GroupId == frame.Data.Id);
                layout.Groups.Remove(frame.Data);
                SavePositions();
                frames.Remove(frame);
                frame.Close();
            }
            catch (Exception error)
            {
                timer.Stop();
                if (!layout.Groups.Contains(frame.Data)) layout.Groups.Add(frame.Data);
                foreach (var member in removed) if (!layout.Members.Contains(member)) layout.Members.Add(member);
                Alert("删除已取消，自动同步已暂停：" + error.Message);
            }
        }

        private void ExitAndRestore()
        {
            var members = layout.Members.ToList();
            try
            {
                RestoreMembers(layout.Members);
                using (var desktop = new DesktopShell())
                    foreach (var item in desktop.Items()) previous[item.Id] = item.Position;
                layout.Members.Clear();
                SavePositions();
                closing = true;
                ExitThread();
            }
            catch (Exception error)
            {
                timer.Stop();
                if (layout.Members.Count == 0) layout.Members.AddRange(members);
                Alert("恢复失败，自动同步已暂停，布局记录已保留：" + error.Message);
            }
        }

        internal static void RestoreMembers(IList<MemberData> members)
        {
            if (members.Count == 0) return;
            using (var desktop = new DesktopShell())
            {
                if (desktop.AutoArrange && members.Count > 0)
                    throw new InvalidOperationException("请先关闭“自动排列图标”，然后重试恢复。");
                var items = desktop.Items();
                var present = new HashSet<string>(items.Select(value => value.Id), StringComparer.OrdinalIgnoreCase);
                var targets = members.Where(value => present.Contains(value.Id)).ToDictionary(value => value.Id, value => new Point(value.OriginalX, value.OriginalY), StringComparer.OrdinalIgnoreCase);
                foreach (var target in targets)
                    if (items.Any(item => !targets.ContainsKey(item.Id) && item.Position == target.Value))
                        throw new InvalidOperationException("原位置已被其他桌面图标占用；恢复记录已保留。");
                desktop.SetPositions(targets);
                var after = desktop.Items().ToDictionary(value => value.Id, StringComparer.OrdinalIgnoreCase);
                if (targets.Any(value => after[value.Key].Position != value.Value))
                    throw new InvalidOperationException("部分图标未回到原位置；恢复记录已保留。");
            }
        }

        private void ShowDesktopStatus()
        {
            try { using (var desktop = new DesktopShell()) Alert(desktop.AutoArrange ? "自动排列图标已开启。请在桌面右键菜单中关闭它。" : "自动排列图标已关闭，可以创建分组。"); }
            catch (Exception error) { Alert(error.Message); }
        }

        private void ShowSettings()
        {
            try
            {
                using (var desktop = new DesktopShell())
                using (var settings = new SettingsForm(ReadAutoStart(), WriteAutoStart, desktop.AutoArrange))
                    settings.ShowDialog();
            }
            catch (Exception error) { Alert("无法打开设置：" + error.Message); }
        }

        private static bool ReadAutoStart()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                return string.Equals(key?.GetValue("DesktopTidyLight") as string, "\"" + Application.ExecutablePath + "\"", StringComparison.OrdinalIgnoreCase);
        }

        private static void WriteAutoStart(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue("DesktopTidyLight", "\"" + Application.ExecutablePath + "\"");
                else key.DeleteValue("DesktopTidyLight", false);
            }
        }

        private static void Alert(string message) { MessageBox.Show(message, "DesktopTidyLight", MessageBoxButtons.OK, MessageBoxIcon.Information); }

        protected override void ExitThreadCore()
        {
            timer.Stop();
            foreach (var frame in frames.ToArray()) frame.Close();
            tray.Visible = false;
            tray.Dispose();
            timer.Dispose();
            base.ExitThreadCore();
        }
    }
}
