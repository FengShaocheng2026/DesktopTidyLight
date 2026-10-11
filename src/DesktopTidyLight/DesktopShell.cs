using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace DesktopTidyLight
{
    internal sealed class DesktopItem
    {
        internal string Id;
        internal Point Position;
    }

    // All COM calls stay on the WinForms STA thread. No desktop files are changed here.
    internal sealed class DesktopShell : IDisposable
    {
        private static readonly Guid ServiceProviderId = new Guid("6D5140C1-7436-11CE-8034-00AA006009FA");
        private static readonly Guid BrowserId = new Guid("000214E2-0000-0000-C000-000000000046");
        private static readonly Guid FolderViewId = new Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
        private static readonly Guid ShellFolderId = new Guid("000214E6-0000-0000-C000-000000000046");
        private const int DesktopClass = 8;
        private const int NeedDispatch = 1;
        private const uint AllItems = 0;
        private const uint ForParsing = 0x8000;
        private const uint PositionItem = 0x80;
        private IntPtr view;
        private IntPtr folder;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { internal int X; internal int Y; }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryServiceCall(IntPtr self, ref Guid service, ref Guid iid, out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryViewCall(IntPtr self, out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetFolderCall(IntPtr self, ref Guid iid, out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ItemCountCall(IntPtr self, uint flags, out int count);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ItemCall(IntPtr self, int index, out IntPtr pidl);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int PositionCall(IntPtr self, IntPtr pidl, out NativePoint point);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int AutoArrangeCall(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int DisplayNameCall(IntPtr self, IntPtr pidl, uint flags, IntPtr strret);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetPositionCall(IntPtr self, uint count, [In] IntPtr[] pidls, [In] NativePoint[] points, uint flags);
        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr pointer);
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrRetToStrW(IntPtr strret, IntPtr pidl, out IntPtr result);

        private static T Method<T>(IntPtr instance, int index) where T : class
        {
            IntPtr table = Marshal.ReadIntPtr(instance);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(table, index * IntPtr.Size), typeof(T));
        }

        private static void Check(int result, string action)
        {
            if (result < 0) throw new COMException(action, result);
        }

        internal DesktopShell()
        {
            IntPtr provider = IntPtr.Zero;
            IntPtr browser = IntPtr.Zero;
            IntPtr shellView = IntPtr.Zero;
            object windows = null;
            object dispatch = null;
            try
            {
                windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")));
                object location = 0;
                object root = Type.Missing;
                int hwnd;
                dispatch = ((dynamic)windows).FindWindowSW(ref location, ref root, DesktopClass, out hwnd, NeedDispatch);
                if (dispatch == null) throw new InvalidOperationException("无法取得桌面 Shell 窗口。");
                IntPtr unknown = Marshal.GetIUnknownForObject(dispatch);
                Guid providerId = ServiceProviderId;
                try { Check(Marshal.QueryInterface(unknown, ref providerId, out provider), "IServiceProvider"); }
                finally { Marshal.Release(unknown); }
                Guid service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
                Guid browserId = BrowserId;
                Check(Method<QueryServiceCall>(provider, 3)(provider, ref service, ref browserId, out browser), "IShellBrowser");
                Check(Method<QueryViewCall>(browser, 15)(browser, out shellView), "IShellView");
                Guid viewId = FolderViewId;
                Check(Marshal.QueryInterface(shellView, ref viewId, out view), "IFolderView");
                Guid folderId = ShellFolderId;
                Check(Method<GetFolderCall>(view, 5)(view, ref folderId, out folder), "IShellFolder");
            }
            catch { Dispose(); throw; }
            finally
            {
                if (shellView != IntPtr.Zero) Marshal.Release(shellView);
                if (browser != IntPtr.Zero) Marshal.Release(browser);
                if (provider != IntPtr.Zero) Marshal.Release(provider);
                if (dispatch != null) Marshal.ReleaseComObject(dispatch);
                if (windows != null) Marshal.ReleaseComObject(windows);
            }
        }

        internal bool AutoArrange
        {
            get
            {
                int result = Method<AutoArrangeCall>(view, 14)(view);
                Check(result, "GetAutoArrange");
                return result == 0;
            }
        }


        internal List<DesktopItem> Items()
        {
            int count;
            Check(Method<ItemCountCall>(view, 7)(view, AllItems, out count), "ItemCount");
            var result = new List<DesktopItem>(count);
            var item = Method<ItemCall>(view, 6);
            var position = Method<PositionCall>(view, 11);
            var name = Method<DisplayNameCall>(folder, 11);
            IntPtr strret = Marshal.AllocCoTaskMem(520);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    IntPtr pidl = IntPtr.Zero;
                    try
                    {
                        Check(item(view, i, out pidl), "Item");
                        NativePoint point;
                        Check(position(view, pidl, out point), "GetItemPosition");
                        Check(name(folder, pidl, ForParsing, strret), "GetDisplayNameOf");
                        IntPtr value;
                        Check(StrRetToStrW(strret, pidl, out value), "StrRetToStrW");
                        try { result.Add(new DesktopItem { Id = Marshal.PtrToStringUni(value), Position = new Point(point.X, point.Y) }); }
                        finally { CoTaskMemFree(value); }
                    }
                    finally { if (pidl != IntPtr.Zero) CoTaskMemFree(pidl); }
                }
            }
            finally { Marshal.FreeCoTaskMem(strret); }
            return result;
        }

        internal void SetPositions(IDictionary<string, Point> positions)
        {
            if (AutoArrange) throw new InvalidOperationException("请先在 Windows 桌面关闭“自动排列图标”。");
            int count;
            Check(Method<ItemCountCall>(view, 7)(view, AllItems, out count), "ItemCount");
            var item = Method<ItemCall>(view, 6);
            var name = Method<DisplayNameCall>(folder, 11);
            var ids = new List<IntPtr>();
            var points = new List<NativePoint>();
            IntPtr strret = Marshal.AllocCoTaskMem(520);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    IntPtr pidl = IntPtr.Zero;
                    Check(item(view, i, out pidl), "Item");
                    try
                    {
                        Check(name(folder, pidl, ForParsing, strret), "GetDisplayNameOf");
                        IntPtr value;
                        Check(StrRetToStrW(strret, pidl, out value), "StrRetToStrW");
                        try
                        {
                            Point target;
                            if (positions.TryGetValue(Marshal.PtrToStringUni(value), out target))
                            {
                                ids.Add(pidl);
                                points.Add(new NativePoint { X = target.X, Y = target.Y });
                                pidl = IntPtr.Zero;
                            }
                        }
                        finally { CoTaskMemFree(value); }
                    }
                    finally { if (pidl != IntPtr.Zero) CoTaskMemFree(pidl); }
                }
                if (ids.Count != positions.Count) throw new InvalidOperationException("部分桌面项目已消失，布局没有更改。");
                if (ids.Count > 0) Check(Method<SetPositionCall>(view, 16)(view, (uint)ids.Count, ids.ToArray(), points.ToArray(), PositionItem), "SelectAndPositionItems");
            }
            finally
            {
                foreach (IntPtr id in ids) CoTaskMemFree(id);
                Marshal.FreeCoTaskMem(strret);
            }
        }

        public void Dispose()
        {
            if (folder != IntPtr.Zero) { Marshal.Release(folder); folder = IntPtr.Zero; }
            if (view != IntPtr.Zero) { Marshal.Release(view); view = IntPtr.Zero; }
        }
    }
}
