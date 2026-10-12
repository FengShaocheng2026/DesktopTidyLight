using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.IO;
using System.Linq;
using Microsoft.Win32.SafeHandles;

namespace DesktopTidyLight
{
    internal sealed class DesktopItem
    {
        internal string Id;
        internal Point Position;
        internal string Identity;
    }

    // Implemented by the real Shell and by an in-memory desktop in the regression checks.
    internal interface IDesktopView
    {
        int PositioningFlags { get; }
        Point Origin { get; }
        Size Spacing { get; }
        int IconSize { get; }
        List<DesktopItem> Items();
        IList<string> SelectedIds();
        void SetPositioningFlags(int flags);
        void SetPositions(IDictionary<string, Point> positions);
    }

    // Each instance keeps all COM calls on its creating STA thread. No desktop files are changed here.
    internal sealed class DesktopShell : IDisposable, IDesktopView
    {
        private static readonly Guid ServiceProviderId = new Guid("6D5140C1-7436-11CE-8034-00AA006009FA");
        private static readonly Guid BrowserId = new Guid("000214E2-0000-0000-C000-000000000046");
        private static readonly Guid FolderViewId = new Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE");
        private static readonly Guid FolderView2Id = new Guid("1AF3A467-214F-4298-908E-06B03E0B39F9");
        private static readonly Guid ShellFolderId = new Guid("000214E6-0000-0000-C000-000000000046");
        private const int DesktopClass = 8;
        private const int NeedDispatch = 1;
        private const uint AllItems = 2; // SVGIO_ALLVIEW
        private const uint ForParsing = 0x8000;
        private const uint PositionItem = 0x80000080; // SVSI_POSITIONITEM | SVSI_NOSTATECHANGE
        private IntPtr view;
        private IntPtr folder;
        private Dictionary<string, IntPtr> prepared;
        private Dictionary<string, int> preparedIds;
        private IntPtr listWindow;
        private Dictionary<string, Point> preparedPoints;
        private Size previewSpacing;
        private int? iconSize;
        internal long PreviewValidationTicks, PreviewMovementTicks;
        internal Action BeforePreviewPaint;
        public Point Origin { get; private set; }
        internal IntPtr ViewWindow { get; private set; }
        internal uint ProcessId { get; private set; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { internal int X; internal int Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct FileInformation
        {
            internal uint Attributes;
            internal System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
            internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetProp(IntPtr window, string name);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr window, out NativeRect rectangle);
        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr window, ref NativeRect rectangle, IntPtr region, uint flags);
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out IntPtr result);
        private IntPtr ListMessage(uint message, int index, int value = 0)
        {
            IntPtr result;
            if (SendMessageTimeout(listWindow, message, new IntPtr(index), new IntPtr(value), 2, 200, out result) == IntPtr.Zero)
                throw new InvalidOperationException("桌面暂时没有响应，已停止实时移动。");
            return result;
        }
        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SpacingCall(IntPtr self, out NativePoint point);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int IconSizeCall(IntPtr self, out uint mode, out int size);

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
        private delegate int ItemsCall(IntPtr self, uint flags, ref Guid iid, out IntPtr enumerator);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int NextCall(IntPtr self, uint count, out IntPtr pidl, out uint fetched);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int FocusedCall(IntPtr self, out int index);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int PositionCall(IntPtr self, IntPtr pidl, out NativePoint point);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int AutoArrangeCall(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetFlagsCall(IntPtr self, out uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetFlagsCall(IntPtr self, uint mask, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int DisplayNameCall(IntPtr self, IntPtr pidl, uint flags, IntPtr strret);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetPositionCall(IntPtr self, uint count, [In] IntPtr[] pidls, [In] NativePoint[] points, uint flags);
        [DllImport("shell32.dll")]
        private static extern bool ILIsEqual(IntPtr first, IntPtr second);
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
                IntPtr viewWindow;
                Check(Method<QueryViewCall>(shellView, 3)(shellView, out viewWindow), "IOleWindow.GetWindow");
                ViewWindow = viewWindow;
                uint processId;
                GetWindowThreadProcessId(viewWindow, out processId);
                ProcessId = processId;
                IntPtr list = FindWindowEx(viewWindow, IntPtr.Zero, "SysListView32", null);
                var corner = new NativePoint();
                if (list == IntPtr.Zero || !ClientToScreen(list, ref corner))
                    throw new InvalidOperationException("无法读取桌面图标区域，已停止定位图标。");
                listWindow = list;
                Origin = new Point(corner.X, corner.Y);
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

        internal int FolderFlags
        {
            get
            {
                IntPtr extended = IntPtr.Zero;
                Guid id = FolderView2Id;
                try
                {
                    Check(Marshal.QueryInterface(view, ref id, out extended), "IFolderView2");
                    uint flags;
                    Check(Method<GetFlagsCall>(extended, 25)(extended, out flags), "GetCurrentFolderFlags");
                    return (int)flags;
                }
                finally { if (extended != IntPtr.Zero) Marshal.Release(extended); }
            }
        }

        public int PositioningFlags { get { return (FolderFlags & 0x4) | (AutoArrange ? 0x1 : 0); } }

        public Size Spacing
        {
            get
            {
                NativePoint point;
                Check(Method<SpacingCall>(view, 12)(view, out point), "GetSpacing");
                if (point.X <= 0 || point.Y <= 0) throw new InvalidOperationException("无法读取图标间距。");
                return new Size(point.X, point.Y);
            }
        }

        public int IconSize
        {
            get
            {
                if (iconSize.HasValue) return iconSize.Value;
                IntPtr extended = IntPtr.Zero;
                var id = FolderView2Id;
                try
                {
                    Check(Marshal.QueryInterface(view, ref id, out extended), "IFolderView2");
                    uint mode; int size;
                    Check(Method<IconSizeCall>(extended, 36)(extended, out mode, out size), "GetViewModeAndIconSize");
                    iconSize = Math.Max(1, size);
                    return iconSize.Value;
                }
                finally { if (extended != IntPtr.Zero) Marshal.Release(extended); }
            }
        }

        internal void PreparePositions(IEnumerable<string> ids)
        {
            if (prepared != null) throw new InvalidOperationException("已有正在进行的拖动。");
            if (!IsWindowVisible(listWindow)) throw new InvalidOperationException("请先显示桌面图标，再拖动分组。");
            prepared = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
            preparedIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            preparedPoints = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
            previewSpacing = Spacing;
            var wanted = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            int count;
            Check(Method<ItemCountCall>(view, 7)(view, AllItems, out count), "ItemCount");
            for (int i = 0; i < count; i++)
            {
                IntPtr pidl;
                Check(Method<ItemCall>(view, 6)(view, i, out pidl), "Item");
                try
                {
                    string id = ParsingName(pidl);
                    if (wanted.Contains(id))
                    {
                        NativePoint point;
                        Check(Method<PositionCall>(view, 11)(view, pidl, out point), "PreparePosition");
                        preparedPoints.Add(id, new Point(point.X, point.Y));
                        preparedIds.Add(id, i); prepared.Add(id, pidl); pidl = IntPtr.Zero;
                    }
                }
                finally { if (pidl != IntPtr.Zero) CoTaskMemFree(pidl); }
            }
            if (prepared.Count != wanted.Count) throw new InvalidOperationException("部分图标已变化，请重新拖动。");
        }

        public void SetPositioningFlags(int flags)
        {
            IntPtr extended = IntPtr.Zero;
            Guid id = FolderView2Id;
            try
            {
                Check(Marshal.QueryInterface(view, ref id, out extended), "IFolderView2");
                Check(Method<SetFlagsCall>(extended, 24)(extended, 0x5, (uint)flags & 0x5), "SetCurrentFolderFlags");
                if (PositioningFlags != (flags & 0x5))
                    throw new InvalidOperationException("Explorer 没有应用图标排列设置。");
            }
            finally { if (extended != IntPtr.Zero) Marshal.Release(extended); }
        }


        internal static string FileIdentity(string id)
        {
            if (id.StartsWith("::", StringComparison.Ordinal) || !Path.IsPathRooted(id)) return "shell:" + id;
            // Open metadata only, including reparse points: never read or hydrate file contents.
            using (var handle = CreateFile(id, 0, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero))
            {
                FileInformation info;
                if (handle.IsInvalid || !GetFileInformationByHandle(handle, out info)) return null;
                if (info.IndexHigh == 0 && info.IndexLow == 0) return null;
                return info.Volume.ToString("X8") + ":" + info.IndexHigh.ToString("X8") + info.IndexLow.ToString("X8") +
                    ":" + info.Creation.dwHighDateTime.ToString("X8") + info.Creation.dwLowDateTime.ToString("X8");
            }
        }

        private string ParsingName(IntPtr pidl)
        {
            IntPtr strret = Marshal.AllocCoTaskMem(520);
            try
            {
                Check(Method<DisplayNameCall>(folder, 11)(folder, pidl, ForParsing, strret), "GetDisplayNameOf");
                IntPtr value;
                Check(StrRetToStrW(strret, pidl, out value), "StrRetToStrW");
                try { return Marshal.PtrToStringUni(value); }
                finally { CoTaskMemFree(value); }
            }
            finally { Marshal.FreeCoTaskMem(strret); }
        }

        public IList<string> SelectedIds()
        {
            var result = new List<string>();
            int count;
            Check(Method<ItemCountCall>(view, 7)(view, 1, out count), "SelectedItemCount");
            if (count == 0) return result;
            IntPtr enumerator = IntPtr.Zero;
            var enumId = new Guid("000214F2-0000-0000-C000-000000000046");
            try
            {
                Check(Method<ItemsCall>(view, 8)(view, 1, ref enumId, out enumerator), "SelectedItems");
                if (enumerator == IntPtr.Zero) return result;
                while (true)
                {
                    IntPtr pidl;
                    uint fetched;
                    Check(Method<NextCall>(enumerator, 3)(enumerator, 1, out pidl, out fetched), "NextSelectedItem");
                    try { if (fetched == 0) break; result.Add(ParsingName(pidl)); }
                    finally { if (pidl != IntPtr.Zero) CoTaskMemFree(pidl); }
                }
                int focused;
                if (Method<FocusedCall>(view, 10)(view, out focused) == 0 && focused >= 0)
                {
                    IntPtr pidl;
                    Check(Method<ItemCall>(view, 6)(view, focused, out pidl), "FocusedItem");
                    try
                    {
                        string id = ParsingName(pidl);
                        if (result.Remove(id)) result.Insert(0, id);
                    }
                    finally { CoTaskMemFree(pidl); }
                }
                return result;
            }
            finally { if (enumerator != IntPtr.Zero) Marshal.Release(enumerator); }
        }

        public List<DesktopItem> Items()
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
                        try
                        {
                            string id = Marshal.PtrToStringUni(value);
                            result.Add(new DesktopItem { Id = id, Identity = FileIdentity(id), Position = new Point(point.X, point.Y) });
                        }
                        finally { CoTaskMemFree(value); }
                    }
                    finally { if (pidl != IntPtr.Zero) CoTaskMemFree(pidl); }
                }
            }
            finally { Marshal.FreeCoTaskMem(strret); }
            return result;
        }

