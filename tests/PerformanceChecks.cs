using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using DesktopTidyLight;

internal static class PerformanceChecks
{
    private static void Measure(string name, int count, Action<int> operation)
    {
        var samples = new double[count];
        for (int i = 0; i < count; i++) { var watch = Stopwatch.StartNew(); operation(i); Application.DoEvents(); samples[i] = watch.Elapsed.TotalMilliseconds; }
        Array.Sort(samples);
        Console.WriteLine(name + ": median=" + samples[count / 2].ToString("F2") + "ms p95=" + samples[Math.Min(count - 1, (int)(count * .95))].ToString("F2") + "ms max=" + samples.Last().ToString("F2") + "ms");
    }
    internal static int Run()
    {
        Application.EnableVisualStyles();
        string folder = Path.Combine(Path.GetTempPath(), "tidy-perf-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var ids = Enumerable.Range(0, 1000).Select(i => Path.Combine(folder, "项目 " + i + ".txt")).ToArray();
            foreach (var id in ids) File.WriteAllText(id, "performance probe");
            using (var canvas = new IconCanvas { Size = new Size(800, 650) })
            using (var image = new Bitmap(1000, 800))
            {
                canvas.CreateControl(); canvas.SetMembers(ids.Select(id => new MemberData { Id = id }));
                Action paint = () => canvas.DrawToBitmap(image, new Rectangle(Point.Empty, canvas.Size));
                Measure("cold visible icons / 1000 members", 1, i => paint());
                var loaded = Stopwatch.StartNew();
                while (canvas.IsLoadingVisuals && loaded.ElapsedMilliseconds < 10000) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                Console.WriteLine("first visible icons ready after background load=" + loaded.ElapsedMilliseconds + "ms pending=" + canvas.IsLoadingVisuals);
                Measure("cached paint", 100, i => paint());
                var scroll = typeof(IconCanvas).GetField("scrollY", BindingFlags.NonPublic | BindingFlags.Instance);
                Measure("scroll down and back", 120, i => { scroll.SetValue(canvas, (i < 60 ? i : 119 - i) * 100); paint(); });
                Measure("continuous resize", 120, i => { canvas.Size = new Size(700 + i % 100, 600 + i % 40); paint(); });
            }
            using (var frame = new GroupFrame(new GroupData { Id = "perf", Name = "perf", X = -24000, Y = -24000, Width = 800, Height = 650 }))
            {
                frame.Show(); frame.AttachToDesktop(); frame.ApplyPreferences(false, 50);
                Measure("native group move translucent", 200, i => frame.SetScreenBounds(new Rectangle(-24000 + i % 100, -24000, 800, 650)));
            }
            var layout = LayoutStore.Load();
            Measure("layout serialize", 50, i => layout.Copy());
            Measure("durable save temporary file", 20, i => LayoutStore.Save(layout, Path.Combine(folder, "layout.json")));
            using (var desktop = new DesktopShell()) Measure("read-only desktop enumeration", 20, i => desktop.Items());
            using (var scan = new DesktopScan())
            {
                var dispatch = new double[20]; double maxGap = 0;
                for (int i = 0; i < dispatch.Length; i++)
                {
                    var start = Stopwatch.StartNew(); scan.Start(); dispatch[i] = start.Elapsed.TotalMilliseconds;
                    var gap = Stopwatch.StartNew(); DesktopSnapshot snapshot;
                    while ((snapshot = scan.Take()) == null && start.ElapsedMilliseconds < 10000)
                    {
                        Application.DoEvents(); System.Threading.Thread.Sleep(1);
                        maxGap = Math.Max(maxGap, gap.Elapsed.TotalMilliseconds); gap.Restart();
                    }
                    if (snapshot == null) throw new Exception("desktop scan timed out");
                    if (snapshot.Error != null) throw snapshot.Error;
                    if (snapshot.Items.Count == 0) throw new Exception("empty desktop snapshot");
                }
                Array.Sort(dispatch);
                Console.WriteLine("background scan UI dispatch median=" + dispatch[10].ToString("F2") + "ms p95=" + dispatch[19].ToString("F2") + "ms; max message pump gap=" + maxGap.ToString("F2") + "ms");
            }
            var a = new DisplayInfo { Id = "a", Work = new Rectangle(-1920, 0, 1920, 1040), Dpi = 96 };
            var b = new DisplayInfo { Id = "b", Work = new Rectangle(0, 0, 3840, 2080), Dpi = 192 };
            var simulated = layout.Copy(); foreach (var group in simulated.Groups) DisplayLayout.Remember(group, new[] { a, b });
            Measure("simulated monitor disconnect/reconnect", 200, i => DisplayLayout.ResolveAll(simulated, i % 2 == 0 ? new[] { a } : new[] { a, b }));
            using (var process = Process.GetCurrentProcess()) Console.WriteLine("process private MiB=" + (process.PrivateMemorySize64 / 1048576d).ToString("F1") + " handles=" + process.HandleCount);
            return 0;
        }
        finally { Directory.Delete(folder, true); }
    }
}
