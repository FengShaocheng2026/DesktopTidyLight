using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopTidyLight
{
    internal sealed partial class DesktopShell
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetMenuCall(IntPtr self, IntPtr owner, uint count, IntPtr[] items, ref Guid iid, IntPtr reserved, out IntPtr menu);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryMenuCall(IntPtr self, IntPtr menu, uint index, uint first, uint last, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int InvokeMenuCall(IntPtr self, ref MenuCommand command);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int MenuMessageCall(IntPtr self, uint message, IntPtr wparam, IntPtr lparam, out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int MenuMessage2Call(IntPtr self, uint message, IntPtr wparam, IntPtr lparam);
        [StructLayout(LayoutKind.Sequential)]
        private struct MenuCommand
        {
            internal int Size;
            internal uint Mask;
            internal IntPtr Owner, Verb, Parameters, Directory;
            internal int Show;
            internal uint HotKey;
            internal IntPtr Icon;
        }
        [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
        [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);

        internal void ShowContextMenu(string[] ids, IntPtr owner, Point point)
        {
            var wanted = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            if (wanted.Count == 0) return;
            var pidls = new List<IntPtr>();
            IntPtr context = IntPtr.Zero, menu = IntPtr.Zero;
            try
            {
                int count; Check(Method<ItemCountCall>(view, 7)(view, AllItems, out count), "ItemCount");
                for (int i = 0; i < count; i++)
                {
                    IntPtr pidl; Check(Method<ItemCall>(view, 6)(view, i, out pidl), "Item");
                    try { if (wanted.Contains(ParsingName(pidl))) { pidls.Add(pidl); pidl = IntPtr.Zero; } }
                    finally { if (pidl != IntPtr.Zero) CoTaskMemFree(pidl); }
                }
                if (pidls.Count != wanted.Count) throw new InvalidOperationException("桌面项目已变化，请重新选择。");
                var iid = new Guid("000214E4-0000-0000-C000-000000000046");
                Check(Method<GetMenuCall>(folder, 10)(folder, owner, (uint)pidls.Count, pidls.ToArray(), ref iid, IntPtr.Zero, out context), "GetUIObjectOf");
                menu = CreatePopupMenu();
                if (menu == IntPtr.Zero) throw new InvalidOperationException("无法创建项目菜单。");
                Check(Method<QueryMenuCall>(context, 3)(context, menu, 0, 1, 0x7fff, 0), "QueryContextMenu");
                using (var messages = new MenuMessages(owner, context))
                {
                    uint command = TrackPopupMenuEx(menu, 0x102, point.X, point.Y, owner, IntPtr.Zero);
                    if (command == 0) return;
                    var invoke = new MenuCommand { Size = Marshal.SizeOf(typeof(MenuCommand)), Owner = owner, Verb = new IntPtr(command - 1), Show = 1 };
                    Check(Method<InvokeMenuCall>(context, 4)(context, ref invoke), "InvokeCommand");
                }
            }
            finally
            {
                if (menu != IntPtr.Zero) DestroyMenu(menu);
                if (context != IntPtr.Zero) Marshal.Release(context);
                foreach (var pidl in pidls) CoTaskMemFree(pidl);
            }
        }

        private sealed class MenuMessages : NativeWindow, IDisposable
        {
            private IntPtr menu;
            private readonly bool extended;
            internal MenuMessages(IntPtr owner, IntPtr context)
            {
                var iid = new Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719");
                extended = Marshal.QueryInterface(context, ref iid, out menu) >= 0;
                if (!extended)
                {
                    iid = new Guid("000214F4-0000-0000-C000-000000000046");
                    Marshal.QueryInterface(context, ref iid, out menu);
                }
                AssignHandle(owner);
            }
            protected override void WndProc(ref Message message)
            {
                if (menu != IntPtr.Zero && (message.Msg == 0x117 || message.Msg == 0x2b || message.Msg == 0x2c || message.Msg == 0x120))
                {
                    IntPtr result = IntPtr.Zero;
                    int status = extended ? Method<MenuMessageCall>(menu, 7)(menu, (uint)message.Msg, message.WParam, message.LParam, out result) :
                        Method<MenuMessage2Call>(menu, 6)(menu, (uint)message.Msg, message.WParam, message.LParam);
                    if (status == 0) { message.Result = result; return; }
                }
                base.WndProc(ref message);
            }
            public void Dispose() { ReleaseHandle(); if (menu != IntPtr.Zero) { Marshal.Release(menu); menu = IntPtr.Zero; } }
        }
    }
}
