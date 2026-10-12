using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using DesktopTidyLight;

// Explicit opt-in integration check. Does not run with the ordinary regression checks.
internal static class ShellSmokeCheck
{
    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint change, uint flags, string first, IntPtr second);

    internal static int Run(int extraCount = 0, bool unified = false)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        if (Process.GetProcessesByName("DesktopTidyLight").Length != 0)
        {
            Console.Error.WriteLine("Stop DesktopTidyLight before this explicit integration check.");
            return 2;
        }
        string root = Path.Combine(Environment.CurrentDirectory, "build", "shell-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string layoutPath = Path.Combine(root, "layout.json");
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string prefix = "DesktopTidyLight-CHECK-" + Guid.NewGuid().ToString("N");
        string textPath = Path.Combine(desktopPath, prefix + ".txt");
        string folderPath = Path.Combine(desktopPath, prefix + "-folder");
        string shortcutPath = Path.Combine(desktopPath, prefix + ".lnk");
        var owned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        byte[] userLayout = File.Exists(LayoutStore.Pathname) ? File.ReadAllBytes(LayoutStore.Pathname) : null;
        LayoutController controller = null;
        int originalFlags = -1;
        bool restored = false;
        try
        {
            using (var shell = new DesktopShell())
            {
                originalFlags = shell.PositioningFlags;
                var before = shell.Items();
                LayoutStore.Save(new Layout { OriginalFolderFlags = originalFlags,
                    Snapshots = before.Select(i => new PositionData { Id = i.Id, Identity = i.Identity, X = i.Position.X, Y = i.Position.Y }).ToList() }, Path.Combine(root, "before.json"));
            }
            File.WriteAllText(textPath, "DesktopTidyLight isolated integration check");
            owned.Add(textPath, DesktopShell.FileIdentity(textPath));
            Directory.CreateDirectory(folderPath);
            owned.Add(folderPath, DesktopShell.FileIdentity(folderPath));
            object scripting = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            object shortcut = null;
            try
            {
                shortcut = ((dynamic)scripting).CreateShortcut(shortcutPath);
                ((dynamic)shortcut).TargetPath = textPath;
                ((dynamic)shortcut).Save();
            }
            finally
            {
                if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
                Marshal.FinalReleaseComObject(scripting);
            }
            owned.Add(shortcutPath, DesktopShell.FileIdentity(shortcutPath));
            for (int index = 0; index < extraCount; index++)
            {
                string path = Path.Combine(desktopPath, prefix + "-" + index + ".txt");
                File.WriteAllText(path, "DesktopTidyLight performance check");
                owned.Add(path, DesktopShell.FileIdentity(path));
            }
            if (owned.Values.Any(i => i == null)) throw new Exception("Cannot establish test-file ownership.");
            foreach (var path in owned.Keys) SHChangeNotify(path == folderPath ? 8u : 2u, 0x1005, path, IntPtr.Zero);
            var screens = Screen.AllScreens.Select(s => s.WorkingArea).ToArray();
            using (var shell = new DesktopShell())
            {
                List<DesktopItem> items = null;
                for (int attempt = 0; attempt < 50; attempt++)
                {
                    Application.DoEvents();
                    items = shell.Items();
                    if (owned.Keys.All(id => items.Any(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase)))) break;
                    Thread.Sleep(100);
                }
                if (!owned.Keys.All(id => items.Any(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase))))
                {
                    Console.WriteLine("Visible temporary IDs: " + string.Join(" | ", items.Where(i => i.Id.Contains(prefix)).Select(i => i.Id)));
                    Console.WriteLine("Expected temporary IDs: " + string.Join(" | ", owned.Keys));
                    Console.WriteLine("Visible item count: " + items.Count);
                    throw new Exception("Shell did not expose all three temporary items.");
                }
                GroupData group = unified ? new GroupData { Id = "shell-check", Name = "Integration check", X = screens[0].Left, Y = screens[0].Top, Width = 420, Height = 360 } : FindSpace(items, owned.Keys.ToArray(), screens, shell.Origin, shell.Spacing, shell.IconSize);
                controller = new LayoutController(new Layout(), state => LayoutStore.Save(state, layoutPath));
                if (unified) controller.EnableUnified();
                controller.Create(shell, group, screens);
                var unrelated = shell.Items().Where(i => !owned.ContainsKey(i.Id)).ToDictionary(i => i.Id, i => i.Position, StringComparer.OrdinalIgnoreCase);
                if (unified)
                {
                    controller.AddMembers(shell, group.Id, owned.Keys);
                    var parked = shell.Items().Where(i => owned.ContainsKey(i.Id)).ToDictionary(i => i.Id, i => i.Position);
                    if (parked.Count != owned.Count || parked.Values.Any(p => screens.Any(screen => screen.Contains(new Point(p.X + shell.Origin.X, p.Y + shell.Origin.Y)))))
                        throw new Exception("A native member remains visible.");
                    for (int step = 0; step < 30; step++) controller.SaveGroup(group.Id, new Rectangle(group.X + step, group.Y, 420, 360), group.Name);
                    Verify(shell, parked);
                    if (shell.Items().Any(i => unrelated.ContainsKey(i.Id) && unrelated[i.Id] != i.Position)) throw new Exception("An unrelated icon moved.");
                    if (controller.State.Members.Count != owned.Count) throw new Exception("Multi-item drop lost members.");
                    Console.WriteLine("PASS unified Shell: whole batch parked outside all displays; 30 group edits leave every native position unchanged");
                    var release = new Point(screens[0].Right - 200, screens[0].Bottom - 200);
                    var dropTargets = DesktopDrop.Targets(controller.State, controller.State.Members, shell.Items(), shell.Origin, shell.Spacing, shell.IconSize, screens, release);
                    controller.RemoveMembers(shell, owned.Keys, screens, release);
                    Verify(shell, dropTargets);
                    if (controller.State.Members.Count != 0) throw new Exception("Batch did not leave the group.");
                    if (shell.Items().Any(i => unrelated.ContainsKey(i.Id) && unrelated[i.Id] != i.Position)) throw new Exception("Desktop drop moved an unrelated icon.");
                    controller.AddMembers(shell, group.Id, owned.Keys);
                    Console.WriteLine("PASS unified Shell: whole batch returned near desktop release, then re-enrolled; unrelated positions unchanged");

                }
                else
                {
                var memberPlan = controller.State.Copy();
                foreach (var id in owned.Keys) memberPlan.Members.Add(new MemberData { Id = id, Identity = owned[id], GroupId = group.Id });
                var targets = GridLayout.Arrange(memberPlan, shell.Items(), new[] { group.Id }, shell.Origin, shell.Spacing, shell.IconSize);
                // Simulate a released drag through Shell positioning; no mouse or selected user items are changed.
                shell.SetPositions(targets.ToDictionary(p => p.Key, p => new Point(p.Value.X + 2, p.Value.Y + 2)));
                controller.Sync(shell);
                if (controller.State.Members.Count != owned.Count) throw new Exception("Mixed batch was not enrolled.");
                Verify(shell, targets);
                Console.WriteLine("PASS real Shell: text, folder and shortcut aligned");
                controller.ChangeGroup(shell, group.Id, new Rectangle(group.X, group.Y, shell.Spacing.Width * (extraCount > 0 ? 5 : 2) + GridLayout.HorizontalInsets, shell.Spacing.Height * (extraCount > 0 ? 5 : 3) + GridLayout.VerticalInsets), group.Name);
                var grid = GridLayout.Arrange(controller.State, shell.Items(), new[] { group.Id }, shell.Origin, shell.Spacing, shell.IconSize);
                Verify(shell, grid);
                if (grid.Values.Select(p => p.Y).Distinct().Count() < 2) throw new Exception("Resize did not wrap.");
                if (shell.Items().Any(i => unrelated.ContainsKey(i.Id) && unrelated[i.Id] != i.Position)) throw new Exception("An unrelated icon moved during manual layout.");
                Console.WriteLine("PASS real Shell: resize wraps; unrelated icons unchanged during manual layout");
                shell.PreparePositions(owned.Keys);
                controller.BeginDrag(shell, group.Id);
                var liveBounds = GridLayout.Bounds(controller.State.Groups.Single());
                var queryTiming = Stopwatch.StartNew();
                for (int query = 0; query < 30; query++) { bool autoArrange = shell.AutoArrange; }
                Console.WriteLine("30 flag reads: " + queryTiming.ElapsedMilliseconds + " ms");
                int presented = 0; shell.BeforePreviewPaint = () => presented++;
                var timing = Stopwatch.StartNew();
                for (int step = 0; step < 30; step++)
                {
                    var preview = liveBounds; preview.X += step % 2 == 0 ? 8 : 0;
                    controller.PreviewDrag(shell, preview);
                }
                timing.Stop();
                if (presented != 30) throw new Exception("Missing coordinated frame presentation");
                Console.WriteLine("Preview validation ms=" + shell.PreviewValidationTicks * 1000 / Stopwatch.Frequency + "; movement ms=" + shell.PreviewMovementTicks * 1000 / Stopwatch.Frequency);
                shell.EndPreview();
                controller.EndDrag(shell, false);
                Verify(shell, grid);
                Console.WriteLine("PASS real Shell: 30 cached live position updates in " + timing.ElapsedMilliseconds + " ms; items " + owned.Count + "; icon size " + shell.IconSize);
                }
                controller.Restore(shell, screens);
                if (shell.PositioningFlags != originalFlags || controller.State.Members.Count != owned.Count) throw new Exception("Exit lost membership or flags.");
                controller = new LayoutController(LayoutStore.Load(layoutPath), state => LayoutStore.Save(state, layoutPath));
                controller.Start(shell, screens);
                if (unified)
                {
                    if (LayoutController.Parking(controller.State, shell.Items(), shell.Origin, screens).Count != 0) throw new Exception("Restart parking differs.");
                    if (controller.State.Members.Count != owned.Count) throw new Exception("Restart lost members.");
                }
                else Verify(shell, GridLayout.Arrange(controller.State, shell.Items(), controller.State.Groups.Select(g => g.Id), shell.Origin, shell.Spacing, shell.IconSize));
                controller.Restore(shell, screens);
                restored = true;
                Console.WriteLine("PASS real Shell: exit, disk reload and restart restore grid and original flags");
            }
            if (owned.Any(p => DesktopShell.FileIdentity(p.Key) != p.Value)) throw new Exception("A test file changed identity.");
            byte[] after = File.Exists(LayoutStore.Pathname) ? File.ReadAllBytes(LayoutStore.Pathname) : null;
            if (userLayout == null ? after != null : after == null || !userLayout.SequenceEqual(after)) throw new Exception("User layout changed.");
            Console.WriteLine("PASS real Shell: file identities and user layout unchanged");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.ToString());
            Console.Error.WriteLine("Recovery records: " + root);
            return 1;
        }
        finally
        {
            try
            {
                using (var shell = new DesktopShell())
                {
                    if (!restored && controller != null)
                        controller.Restore(shell, Screen.AllScreens.Select(s => s.WorkingArea).ToArray());
                    if (originalFlags >= 0 && shell.PositioningFlags != originalFlags) shell.SetPositioningFlags(originalFlags);
                }
            }
            catch (Exception error) { Console.Error.WriteLine("Recovery requires attention: " + error.Message); }
            foreach (var entry in owned)
            {
                if (entry.Value == null || DesktopShell.FileIdentity(entry.Key) != entry.Value) continue;
                if (entry.Key == folderPath) Directory.Delete(entry.Key, false);
                else File.Delete(entry.Key);
                SHChangeNotify(entry.Key == folderPath ? 0x10u : 4u, 0x1005, entry.Key, IntPtr.Zero);
            }
            Console.WriteLine("Removed only this run's owned temporary desktop items; retained diagnostic records.");
        }
    }

    private static void Verify(DesktopShell shell, IDictionary<string, Point> positions)
    {
        var actual = shell.Items().ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        if (positions.Any(p => !actual.ContainsKey(p.Key) || actual[p.Key].Position != p.Value)) throw new Exception("Position readback differs.");
    }

    private static GroupData FindSpace(IList<DesktopItem> items, string[] ids, Rectangle[] screens, Point origin, Size spacing, int iconSize)
    {
        foreach (var screen in screens)
            foreach (var position in GridLayout.Slots(screen, spacing).Reverse())
            {
                var group = new GroupData { Id = "shell-check", Name = "Integration check", X = position.X, Y = position.Y,
                    Width = spacing.Width * (ids.Length > 3 ? 6 : 3) + GridLayout.HorizontalInsets, Height = spacing.Height * (ids.Length > 3 ? 5 : 3) + GridLayout.VerticalInsets };
                if (!screen.Contains(GridLayout.Bounds(group))) continue;
                var layout = new Layout(); layout.Groups.Add(group);
                foreach (var id in ids) layout.Members.Add(new MemberData { Id = id, GroupId = group.Id });
                try
                {
                    GridLayout.Arrange(layout, items, new[] { group.Id }, origin, spacing, iconSize);
                    group.Width = spacing.Width * (ids.Length > 3 ? 5 : 2) + GridLayout.HorizontalInsets;
                    GridLayout.Arrange(layout, items, new[] { group.Id }, origin, spacing, iconSize);
                    group.Width = spacing.Width * (ids.Length > 3 ? 6 : 3) + GridLayout.HorizontalInsets;
                    return group;
                }
                catch (InvalidOperationException) { }
            }
        throw new Exception("No empty visible test area is available; desktop was not repositioned.");
    }
}
