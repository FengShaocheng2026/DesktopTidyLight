using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DesktopTidyLight;

internal static class UnifiedChecks
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int w, int h, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    private static bool IsAbove(IntPtr top, IntPtr lower)
    {
        for (var current = GetWindow(lower, 3); current != IntPtr.Zero; current = GetWindow(current, 3))
            if (current == top) return true;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition
    {
        internal IntPtr Window, After;
        internal int X, Y, Width, Height;
        internal uint Flags;
    }

    private sealed class Desktop : IDesktopView
    {
        internal List<DesktopItem> Data = Enumerable.Range(0, 60).Select(i => new DesktopItem { Id = "item" + i, Identity = "identity" + i, Position = new Point(26 + i % 15 * 100, i / 15 * 100) }).ToList();
        internal int Writes; internal bool Fail;
        public int PositioningFlags { get; set; }
        public bool IconsVisible { get; set; } = true;
        internal int VisibilityWrites;
        public void SetIconsVisible(bool visible) { IconsVisible = visible; VisibilityWrites++; }
        public Point Origin { get; set; }
        public Size Spacing { get { return new Size(100, 100); } }
        public int IconSize { get { return 48; } }
        public IList<string> SelectedIds() { return new string[0]; }
        public List<DesktopItem> Items() { return Data.Select(i => new DesktopItem { Id = i.Id, Identity = i.Identity, Position = i.Position }).ToList(); }
        public void SetPositioningFlags(int value) { PositioningFlags = value; }
        public void SetPositions(IDictionary<string, Point> positions)
        {
            Writes++; foreach (var p in positions) { Data.Single(i => i.Id == p.Key).Position = p.Value; if (Fail) { Fail = false; throw new IOException("partial write"); } }
        }
    }
    private static Rectangle[] screens = { new Rectangle(-1920, 0, 1920, 1080), new Rectangle(0, 0, 3840, 2160) };
    private static GroupData Group(string id) { return new GroupData { Id = id, Name = id, X = 100, Y = 100, Width = 420, Height = 360 }; }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static int NativeVisibility(bool observeOnly = false)
    {
        if (!observeOnly && Process.GetProcessesByName("DesktopTidyLight").Length != 0)
        { Console.Error.WriteLine("Stop DesktopTidyLight before this explicit native visibility check."); return 2; }
        using (var desktop = new DesktopShell())
        {
            bool visible = desktop.IconsVisible;
            var before = desktop.Items(); int flags = desktop.PositioningFlags;
            try
            {
                if (observeOnly) Assert(!visible, "managed desktop has not disabled native drawing");
                else desktop.SetIconsVisible(false);
                for (int pass = 0; pass < 3; pass++)
                {
                    desktop.Refresh();
                    System.Threading.Thread.Sleep(250);
                    using (var current = new DesktopShell())
                    {
                        Assert(!current.IconsVisible, "refresh re-enabled native icon drawing");
                        Assert(current.Items().Select(i => i.Id).OrderBy(x => x).SequenceEqual(before.Select(i => i.Id).OrderBy(x => x)),
                            "hiding icons changed desktop enumeration");
                        Assert(current.PositioningFlags == flags, "visibility changed positioning preferences");
                    }
                }
                Console.WriteLine("PASS native visibility: 3 Shell refreshes kept icons hidden; all " + before.Count + " items remained enumerable.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally
            {
                if (!observeOnly) desktop.SetIconsVisible(visible);
                Assert(desktop.IconsVisible == visible, "native visibility was not restored");
            }
        }
    }

    internal static int Run()
    {
        int count = 0;
        Action<string, Action> check = (name, test) => { test(); count++; Console.WriteLine("PASS unified: " + name); };
        check("native icons stay hidden through refreshes and restore their original visibility after restart", () =>
        {
            foreach (bool visible in new[] { true, false })
            {
                var d = new Desktop { IconsVisible = visible }; Layout disk = null;
                var c = new LayoutController(new Layout(), state => disk = state.Copy());
                c.EnableUnified(); c.Create(d, Group("one"), screens); c.AddMembers(d, "one", new[] { "item0" });
                Assert(!d.IconsVisible && disk.OriginalIconsVisible == visible, "original visibility was not saved before hiding");
                int writes = d.VisibilityWrites;
                for (int i = 0; i < 5; i++)
                {
                    d.Data[0].Position = new Point(50, 50); c.Sync(d);
                    Assert(!d.IconsVisible && d.VisibilityWrites == writes, "refresh briefly re-enabled native icons");
                }
                c = new LayoutController(disk.Copy(), state => disk = state.Copy()); c.Start(d, screens);
                Assert(c.State.OriginalIconsVisible == visible, "restart forgot original visibility");
                c.Restore(d, screens);
                Assert(d.IconsVisible == visible && !c.State.OriginalIconsVisible.HasValue && !c.State.PendingIconsVisible.HasValue,
                    "restore lost the user's display preference or journal");
            }
        });
        check("visibility changes require a durable journal and rollback restores the native desktop", () =>
        {
            var d = new Desktop(); int saves = 0; Layout disk = null;
            var layout = new Layout { UnifiedIcons = true };
            var c = new LayoutController(layout, state => { if (++saves == 1) throw new IOException("journal"); disk = state.Copy(); });
            try { c.Create(d, Group("one"), screens); } catch (RecoveryRequiredException) { }
            Assert(d.IconsVisible && d.VisibilityWrites == 0, "failed save still hid icons");
            saves = 0;
            c = new LayoutController(layout, state => { if (++saves == 2) throw new IOException("commit"); disk = state.Copy(); });
            try { c.Create(d, Group("one"), screens); } catch (RecoveryRequiredException) { }
            Assert(d.IconsVisible && disk.Groups.Count == 0 && disk.PendingPositions == null, "failed commit left icons hidden");
            var interrupted = new Layout { UnifiedIcons = true, PendingPositions = new List<PositionData>(), PendingFlags = 0, PendingIconsVisible = true };
            d.IconsVisible = false;
            c = new LayoutController(interrupted, state => disk = state.Copy()); c.Recover(d);
            Assert(d.IconsVisible && disk.PendingIconsVisible == null, "crash recovery left native icons hidden");
        });
        check("an interrupted offscreen-parking session upgrades without losing restore positions", () =>
        {
            var d = new Desktop(); var c = new LayoutController(new Layout(), state => { });
            var original = d.Data[0].Position;
            c.EnableUnified(); c.Create(d, Group("one"), screens); c.AddMembers(d, "one", new[] { "item0" });
            var legacy = c.State.Copy(); legacy.OriginalIconsVisible = null;
            d.Data[0].Position = new Point(-9000, -9000); d.IconsVisible = true;
            c = new LayoutController(legacy, state => { }); c.Start(d, screens);
            Assert(!d.IconsVisible && c.State.Members.Count == 1 && c.State.Members[0].OriginalX == original.X,
                "migration lost a grouped icon or captured its offscreen position");
            c.Restore(d, screens);
            Assert(d.IconsVisible && d.Data[0].Position == original, "migration could not restore the old desktop");
        });
        check("loose icons move on the managed desktop and transfer into and out of groups", () =>
        {
            var d = new Desktop(); var c = new LayoutController(new Layout(), state => { });
            c.EnableUnified(); c.Create(d, Group("one"), screens); c.AddMembers(d, "one", new[] { "item0" });
            var untouched = d.Data[2].Position;
            c.RemoveMembers(d, new[] { "item1" }, screens, new Point(2500, 700));
            Assert(!d.IconsVisible && c.State.Members.Count == 1 && d.Data[1].Position.X > 2000 && d.Data[2].Position == untouched,
                "moving a loose icon affected membership or unrelated items");
            c.AddMembers(d, "one", new[] { "item1" });
            Assert(c.State.Members.Count == 2, "loose icon could not join group");
            c.RemoveMembers(d, new[] { "item1" }, screens, new Point(2500, 900));
            Assert(c.State.Members.Count == 1 && !d.IconsVisible && d.Data[1].Position.X > 2000, "return to managed desktop failed");
            c.Restore(d, screens, "one");
            Assert(d.IconsVisible && c.State.Groups.Count == 0, "deleting last group did not restore the desktop");
        });
        check("managed desktop uses native positions and only intercepts icon cells", () =>
        {
            var data = new GroupData { Id = "desktop", X = -1920, Y = -500, Width = 1920, Height = 1080, Locked = true };
            using (var frame = new GroupFrame(data, true))
            {
                frame.ApplyPreferences(false, 100);
                frame.SetDesktopItems(new[] { new DesktopItem { Id = "loose", Position = new Point(40, 70) } },
                    new Point(-1920, -500), new Size(100, 104), 48);
                Assert(frame.Icons.Bounds == frame.ClientRectangle && frame.Region.IsVisible(40, 70), "native position was not mapped to desktop surface");
                Assert(!frame.Region.IsVisible(900, 700), "desktop surface blocked the wallpaper context menu");
                var hit = typeof(IconCanvas).GetMethod("Hit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert((int)hit.Invoke(frame.Icons, new object[] { new Point(40, 70) }) == 0 &&
                    (int)hit.Invoke(frame.Icons, new object[] { new Point(900, 700) }) == -1, "desktop hit testing used group grid");
                frame.SetDesktopItems(new DesktopItem[0], new Point(-1920, -500), new Size(100, 104), 48);
                Assert(!frame.Region.IsVisible(40, 70), "removed icon left an invisible input blocker");
            }
        });
        check("managed desktop supports marquee selection, Ctrl extension, native menu requests and refresh", () =>
        {
            using (var canvas = new IconCanvas { Size = new Size(600, 400) })
            {
                canvas.SetDesktopItems(new[] { new DesktopItem { Id = "one", Position = new Point(30, 20) },
                    new DesktopItem { Id = "two", Position = new Point(230, 20) } }, Point.Empty, new Size(100, 104), 48);
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var selected = (HashSet<string>)typeof(IconCanvas).GetField("selection", flags).GetValue(canvas);
                canvas.BeginDesktopSelection(false); canvas.SetDesktopSelection(new Rectangle(0, 0, 150, 150), true);
                Assert(selected.SetEquals(new[] { "one" }), "marquee did not select the first icon");
                canvas.BeginDesktopSelection(true); canvas.SetDesktopSelection(new Rectangle(180, 0, 150, 150), false);
                Assert(selected.SetEquals(new[] { "one", "two" }), "Ctrl marquee lost the original selection");
                string[] requested = null; canvas.DesktopMenuRequested += (ids, point) => requested = ids;
                typeof(IconCanvas).GetMethod("ShowItemMenu", flags).Invoke(canvas, new object[] { Point.Empty });
                Assert(requested != null && requested.Length == 2, "native menu did not receive the selection");
                int refreshes = 0; canvas.RefreshRequested += () => refreshes++;
                typeof(IconCanvas).GetMethod("OnKeyDown", flags).Invoke(canvas, new object[] { new KeyEventArgs(Keys.F5) });
                Assert(refreshes == 1, "F5 did not request desktop refresh");
                canvas.BeginDesktopSelection(false); canvas.SetDesktopSelection(Rectangle.Empty, false);
                Assert(selected.Count == 0, "blank desktop click did not clear selection");
            }
        });
        check("desktop selection spans monitors without losing the other monitor's items", () =>
        {
            using (var first = new IconCanvas { Size = new Size(600, 400) })
            using (var second = new IconCanvas { Size = new Size(600, 400) })
            {
                var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var all = new[] { "left", "right" };
                foreach (var entry in new[] { Tuple.Create(first, "left"), Tuple.Create(second, "right") })
                {
                    entry.Item1.SetDesktopItems(new[] { new DesktopItem { Id = entry.Item2, Position = new Point(30, 20) } }, Point.Empty, new Size(100, 104), 48);
                    entry.Item1.ShareDesktopSelection(selected); entry.Item1.DesktopIds = () => all;
                    entry.Item1.BeginDesktopSelection(false);
                }
                first.SetDesktopSelection(new Rectangle(0, 0, 150, 150), false);
                second.SetDesktopSelection(new Rectangle(0, 0, 150, 150), false);
                Assert(selected.SetEquals(all), "second monitor replaced the first selection");
                string[] requested = null; first.DesktopMenuRequested += (ids, point) => requested = ids;
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(IconCanvas).GetMethod("ShowItemMenu", flags).Invoke(first, new object[] { Point.Empty });
                Assert(requested.SequenceEqual(all), "cross-monitor operation lost selected items");
                selected.Clear();
                typeof(IconCanvas).GetMethod("OnKeyDown", flags).Invoke(first, new object[] { new KeyEventArgs(Keys.Control | Keys.A) });
                Assert(selected.SetEquals(all), "Ctrl+A selected only the current monitor");
                first.SetDesktopItems(new DesktopItem[0], Point.Empty, new Size(100, 104), 48);
                Assert(selected.SetEquals(new[] { "right" }), "removing one monitor's icon cleared another monitor's selection");
            }
        });
        check("icon canvas keeps its arrow after crossing a movable header or resize border", () =>
        {
            using (var frame = new GroupFrame(Group("cursor")))
            {
                var move = typeof(GroupFrame).GetMethod("OnMouseMove", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                foreach (var point in new[] { new Point(100, 20), new Point(1, 100), new Point(1, 1) })
                {
                    move.Invoke(frame, new object[] { new MouseEventArgs(MouseButtons.None, 0, point.X, point.Y, 0) });
                    Assert(frame.Cursor != Cursors.Default, "header/border lost its interaction cursor");
                    Assert(frame.Icons.Cursor == Cursors.Default, "icon canvas inherited the frame drag/resize cursor");
                }
            }
        });
        check("temporary refresh omission cannot delete a member whose file still exists", () =>
        {
            string path = Path.GetTempFileName();
            try
            {
                var d = new Desktop(); int saves = 0;
                var c = new LayoutController(new Layout(), state => saves++);
                var item = d.Data[0]; item.Id = path; item.Identity = DesktopShell.FileIdentity(path);
                Assert(item.Identity != null, "temporary file identity unavailable");
                c.EnableUnified(); c.Create(d, Group("one"), screens); c.AddMembers(d, "one", new[] { path });
                var original = new Point(c.State.Members[0].OriginalX, c.State.Members[0].OriginalY);
                int before = saves, writes = d.Writes;
                d.Data.Remove(item);
                bool deferred = false;
                try { c.Sync(d); } catch (InvalidOperationException) { deferred = true; }
                Assert(deferred && c.State.Members.Count == 1 && saves == before && d.Writes == writes,
                    "incomplete view removed membership or wrote a layout");
                deferred = false;
                try { c.Start(d, screens); } catch (DesktopNotReadyException) { deferred = true; }
                Assert(deferred && c.State.Members.Count == 1 && saves == before && d.Writes == writes,
                    "desktop restart discarded a temporarily missing member");
                item.Position = new Point(40, 40); d.Data.Add(item);
                c.Sync(d);
                Assert(!d.IconsVisible && c.State.Members.Count == 1, "returning member lost managed visibility");
                Assert(c.State.Members[0].OriginalX == original.X && c.State.Members[0].OriginalY == original.Y,
                    "refresh overwrote original desktop position");
                File.Delete(path); d.Data.Remove(item);
                Assert(c.Sync(d) && c.State.Members.Count == 0, "actually deleted file was retained");
            }
            finally { File.Delete(path); }
        });
        check("automatic window raises re-anchor both layers instead of preserving stale order", () =>
        {
            using (var frame = new GroupFrame(Group("order")))
            {
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var owner = new IntPtr(1234);
                typeof(GroupFrame).GetField("desktopOwner", flags).SetValue(frame, owner);
                var constrain = typeof(GroupFrame).GetMethod("ConstrainDesktopOrder", flags);
                IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WindowPosition)));
                try
                {
                    foreach (bool background in new[] { false, true })
                    foreach (var after in new[] { IntPtr.Zero, new IntPtr(-1), new IntPtr(4321) })
                    {
                        var position = new WindowPosition { After = after, X = 12, Y = 34, Width = 420, Height = 360, Flags = 0x10 };
                        Marshal.StructureToPtr(position, memory, false);
                        constrain.Invoke(frame, new object[] { Message.Create(IntPtr.Zero, 0x46, IntPtr.Zero, memory), background });
                        position = (WindowPosition)Marshal.PtrToStructure(memory, typeof(WindowPosition));
                        Assert(position.After == (background ? frame.Handle : owner), "raise was not redirected to desktop");
                        Assert((position.Flags & 0x4) == 0 && (position.Flags & 0x210) == 0x210, "order frozen or activation/owner protection lost");
                        Assert(position.X == 12 && position.Y == 34 && position.Width == 420 && position.Height == 360, "geometry changed");
                    }
                    Marshal.StructureToPtr(new WindowPosition { After = new IntPtr(99), Flags = 0x4 }, memory, false);
                    constrain.Invoke(frame, new object[] { Message.Create(IntPtr.Zero, 0x46, IntPtr.Zero, memory), false });
                    Assert(((WindowPosition)Marshal.PtrToStructure(memory, typeof(WindowPosition))).After == new IntPtr(99), "geometry-only request changed order");
                }
                finally { Marshal.FreeHGlobal(memory); }
            }
        });
        check("hidden native frame stays hidden during attachment and rejects actual topmost requests", () =>
        {
            using (var owner = new Form())
            using (var frame = new GroupFrame(Group("native-order")))
            {
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(GroupFrame).GetField("desktopOwner", flags).SetValue(frame, owner.Handle);
                SetWindowLongPtr(frame.Handle, -8, owner.Handle);
                frame.SetScreenBounds(new Rectangle(-24000, -24000, 420, 360));
                Assert(!IsWindowVisible(frame.Handle), "positioning showed frame before explicit Show");
                Assert(frame.IsDesktopWindow(owner.Handle) && !frame.IsDesktopWindow(frame.Handle), "desktop detection matched an ordinary window");
                Assert(SetWindowPos(frame.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13), "native topmost request failed");
                Assert((GetWindowLongPtr(frame.Handle, -20).ToInt64() & 8) == 0, "native request made frame topmost");
                Assert(!IsWindowVisible(frame.Handle), "order repair showed hidden frame");
            }
        });
        check("desktop events detect refresh and child movement but ignore unrelated windows and selection", () =>
        {
            // Hidden controls provide real HWNDs without showing a window or touching Explorer.
            using (var host = new Control())
            using (var child = new Control())
            using (var unrelated = new Control())
            using (var events = new DesktopEvents())
            {
                host.Controls.Add(child);
                var hostWindow = host.Handle; var childWindow = child.Handle; var otherWindow = unrelated.Handle;
                events.Watch(hostWindow, (uint)Process.GetCurrentProcess().Id);
                long revision = events.Revision;
                int changes = 0; events.Changed += () => changes++;
                foreach (uint kind in new uint[] { 0x8000, 0x8001, 0x8002, 0x8003, 0x8004, 0x800B, 0x800C })
                { events.Observe(kind, childWindow); Assert(events.Revision == ++revision, "refresh event missed"); }
                events.Observe(0x800B, hostWindow); Assert(events.Revision == ++revision, "view movement missed");
                events.Observe(0x800B, otherWindow); events.Observe(0x8006, childWindow); events.Observe(0x800B, IntPtr.Zero);
                Assert(events.Revision == revision, "unrelated event requested scan");
                Assert(changes == 8, "desktop changes did not wake sync or unrelated events woke it");
                int orderChanges = 0; events.OrderChanged += () => orderChanges++;
                events.Observe(3, otherWindow); Assert(orderChanges == 1 && events.Revision == revision, "foreground did not request independent order repair");
                events.Watch(hostWindow, (uint)Process.GetCurrentProcess().Id);
                Assert(events.Revision == revision, "unchanged desktop invalidated snapshot");
                events.Watch(otherWindow, (uint)Process.GetCurrentProcess().Id);
                Assert(events.Revision == ++revision, "recreated view did not invalidate snapshot");
                events.Dispose(); events.Observe(0x800B, otherWindow); events.Observe(3, otherWindow);
                Assert(events.Revision == revision && orderChanges == 1, "disposed observer remained active");
                Assert(changes == 8, "disposed observer still requested sync");
            }
        });
        check("unchanged transparency does not save repeatedly and final drag value flushes", () =>
        {
            using (var slider = new TransparencySlider { Width = 212, Value = 50 })
            {
                int saves = 0; slider.ValueChanged += (s, e) => saves++;
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var mouse = typeof(TransparencySlider).GetMethod("SetFromMouse", flags);
                var flush = typeof(TransparencySlider).GetMethod("Flush", flags);
                for (int i = 0; i < 20; i++) { mouse.Invoke(slider, new object[] { 106 }); flush.Invoke(slider, null); }
                Assert(saves == 0, "unchanged pointer position saved");
                mouse.Invoke(slider, new object[] { 206 }); flush.Invoke(slider, null);
                Assert(saves == 1 && slider.Value == 100, "final value lost");
                var key = typeof(TransparencySlider).GetMethod("OnKeyDown", flags);
                for (int i = 0; i < 20; i++) key.Invoke(slider, new object[] { new KeyEventArgs(Keys.Right) });
                Assert(saves == 1, "keyboard limit saved repeatedly");
            }
        });
        check("dual 4K desktop reflow keeps native drawing disabled without moving icons", () =>
        {
            var d = new Desktop(); var c = new LayoutController(new Layout(), state => { });
            c.EnableUnified(); c.Create(d, Group("one"), screens);
            c.AddMembers(d, "one", d.Data.Select(i => i.Id).ToArray());
            var dual = new[] { new Rectangle(0, 0, 3840, 2076), new Rectangle(-3840, -4, 3840, 2076) };
            d.Origin = new Point(-3840, -4);
            foreach (var item in d.Data) item.Position = new Point(23, 171);
            c.Start(d, dual);
            Assert(!d.IconsVisible && d.Writes == 0, "reset exposed native icons or moved them");
            foreach (var item in d.Data) item.Position = new Point(154, 171);
            c.Sync(d); Assert(!d.IconsVisible && d.Writes == 0, "later reflow exposed or moved native icons");
            Assert(c.State.Members.Count == 60 && c.State.PendingPositions == null, "reflow lost membership or journal");
        });
        check("60-item batch, no Shell writes during 120 frame geometry changes, persistent order and restore", () =>
        {
            var desktop = new Desktop { PositioningFlags = 5 }; Layout disk = null;
            var controller = new LayoutController(new Layout(), state => disk = state.Copy());
            controller.EnableUnified(); controller.Create(desktop, Group("one"), screens);
            controller.AddMembers(desktop, "one", desktop.Data.Select(i => i.Id));
            Assert(controller.State.Members.Count == 60, "batch lost items");
            Assert(!desktop.IconsVisible && desktop.Writes == 0, "grouping moved or exposed native icons");
            int writes = desktop.Writes;
            for (int i = 0; i < 120; i++) controller.SaveGroup("one", new Rectangle(i, i, 420, 360), "one");
            Assert(desktop.Writes == writes, "geometry touched Shell");
            var order = controller.State.Members.Select(m => m.Id).Reverse().ToArray(); controller.Reorder("one", order);
            controller.SavePreferences("plum", true); controller.Restore(desktop, screens);
            Assert(!disk.SessionActive && disk.Members.Count == 60 && desktop.PositioningFlags == 5, "exit did not restore");
            controller = new LayoutController(disk.Copy(), state => disk = state.Copy()); controller.Start(desktop, screens);
            Assert(controller.State.Members.Select(m => m.Id).SequenceEqual(order) && disk.AutoHideTitle && disk.Skin == "plum", "restart lost settings/order");
            Assert(!desktop.IconsVisible, "restart did not suppress native drawing");
        });
        check("whole batch validation, transfer and explicit return to desktop", () =>
        {
            var desktop = new Desktop(); var c = new LayoutController(new Layout(), state => { });
            c.EnableUnified(); c.Create(desktop, Group("one"), screens); c.Create(desktop, Group("two"), screens);
            int writes = desktop.Writes;
            try { c.AddMembers(desktop, "one", new[] { "item0", "missing" }); throw new Exception("invalid drop accepted"); } catch (InvalidOperationException) { }
            Assert(c.State.Members.Count == 0 && desktop.Writes == writes, "partial batch committed");
            c.AddMembers(desktop, "one", new[] { "item0", "item1", "item2" });
            c.AddMembers(desktop, "two", new[] { "item0", "item1" });
            Assert(c.State.Members.Count == 3 && c.State.Members.Count(m => m.GroupId == "two") == 2, "transfer duplicates");
            c.RemoveMembers(desktop, new[] { "item0", "item1" }, screens);
            Assert(c.State.Members.Count == 1 && screens.Any(r => r.Contains(desktop.Data[0].Position)), "explicit removal failed");
        });
        check("grouping no longer depends on native icon movement", () =>
        {
            var d = new Desktop(); var c = new LayoutController(new Layout(), state => { }); c.EnableUnified(); c.Create(d, Group("one"), screens);
            var before = d.Items(); d.Fail = true;
            c.AddMembers(d, "one", new[] { "item0", "item1" });
            Assert(c.State.Members.Count == 2 && d.Writes == 0 && d.Data.Select(i => i.Position).SequenceEqual(before.Select(i => i.Position)), "grouping still moved native icons");
        });
        check("crash session restart preserves original desktop positions", () =>
        {
            var d = new Desktop(); Layout disk = null; var c = new LayoutController(new Layout(), state => disk = state.Copy());
            c.EnableUnified(); c.Create(d, Group("one"), screens); c.AddMembers(d, "one", new[] { "item0", "item1" });
            var originals = c.State.Members.Select(m => new Point(m.OriginalX, m.OriginalY)).ToArray();
            c = new LayoutController(disk, state => disk = state.Copy()); c.Start(d, screens);
            Assert(c.State.Members.Select(m => new Point(m.OriginalX, m.OriginalY)).SequenceEqual(originals), "parking became original");
            c.Restore(d, screens); Assert(d.Data.Take(2).Select(i => i.Position).SequenceEqual(originals), "crash recovery wrong");
        });
        check("monitor reorder, 4K to 1080p, mixed DPI, unplug and reconnect", () =>
        {
            var a = new DisplayInfo { Id = "physical-a", Work = screens[1], Dpi = 192 };
            var b = new DisplayInfo { Id = "physical-b", Work = screens[0], Dpi = 96 };
            var g = Group("one"); g.X = 2800; g.Y = 1400; g.Width = 840; g.Height = 720;
            DisplayLayout.Remember(g, new[] { a, b }); var original = GridLayout.Bounds(g);
            DisplayLayout.Resolve(g, new[] { b, a }); Assert(GridLayout.Bounds(g) == original, "enumeration order changed location");
            var smaller = new DisplayInfo { Id = a.Id, Work = new Rectangle(0, 0, 1920, 1080), Dpi = 96 };
            DisplayLayout.Resolve(g, new[] { smaller, b }); Assert(smaller.Work.Contains(GridLayout.Bounds(g)) && g.Width == 420, "DPI/resize bounds");
            DisplayLayout.Resolve(g, new[] { b }); Assert(b.Work.Contains(GridLayout.Bounds(g)) && g.MonitorId == a.Id, "fallback lost affinity");
            DisplayLayout.Resolve(g, new[] { a, b }); Assert(GridLayout.Bounds(g) == original, "reconnect lost layout");
            var sameDpi = new DisplayInfo { Id = a.Id, Work = new Rectangle(0, 0, 1920, 1080), Dpi = a.Dpi };
            DisplayLayout.Resolve(g, new[] { sameDpi, b });
            Assert(g.Width == 420 && g.Height == 360, "same-DPI resolution change did not scale group");
        });
        check("monitor fallback preserves overlap and preferred records", () =>
        {
            var a = new DisplayInfo { Id = "a", Work = new Rectangle(0, 0, 1920, 1080) };
            var b = new DisplayInfo { Id = "b", Work = new Rectangle(1920, 0, 1920, 1080) };
            var layout = new Layout();
            for (int i = 0; i < 4; i++) { var g = Group("g" + i); g.X = i % 2 * 1920; g.Y = i / 2 * 540; g.Width = 900; g.Height = 500; DisplayLayout.Remember(g, new[] { a, b }); layout.Groups.Add(g); }
            var preferred = layout.Groups.Select(g => g.MonitorId).ToArray(); DisplayLayout.ResolveAll(layout, new[] { a });
            Assert(layout.Groups.All(g => a.Work.Contains(GridLayout.Bounds(g))), "offscreen fallback");
            Assert(layout.Groups.Any(g => layout.Groups.Any(h => h.Id != g.Id && GridLayout.Bounds(g).IntersectsWith(GridLayout.Bounds(h)))), "overlap removed");
            Assert(layout.Groups.Select(g => g.MonitorId).SequenceEqual(preferred), "fallback overwrote preference");
        });
        check("overlap survives save, unplug restart, reconnect and explicit monitor transfer", () =>
        {
            var a = new DisplayInfo { Id = "left", Work = new Rectangle(-1920, 0, 1920, 1040), Dpi = 96 };
            var b = new DisplayInfo { Id = "right", Work = new Rectangle(0, 0, 3840, 2080), Dpi = 192 };
            var layout = new Layout { UnifiedIcons = true };
            var one = Group("one"); one.X = 300; one.Y = 300;
            var two = Group("two"); two.X = 400; two.Y = 400;
            layout.Groups.Add(one); layout.Groups.Add(two);
            foreach (var g in layout.Groups) DisplayLayout.Remember(g, new[] { a, b });
            var original = layout.Groups.Select(GridLayout.Bounds).ToArray();
            Layout disk = layout.Copy(); var c = new LayoutController(disk, value => disk = value.Copy());
            c.SaveGroup("two", new Rectangle(350, 350, two.Width, two.Height), two.Name, new[] { a, b });
            original[1] = GridLayout.Bounds(c.State.Groups[1]);
            c.SaveMonitors(new[] { a });
            c = new LayoutController(disk, value => disk = value.Copy()); c.SaveMonitors(new[] { a });
            Assert(c.State.Groups.All(g => a.Work.Contains(GridLayout.Bounds(g)) && g.MonitorId == b.Id), "restart lost monitor affinity");
            c.SaveMonitors(new[] { b, a });
            Assert(c.State.Groups.Select(GridLayout.Bounds).SequenceEqual(original), "reconnect changed overlap");
            c.SaveGroup("two", new Rectangle(-1000, 200, 420, 360), two.Name, new[] { a, b });
            c = new LayoutController(disk, value => disk = value.Copy()); c.SaveMonitors(new[] { b, a });
            Assert(c.State.Groups[1].MonitorId == a.Id && GridLayout.Bounds(c.State.Groups[1]) == new Rectangle(-1000, 200, 420, 360), "cross-monitor move not remembered");
            var taskbar = new DisplayInfo { Id = a.Id, Work = new Rectangle(-1880, 40, 1880, 1000), Dpi = 96 };
            c.SaveMonitors(new[] { taskbar, b });
            Assert(taskbar.Work.Contains(GridLayout.Bounds(c.State.Groups[1])), "taskbar change escaped work area");
            Assert(GridLayout.Bounds(c.State.Groups[0]) == original[0], "other monitor disturbed");
        });
        check("bounds clamp includes taskbar, negative origins and oversized windows", () =>
        {
            var work = new Rectangle(-1920, 0, 1920, 1040);
            foreach (var r in new[] { new Rectangle(-5000, -500, 420, 360), new Rectangle(-50, 900, 420, 360), new Rectangle(-2000, 0, 4000, 2000) })
                Assert(work.Contains(DisplayLayout.Clamp(r, work)), "clamp escaped");
        });
        check("FileDrop preserves complete selection and outbound format excludes file operations", () =>
        {
            var data = new DataObject(); data.SetData(DataFormats.FileDrop, new[] { "a", "b", "c" }); Assert(DropItems.Read(data).Length == 3, "multi-drop parse");
            data = new DataObject(); data.SetData(IconCanvas.InternalFormat, false, new[] { "a", "b" });
            Assert(!data.GetDataPresent(DataFormats.FileDrop) && DropItems.Read(data).Length == 2, "outbound filesystem operation enabled");
        });
        check("desktop release only after left release; Esc, right click and foreign apps cancel", () =>
        {
            Assert(DesktopDrop.ShouldReturn(false, 0, true), "release ignored");
            Assert(DesktopDrop.ShouldReturn(false, 8, true), "Ctrl release ignored");
            Assert(!DesktopDrop.ShouldReturn(true, 0, true), "Escape removed membership");
            Assert(!DesktopDrop.ShouldReturn(false, 1, true), "press removed membership");
            Assert(!DesktopDrop.ShouldReturn(false, 2, true), "right click removed membership");
            Assert(!DesktopDrop.ShouldReturn(false, 0, false), "foreign window accepted");
        });
        check("desktop batch drop places near pointer, skips existing icons and fences, survives restart", () =>
        {
            var d = new Desktop(); Layout disk = null; var c = new LayoutController(new Layout(), state => disk = state.Copy());
            c.EnableUnified(); c.Create(d, Group("one"), screens); c.AddMembers(d, "one", new[] { "item0", "item1", "item2" });
            var unrelated = d.Data.Skip(3).Select(i => i.Position).ToArray();
            c.RemoveMembers(d, new[] { "item0", "item1", "item2" }, screens, new Point(1700, 600));
            Assert(c.State.Members.Count == 0, "batch not removed");
            Assert(d.Data.Skip(3).Select(i => i.Position).SequenceEqual(unrelated), "unrelated icon moved");
            Assert(d.Data.Take(3).All(i => Math.Abs(i.Position.X - 1700) < 250 && Math.Abs(i.Position.Y - 600) < 250), "drop not near pointer");
            var positions = d.Data.Take(3).Select(i => i.Position).ToArray();
            Assert(positions.Distinct().Count() == 3, "drop overlaps");
            c = new LayoutController(disk, state => disk = state.Copy()); c.Start(d, screens);
            Assert(c.State.Members.Count == 0 && d.Data.Take(3).Select(i => i.Position).SequenceEqual(positions), "restart re-enrolled dropped members");
        });
        check("desktop drop with no space rejects whole selection without mutation", () =>
        {
            var d = new Desktop(); var c = new LayoutController(new Layout(), state => { }); c.EnableUnified();
            c.Create(d, Group("one"), screens); c.AddMembers(d, "one", new[] { "item0", "item1" });
            var before = d.Data.Select(i => i.Position).ToArray(); int writes = d.Writes;
            try { c.RemoveMembers(d, new[] { "item0", "item1" }, new[] { new Rectangle(100, 100, 200, 200) }, new Point(150, 150)); throw new Exception("full desktop accepted"); }
            catch (InvalidOperationException) { }
            Assert(c.State.Members.Count == 2 && d.Writes == writes && d.Data.Select(i => i.Position).SequenceEqual(before), "partial removal");
        });
        check("desktop drop failure restores parked positions and members", () =>
        {
            var d = new Desktop(); var c = new LayoutController(new Layout(), state => { }); c.EnableUnified();
            c.Create(d, Group("one"), screens); c.AddMembers(d, "one", new[] { "item0", "item1" });
            var before = d.Data.Select(i => i.Position).ToArray(); d.Fail = true;
            try { c.RemoveMembers(d, new[] { "item0", "item1" }, screens, new Point(1700, 700)); throw new Exception("failure ignored"); }
            catch (RecoveryRequiredException) { }
            Assert(c.State.Members.Count == 2 && d.Data.Select(i => i.Position).SequenceEqual(before), "failed drop lost membership");
        });
        check("two icons fit a narrow fence; custom scrollbar has no native white track", () =>
        {
            using (var frame = new GroupFrame(new GroupData { Id = "preview", Name = "test", Width = 457, Height = 450 }))
            {
                frame.CreateControl();
                frame.Icons.SetMembers(new[] { new MemberData { Id = Environment.GetFolderPath(Environment.SpecialFolder.Windows) }, new MemberData { Id = typeof(object).Assembly.Location } });
                using (var bitmap = RenderControl(frame)) { }
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                int max = (int)typeof(IconCanvas).GetProperty("MaxScroll", flags).GetValue(frame.Icons);
                Assert(max == 0, "two icons unnecessarily scroll");
                var style = (CreateParams)typeof(IconCanvas).GetProperty("CreateParams", flags).GetValue(frame.Icons);
                Assert((style.Style & 0x300000) == 0, "native scrollbar retained");
                int invalidations = 0; frame.Invalidated += (sender, args) => invalidations++;
                for (int i = 0; i < 10; i++) frame.Size = new Size(457 + i * 8, 450);
                Assert(invalidations > 0, "resize failed to invalidate old frame content");
            }
        });
        check("background transparency persists independently of style and keeps icon foreground opaque", () =>
        {
            Layout saved = null;
            var c = new LayoutController(new Layout(), state => saved = state.Copy());
            Assert(c.State.BackgroundTransparency == 0, "legacy default changed");
            c.SavePreferences("paper", true, 65);
            c = new LayoutController(saved.Copy(), state => saved = state.Copy());
            c.SavePreferences("plum", false);
            Assert(saved.BackgroundTransparency == 65, "style switch lost transparency");
            c.SavePreferences("mist", false, 200);
            Assert(saved.BackgroundTransparency == 100, "range not bounded");
            using (var frame = new GroupFrame(Group("transparent")))
            {
                frame.ApplyPreferences(false, 65);
                Assert(frame.Opacity == 1 && frame.TransparencyKey == Theme.TransparencyColor && frame.Icons.BackColor == Theme.TransparencyColor, "foreground faded or background opaque");
                frame.ApplyPreferences(false, 0);
                Assert(frame.TransparencyKey == Color.Empty && frame.Icons.BackColor == Theme.Background, "opaque mode not restored");
            }
        });
        check("Shell namespace labels use display names instead of GUIDs", () =>
        {
            using (var visual = ShellVisual.Load("::{645FF040-5081-101B-9F08-00AA002F954E}", 48))
                Assert(!string.IsNullOrWhiteSpace(visual.Name) && !visual.Name.Contains("645FF040") && visual.Name != "桌面项目", "recycle bin display name unavailable");
        });
        check("shortcut custom icon and Unicode name survive loading without changing the shortcut", () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "tidy-shortcut-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory); object shell = null, shortcut = null;
            try
            {
                string icon = Path.Combine(directory, "custom,icon.ico"), link = Path.Combine(directory, "开发工具.lnk");
                string initialLink = Path.Combine(directory, "shortcut.lnk");
                using (var file = File.Create(icon)) SystemIcons.Error.Save(file);
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                shortcut = ((dynamic)shell).CreateShortcut(initialLink);
                ((dynamic)shortcut).TargetPath = Application.ExecutablePath;
                ((dynamic)shortcut).IconLocation = icon + ",0"; ((dynamic)shortcut).Save();
                File.Move(initialLink, link);
                byte[] before = File.ReadAllBytes(link);
                using (var visual = ShellVisual.Load(link, 48))
                {
                    Assert(visual.Name.Contains("开发工具"), "Unicode label lost");
                    int red = 0;
                    for (int y = 0; y < visual.Image.Height; y++) for (int x = 0; x < visual.Image.Width; x++)
                    { var pixel = visual.Image.GetPixel(x, y); if (pixel.A > 100 && pixel.R > pixel.G * 1.4 && pixel.R > pixel.B * 1.4) red++; }
                    Assert(red > 20, "custom icon was replaced by generic application icon");
                }
                Assert(before.SequenceEqual(File.ReadAllBytes(link)), "shortcut modified while reading");
            }
            finally
            {
                if (shortcut != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
                if (shell != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
                Directory.Delete(directory, true);
            }
        });
        check("desktop scan runs on STA, rejects overlapping jobs and does not wait during disposal", () =>
        {
            using (var started = new System.Threading.ManualResetEvent(false))
            using (var release = new System.Threading.ManualResetEvent(false))
            using (var finished = new System.Threading.ManualResetEvent(false))
            {
                int caller = System.Threading.Thread.CurrentThread.ManagedThreadId, worker = caller;
                System.Threading.ApartmentState apartment = System.Threading.ApartmentState.Unknown;
                var scan = new DesktopScan(() =>
                {
                    worker = System.Threading.Thread.CurrentThread.ManagedThreadId; apartment = System.Threading.Thread.CurrentThread.GetApartmentState();
                    started.Set(); release.WaitOne(); finished.Set(); return new DesktopSnapshot();
                });
                try
                {
                    Assert(scan.Start() && started.WaitOne(3000), "scan did not start");
                    Assert(worker != caller && apartment == System.Threading.ApartmentState.STA, "scan on UI thread");
                    Assert(!scan.Start() && scan.Take() == null, "scan jobs accumulated");
                    scan.Dispose(); Assert(!scan.Start(), "disposed scan restarted");
                    release.Set(); Assert(finished.WaitOne(3000), "worker never returned");
                    Assert(scan.Take() == null, "disposed scan published stale result");
                }
                finally { release.Set(); finished.WaitOne(3000); scan.Dispose(); }
            }
        });
        check("desktop scan reports errors and can retry without accumulating tasks", () =>
        {
            int calls = 0; var threads = new HashSet<int>();
            using (var scan = new DesktopScan(() => { threads.Add(System.Threading.Thread.CurrentThread.ManagedThreadId); if (++calls == 1) throw new IOException("injected"); return new DesktopSnapshot(); }))
            {
                for (int attempt = 0; attempt < 50; attempt++)
                {
                    Assert(scan.Start(), "retry blocked"); var watch = Stopwatch.StartNew(); DesktopSnapshot result;
                    while ((result = scan.Take()) == null && watch.ElapsedMilliseconds < 3000) System.Threading.Thread.Sleep(5);
                    Assert(result != null && (attempt == 0 ? result.Error is IOException : result.Error == null), "scan error not delivered/recovered");
                }
                Assert(threads.Count == 1, "periodic scans created new threads");
            }
        });
        check("desktop scan wakes its consumer as soon as a complete snapshot is available", () =>
        {
            using (var ready = new System.Threading.ManualResetEvent(false))
            using (var scan = new DesktopScan(() => new DesktopSnapshot { Items = new List<DesktopItem>() }))
            {
                DesktopSnapshot received = null;
                scan.Completed += () => { received = scan.Take(); ready.Set(); };
                Assert(scan.Start() && ready.WaitOne(3000), "completion did not wake the consumer");
                Assert(received != null && received.Items.Count == 0 && received.Error == null,
                    "completion fired before the snapshot was available");
            }
        });
        check("desktop changes during a scan force fresh visibility checks instead of accepting an old clean snapshot", () =>
        {
            var d = new Desktop(); var c = new LayoutController(new Layout(), state => { });
            c.EnableUnified(); c.Create(d, Group("one"), screens); c.AddMembers(d, "one", new[] { "item0" });
            var snapshot = new DesktopSnapshot { Items = d.Items(), Origin = d.Origin };
            for (int refresh = 0; refresh < 5; refresh++)
            {
                d.Data[0].Position = new Point(40 + refresh * 100, 40);
                d.IconsVisible = true;
                Assert(!snapshot.NeedsSync(c.State, screens), "test snapshot should still report native drawing disabled");
                Assert(snapshot.NeedsSync(c.State, screens, true), "new desktop event accepted an obsolete snapshot");
                Assert(c.Sync(d) && !d.IconsVisible && d.Writes == 0,
                    "fresh sync did not repair the latest refresh");
            }
            Assert(c.State.Members.Count == 1 && c.State.PendingPositions == null, "refresh damaged layout");
        });
        check("unchanged drag feedback does not repaint every mouse movement", () =>
        {
            using (var canvas = new IconCanvas())
            {
                canvas.CreateControl(); int invalidations = 0; canvas.Invalidated += (s, e) => invalidations++;
                var data = new DataObject(); data.SetData(IconCanvas.InternalFormat, new[] { "item" });
                var args = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Move, DragDropEffects.None);
                canvas.AcceptDrag(args); int first = invalidations;
                for (int i = 0; i < 100; i++) canvas.AcceptDrag(args);
                Assert(first > 0 && invalidations == first, "unchanged drag feedback repainted");
                canvas.AcceptLeave(); Assert(invalidations > first, "leave did not clear feedback");
            }
        });
        check("desktop snapshot detects identity and visibility changes but ignores native reflow", () =>
        {
            var d = new Desktop(); var c = new LayoutController(new Layout(), state => { });
            c.EnableUnified(); c.Create(d, Group("one"), screens); c.AddMembers(d, "one", new[] { "item0" });
            var snapshot = new DesktopSnapshot { Items = d.Items(), Origin = d.Origin };
            Assert(!snapshot.NeedsSync(c.State, screens), "unchanged snapshot triggered writes");
            snapshot.Items[0].Position = Point.Empty; Assert(!snapshot.NeedsSync(c.State, screens), "native reflow requested redundant movement");
            snapshot.IconsVisible = true; Assert(snapshot.NeedsSync(c.State, screens), "native visibility change missed"); snapshot.IconsVisible = false;
            snapshot.Items = d.Items(); snapshot.Items[0].Id = "ITEM0"; Assert(snapshot.NeedsSync(c.State, screens), "case-only rename missed");
            snapshot.Items = d.Items(); snapshot.Items[0].Id = "renamed"; Assert(snapshot.NeedsSync(c.State, screens), "rename missed");
            snapshot.Items = d.Items(); snapshot.Items[0].Identity = "replacement"; Assert(snapshot.NeedsSync(c.State, screens), "replacement missed");
            Assert(c.State.Members[0].Id == "item0", "snapshot changed membership");
        });
        check("fallback icons retry and successful icons survive a transient reload failure", () =>
        {
            int attempts = 0;
            using (var canvas = new IconCanvas((id, size) => ++attempts == 3
                ? new ShellVisual { Name = "real icon", Image = SystemIcons.Information.ToBitmap() }
                : ShellVisual.Placeholder(id)) { Size = new Size(500, 420) })
            {
                canvas.CreateControl(); canvas.SetMembers(new[] { new MemberData { Id = "retry.lnk" } });
                Action pump = () =>
                {
                    using (var image = RenderControl(canvas)) { }
                    var watch = Stopwatch.StartNew();
                    while (canvas.IsLoadingVisuals && watch.ElapsedMilliseconds < 3000) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
                    Assert(!canvas.IsLoadingVisuals, "icon loader timed out");
                };
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var visuals = (Dictionary<string, ShellVisual>)typeof(IconCanvas).GetField("visuals", flags).GetValue(canvas);
                pump(); Assert(visuals["retry.lnk"].IsFallback, "expected initial fallback");
                canvas.RetryVisuals(); pump(); Assert(visuals["retry.lnk"].IsFallback, "expected retryable failure");
                canvas.RetryVisuals(); pump(); Assert(!visuals["retry.lnk"].IsFallback && attempts == 3, "fallback remained cached");
                ((HashSet<string>)typeof(IconCanvas).GetField("ready", flags).GetValue(canvas)).Clear();
                pump(); Assert(!visuals["retry.lnk"].IsFallback && visuals["retry.lnk"].Name == "real icon", "temporary failure replaced valid icon");
            }
        });
        check("slow icon loader stays off UI, cancels stale results and bounds queued work", () =>
        {
            using (var started = new System.Threading.ManualResetEvent(false))
            using (var release = new System.Threading.ManualResetEvent(false))
            {
                int caller = System.Threading.Thread.CurrentThread.ManagedThreadId, worker = caller;
                System.Threading.ApartmentState apartment = System.Threading.ApartmentState.Unknown;
                using (var queue = new ShellVisualQueue((id, size) =>
                {
                    worker = System.Threading.Thread.CurrentThread.ManagedThreadId; apartment = System.Threading.Thread.CurrentThread.GetApartmentState();
                    started.Set(); release.WaitOne(); return ShellVisual.Placeholder(id);
                }))
                {
                    try
                    {
                        queue.Request("old", 48); Assert(started.WaitOne(3000), "worker did not start");
                        Assert(worker != caller && apartment == System.Threading.ApartmentState.STA, "Shell loading is not on STA worker");
                        for (int i = 0; i < 1000; i++) queue.Request("queued" + i, 48);
                        var field = typeof(ShellVisualQueue).GetField("pending", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        Assert(((System.Collections.IDictionary)field.GetValue(queue)).Count <= 128, "unbounded queue");
                        queue.Reset(); queue.Request("new", 96); release.Set();
                        var received = new List<string>(); var watch = Stopwatch.StartNew();
                        while (queue.HasWork && watch.ElapsedMilliseconds < 3000)
                        {
                            foreach (var result in queue.TakeCompleted()) { received.Add(result.Key); result.Value.Dispose(); }
                            System.Threading.Thread.Sleep(5);
                        }
                        Assert(received.SequenceEqual(new[] { "new" }), "stale result applied after reset");
                    }
                    finally { release.Set(); }
                }
            }
        });
        check("large groups bound nearby icon cache and refresh releases old images", () =>
        {
            using (var canvas = new IconCanvas { Size = new Size(500, 420) })
            {
                canvas.SetMembers(Enumerable.Range(0, 1000).Select(i => new MemberData { Id = Path.Combine(Path.GetTempPath(), "tidy-cache-probe-" + i + ".lnk") }));
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var images = (System.Collections.IDictionary)typeof(IconCanvas).GetField("visuals", flags).GetValue(canvas);
                var cells = (System.Collections.IDictionary)typeof(IconCanvas).GetField("cells", flags).GetValue(canvas);
                Assert(images.Count == 0, "offscreen icons loaded eagerly");
                for (int page = 0; page < 8; page++)
                {
                    typeof(IconCanvas).GetField("scrollY", flags).SetValue(canvas, page * 420);
                    using (var bitmap = RenderControl(canvas)) { }
                    Assert(images.Count <= 90 && cells.Count <= 30, "cache grew with scrolled history");
                }
                canvas.RefreshVisuals(); Assert(images.Count == 0 && cells.Count == 0, "refresh retained old visuals");
            }
        });
        check("crowded monitor preserves usable group sizes", () =>
        {
            var monitor = new DisplayInfo { Id = "small", Work = new Rectangle(0, 0, 640, 480), Dpi = 96 };
            var layout = new Layout();
            for (int i = 0; i < 8; i++) { var g = Group("dense" + i); DisplayLayout.Remember(g, new[] { monitor }); layout.Groups.Add(g); }
            DisplayLayout.ResolveAll(layout, new[] { monitor });
            Assert(layout.Groups.All(g => g.Width >= 240 && g.Height >= 180 && monitor.Work.Contains(GridLayout.Bounds(g))), "groups shrank");
        });
        check("locked frame blocks dragging while unlocked frame reaches drag handler", () =>
        {
            var group = Group("locked"); group.Locked = true;
            using (var frame = new GroupFrame(group))
            {
                int starts = 0; frame.DragStarting = f => { starts++; return false; };
                var method = typeof(GroupFrame).GetMethod("OnMouseDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var down = new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0);
                method.Invoke(frame, new object[] { down }); Assert(starts == 0, "locked frame moved");
                group.Locked = false; method.Invoke(frame, new object[] { down }); Assert(starts == 1, "unlock did not restore dragging");
            }
        });
        return count;
    }

    private static Button FindButton(Control parent, string text)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is Button && child.Text == text) return (Button)child;
            var found = FindButton(child, text); if (found != null) return found;
        }
        return null;
    }
    private static void DrawChildren(Control parent, Graphics graphics, Point offset)
    {
        foreach (Control child in parent.Controls)
        {
            if (parent.Visible && !child.Visible) continue;
            child.CreateControl();
            using (var bitmap = new Bitmap(Math.Max(1, child.Width), Math.Max(1, child.Height)))
            { child.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); graphics.DrawImageUnscaled(bitmap, offset.X + child.Left, offset.Y + child.Top); }
            DrawChildren(child, graphics, new Point(offset.X + child.Left, offset.Y + child.Top));
        }
    }
    private static Bitmap RenderControl(Control control)
    {
        control.CreateControl(); control.PerformLayout();
        var bitmap = new Bitmap(control.ClientSize.Width, control.ClientSize.Height);
        using (var g = Graphics.FromImage(bitmap))
        {
            control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            DrawChildren(control, g, Point.Empty);
        }
        return bitmap;
    }

    internal static int Render()
    {
        Application.EnableVisualStyles(); Directory.CreateDirectory("build/unified-ui");
        using (var probe = new GroupFrame(new GroupData { Id = "alpha-probe", Name = "probe", X = -24000, Y = -24000, Width = 420, Height = 360 }))
        {
            probe.AttachToDesktop();
            var field = typeof(GroupFrame).GetField("backgroundLayer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (int transparency in new[] { 50, 100, 25 })
            {
                probe.ApplyPreferences(false, transparency); Application.DoEvents();
                var layer = (Form)field.GetValue(probe);
                Assert(layer != null && layer.Visible && layer.AllowDrop && layer.Opacity > 0 && layer.Opacity < 1, "background not interactive/translucent");
                Assert(probe.IsAttachedToDesktop() && probe.Opacity == 1, "foreground owner/alpha changed");
                probe.SetScreenBounds(new Rectangle(-24000, -24000, 440 + transparency, 400));
                Assert(layer.Bounds == probe.Bounds, "background does not follow frame");
            }
            var previous = (Form)field.GetValue(probe);
            probe.ApplyPreferences(false, 0);
            Assert(previous.IsDisposed && field.GetValue(probe) == null, "background layer leaked");
            Console.WriteLine("Transparency layer: owner, alpha, drop target, resize and disposal passed.");
        }
        using (var application = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-24000, -24000), ShowInTaskbar = false })
        using (var frame = new GroupFrame(new GroupData { Id = "order-probe", Name = "probe", X = -24000, Y = -24000, Width = 420, Height = 360 }))
        {
            application.Show(); frame.Show(); frame.AttachToDesktop();
            var field = typeof(GroupFrame).GetField("backgroundLayer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (int transparency in new[] { 0, 50, 100 })
            {
                frame.ApplyPreferences(false, transparency);
                SetWindowPos(frame.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x13);
                frame.Icons.Focus(); Application.DoEvents();
                Assert(frame.Icons.Focused, "desktop layer lost keyboard focus");
                Assert(IsAbove(application.Handle, frame.Handle), "focus raised group above application");
                frame.SetScreenBounds(new Rectangle(-24000, -24000, 440, 380));
                var background = (Form)field.GetValue(frame);
                if (background != null)
                {
                    SetWindowPos(background.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
                    Application.DoEvents();
                    Assert(IsAbove(frame.Handle, background.Handle) && IsAbove(application.Handle, background.Handle), "background escaped desktop layer");
                }
                Assert(IsAbove(application.Handle, frame.Handle), "move raised group above application");
            }
            Console.WriteLine("Desktop z-order: focus, top requests, move, transparency and background passed.");
        }
        var items = new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows), typeof(object).Assembly.Location, Application.ExecutablePath };
        foreach (string skin in new[] { "mist", "paper", "outline", "plum" })
        {
            Theme.Apply(skin);
            using (var frame = new GroupFrame(new GroupData { Id = "preview", Name = "工作与灵感", X = 0, Y = 0, Width = 560, Height = 430 }))
            using (var settings = new SettingsForm(true, enabled => { }, skin, false, (s, h) => { }))
            {
                var layout = new Layout(); layout.Groups.Add(Group("工作")); layout.Groups.Add(Group("常用应用"));
                settings.StartPosition = FormStartPosition.Manual; settings.Location = new Point(-24000, -24000);
                settings.Show(); Application.DoEvents();
                frame.ApplyPreferences(false); frame.Icons.SetMembers(items.Select(id => new MemberData { Id = id })); frame.MemberCount = 3;
                using (var initial = RenderControl(frame)) { }
                var loadingWatch = Stopwatch.StartNew();
                while (frame.Icons.IsLoadingVisuals && loadingWatch.ElapsedMilliseconds < 5000) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                Assert(!frame.Icons.IsLoadingVisuals, "render icons did not finish loading");
                using (var frameImage = RenderControl(frame))
                using (var settingsImage = RenderControl(settings))
                using (var output = new Bitmap(frameImage.Width + settingsImage.Width + 60, Math.Max(frameImage.Height * 2 + 80, settingsImage.Height + 260)))
                using (var g = Graphics.FromImage(output))
                {
                    g.Clear(Color.FromArgb(13, 20, 28));
                    g.DrawImageUnscaled(frameImage, 20, 20); g.DrawImageUnscaled(settingsImage, frameImage.Width + 40, 20);
                    frame.ApplyPreferences(true);
                    using (var bitmap = RenderControl(frame)) g.DrawImageUnscaled(bitmap, 20, frameImage.Height + 50);
                    using (var menu = Theme.Menu())
                    {
                        menu.Items.Add("打开"); menu.Items.Add("在资源管理器中查看"); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("移回普通桌面");
                        menu.Show(new Point(-24000, -24000)); Application.DoEvents();
                        using (var bitmap = new Bitmap(menu.Width, menu.Height)) { menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); g.DrawImageUnscaled(bitmap, frameImage.Width + 40, settingsImage.Height + 50); }
                        menu.Close();
                    }
                    output.Save("build/unified-ui/" + skin + ".png", ImageFormat.Png);
                }
                Console.WriteLine("Settings " + skin + " dpi=" + settings.DeviceDpi + " client=" + settings.ClientSize);
                settings.Close();
            }
        }
        // Paint cost with cached visual data; this is a rendering benchmark, not a desktop FPS claim.
        foreach (int count in new[] { 10, 30, 60 })
        using (var view = new IconCanvas { Size = new Size(800, 650) })
        {
            var directory = "build/unified-ui/items"; Directory.CreateDirectory(directory);
            var ids = Enumerable.Range(0, count).Select(i => Path.GetFullPath(Path.Combine(directory, "示例项目 " + i + ".txt"))).ToArray();
            foreach (var id in ids) File.WriteAllText(id, "render test");
            view.SetMembers(ids.Select(id => new MemberData { Id = id }));
            using (var bitmap = new Bitmap(view.Width, view.Height))
            {
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < 60; i++) view.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                Console.WriteLine(count + " cached icons / 60 paints: " + watch.ElapsedMilliseconds + " ms; no Shell positioning");
            }
        }
        Console.WriteLine("Actual controls rendered to build/unified-ui, no desktop capture."); return 0;
    }
}
