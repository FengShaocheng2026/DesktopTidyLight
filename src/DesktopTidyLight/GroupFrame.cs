using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopTidyLight
{
    internal sealed class GroupFrame : Form
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        private delegate bool WindowCallback(IntPtr window, IntPtr data);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(WindowCallback callback, IntPtr data);
        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { internal int Left, Top, Right, Bottom; }
        private readonly GroupData data;
        private Point dragStart;
        private Rectangle beforeDrag;
        private bool dragging;
        private bool sizing;

        internal event Action<GroupFrame, Rectangle> LayoutChanged;
        internal event Action<GroupFrame> DeleteRequested;
        internal GroupData Data { get { return data; } }
        internal bool IsDragging { get { return dragging; } }
        internal Rectangle ScreenBounds
        {
            get
            {
                NativeRect rectangle;
                if (!GetWindowRect(Handle, out rectangle)) throw new InvalidOperationException("无法读取分组框位置。");
                return Rectangle.FromLTRB(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var values = base.CreateParams;
                values.ExStyle |= 0x00000080; // TOOLWINDOW
                return values;
            }
        }

        internal GroupFrame(GroupData data)
        {
            this.data = data;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(160, 120);
            BackColor = Color.FromArgb(31, 42, 60);
            DoubleBuffered = true;
            Bounds = new Rectangle(data.X, data.Y, Math.Max(160, data.Width), Math.Max(120, data.Height));
            UpdateWindowRegion();

        }

        private void RenameGroup()
        {
            using (var dialog = new Form())
            {
                dialog.Text = "重命名分组";
                dialog.StartPosition = FormStartPosition.CenterScreen;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.ClientSize = new Size(310, 105);
                var input = new TextBox { Text = data.Name, Location = new Point(15, 15), Width = 280 };
                var save = new Button { Text = "保存", DialogResult = DialogResult.OK, Location = new Point(220, 58) };
                dialog.Controls.Add(input);
                dialog.Controls.Add(save);
                dialog.AcceptButton = save;
                dialog.Shown += (sender, args) => { input.Focus(); input.SelectAll(); };
                if (dialog.ShowDialog() != DialogResult.OK) return;
                data.Name = string.IsNullOrWhiteSpace(input.Text) ? "新建分组" : input.Text.Trim();
                Invalidate();
                LayoutChanged?.Invoke(this, ScreenBounds);
            }
        }

        private void UpdateWindowRegion()
        {
            if (Width < 12 || Height < 40) return;
            var outline = new Region(new Rectangle(0, 0, Width, Height));
            outline.Exclude(new Rectangle(6, 34, Width - 12, Height - 40));
            var previous = Region;
            Region = outline;
            previous?.Dispose();
        }

        protected override void OnSizeChanged(EventArgs args)
        {
            base.OnSizeChanged(args);
            UpdateWindowRegion();
        }

        internal void AttachToDesktop()
        {
            IntPtr root = DesktopRoot();
            if (root == IntPtr.Zero) throw new InvalidOperationException("找不到 Explorer 桌面窗口，分组框未显示。");
            SetWindowLongPtr(Handle, -8, root); // Own the popup by the desktop, not by an app window.
            if (GetWindow(Handle, 4) != root) throw new InvalidOperationException("无法把分组框附到桌面窗口。");
            SetScreenBounds(new Rectangle(data.X, data.Y, data.Width, data.Height));
        }

        internal void SetScreenBounds(Rectangle bounds)
        {
            IntPtr root = DesktopRoot();
            if (root == IntPtr.Zero ||
                !SetWindowPos(Handle, root, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x10 | 0x40))
                throw new InvalidOperationException("无法定位桌面分组框。");
            Invalidate();
        }

        internal bool IsAttachedToDesktop()
        {
            IntPtr root = DesktopRoot();
            return root != IntPtr.Zero && IsHandleCreated && GetWindow(Handle, 4) == root;
        }

        private static IntPtr DesktopRoot()
        {
            IntPtr host = DesktopHost();
            return host == IntPtr.Zero ? IntPtr.Zero : GetAncestor(host, 2);
        }

        private static IntPtr DesktopHost()
        {
            IntPtr progman = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null);
            IntPtr host = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (host != IntPtr.Zero) return host;
            EnumWindows((window, data) =>
            {
                host = FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null);
                return host == IntPtr.Zero;
            }, IntPtr.Zero);
            return host;
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            using (var border = new Pen(Color.FromArgb(80, 139, 206), 3))
            using (var header = new SolidBrush(Color.FromArgb(45, 78, 126)))
            using (var ink = new SolidBrush(Color.White))
            {
                args.Graphics.FillRectangle(header, 0, 0, Width, 30);
                args.Graphics.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
                args.Graphics.DrawString(data.Name ?? "新建分组", Font, ink, 8, 7);
                args.Graphics.DrawString("改", Font, ink, Width - 60, 7);
                args.Graphics.DrawString("×", Font, ink, Width - 30, 7);
                args.Graphics.FillRectangle(ink, Width - 16, Height - 6, 12, 3);
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x84) // WM_NCHITTEST
            {
                var screen = new Point(unchecked((short)(long)message.LParam), unchecked((short)((long)message.LParam >> 16)));
                Point point = PointToClient(screen);
                if (point.X > 4 && point.X < Width - 5 && point.Y > 32 && point.Y < Height - 6)
                {
                    message.Result = new IntPtr(-1); // HTTRANSPARENT
                    return;
                }
            }
            base.WndProc(ref message);
        }

        protected override void OnMouseDown(MouseEventArgs args)
        {
            base.OnMouseDown(args);
            if (args.Button != MouseButtons.Left) return;
            Rectangle bounds = ScreenBounds;
            Point cursor = Cursor.Position;
            int x = cursor.X - bounds.Left;
            int y = cursor.Y - bounds.Top;
            if (y <= 30 && x >= bounds.Width - 64)
            {
                if (x >= bounds.Width - 34) DeleteRequested?.Invoke(this);
                else RenameGroup();
                return;
            }
            sizing = x >= bounds.Width - 22 && y >= bounds.Height - 22;
            dragging = sizing || y <= 30;
            if (!dragging) return;
            dragStart = cursor;
            beforeDrag = bounds;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs args)
        {
            base.OnMouseMove(args);
            if (!dragging) return;
            Point cursor = Cursor.Position;
            int dx = cursor.X - dragStart.X;
            int dy = cursor.Y - dragStart.Y;
            Rectangle target = sizing
                ? new Rectangle(beforeDrag.Location, new Size(Math.Max(MinimumSize.Width, beforeDrag.Width + dx), Math.Max(MinimumSize.Height, beforeDrag.Height + dy)))
                : new Rectangle(beforeDrag.X + dx, beforeDrag.Y + dy, beforeDrag.Width, beforeDrag.Height);
            SetScreenBounds(target);
        }

        protected override void OnMouseUp(MouseEventArgs args)
        {
            base.OnMouseUp(args);
            if (!dragging) return;
            dragging = false;
            Capture = false;
            LayoutChanged?.Invoke(this, beforeDrag);
        }

        internal void StoreBounds()
        {
            Rectangle bounds = ScreenBounds;
            data.X = bounds.X;
            data.Y = bounds.Y;
            data.Width = bounds.Width;
            data.Height = bounds.Height;
        }
    }
}
