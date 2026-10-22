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
        [StructLayout(LayoutKind.Sequential)]
        private struct WindowPosition
        {
            internal IntPtr Window, After;
            internal int X, Y, Width, Height;
            internal uint Flags;
        }

        // Activation and Control.Focus may request HWND_TOP independently of our move code.
        // Keep both surfaces next to Explorer, without moving Explorer's own z-order.
        private void ConstrainDesktopOrder(ref Message message, bool background)
        {
            if (message.Msg != 0x46 || desktopOwner == IntPtr.Zero || shuttingDown) return;
            var position = (WindowPosition)Marshal.PtrToStructure(message.LParam, typeof(WindowPosition));
            if ((position.Flags & 0x4) != 0) return; // SWP_NOZORDER
            // Re-anchor instead of freezing an already incorrect position in the window stack.
            position.After = background ? Handle : OrderAnchor;
            position.Flags |= 0x200; // SWP_NOOWNERZORDER
            Marshal.StructureToPtr(position, message.LParam, false);
        }

        protected override void WndProc(ref Message message)
        {
            ConstrainDesktopOrder(ref message, false);
            base.WndProc(ref message);
        }

        private readonly GroupData data;
        internal bool IsDesktopSurface { get; private set; }
        internal IntPtr DesktopOrderAfter { get; set; }
        private IntPtr OrderAnchor { get { return DesktopOrderAfter == IntPtr.Zero ? desktopOwner : DesktopOrderAfter; } }
        internal IntPtr BottomWindow { get { return backgroundLayer == null ? Handle : backgroundLayer.Handle; } }
        private readonly Timer previewTimer = new Timer { Interval = 16 };
        private readonly ContextMenuStrip menu;
        private readonly IconCanvas canvas = new IconCanvas();
        private readonly Timer headerTimer = new Timer { Interval = 180 };
        private bool autoHideTitle, headerVisible = true;
        private string appliedSkin;
        private int transparency;
        private BackgroundLayer backgroundLayer;
        private int HeaderPixels { get { return IsDesktopSurface ? 0 : (int)(Theme.HeaderHeight * DeviceDpi / 96f); } }
        internal IconCanvas Icons { get { return canvas; } }
        private IntPtr desktopOwner;
        private Point dragStart;
        private Rectangle beforeDrag;
        private Rectangle? requested;
        private bool dragging, moreHover, shuttingDown;
        private int resizeEdges, count;
        private string hint;

        internal event Action<GroupFrame, Rectangle> LayoutChanged;
        internal event Action<GroupFrame> DeleteRequested;
        internal event Action SettingsRequested;
        internal event Action<string> LockRequested;
        internal event Action DpiLayoutChanged;
        internal Func<GroupFrame, bool> DragStarting { get; set; }
        internal Func<GroupFrame, Rectangle, bool> DragPreview { get; set; }
        internal Action<GroupFrame, bool> DragFinished { get; set; }
        internal GroupData Data { get { return data; } }
        internal int ResizeEdges { get { return resizeEdges; } }
        internal bool IsDragging { get { return dragging; } }
        internal int MemberCount { set { if (count != value) { count = value; Invalidate(); } } }
        internal string Hint { set { if (hint != value) { hint = value; Invalidate(); } } }
        internal Rectangle ScreenBounds
        {
            get
            {
                NativeRect r;
                if (!GetWindowRect(Handle, out r)) throw new InvalidOperationException("无法读取分组框位置。");
                return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= 0x80; return p; }
        }

        internal GroupFrame(GroupData data, bool desktopSurface = false)
        {
            this.data = data;
            IsDesktopSurface = desktopSurface;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = desktopSurface ? new Size(1, 1) : new Size(240, 180);
            BackColor = Theme.Background; Font = Theme.BodyFont;
            DoubleBuffered = true; KeyPreview = true;
            Bounds = GridLayout.Bounds(data);
            UpdateRegion();
            Controls.Add(canvas); LayoutCanvas();
            AllowDrop = true;
            DragEnter += (sender, args) => canvas.AcceptDrag(args);
            DragOver += (sender, args) => canvas.AcceptDrag(args);
            DragDrop += (sender, args) => canvas.AcceptDrop(args);
            headerTimer.Tick += (sender, args) =>
            {
                bool visible = !autoHideTitle || dragging || menu.Visible || new Rectangle(Point.Empty, new Size(Width, HeaderPixels)).Contains(PointToClient(Cursor.Position));
                if (visible != headerVisible) { headerVisible = visible; Invalidate(); }
            };
            menu = Theme.Menu();
            canvas.BackgroundMenuRequested += () => menu.Show(Cursor.Position);
            menu.Items.Add("重命名…", null, (s, e) => RenameGroup());
            menu.Items.Add("外观与设置…", null, (s, e) => SettingsRequested?.Invoke());
            menu.Items.Add("刷新图标", null, (s, e) => canvas.RefreshVisuals());
            var lockItem = new ToolStripMenuItem("锁定位置", null, (s, e) => LockRequested?.Invoke(data.Id)); menu.Items.Add(lockItem);
            menu.Opening += (s, e) => { lockItem.Text = data.Locked ? "解除位置锁定" : "锁定位置"; };
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("删除分组…", null, (s, e) => DeleteRequested?.Invoke(this)).Tag = "danger";
            previewTimer.Tick += (s, e) => FlushPreview();
        }

        private void RenameGroup()
        {
            using (var dialog = new Form { Text = "重命名", StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false })
            {
                dialog.SuspendLayout(); dialog.AutoScaleMode = AutoScaleMode.Dpi; Theme.StyleDialog(dialog); dialog.ClientSize = new Size(352, 124);
                var content = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 2 };
                content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                content.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                var input = new TextBox { Text = data.Name, Dock = DockStyle.Top, BackColor = Theme.Surface,
                    ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, MaxLength = 80, Margin = Padding.Empty, AccessibleName = "分组名称" };
                var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty };
                var save = Theme.Button("保存", true); save.Size = new Size(78, 32); save.DialogResult = DialogResult.OK; save.Margin = Padding.Empty;
                var cancel = Theme.Button("取消"); cancel.Size = new Size(78, 32); cancel.DialogResult = DialogResult.Cancel; cancel.Margin = new Padding(0, 0, 8, 0);
                actions.Controls.Add(save); actions.Controls.Add(cancel); content.Controls.Add(input, 0, 0); content.Controls.Add(actions, 0, 1); dialog.Controls.Add(content);
                dialog.AcceptButton = save; dialog.CancelButton = cancel; dialog.AutoScaleDimensions = new SizeF(96, 96); dialog.ResumeLayout(true);
                dialog.Shown += (s, e) => { input.Focus(); input.SelectAll(); };
                if (Dialogs.Show(dialog) != DialogResult.OK) return;
                data.Name = string.IsNullOrWhiteSpace(input.Text) ? "新建分组" : input.Text.Trim();
                Invalidate(); LayoutChanged?.Invoke(this, ScreenBounds);
            }
        }

        private void UpdateRegion()
        {
            if (IsDesktopSurface)
            {
                var old = Region; Region = canvas.ItemRegion(); old?.Dispose();
                backgroundLayer?.UpdateShape(); return;
            }
            if (Width < 20 || Height < 60) return;
            using (var shape = Theme.Rounded(new RectangleF(0, 0, Width, Height), Theme.CornerRadius * DeviceDpi / 96f))
            {
                var region = new Region(shape);

                var previous = Region; Region = region; previous?.Dispose();
            }
        }

        private void LayoutCanvas()
        {
            if (canvas == null) return;
            if (IsDesktopSurface) { canvas.Bounds = ClientRectangle; return; }
            int edge = Math.Max(5, (int)(5 * DeviceDpi / 96f));
            canvas.Bounds = new Rectangle(edge, HeaderPixels, Math.Max(1, ClientSize.Width - edge * 2), Math.Max(1, ClientSize.Height - HeaderPixels - edge));
        }
        protected override void OnSizeChanged(EventArgs args)
        {
            base.OnSizeChanged(args); UpdateRegion(); LayoutCanvas();
            // Resize changes the whole header/border geometry, not just newly exposed pixels.
            Invalidate(true);
        }
        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            // DisplayLayout owns physical geometry; do not also apply WinForms' suggested rectangle.
            e.Cancel = true; base.OnDpiChanged(e);
            LayoutCanvas(); Invalidate();
            DpiLayoutChanged?.Invoke();
        }
        internal void ApplyPreferences(bool autoHide, int backgroundTransparency = 0)
        {
            if (IsDesktopSurface) { autoHide = false; backgroundTransparency = 100; }
            if (appliedSkin == Theme.Skin && autoHideTitle == autoHide && transparency == backgroundTransparency) return;
            appliedSkin = Theme.Skin;
            transparency = Math.Max(0, Math.Min(100, backgroundTransparency));
            autoHideTitle = autoHide; headerVisible = !autoHide;
            headerTimer.Enabled = autoHide;
            BackColor = transparency > 0 ? Theme.TransparencyColor : Theme.Background;
            TransparencyKey = transparency > 0 ? Theme.TransparencyColor : Color.Empty;
            canvas.RefreshTheme(transparency > 0); UpdateBackground(); LayoutCanvas(); UpdateRegion(); Invalidate(true);
        }

        internal void AttachToDesktop()
        {
            desktopOwner = DesktopRoot();
            if (desktopOwner == IntPtr.Zero) throw new InvalidOperationException("找不到 Explorer 桌面窗口。");
            SetWindowLongPtr(Handle, -8, desktopOwner);
            if (GetWindow(Handle, 4) != desktopOwner) throw new InvalidOperationException("无法把分组附到桌面。");
            UpdateBackground();
            SetScreenBounds(GridLayout.Bounds(data));
        }

        internal void SetScreenBounds(Rectangle bounds)
        {
            uint flags = 0x10 | 0x200;
            if (Size != bounds.Size) flags |= 0x100; // SWP_NOCOPYBITS: never stretch/copy an old border into a resized frame.
            if (desktopOwner == IntPtr.Zero || !SetWindowPos(Handle, OrderAnchor, bounds.X, bounds.Y, bounds.Width, bounds.Height, flags))
                throw new System.ComponentModel.Win32Exception("无法定位桌面分组框。");
            if (backgroundLayer != null)
                SetWindowPos(backgroundLayer.Handle, Handle, bounds.X, bounds.Y, bounds.Width, bounds.Height, flags);
        }

        internal void RestoreDesktopOrder()
        {
            if (desktopOwner == IntPtr.Zero || !IsHandleCreated) return;
            if (!SetWindowPos(Handle, OrderAnchor, 0, 0, 0, 0, 0x13 | 0x200))
                throw new System.ComponentModel.Win32Exception("无法恢复桌面分组层级。");
            if (backgroundLayer != null)
                SetWindowPos(backgroundLayer.Handle, Handle, 0, 0, 0, 0, 0x13 | 0x200);
        }

        internal bool IsAbove(IntPtr window)
        {
            if (window == IntPtr.Zero || !IsHandleCreated) return false;
            for (var current = GetWindow(window, 3); current != IntPtr.Zero; current = GetWindow(current, 3))
                if (current == Handle || (backgroundLayer != null && current == backgroundLayer.Handle)) return true;
            return false;
        }

        private void UpdateBackground()
        {
            if (desktopOwner == IntPtr.Zero) return;
            if (transparency == 0) { backgroundLayer?.Dispose(); backgroundLayer = null; return; }
            if (backgroundLayer == null)
            {
                backgroundLayer = new BackgroundLayer(this);
                SetWindowLongPtr(backgroundLayer.Handle, -8, desktopOwner);
            }
            backgroundLayer.BackColor = Theme.Background;
            // One alpha step keeps the otherwise invisible empty area a usable drop target.
            backgroundLayer.Opacity = Math.Max(1d / 255, (100 - transparency) / 100d);
            backgroundLayer.Bounds = ScreenBounds; backgroundLayer.UpdateShape();
            backgroundLayer.Show();
            SetWindowPos(backgroundLayer.Handle, Handle, Left, Top, Width, Height, 0x10 | 0x40 | 0x200);
        }

        private sealed class BackgroundLayer : Form
        {
            private readonly GroupFrame frame;
            [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
            internal BackgroundLayer(GroupFrame frame)
            {
                this.frame = frame; Tag = frame; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None; AllowDrop = true;
                DragEnter += (s, e) => frame.canvas.AcceptDrag(e);
                DragOver += (s, e) => frame.canvas.AcceptDrag(e);
                DragLeave += (s, e) => frame.canvas.AcceptLeave();
                DragDrop += (s, e) => frame.canvas.AcceptDrop(e);
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x80 | 0x08000000; return p; } }
            internal void UpdateShape()
            {
                if (frame.IsDesktopSurface)
                { var previous = Region; Region = frame.canvas.ItemRegion(); previous?.Dispose(); return; }
                using (var shape = Theme.Rounded(new RectangleF(0, 0, Width, Height), Theme.CornerRadius * frame.DeviceDpi / 96f))
                { var old = Region; Region = new Region(shape); old?.Dispose(); }
            }
            protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); if (frame != null) UpdateShape(); }
            protected override void WndProc(ref Message m)
            {
                frame?.ConstrainDesktopOrder(ref m, true);
                if (frame != null && m.Msg >= 0x200 && m.Msg <= 0x20E && m.Msg != 0x20D)
                {
                    Point screen = Cursor.Position, point = frame.PointToClient(screen);
                    Control target = frame.canvas.Bounds.Contains(point) ? (Control)frame.canvas : frame;
                    Point local = target.PointToClient(screen);
                    IntPtr coordinates = m.Msg == 0x20A || m.Msg == 0x20E ? m.LParam : new IntPtr((local.X & 0xffff) | (local.Y << 16));
                    m.Result = SendMessage(target.Handle, m.Msg, m.WParam, coordinates); return;
                }
                base.WndProc(ref m);
            }
        }

        internal bool IsAttachedToDesktop()
        {
            IntPtr root = DesktopRoot();
            return root != IntPtr.Zero && IsHandleCreated && GetWindow(Handle, 4) == root;
        }

        private static IntPtr DesktopRoot()
        {
            IntPtr progman = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null);
            IntPtr host = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (host == IntPtr.Zero) EnumWindows((window, data) =>
            {
                host = FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null); return host == IntPtr.Zero;
            }, IntPtr.Zero);
            return host == IntPtr.Zero ? IntPtr.Zero : GetAncestor(host, 2);
        }

        internal bool IsDesktopWindow(IntPtr window)
        { return window != IntPtr.Zero && window == desktopOwner; }

        protected override void OnPaint(PaintEventArgs args)
        {
            if (IsDesktopSurface) return;
            float scale = DeviceDpi / 96f;
            args.Graphics.ScaleTransform(scale, scale);
            Theme.PaintContainer(args.Graphics, new Size((int)(ClientSize.Width / scale), (int)(ClientSize.Height / scale)), data.Name ?? "新建分组", count, moreHover, dragging, hint, true, headerVisible, transparency > 0);
        }

        protected override void OnMouseDown(MouseEventArgs args)
        {
            base.OnMouseDown(args);
            if (IsDesktopSurface) return;
            if (args.Button == MouseButtons.Right && args.Y < HeaderPixels) { menu.Show(this, args.Location); return; }
            if (args.Button != MouseButtons.Left) return;
            if (new Rectangle(Width - (int)(40 * DeviceDpi / 96f), 4, (int)(32 * DeviceDpi / 96f), HeaderPixels - 8).Contains(args.Location)) { menu.Show(this, new Point(Width - (int)(40 * DeviceDpi / 96f), HeaderPixels)); return; }
            if (data.Locked) return;
            resizeEdges = Edges(args.Location);
            if (resizeEdges == 0 && args.Y >= HeaderPixels) return;
            if (DragStarting == null || !DragStarting(this)) return;
            beforeDrag = ScreenBounds; dragStart = Cursor.Position;
            dragging = true; Capture = true; previewTimer.Start(); Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs args)
        {
            base.OnMouseMove(args);
            if (IsDesktopSurface) { Cursor = Cursors.Default; return; }
            if (!dragging)
            {
                bool hover = new Rectangle(Width - (int)(40 * DeviceDpi / 96f), 4, (int)(32 * DeviceDpi / 96f), HeaderPixels - 8).Contains(args.Location);
                if (hover != moreHover) { moreHover = hover; Invalidate(); }
                int edges = Edges(args.Location);
                Cursor = hover ? Cursors.Hand : data.Locked ? Cursors.Default : edges == 3 || edges == 12 ? Cursors.SizeNWSE : edges == 6 || edges == 9 ? Cursors.SizeNESW :
                    (edges & 5) != 0 ? Cursors.SizeWE : (edges & 10) != 0 ? Cursors.SizeNS : Cursors.SizeAll;
                return;
            }
            Point cursor = Cursor.Position;
            int dx = cursor.X - dragStart.X, dy = cursor.Y - dragStart.Y;
            if (resizeEdges == 0) requested = new Rectangle(beforeDrag.X + dx, beforeDrag.Y + dy, beforeDrag.Width, beforeDrag.Height);
            else
            {
                int left = beforeDrag.Left, top = beforeDrag.Top, right = beforeDrag.Right, bottom = beforeDrag.Bottom;
                if ((resizeEdges & 1) != 0) left = Math.Min(right - MinimumSize.Width, left + dx);
                if ((resizeEdges & 2) != 0) top = Math.Min(bottom - MinimumSize.Height, top + dy);
                if ((resizeEdges & 4) != 0) right = Math.Max(left + MinimumSize.Width, right + dx);
                if ((resizeEdges & 8) != 0) bottom = Math.Max(top + MinimumSize.Height, bottom + dy);
                requested = Rectangle.FromLTRB(left, top, right, bottom);
            }
        }

        private void FlushPreview()
        {
            if (!dragging || !requested.HasValue) return;
            Rectangle bounds = requested.Value; requested = null;
            if (bounds == ScreenBounds) return;
            DragPreview?.Invoke(this, bounds);
        }

        private void Finish(bool cancel)
        {
            if (!dragging) return;
            if (!cancel) FlushPreview();
            if (IsDisposed || shuttingDown) return;
            dragging = false; previewTimer.Stop(); requested = null; Capture = false; hint = null;
            DragFinished?.Invoke(this, cancel);
            if (!IsDisposed) Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs args) { base.OnMouseUp(args); if (args.Button == MouseButtons.Left) Finish(false); }
        protected override void OnMouseLeave(EventArgs args) { base.OnMouseLeave(args); moreHover = false; Invalidate(); }
        protected override void OnMouseCaptureChanged(EventArgs args) { base.OnMouseCaptureChanged(args); if (!shuttingDown && dragging && !Capture) Finish(true); }
        protected override void OnKeyDown(KeyEventArgs args)
        {
            if (args.KeyCode == Keys.Escape && dragging) { Finish(true); args.Handled = true; }
            else if (args.KeyCode == Keys.F2 && !dragging) { RenameGroup(); args.Handled = true; }
            base.OnKeyDown(args);
        }

        private int Edges(Point p)
        {
            return (p.X < 6 ? 1 : p.X >= Width - 6 ? 4 : 0) | (p.Y < 5 ? 2 : p.Y >= Height - 6 ? 8 : 0);
        }

        internal void LoadData(GroupData source)
        {
            bool renamed = data.Name != source.Name;
            data.Name = source.Name; data.X = source.X; data.Y = source.Y; data.Width = source.Width; data.Height = source.Height;
            data.Locked = source.Locked;
            var bounds = GridLayout.Bounds(source);
            if (ScreenBounds != bounds) SetScreenBounds(bounds); else if (renamed) Invalidate();
        }

        internal void SetDesktopItems(System.Collections.Generic.IEnumerable<DesktopItem> items, Point origin, Size spacing, int iconSize)
        {
            canvas.SetDesktopItems(items, new Point(origin.X - Left, origin.Y - Top), spacing, iconSize);
            UpdateRegion();
        }

        internal void SetDesktopSelection(Rectangle? screenArea, bool active)
        {
            Rectangle? local = screenArea;
            if (screenArea.HasValue) { var value = screenArea.Value; value.Offset(-Left, -Top); local = value; }
            canvas.SetDesktopSelection(local, active); UpdateRegion();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { shuttingDown = true; previewTimer.Stop(); previewTimer.Dispose(); headerTimer.Dispose(); backgroundLayer?.Dispose(); menu?.Dispose(); }
            base.Dispose(disposing);
        }

    }
}
