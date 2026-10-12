using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DesktopTidyLight
{
    internal sealed class TidyContext : ApplicationContext
    {
        private readonly LayoutController controller;
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly Icon appIcon = Theme.CreateIcon();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly List<GroupFrame> frames = new List<GroupFrame>();
        private readonly EventWaitHandle createEvent, restoreEvent, unregisterEvent, settingsEvent;
        private readonly Control dispatcher = new Control();
        private bool closing, busy, paused;
        private volatile bool environmentChanged;
        private int syncFailures;
        private Point origin;
        private Size spacing;
        private IntPtr viewWindow;
        private uint explorerProcessId;
        private GroupFrame activeDrag;
        private Rectangle dragOriginal;
        private Rectangle[] dragScreens;
        private bool mouseWasDown;
        private long nextSync;
        private long retryAfter;
        private Rectangle[] knownScreens = new Rectangle[0];
        private readonly DesktopScan scan = new DesktopScan();
        private Layout scannedLayout;
        private SettingsForm settingsWindow;

        internal TidyContext(Layout layout, EventWaitHandle createEvent, EventWaitHandle restoreEvent, EventWaitHandle unregisterEvent, EventWaitHandle settingsEvent, bool createNow)
        {
            controller = new LayoutController(layout, state => LayoutStore.Save(state));
            Theme.Apply(layout.Skin);
            this.createEvent = createEvent; this.restoreEvent = restoreEvent; this.unregisterEvent = unregisterEvent;
            this.settingsEvent = settingsEvent;
            var handle = dispatcher.Handle;
            var menu = Theme.Menu();
            menu.Items.Add("设置", null, (s, e) => ShowSettings());
            menu.Items.Add("退出", null, (s, e) => Restore(true, false));
            tray.Icon = appIcon;
            tray.Text = "DesktopTidyLight";
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.DoubleClick += (s, e) => ShowSettings();
            SystemEvents.DisplaySettingsChanged += DisplayChanged;
            SystemEvents.UserPreferenceChanged += WorkAreaChanged;
            SystemEvents.SessionEnding += SessionEnding;

            RunAction(desktop =>
            {
                if (!controller.State.UnifiedIcons)
                {
                    if (layout.SessionActive || layout.PendingPositions != null || layout.OriginalFolderFlags.HasValue)
                        controller.Restore(desktop, Screens());
                    controller.EnableUnified();
                }
                controller.Start(desktop, Screens());
                controller.SaveMonitors(DisplayLayout.Capture());
                RememberEnvironment(desktop);
                RefreshFrames();
            });
            timer.Interval = 80;
            timer.Tick += (s, e) => Tick();
            timer.Start();
            if (createNow) CreateGroup();
        }

        private static Rectangle[] Screens() { return Screen.AllScreens.Select(s => s.WorkingArea).ToArray(); }

        private void RememberEnvironment(DesktopShell desktop)
        {
            origin = desktop.Origin; spacing = desktop.Spacing; environmentChanged = false;
            knownScreens = Screens(); retryAfter = 0;
            viewWindow = desktop.ViewWindow; explorerProcessId = desktop.ProcessId;
        }

        private void Tick()
        {
            if (closing || busy || IconCanvas.DragInProgress) return;
            if (unregisterEvent.WaitOne(0)) { Restore(true, true); return; }
            if (restoreEvent.WaitOne(0)) { Restore(true, false); return; }
            if (createEvent.WaitOne(0)) { CreateGroup(); return; }
            if (settingsEvent.WaitOne(0)) { ShowSettings(); return; }
            if (paused || controller.State.Groups.Count == 0 || frames.Any(f => f.IsDragging)) return;
            if (Control.MouseButtons != MouseButtons.None) { mouseWasDown |= DesktopDrop.IsDesktop(Cursor.Position); return; }
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (now < retryAfter) return;
            var snapshot = scan.Take();
            if (snapshot == null)
            {
                if (!mouseWasDown && !environmentChanged && now < nextSync) return;
                if (scan.Start())
                {
                    scannedLayout = controller.State; mouseWasDown = false;
                    nextSync = now + System.Diagnostics.Stopwatch.Frequency * 3;
                }
                return;
            }
            // A user edit while scanning invalidates the snapshot. Never apply stale membership.
            if (!ReferenceEquals(scannedLayout, controller.State)) { nextSync = 0; return; }
            busy = true;
            try
            {
                if (snapshot.Error != null) throw snapshot.Error;
                bool detached = frames.Count != controller.State.Groups.Count || frames.Any(f => f.IsDisposed || !f.IsAttachedToDesktop());
                bool restarted = viewWindow != IntPtr.Zero && (snapshot.ViewWindow != viewWindow || snapshot.ProcessId != explorerProcessId);
                bool reset = snapshot.Flags != 0 || restarted || detached || environmentChanged || snapshot.Origin != origin || snapshot.Spacing != spacing || !knownScreens.SequenceEqual(Screens());
                if (reset || snapshot.NeedsSync(controller.State, Screens()))
                {
                    // The snapshot is only a hint. Fresh reads and the existing journal/identity checks
                    // remain mandatory before any native desktop mutation.
                    using (var desktop = new DesktopShell())
                    {
                        if (reset)
                        {
                            HideFrames(); controller.Start(desktop, Screens());
                            controller.SaveMonitors(DisplayLayout.Capture()); RememberEnvironment(desktop);
                            RefreshFrames();
                        }
                        else if (controller.Sync(desktop)) RefreshFrames();
                        ShowNotice();
                    }
                }
                syncFailures = 0;
            }
            catch (COMException error)
            {
                HideFrames();
                if (++syncFailures == 3) Notify("桌面暂时不可用", "正在等待 Explorer 恢复。" + error.Message);
            }
            catch (RecoveryRequiredException error) { HandleRecovery(error); }
            catch (System.IO.IOException error) { Pause("布局保存失败", error.Message); }
            catch (UnauthorizedAccessException error) { Pause("无法保存布局", error.Message); }
            catch (InvalidOperationException error)
            {
                if (++syncFailures == 1) Notify("此次整理未完成", error.Message);
            }
            catch (Exception error) { Pause("整理已暂停", error.Message); }
            finally { busy = false; }
        }

        private void RunAction(Action<DesktopShell> action)
        {
            if (busy || closing) return;
            busy = true;
            try
            {
                using (var desktop = new DesktopShell()) action(desktop);
                if (!closing) ShowNotice();
            }
            catch (RecoveryRequiredException error) { HandleRecovery(error); }
            catch (System.IO.IOException error) { Pause("布局保存失败", error.Message); }
            catch (UnauthorizedAccessException error) { Pause("无法保存布局", error.Message); }
            catch (Exception error) { Notify("操作未完成", error.Message); }
            finally { busy = false; }
        }

        private void Resume()
        {
            RunAction(desktop =>
            {
                controller.Start(desktop, Screens());
                controller.SaveMonitors(DisplayLayout.Capture());
                paused = false;
                RememberEnvironment(desktop);
                RefreshFrames();
            });
        }

        private void CreateGroup()
        {
            RunAction(desktop =>
            {
                if (paused) controller.Start(desktop, Screens());
                var cursor = Cursor.Position;
                var displays = DisplayLayout.Capture();
                var display = DisplayLayout.Nearest(new Rectangle(cursor, new Size(1, 1)), displays);
                int width = (int)(420 * display.Dpi / 96f), height = (int)(360 * display.Dpi / 96f);
                var bounds = DisplayLayout.Clamp(new Rectangle(cursor, new Size(width, height)), display.Work);
                controller.Create(desktop, new GroupData { Id = Guid.NewGuid().ToString("N"), Name = "新建分组",
                    X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height }, Screens());
                controller.SaveMonitors(displays);
                paused = false;
                RememberEnvironment(desktop);
                RefreshFrames();
            });
        }

        private void RefreshFrames()
        {
            if (paused) return;
            foreach (var frame in frames.ToArray())
                if (frame.IsDisposed || !controller.State.Groups.Any(g => g.Id == frame.Data.Id)) { frame.Dispose(); frames.Remove(frame); }
            foreach (var group in controller.State.Groups)
            {
                var frame = frames.FirstOrDefault(f => f.Data.Id == group.Id);
                if (frame == null)
                {
                    frame = new GroupFrame(new GroupData { Id = group.Id, Name = group.Name, X = group.X, Y = group.Y, Width = group.Width, Height = group.Height });
                    frame.LayoutChanged += ChangeFrame;
                    frame.DeleteRequested += DeleteGroup;
                    frame.SettingsRequested += ShowSettings;
                    frame.LockRequested += id => RunAction(d => { var groupData = controller.State.Groups.Single(g => g.Id == id); controller.SetLocked(id, !groupData.Locked); RefreshFrames(); });
                    frame.DpiLayoutChanged += () => environmentChanged = true;
                    frame.DragStarting = BeginDrag;
                    frame.DragPreview = PreviewDrag;
                    frame.DragFinished = FinishDrag;
                    var targetFrame = frame;
                    frame.Icons.Dropped += ids => RunAction(desktop => { controller.AddMembers(desktop, targetFrame.Data.Id, ids); RefreshFrames(); });
                    frame.Icons.DesktopDropRequested += (ids, point) => RunAction(desktop => { controller.RemoveMembers(desktop, ids, Screens(), point); RefreshFrames(); });
                    frame.Icons.RemoveRequested += ids => RunAction(desktop => { controller.RemoveMembers(desktop, ids, Screens()); RefreshFrames(); });
                    frame.Icons.ReorderRequested += ids => RunAction(desktop => { controller.Reorder(targetFrame.Data.Id, ids); RefreshFrames(); });
                    frames.Add(frame);
                    try { frame.Show(); frame.AttachToDesktop(); }
                    catch { frame.Dispose(); frames.Remove(frame); throw; }
                }
                frame.LoadData(group);
                frame.ApplyPreferences(controller.State.AutoHideTitle, controller.State.BackgroundTransparency);
                frame.Icons.SetMembers(controller.State.Members.Where(m => m.GroupId == group.Id));
                frame.MemberCount = controller.State.Members.Count(m => m.GroupId == group.Id);
            }
        }

        private bool BeginDrag(GroupFrame frame)
        {
            if (busy || paused || controller.State.Groups.Any(g => g.Id == frame.Data.Id && g.Locked)) return false;
            busy = true; activeDrag = frame; dragOriginal = frame.ScreenBounds; dragScreens = Screens();
            return true;
        }

        private bool PreviewDrag(GroupFrame frame, Rectangle bounds)
        {
            if (activeDrag != frame) return false;
            var requestedBounds = bounds;
            var monitor = Screen.FromPoint(Cursor.Position).WorkingArea;
            bounds = DisplayLayout.Clamp(bounds, monitor);
            if ((Control.ModifierKeys & Keys.Alt) == 0)
                bounds = GridLayout.SnapBounds(bounds, controller.State.Groups.Where(g => g.Id != frame.Data.Id).Select(GridLayout.Bounds).ToArray(), dragScreens, frame.ResizeEdges);
            bounds = DisplayLayout.Clamp(bounds, monitor);
            frame.SetScreenBounds(bounds);
            frame.Hint = bounds != requestedBounds ? "已对齐边界" : null;
            return true;
        }

        private void FinishDrag(GroupFrame frame, bool cancel)
        {
            if (activeDrag != frame) return;
            try
            {
                if (cancel) frame.SetScreenBounds(dragOriginal);
                else controller.SaveGroup(frame.Data.Id, frame.ScreenBounds, frame.Data.Name, DisplayLayout.Capture());
            }
            catch (Exception error) { frame.SetScreenBounds(dragOriginal); Notify("位置未保存", error.Message); }
            finally { activeDrag = null; busy = false; }
            if (!frame.IsDisposed) frame.LoadData(controller.State.Groups.Single(g => g.Id == frame.Data.Id));
            if (environmentChanged) Resume();
        }

        private void ChangeFrame(GroupFrame frame, Rectangle oldBounds)
        {
            RunAction(desktop =>
            {
                try
                {
                    var bounds = frame.ScreenBounds;
                    if (!Screens().Any(s => s.Contains(bounds))) throw new InvalidOperationException("请把格子完整放在一块屏幕的可见区域内。");
                    controller.SaveGroup(frame.Data.Id, bounds, frame.Data.Name, DisplayLayout.Capture());
                }
                finally
                {
                    if (!frame.IsDisposed) frame.LoadData(controller.State.Groups.Single(g => g.Id == frame.Data.Id));
                }
            });
        }

        private void DeleteGroup(GroupFrame frame) { DeleteGroup(frame.Data.Id); }

        private void DeleteGroup(string groupId)
        {
            if (busy) return;
            busy = true;
            bool confirmed;
            try { confirmed = Dialogs.Message("删除格子后，成员会回到普通桌面，文件不会删除。", "删除分组", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK; }
            finally { busy = false; }
            if (!confirmed) return;
            RunAction(desktop =>
            {
                if (!paused && desktop.PositioningFlags == 0 && controller.State.PendingPositions == null) controller.Sync(desktop);
                controller.Restore(desktop, Screens(), groupId);
                RefreshFrames();
            });
        }

        private void Restore(bool exit, bool unregister)
        {
            RunAction(desktop =>
            {
                if (!paused && controller.State.SessionActive && controller.State.PendingPositions == null && desktop.PositioningFlags == 0 &&
                    !environmentChanged && frames.All(f => !f.IsDisposed && f.IsAttachedToDesktop())) controller.Sync(desktop);
                controller.Restore(desktop, Screens());
                paused = true;
                HideFrames();
                if (unregister) DesktopMenu.Unregister();
                if (exit) { closing = true; ExitThread(); }
                else tray.Text = "DesktopTidyLight — 已恢复普通桌面";
            });
        }

        private void HandleRecovery(RecoveryRequiredException error)
        {
            if (!error.CanRetry) { Pause("整理已暂停", error.Message); return; }
            HideFrames(); environmentChanged = true;
            retryAfter = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency * 10;
            if (++syncFailures == 1) Notify("正在等待桌面稳定", error.Message);
        }

        private void Pause(string title, string message)
        {
            paused = true;
            HideFrames();
            tray.Text = "DesktopTidyLight — 整理已暂停";
            Notify(title, message);
        }

        private void HideFrames()
        {
            foreach (var frame in frames.ToArray()) frame.Dispose();
            frames.Clear();
        }

        private void Notify(string title, string message)
        {
            try
            {
                var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTidyLight", "last-notice.txt");
                System.IO.File.WriteAllText(path, DateTimeOffset.Now.ToString("O") + " " + title + Environment.NewLine + message);
            }
            catch (System.IO.IOException) { }
            catch (UnauthorizedAccessException) { }
            tray.BalloonTipTitle = title;
            tray.BalloonTipText = message;
            tray.ShowBalloonTip(5000);
        }

        private void ShowNotice()
        {
            if (!string.IsNullOrEmpty(controller.Notice)) Notify("分组成员已更新", controller.Notice);
            if (!paused) tray.Text = "DesktopTidyLight";
        }

        private void ShowSettings()
        {
            if (settingsWindow != null && !settingsWindow.IsDisposed) { settingsWindow.Activate(); return; }
            if (busy || closing) return;
            using (var settings = new SettingsForm(AutoStart.Read(), AutoStart.Write, controller.State.Skin, controller.State.AutoHideTitle,
                (skin, hide, transparency) => { controller.SavePreferences(skin, hide, transparency); Theme.Apply(skin); RefreshFrames(); }, controller.State.BackgroundTransparency))
            {
                settingsWindow = settings;
                try { Dialogs.Show(settings); } finally { settingsWindow = null; }
            }
        }

        private void WorkAreaChanged(object sender, UserPreferenceChangedEventArgs args)
        { if (args.Category == UserPreferenceCategory.Desktop || args.Category == UserPreferenceCategory.Window) environmentChanged = true; }

        private void DisplayChanged(object sender, EventArgs args) { environmentChanged = true; }

        private void SessionEnding(object sender, SessionEndingEventArgs args)
        {
            if (closing || dispatcher.IsDisposed) return;
            try { dispatcher.Invoke(new Action(() => Restore(true, false))); }
            catch (InvalidOperationException) { }
        }

        protected override void ExitThreadCore()
        {
            timer.Stop(); scan.Dispose();
            SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            SystemEvents.UserPreferenceChanged -= WorkAreaChanged;
            SystemEvents.SessionEnding -= SessionEnding;
            HideFrames();
            tray.Visible = false;
            tray.Dispose();
            appIcon.Dispose();
            timer.Dispose();
            dispatcher.Dispose();
            base.ExitThreadCore();
        }
    }
}
