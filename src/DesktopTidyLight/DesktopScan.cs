using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;

namespace DesktopTidyLight
{
    // Only plain values leave the worker. COM objects are created and disposed on its STA.
    internal sealed class DesktopSnapshot
    {
        internal List<DesktopItem> Items;
        internal Point Origin;
        internal Size Spacing;
        internal int Flags;
        internal IntPtr ViewWindow;
        internal uint ProcessId;
        internal Exception Error;
        internal static DesktopSnapshot Read()
        {
            using (var desktop = new DesktopShell())
                return new DesktopSnapshot { Items = desktop.Items(), Origin = desktop.Origin, Spacing = desktop.Spacing,
                    Flags = desktop.PositioningFlags, ViewWindow = desktop.ViewWindow, ProcessId = desktop.ProcessId };
        }
        internal bool NeedsSync(Layout state, Rectangle[] screens)
        {
            var byId = Items.ToLookup(i => i.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var member in state.Members)
            {
                var item = byId[member.Id].FirstOrDefault();
                if (item == null || member.Identity == null || item.Identity != member.Identity || !string.Equals(item.Id, member.Id, StringComparison.Ordinal)) return true;
            }
            return LayoutController.Parking(state, Items, Origin, screens).Count != 0;
        }
    }

    internal sealed class DesktopScan : IDisposable
    {
        private readonly object gate = new object();
        private readonly Func<DesktopSnapshot> read;
        private bool running, disposed;
        private Thread worker;
        private DesktopSnapshot completed;
        internal DesktopScan(Func<DesktopSnapshot> read = null) { this.read = read ?? DesktopSnapshot.Read; }
        internal bool Start()
        {
            lock (gate)
            {
                if (disposed || running || completed != null) return false;
                running = true;
                if (worker == null)
                {
                    worker = new Thread(Work) { IsBackground = true, Name = "Desktop Tidy desktop scan" };
                    worker.SetApartmentState(ApartmentState.STA); worker.Start();
                }
                Monitor.Pulse(gate); return true;
            }
        }
        private void Work()
        {
            while (true)
            {
                lock (gate)
                {
                    while (!running && !disposed) Monitor.Wait(gate);
                    if (disposed) return;
                }
                DesktopSnapshot result;
                try { result = read(); } catch (Exception error) { result = new DesktopSnapshot { Error = error }; }
                lock (gate) { if (!disposed) completed = result; running = false; }
            }
        }
        internal DesktopSnapshot Take()
        { lock (gate) { var result = completed; completed = null; return result; } }
        public void Dispose() { lock (gate) { disposed = true; completed = null; Monitor.Pulse(gate); } }
    }
}