        private void ValidatePreviewIndices(IEnumerable<string> ids)
        {
            var item = Method<ItemCall>(view, 6);
            foreach (var id in ids)
            {
                IntPtr current;
                Check(item(view, preparedIds[id], out current), "PreviewItem");
                try { if (!ILIsEqual(current, prepared[id])) throw new InvalidOperationException("桌面项目已变化，请重新拖动。"); }
                finally { CoTaskMemFree(current); }
            }
        }

        public void SetPositions(IDictionary<string, Point> positions)
        {
            if (AutoArrange) throw new InvalidOperationException("请先在 Windows 桌面关闭“自动排列图标”。");
            if (prepared != null && positions.Keys.All(prepared.ContainsKey))
            {
                var entries = positions.ToArray();
                // Check binary PIDLs locally; do not resolve file names on every frame.
                var measure = System.Diagnostics.Stopwatch.StartNew();
                ValidatePreviewIndices(entries.Select(p => p.Key));
                PreviewValidationTicks += measure.ElapsedTicks; measure.Restart();
                Rectangle dirty = Rectangle.Empty;
                foreach (var entry in entries)
                    foreach (var point in new[] { preparedPoints[entry.Key], entry.Value })
                    {
                        var cell = GridLayout.ItemBounds(point, previewSpacing, IconSize); cell.Inflate(24, 24);
                        dirty = dirty.IsEmpty ? cell : Rectangle.Union(dirty, cell);
                    }
                var nativeDirty = new NativeRect { Left = dirty.Left, Top = dirty.Top, Right = dirty.Right, Bottom = dirty.Bottom };
                ListMessage(0xB, 0); // WM_SETREDRAW: present the whole group in one repaint.
                try
                {
                    foreach (var entry in entries)
                    {
                        int index = preparedIds[entry.Key];
                        var point = entry.Value;
                        if (point.X < short.MinValue || point.X > short.MaxValue || point.Y < short.MinValue || point.Y > short.MaxValue)
                        {
                            Check(Method<SetPositionCall>(view, 16)(view, 1, new[] { prepared[entry.Key] },
                                new[] { new NativePoint { X = point.X, Y = point.Y } }, PositionItem), "PreviewPositions");
                        }
                        else if (ListMessage(0x100F, index, (point.X & 0xffff) | (point.Y << 16)) == IntPtr.Zero)
                            throw new InvalidOperationException("桌面拒绝了实时定位。"); // LVM_SETITEMPOSITION
                        preparedPoints[entry.Key] = point;
                    }
                    BeforePreviewPaint?.Invoke();
                }
                finally
                {
                    ListMessage(0xB, 1);
                    RedrawWindow(listWindow, ref nativeDirty, IntPtr.Zero, 0x1); // invalidate without blocking on paint
                }
                PreviewMovementTicks += measure.ElapsedTicks;
                return;
            }
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

        internal void RecoverPreviewDrawing()
        {
            // A process exit inside the short redraw batch must not leave Explorer unable to repaint.
            if (GetProp(listWindow, "SysSetRedraw") == IntPtr.Zero) return;
            ListMessage(0xB, 1);
            NativeRect area;
            if (GetClientRect(listWindow, out area)) RedrawWindow(listWindow, ref area, IntPtr.Zero, 0x1 | 0x4);
        }

        internal void EndPreview()
        {
            if (prepared != null) { foreach (var pidl in prepared.Values) CoTaskMemFree(pidl); prepared = null; }
            preparedIds = null; preparedPoints = null; BeforePreviewPaint = null;
        }

        public void Dispose()
        {
            EndPreview();
            if (folder != IntPtr.Zero) { Marshal.Release(folder); folder = IntPtr.Zero; }
            if (view != IntPtr.Zero) { Marshal.Release(view); view = IntPtr.Zero; }
        }
    }
}
