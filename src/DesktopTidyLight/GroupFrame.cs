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
        private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr child);
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr window, ref Point point);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        private static readonly Color Hole = Color.FromArgb(255, 1, 2);
        private readonly GroupData data;
        private readonly ToolStripTextBox rename = new ToolStripTextBox();
        private Point dragStart;
        private Rectangle beforeDrag;
        private bool dragging;
        private bool sizing;

        internal event Action<GroupFrame, Rectangle> LayoutChanged;
        internal event Action<GroupFrame> DeleteRequested;
        internal GroupData Data { get { return data; } }
        internal bool IsDragging { get { return dragging; } }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var values = base.CreateParams;
                values.ExStyle |= 0x00000080 | 0x08000000; // TOOLWINDOW | NOACTIVATE
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
            BackColor = Hole;
            TransparencyKey = Hole;
            DoubleBuffered = true;
            Bounds = new Rectangle(data.X, data.Y, Math.Max(160, data.Width), Math.Max(120, data.Height));

            var menu = new ContextMenuStrip();
            menu.Items.Add("重命名", null, (sender, args) => rename.Focus());
            rename.Text = data.Name;
            rename.KeyDown += (sender, args) =>
            {
                if (args.KeyCode != Keys.Enter) return;
                data.Name = string.IsNullOrWhiteSpace(rename.Text) ? "新建分组" : rename.Text.Trim();
                menu.Close();
                Invalidate();
                LayoutChanged?.Invoke(this, Bounds);
                args.Handled = true;
            };
            menu.Items.Add(rename);
            menu.Items.Add("删除分组", null, (sender, args) => DeleteRequested?.Invoke(this));
            ContextMenuStrip = menu;
        }

        internal void AttachToDesktop()
        {
            // Explorer's desktop view is the only layer below app windows and above its icon list.
            IntPtr host = DesktopHost();
            if (host == IntPtr.Zero) throw new InvalidOperationException("找不到 Explorer 桌面窗口，分组框未显示。");
            Point screen = new Point(data.X, data.Y);
            int style = GetWindowLong(Handle, -16);
            SetWindowLong(Handle, -16, (style | 0x40000000) & unchecked((int)~0x80000000));
            SetParent(Handle, host);
            if (GetParent(Handle) != host) throw new InvalidOperationException("无法把分组框放到桌面层。");
            if (!ScreenToClient(host, ref screen) || !SetWindowPos(Handle, IntPtr.Zero, screen.X, screen.Y, data.Width, data.Height, 0x10 | 0x40))
                throw new InvalidOperationException("无法定位桌面分组框。");
        }

        internal bool IsAttachedToDesktop()
        {
            IntPtr host = DesktopHost();
            return host != IntPtr.Zero && IsHandleCreated && GetParent(Handle) == host;
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
            sizing = args.X >= Width - 22 && args.Y >= Height - 22;
            dragging = sizing || args.Y <= 30;
            if (!dragging) return;
            dragStart = Cursor.Position;
            beforeDrag = Bounds;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs args)
        {
            base.OnMouseMove(args);
            if (!dragging) return;
            Point cursor = Cursor.Position;
            int dx = cursor.X - dragStart.X;
            int dy = cursor.Y - dragStart.Y;
            Bounds = sizing
                ? new Rectangle(beforeDrag.Location, new Size(Math.Max(MinimumSize.Width, beforeDrag.Width + dx), Math.Max(MinimumSize.Height, beforeDrag.Height + dy)))
                : new Rectangle(beforeDrag.X + dx, beforeDrag.Y + dy, beforeDrag.Width, beforeDrag.Height);
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
            Point screen = PointToScreen(Point.Empty);
            data.X = screen.X;
            data.Y = screen.Y;
            data.Width = Width;
            data.Height = Height;
        }
    }
}
