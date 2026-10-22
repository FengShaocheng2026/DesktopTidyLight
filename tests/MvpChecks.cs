using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using DesktopTidyLight;

internal static class MvpChecks
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    private static readonly Rectangle[] Screens = { new Rectangle(0, 0, 2000, 1200) };
    private static int passed;

    private sealed class FakeDesktop : IDesktopView
    {
        internal readonly List<DesktopItem> Data = new List<DesktopItem>();
        internal readonly List<string> Selection = new List<string>();
        public IList<string> SelectedIds() { return Selection; }
        public int PositioningFlags { get; set; }
        public bool IconsVisible { get; private set; } = true;
        public void SetIconsVisible(bool visible) { IconsVisible = visible; }
        public Point Origin { get; set; }
        public Size Spacing { get; set; } = new Size(100, 100);
        public int IconSize { get { return Spacing.Width; } }
        internal int Writes;
        internal bool FailMoveOnce, FailMoveAlways, IgnoreMoveOnce, ReorderOnAuto;
        public List<DesktopItem> Items() { return Data.Select(i => new DesktopItem { Id = i.Id, Identity = i.Identity, Position = i.Position }).ToList(); }
        public void SetPositioningFlags(int flags)
        {
            PositioningFlags = flags;
            if (ReorderOnAuto && (flags & 1) != 0)
                for (int i = 0; i < Data.Count; i++) Data[i].Position = new Point(900, i * 100);
        }
        public void SetPositions(IDictionary<string, Point> positions)
        {
            Writes++;
            if ((PositioningFlags & 1) != 0) throw new Exception("Cannot position while auto arranged");
            if (IgnoreMoveOnce) { IgnoreMoveOnce = false; return; }
            foreach (var point in positions)
            {
                Data.Single(i => i.Id == point.Key).Position = point.Value;
                if (FailMoveOnce || FailMoveAlways) { FailMoveOnce = false; throw new IOException("Injected partial Shell failure"); }
            }
        }
        internal void Add(string id, int x, int y) { Data.Add(new DesktopItem { Id = id, Identity = "file:" + id, Position = new Point(x, y) }); }
        internal Point At(string id) { return Data.Single(i => i.Id == id).Position; }
        internal void Drag(string id, int x, int y)
        {
            Data.Single(i => i.Id == id).Position = new Point(x, y);
            if (!Selection.Contains(id)) Selection.Add(id);
        }
    }

    private sealed class Scenario
    {
        internal readonly FakeDesktop Desktop = new FakeDesktop();
        internal Layout Disk = new Layout();
        internal readonly LayoutController Controller;
        internal int Saves;
        internal int FailSaveAt;
        internal int FailSaveFrom;
        internal Scenario(int flags = 0)
        {
            Desktop.PositioningFlags = flags;
            Desktop.Add("a.txt", 900, 0); Desktop.Add("folder", 900, 100); Desktop.Add("link.lnk", 900, 200);
            Controller = new LayoutController(Disk.Copy(), state =>
            {
                Saves++;
                if (Saves == FailSaveAt || (FailSaveFrom > 0 && Saves >= FailSaveFrom)) throw new IOException("Injected disk failure");
                Disk = state.Copy();
            });
            Controller.Create(Desktop, Group("one", 0, 364, 408), Screens);
        }
        internal void JoinThree()
        {
            Desktop.Drag("a.txt", 40, 90); Desktop.Drag("folder", 140, 90); Desktop.Drag("link.lnk", 240, 90);
            Controller.Sync(Desktop);
        }
    }

    private static GroupData Group(string id, int x, int width, int height)
    {
        return new GroupData { Id = id, Name = id, X = x, Y = 0, Width = width, Height = height };
    }

    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static void Check(string name, Action action)
    {
        action(); passed++; Console.WriteLine("PASS " + name);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--desktop-diagnostics")
        {
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            var layout = LayoutStore.Load();
            var screens = System.Windows.Forms.Screen.AllScreens;
            foreach (var screen in screens) Console.WriteLine("SCREEN " + screen.DeviceName + " bounds=" + screen.Bounds + " work=" + screen.WorkingArea + " primary=" + screen.Primary);
            using (var shell = new DesktopShell())
            {
                var items = shell.Items(); var work = screens.Select(s => s.WorkingArea).ToArray();
                Console.WriteLine("DESKTOP origin=" + shell.Origin + " flags=" + shell.PositioningFlags + " iconsVisible=" + shell.IconsVisible + " items=" + items.Count + " members=" + layout.Members.Count + " session=" + layout.SessionActive + " pending=" + (layout.PendingPositions != null));
                foreach (var item in items)
                {
                    bool member = layout.Members.Any(m => m.Id == item.Id);
                    var point = new Point(item.Position.X + shell.Origin.X, item.Position.Y + shell.Origin.Y);
                    var display = screens.FirstOrDefault(screen => screen.Bounds.Contains(point));
                    Console.WriteLine((member ? "MEMBER " : "LOOSE ") + Path.GetFileName(item.Id) + " native=" + item.Position + " screen=" + point + " on=" + (display == null ? "offscreen" : display.DeviceName));
                }
            }
            return 0;
        }
        if (args.Length == 1 && args[0] == "--preview-readcheck")
        {
            using (var shell = new DesktopShell())
            {
                var ids = shell.Items().Select(i => i.Id).ToArray(); shell.PreparePositions(ids);
                try { typeof(DesktopShell).GetMethod("ValidatePreviewIndices", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(shell, new object[] { ids }); Console.WriteLine("Preview index verification passed"); return 0; }
                catch (Exception error) { Console.WriteLine(error); return 1; }
            }
        }
        if (args.Length == 1 && args[0] == "--render-ui") return UiRenderCheck.Run();
        if (args.Length == 1 && args[0] == "--shell-many") return ShellSmokeCheck.Run(21);
        if (args.Length == 1 && args[0] == "--performance") return PerformanceChecks.Run();
        if (args.Length == 1 && args[0] == "--unified-render") return UnifiedChecks.Render();
        if (args.Length == 1 && args[0] == "--native-visibility") return UnifiedChecks.NativeVisibility();
        if (args.Length == 1 && args[0] == "--managed-refresh-check") return UnifiedChecks.NativeVisibility(true);
        if (args.Length == 1 && args[0] == "--unified-shell") return ShellSmokeCheck.Run(7, true);
        if (args.Length == 1 && args[0] == "--shell-smoke") return ShellSmokeCheck.Run();
        try
        {
            Check("three mixed items align in a stable row", () =>
            {
                var s = new Scenario(); s.JoinThree();
                Require(s.Controller.State.Members.Count == 3, "members");
                Require(s.Desktop.At("a.txt") == new Point(32, 76) && s.Desktop.At("folder") == new Point(132, 76) && s.Desktop.At("link.lnk") == new Point(232, 76), "grid");
                int writes = s.Desktop.Writes, saves = s.Saves;
                s.Controller.Sync(s.Desktop);
                Require(s.Desktop.Writes == writes && s.Saves == saves, "idle must not write");
            });
            Check("resize wraps rows and rejects insufficient height without changes", () =>
            {
                var s = new Scenario(); s.JoinThree();
                s.Controller.ChangeGroup(s.Desktop, "one", new Rectangle(0, 0, 264, 408), "renamed");
                Require(s.Desktop.At("link.lnk") == new Point(32, 176), "wrap");
                var original = s.Desktop.Items();
                Throws<InvalidOperationException>(() => s.Controller.ChangeGroup(s.Desktop, "one", new Rectangle(0, 0, 164, 208), "one"));
                Require(original.All(i => s.Desktop.At(i.Id) == i.Position), "rejected resize mutated desktop");
                Require(s.Controller.State.Groups[0].Name == "renamed", "rename not committed");
            });
            Check("cross-group batch and removal close grid gaps", () =>
            {
                var s = new Scenario(); s.JoinThree();
                s.Controller.Create(s.Desktop, Group("two", 400, 364, 408), Screens);
                s.Desktop.Drag("folder", 440, 90); s.Desktop.Drag("link.lnk", 540, 90);
                s.Controller.Sync(s.Desktop);
                Require(s.Controller.State.Members.Count(m => m.GroupId == "two") == 2, "cross-group");
                s.Desktop.Drag("folder", 1300, 500); s.Controller.Sync(s.Desktop);
                Require(s.Desktop.At("link.lnk") == new Point(432, 76), "gap not closed");
                Require(s.Desktop.At("folder") == new Point(1300, 500), "removed item moved");
            });
            Check("full group rejects the entire three-item drag", () =>
            {
                var s = new Scenario();
                s.Controller.ChangeGroup(s.Desktop, "one", new Rectangle(0, 0, 164, 208), "one");
                Throws<InvalidOperationException>(() => s.JoinThree());
                Require(s.Controller.State.Members.Count == 0, "partial membership");
                Require(s.Desktop.At("a.txt") == new Point(900, 0) && s.Desktop.At("folder") == new Point(900, 100) && s.Desktop.At("link.lnk") == new Point(900, 200), "batch not restored");
            });
            Check("exit preserves membership, restart restores grid and flags", () =>
            {
                var s = new Scenario(5); s.JoinThree(); s.Desktop.ReorderOnAuto = true;
                s.Controller.Restore(s.Desktop, Screens);
                Require(!s.Disk.SessionActive && s.Disk.Members.Count == 3 && s.Desktop.PositioningFlags == 5, "exit lost state");
                var restart = new LayoutController(s.Disk.Copy(), state => s.Disk = state.Copy());
                restart.Start(s.Desktop, Screens);
                Require(restart.State.Members.Count == 3 && s.Desktop.At("folder") == new Point(132, 76) && s.Desktop.PositioningFlags == 0, "restart");
                restart.Restore(s.Desktop, Screens);
                Require(s.Desktop.PositioningFlags == 5, "original flags");
            });
            Check("restore after user enables auto-arrange needs no manual toggle", () =>
            {
                var s = new Scenario(5); s.JoinThree(); s.Desktop.SetPositioningFlags(5);
                s.Controller.Restore(s.Desktop, Screens);
                Require(s.Desktop.PositioningFlags == 5 && !s.Controller.State.SessionActive, "restore flags");
            });
            Check("deleting one group leaves other members and icons untouched", () =>
            {
                var s = new Scenario(); s.JoinThree(); s.Controller.Create(s.Desktop, Group("two", 400, 364, 408), Screens);
                s.Desktop.Drag("folder", 440, 90); s.Controller.Sync(s.Desktop);
                var position = s.Desktop.At("folder"); s.Controller.Restore(s.Desktop, Screens, "one");
                Require(s.Controller.State.Groups.Count == 1 && s.Desktop.At("folder") == position && s.Controller.State.Members.Count == 1, "other group changed");
            });
            Check("new icons inside a frame are not enrolled automatically", () =>
            {
                var s = new Scenario(); s.Desktop.Add("new.txt", 40, 90); s.Controller.Sync(s.Desktop);
                Require(s.Controller.State.Members.Count == 0, "new item auto-enrolled");
                Require(s.Desktop.At("new.txt") == new Point(40, 90), "new item moved");
            });
            Check("grid skips ungrouped icon cells", () =>
            {
                var s = new Scenario(); s.Desktop.Add("untouched", 32, 76); s.Controller.Sync(s.Desktop);
                s.Desktop.Drag("a.txt", 170, 100); s.Controller.Sync(s.Desktop);
                Require(s.Desktop.At("a.txt") == new Point(132, 76) && s.Desktop.At("untouched") == new Point(32, 76), "collision");
            });
            Check("rename follows identity; same-path replacement is not adopted", () =>
            {
                var s = new Scenario(); s.JoinThree();
                s.Desktop.Data[0].Id = "renamed.txt"; s.Controller.Sync(s.Desktop);
                Require(s.Controller.State.Members.Any(m => m.Id == "renamed.txt"), "rename lost");
                s.Desktop.Data[0].Identity = "different-file";
                var position = s.Desktop.Data[0].Position; s.Controller.Sync(s.Desktop);
                Require(!s.Controller.State.Members.Any(m => m.Id == "renamed.txt"), "wrong file adopted");
                Require(s.Desktop.Data[0].Position == position, "replacement moved");
            });
            Check("deleted and inaccessible members are handled safely", () =>
            {
                var s = new Scenario(); s.JoinThree(); s.Desktop.Data.RemoveAt(0); s.Controller.Sync(s.Desktop);
                Require(s.Controller.State.Members.Count == 2, "deleted record retained");
                s.Desktop.Data[0].Identity = null;
                int writes = s.Desktop.Writes;
                Throws<InvalidOperationException>(() => s.Controller.Sync(s.Desktop));
                Require(s.Desktop.Writes == writes, "inaccessible target moved");
            });
            Check("partial Shell failure rolls back membership and all dragged items", () =>
            {
                var s = new Scenario(); s.Desktop.FailMoveOnce = true;
                Throws<RecoveryRequiredException>(() => s.JoinThree());
                Require(s.Controller.State.Members.Count == 0 && s.Controller.State.PendingPositions == null, "rollback state");
                Require(s.Desktop.At("a.txt") == new Point(900, 0) && s.Desktop.At("link.lnk") == new Point(900, 200), "rollback positions");
            });
            Check("Shell success without movement is rejected by readback", () =>
            {
                var s = new Scenario(); s.Desktop.IgnoreMoveOnce = true;
                try { s.JoinThree(); throw new Exception("silent failure accepted"); }
                catch (RecoveryRequiredException error) { Require(error.CanRetry, "safe rollback must allow retry"); }
                Require(s.Controller.State.Members.Count == 0 && s.Desktop.At("a.txt") == new Point(900, 0), "silent position failure");
            });
            Check("failed journal write prevents Shell mutations", () =>
            {
                var s = new Scenario(); s.FailSaveAt = s.Saves + 1; int writes = s.Desktop.Writes;
                Throws<RecoveryRequiredException>(() => s.JoinThree());
                Require(s.Desktop.Writes == writes && s.Disk.Members.Count == 0, "moved before durable record");
            });
            Check("commit write failure rolls back positions and membership", () =>
            {
                var s = new Scenario(); s.FailSaveAt = s.Saves + 2;
                try { s.JoinThree(); throw new Exception("save failure accepted"); }
                catch (RecoveryRequiredException error) { Require(!error.CanRetry, "save failure must not auto retry"); }
                Require(s.Disk.Members.Count == 0 && s.Desktop.At("folder") == new Point(900, 100), "commit failure lost rollback");
            });
            Check("failed rollback journal survives process restart", () =>
            {
                var s = new Scenario(); s.Desktop.FailMoveAlways = true;
                try { s.JoinThree(); throw new Exception("rollback failure accepted"); }
                catch (RecoveryRequiredException error) { Require(!error.CanRetry, "failed rollback must not auto retry"); }
                Require(s.Disk.PendingPositions != null && s.Disk.PendingPositions.Count == 3, "journal lost");
                s.Desktop.FailMoveAlways = false;
                var restart = new LayoutController(s.Disk.Copy(), state => s.Disk = state.Copy()); restart.Recover(s.Desktop);
                Require(s.Disk.PendingPositions == null && s.Desktop.At("link.lnk") == new Point(900, 200), "restart recovery");
            });
            Check("occupied original location gets a free visible slot", () =>
            {
                var s = new Scenario(); s.JoinThree(); s.Desktop.Add("unrelated", 900, 0);
                s.Controller.Restore(s.Desktop, Screens);
                Require(s.Desktop.At("unrelated") == new Point(900, 0) && s.Desktop.At("a.txt") != new Point(900, 0), "restore collision");
            });
            Check("screen removal and spacing changes refit and reflow", () =>
            {
                var s = new Scenario(); s.JoinThree();
                s.Desktop.Origin = new Point(-1000, 0); s.Desktop.Spacing = new Size(120, 130);
                var screens = new[] { new Rectangle(-1000, 0, 800, 700) };
                s.Controller.Start(s.Desktop, screens);
                var group = s.Controller.State.Groups.Single();
                Require(screens[0].Contains(GridLayout.Bounds(group)), "frame offscreen");
                foreach (var member in s.Controller.State.Members)
                {
                    var p = s.Desktop.At(member.Id); p.Offset(s.Desktop.Origin);
                    Require(GridLayout.Interior(GridLayout.Bounds(group)).Contains(new Rectangle(p, s.Desktop.Spacing)), "icon clipped");
                }
            });
            Check("group overlap and oversized restore are rejected", () =>
            {
                var s = new Scenario(); s.Controller.Create(s.Desktop, Group("two", 400, 364, 408), Screens);
                Throws<InvalidOperationException>(() => s.Controller.ChangeGroup(s.Desktop, "one", new Rectangle(400, 0, 364, 408), "one"));
                s.JoinThree();
                Throws<InvalidOperationException>(() => s.Controller.Start(s.Desktop, new[] { new Rectangle(0, 0, 100, 100) }));
            });
            Check("legacy format migrates without trusting stale path identities", () =>
            {
                var s = new Scenario(); s.JoinThree(); var legacy = s.Disk.Copy();
                legacy.Version = 1; legacy.Members.ForEach(m => m.Identity = null);
                var controller = new LayoutController(legacy, state => { });
                int writes = s.Desktop.Writes; controller.Restore(s.Desktop, Screens);
                Require(controller.State.Members.Count == 0 && s.Desktop.Writes == writes, "legacy path trusted");
            });
            Check("selected batch remains whole when some drop points cross the frame edge", () =>
            {
                var s = new Scenario();
                s.Desktop.Drag("a.txt", 40, 90); s.Desktop.Drag("folder", 340, 90); s.Desktop.Drag("link.lnk", 640, 90);
                s.Controller.Sync(s.Desktop);
                Require(s.Controller.State.Members.Count == 3 && s.Desktop.At("link.lnk") == new Point(232, 76), "split selection");
            });
            Check("permanent disk failure retains a restartable journal", () =>
            {
                var s = new Scenario(); s.FailSaveFrom = s.Saves + 2;
                Throws<RecoveryRequiredException>(() => s.JoinThree());
                Require(s.Disk.PendingPositions != null, "disk journal lost");
                var restart = new LayoutController(s.Disk.Copy(), state => s.Disk = state.Copy());
                restart.Recover(s.Desktop);
                Require(s.Disk.PendingPositions == null && s.Desktop.At("folder") == new Point(900, 100), "disk recovery failed");
            });
            Check("restoring an already stopped session does not undo later user moves", () =>
            {
                var s = new Scenario(); s.JoinThree(); s.Controller.Restore(s.Desktop, Screens);
                s.Desktop.Drag("a.txt", 1400, 700); int writes = s.Desktop.Writes;
                s.Controller.Restore(s.Desktop, Screens);
                Require(s.Desktop.At("a.txt") == new Point(1400, 700) && s.Desktop.Writes == writes, "stale restore");
            });
            Check("deleting a saved group while paused preserves Windows settings", () =>
            {
                var s = new Scenario(5); s.Controller.Create(s.Desktop, Group("two", 400, 364, 408), Screens);
                s.Controller.Restore(s.Desktop, Screens);
                s.Controller.Restore(s.Desktop, Screens, "one");
                Require(s.Controller.State.Groups.Count == 1 && s.Desktop.PositioningFlags == 5 && !s.Controller.State.SessionActive, "paused deletion changed settings");
            });
            Check("group movement failure restores old bounds and positions", () =>
            {
                var s = new Scenario(); s.JoinThree(); s.Desktop.FailMoveOnce = true;
                Throws<RecoveryRequiredException>(() => s.Controller.ChangeGroup(s.Desktop, "one", new Rectangle(400, 0, 364, 408), "one"));
                Require(s.Controller.State.Groups[0].X == 0 && s.Desktop.At("folder") == new Point(132, 76), "group rollback");
            });
            Check("screen coordinates preserve original positions when desktop origin changes", () =>
            {
                var s = new Scenario(); s.JoinThree(); s.Desktop.Origin = new Point(-1000, 0);
                var screens = new[] { new Rectangle(-1000, 0, 3000, 1200) };
                s.Controller.Start(s.Desktop, screens); s.Controller.Restore(s.Desktop, screens);
                Require(s.Desktop.At("a.txt") == new Point(1900, 0), "original screen position drifted");
            });
            Check("Explorer reset reflows members without losing original flags", () =>
            {
                var s = new Scenario(5); s.JoinThree(); s.Desktop.ReorderOnAuto = true; s.Desktop.SetPositioningFlags(5);
                s.Controller.Start(s.Desktop, Screens);
                Require(s.Desktop.At("folder") == new Point(132, 76) && s.Controller.State.OriginalFolderFlags == 5, "Explorer reset");
            });
            Check("live drag updates before release with only two disk writes", () =>
            {
                var s = new Scenario(); s.JoinThree(); int saves = s.Saves;
                s.Controller.BeginDrag(s.Desktop, "one");
                for (int x = 1; x <= 60; x++) s.Controller.PreviewDrag(s.Desktop, new Rectangle(x, 0, 364, 408));
                Require(s.Desktop.At("folder") == new Point(192, 76), "preview lagged");
                Require(s.Saves == saves + 1 && s.Disk.Groups[0].X == 0, "per-frame disk write");
                s.Controller.EndDrag(s.Desktop, false);
                Require(s.Saves == saves + 2 && s.Disk.Groups[0].X == 60, "final commit");
            });
            Check("live resize wraps and rejects clipping", () =>
            {
                var s = new Scenario(); s.JoinThree(); s.Controller.BeginDrag(s.Desktop, "one");
                s.Controller.PreviewDrag(s.Desktop, new Rectangle(0, 0, 264, 408));
                Require(s.Desktop.At("link.lnk") == new Point(32, 176), "live wrap");
                Throws<InvalidOperationException>(() => s.Controller.PreviewDrag(s.Desktop, new Rectangle(0, 0, 164, 208)));
                s.Controller.EndDrag(s.Desktop, false);
                Require(s.Disk.Groups[0].Width == 264, "invalid preview committed");
            });
            Check("cancel and crash recover live movement", () =>
            {
                var s = new Scenario(); s.JoinThree(); s.Controller.BeginDrag(s.Desktop, "one");
                s.Controller.PreviewDrag(s.Desktop, new Rectangle(100, 0, 364, 408));
                s.Controller.EndDrag(s.Desktop, true);
                Require(s.Desktop.At("folder") == new Point(132, 76) && s.Disk.Groups[0].X == 0, "cancel");
                s.Controller.BeginDrag(s.Desktop, "one");
                s.Controller.PreviewDrag(s.Desktop, new Rectangle(100, 0, 364, 408));
                var restart = new LayoutController(s.Disk.Copy(), state => s.Disk = state.Copy());
                restart.Recover(s.Desktop);
                Require(s.Desktop.At("folder") == new Point(132, 76) && s.Disk.PendingPositions == null, "crash recovery");
            });
            Check("live Shell and commit failures roll back", () =>
            {
                var s = new Scenario(); s.JoinThree(); s.Controller.BeginDrag(s.Desktop, "one"); s.Desktop.FailMoveOnce = true;
                Throws<RecoveryRequiredException>(() => s.Controller.PreviewDrag(s.Desktop, new Rectangle(100, 0, 364, 408)));
                s.Controller.EndDrag(s.Desktop, true);
                Require(s.Desktop.At("folder") == new Point(132, 76), "Shell rollback");
                s.Controller.BeginDrag(s.Desktop, "one"); s.Controller.PreviewDrag(s.Desktop, new Rectangle(100, 0, 364, 408));
                s.FailSaveAt = s.Saves + 1;
                Throws<RecoveryRequiredException>(() => s.Controller.EndDrag(s.Desktop, false));
                Require(s.Desktop.At("folder") == new Point(132, 76) && s.Disk.Groups[0].X == 0, "commit rollback");
            });
            Check("native label width stays within padded container", () =>
            {
                var s = new Scenario(); s.JoinThree();
                var spacing = new Size(100, 100);
                var targets = GridLayout.Arrange(s.Disk, s.Desktop.Items(), new[] { "one" }, Point.Empty, spacing, 48);
                var inside = GridLayout.Interior(GridLayout.Bounds(s.Disk.Groups[0]));
                Require(targets["a.txt"].X == 58, "label offset");
                Require(targets.Values.All(p => inside.Contains(GridLayout.ItemBounds(p, spacing, 48))), "label clipped");
            });
            Check("mouse drop accepts the full selection even when every icon anchor is outside", () =>
            {
                var s = new Scenario();
                s.Desktop.Drag("a.txt", 400, 90); s.Desktop.Drag("folder", 500, 90); s.Desktop.Drag("link.lnk", 600, 90);
                s.Controller.Sync(s.Desktop, new Point(8, 100));
                Require(s.Disk.Members.Count == 3 && s.Desktop.At("folder") == new Point(132, 76), "pointer drop lost batch");
            });
            Check("padding accepts a single icon without needing content-area contact", () =>
            {
                var s = new Scenario(); s.Desktop.Drag("a.txt", 5, 60); s.Controller.Sync(s.Desktop);
                Require(s.Disk.Members.Count == 1 && s.Desktop.At("a.txt") == new Point(32, 76), "padding ignored");
            });
            Check("batch includes selected stationary icons and excludes unselected items", () =>
            {
                var s = new Scenario(); s.Desktop.Selection.Add("folder"); s.Desktop.Drag("a.txt", 400, 100);
                s.Controller.Sync(s.Desktop, new Point(8, 100));
                Require(s.Disk.Members.Count == 2 && s.Desktop.At("link.lnk") == new Point(900, 200), "selection scope");
            });
            Check("pointer outside removes a batch even if an icon remains visually inside", () =>
            {
                var s = new Scenario(); s.JoinThree(); s.Desktop.Drag("a.txt", 40, 90);
                s.Controller.Sync(s.Desktop, new Point(700, 600));
                Require(s.Disk.Members.Count == 0, "outside drop retained batch");
            });
            Check("full group rolls back pointer-based batch as a whole", () =>
            {
                var s = new Scenario(); s.Controller.ChangeGroup(s.Desktop, "one", new Rectangle(0, 0, 164, 208), "one");
                s.Desktop.Drag("a.txt", 400, 90); s.Desktop.Drag("folder", 500, 90); s.Desktop.Drag("link.lnk", 600, 90);
                Throws<InvalidOperationException>(() => s.Controller.Sync(s.Desktop, new Point(8, 90)));
                Require(s.Disk.Members.Count == 0 && s.Desktop.At("folder") == new Point(900, 100), "partial batch");
            });
            Check("fences snap edges and keep a sixteen-pixel gap", () =>
            {
                var other = new[] { new Rectangle(0, 100, 300, 300) };
                var actual = GridLayout.SnapBounds(new Rectangle(322, 107, 300, 300), other, Screens);
                Require(actual == new Rectangle(316, 100, 300, 300), "edge/gap snap");
                Require(GridLayout.SnapBounds(actual, other, Screens) == actual, "aligned fence drifted");
                var far = new Rectangle(350, 150, 300, 300);
                Require(GridLayout.SnapBounds(far, other, Screens) == far, "snap too far");
            });
            Check("resize snaps only dragged edge and allows stacked alignment", () =>
            {
                var other = new[] { new Rectangle(0, 100, 300, 300) };
                var actual = GridLayout.SnapBounds(new Rectangle(400, 100, 300, 292), other, Screens, 8);
                Require(actual == new Rectangle(400, 100, 300, 300), "resize edge");
                var overlap = new Rectangle(290, 100, 300, 300);
                Require(GridLayout.SnapBounds(overlap, other, Screens) == overlap, "overlapping position changed");
                Require(GridLayout.SnapBounds(new Rectangle(5, 105, 300, 300), other, Screens) == other[0], "stacked alignment rejected");
                var negative = new[] { new Rectangle(-1920, 0, 1920, 1080) };
                Require(GridLayout.SnapBounds(new Rectangle(-1914, 30, 300, 300), new Rectangle[0], negative).X == -1920, "negative monitor");
            });
            Check("restoring multiple occupied origins reserves different fallback cells", () =>
            {
                var s = new Scenario(); s.JoinThree();
                s.Desktop.Add("blocked-a", 900, 0); s.Desktop.Add("blocked-b", 900, 100);
                s.Controller.Restore(s.Desktop, Screens);
                Require(s.Desktop.At("a.txt") != s.Desktop.At("folder"), "restore overlap");
            });
            Check("JSON roundtrip, backup recovery and invalid data rejection", TestStore);
            Check("native file identity survives rename but rejects replacement", TestIdentity);
            passed += UnifiedChecks.Run();
            Console.WriteLine("Passed " + passed + " checks. No desktop icons or user layout were modified.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void TestStore()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DesktopTidyLight-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "layout.json");
            var s = new Scenario(); s.JoinThree(); LayoutStore.Save(s.Disk, path);
            var loaded = LayoutStore.Load(path);
            Require(loaded.Members.Count == 3 && loaded.SessionActive, "roundtrip");
            loaded.Groups[0].Name = "new"; LayoutStore.Save(loaded, path);
            File.WriteAllText(path, "broken");
            Require(LayoutStore.Load(path).Groups[0].Name == "one", "backup");
            File.WriteAllText(path, "null"); File.WriteAllText(path + ".bak", "null");
            Throws<InvalidDataException>(() => LayoutStore.Load(path));
            loaded.Groups.Add(loaded.Groups[0]);
            Throws<InvalidDataException>(() => LayoutStore.Save(loaded, path));
        }
        finally { DeleteTemporaryDirectory(directory); }
    }

    private static void TestIdentity()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DesktopTidyLight-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string first = Path.Combine(directory, "first.txt"), renamed = Path.Combine(directory, "renamed.txt");
            File.WriteAllText(first, "one"); string identity = DesktopShell.FileIdentity(first);
            Require(identity != null, "identity not available");
            File.Move(first, renamed);
            Require(DesktopShell.FileIdentity(renamed) == identity, "rename changed identity");
            File.WriteAllText(first, "two");
            Require(DesktopShell.FileIdentity(first) != identity, "replacement identity reused");
            Require(DesktopShell.FileIdentity("::{test}") == "shell:::{test}", "virtual Shell identity");
        }
        finally { DeleteTemporaryDirectory(directory); }
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        string absolute = Path.GetFullPath(directory);
        string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!absolute.StartsWith(parent + "DesktopTidyLight-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing cleanup outside this check's temporary directory.");
        foreach (string file in Directory.GetFiles(absolute)) File.Delete(file);
        Directory.Delete(absolute, false);
    }
}
