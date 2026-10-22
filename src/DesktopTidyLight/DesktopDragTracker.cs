using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Diagnostics;
namespace DesktopTidyLight
{
    // Observe only: never consume input or perform Shell/disk work inside the callback.
    internal sealed class DesktopDragTracker : IDisposable
    {
        private delegate IntPtr MouseCallback(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, MouseCallback callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        private readonly MouseCallback callback;
        private IntPtr hook;
        private Point? pressed, released;
        private long expires;
        internal Point? SelectionStart { get; private set; }
        internal Point SelectionEnd { get; private set; }
        internal bool Selecting { get; private set; }
        internal int SelectionVersion { get; private set; }
        internal event Action SelectionChanged;
        internal Point? ReleasePoint { get { return Stopwatch.GetTimestamp() < expires ? released : null; } }
        internal DesktopDragTracker()
        {
            callback = Observe;
            hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        private IntPtr Observe(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                int kind = message.ToInt32();
                var cursor = new Point(Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4));
                if (kind == 0x201 && DesktopDrop.IsDesktop(cursor, false))
                { SelectionStart = SelectionEnd = cursor; Selecting = true; SelectionVersion++; SelectionChanged?.Invoke(); }
                else if (Selecting && (kind == 0x200 || kind == 0x202))
                { SelectionEnd = cursor; Selecting = kind != 0x202; SelectionChanged?.Invoke(); }
                else if (kind == 0x204 && Selecting)
                { Selecting = false; SelectionStart = null; SelectionChanged?.Invoke(); }
                if (kind == 0x201 || kind == 0x202)
                {
                    var point = new Point(Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4));
                    if (kind == 0x201) { pressed = point; released = null; }
                    else
                    {
                        if (pressed.HasValue && (Math.Abs(point.X - pressed.Value.X) > 4 || Math.Abs(point.Y - pressed.Value.Y) > 4))
                        { released = point; expires = Stopwatch.GetTimestamp() + Stopwatch.Frequency; }
                        pressed = null;
                    }
                }
                else if (kind == 0x204) { pressed = null; released = null; }
            }
            return CallNextHookEx(hook, code, message, data);
        }
        internal void Clear() { released = null; }
        public void Dispose() { if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } SelectionChanged = null; }
    }
}
