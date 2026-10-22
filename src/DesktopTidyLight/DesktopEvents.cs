using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DesktopTidyLight
{
    // Out-of-context hooks run on the installing UI thread; never call Shell from the callback.
    internal sealed class DesktopEvents : IDisposable
    {
        private delegate void WinEventCallback(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEventCallback callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")]
        private static extern bool IsChild(IntPtr parent, IntPtr window);

        private readonly WinEventCallback callback;
        private IntPtr foregroundHook, desktopHook, view;
        private uint process;
        internal long Revision { get; private set; }
        internal event Action OrderChanged;
        internal event Action Changed;

        internal DesktopEvents() { callback = OnEvent; }

        internal void Watch(IntPtr viewWindow, uint processId)
        {
            if (view != viewWindow) { view = viewWindow; Revision++; }
            if (foregroundHook == IntPtr.Zero)
                foregroundHook = Install(3, 3, 0); // EVENT_SYSTEM_FOREGROUND
            if (desktopHook != IntPtr.Zero && process == processId) return;
            if (desktopHook != IntPtr.Zero) { UnhookWinEvent(desktopHook); desktopHook = IntPtr.Zero; }
            process = processId;
            desktopHook = Install(0x8000, 0x800C, processId);
        }

        private IntPtr Install(uint first, uint last, uint processId)
        {
            var hook = SetWinEventHook(first, last, IntPtr.Zero, callback, processId, 0, 0);
            if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法监听桌面变化。");
            return hook;
        }

        private void OnEvent(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time)
        { Observe(kind, window); }

        internal void Observe(uint kind, IntPtr window)
        {
            if (kind == 3 || kind == 0x8004) OrderChanged?.Invoke();
            if (view == IntPtr.Zero || window == IntPtr.Zero || (window != view && !IsChild(view, window))) return;
            // Refresh may recreate items, reorder them, or only change their positions.
            if ((kind >= 0x8000 && kind <= 0x8004) || kind == 0x800B || kind == 0x800C)
            { Revision++; Changed?.Invoke(); }
        }

        public void Dispose()
        {
            if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
            if (desktopHook != IntPtr.Zero) UnhookWinEvent(desktopHook);
            foregroundHook = desktopHook = view = IntPtr.Zero;
            OrderChanged = Changed = null;
        }
    }
}
