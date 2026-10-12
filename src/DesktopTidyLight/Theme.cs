using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopTidyLight
{
    internal static class Theme
    {
        internal static readonly Color TransparencyColor = Color.FromArgb(1, 2, 3);
        internal static Color Background = Color.FromArgb(22, 29, 40);
        internal static Color Surface = Color.FromArgb(31, 42, 57);
        internal static Color Text = Color.FromArgb(238, 244, 251);
        internal static Color Muted = Color.FromArgb(151, 169, 190);
        internal static Color Accent = Color.FromArgb(127, 222, 202);
        internal static readonly Font BodyFont = new Font("Segoe UI", 10f);
        internal static readonly Font TitleFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        internal static readonly Font SmallFont = new Font("Segoe UI", 9f);

        internal static Color Border = Color.FromArgb(63, 65, 71);
        internal static int HeaderHeight { get { return Skin == "outline" ? 34 : Skin == "plum" ? 44 : 40; } }
        internal static int CornerRadius { get { return Skin == "outline" ? 4 : Skin == "plum" ? 18 : 12; } }
        internal static string Skin { get; private set; } = "mist";
        internal static void Apply(string skin)
        {
            Skin = skin;
            if (skin == "paper") { Background = Color.FromArgb(249,249,247); Surface = Color.FromArgb(234,235,233); Text = Color.FromArgb(34,36,40); Muted = Color.FromArgb(115,118,126); Accent = Color.FromArgb(83,106,207); Border = Color.FromArgb(217,219,218); }
            else if (skin == "outline") { Background = Color.FromArgb(23,24,26); Surface = Color.FromArgb(39,41,45); Text = Color.FromArgb(239,240,243); Muted = Color.FromArgb(139,144,152); Accent = Color.FromArgb(191,202,224); Border = Color.FromArgb(66,70,78); }
            else if (skin == "plum") { Background = Color.FromArgb(35,31,41); Surface = Color.FromArgb(51,44,61); Text = Color.FromArgb(243,238,248); Muted = Color.FromArgb(168,156,183); Accent = Color.FromArgb(192,170,230); Border = Color.FromArgb(73,61,86); }
            else { Background = Color.FromArgb(28,29,33); Surface = Color.FromArgb(41,43,49); Text = Color.FromArgb(239,241,246); Muted = Color.FromArgb(146,151,163); Accent = Color.FromArgb(150,173,246); Border = Color.FromArgb(64,67,76); }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        internal static Icon CreateIcon()
        {
            using (var stream = typeof(Theme).Assembly.GetManifestResourceStream("DesktopTidyLight.AppIcon"))
            using (var icon = new Icon(stream)) return (Icon)icon.Clone();
        }

        internal static GraphicsPath Rounded(RectangleF bounds, float radius = 14)
        {
            if (Skin == "outline") radius = Math.Min(radius, 3);
            float d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static void PaintContainer(Graphics graphics, Size size, string name, int count, bool hover, bool dragging, string hint, bool body, bool showHeader = true, bool transparent = false)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(transparent ? TransparencyColor : Background);
            using (var shape = Rounded(new RectangleF(.5f, .5f, size.Width - 1, size.Height - 1), CornerRadius))
            using (var border = new Pen(dragging ? Color.FromArgb(130, Accent) : Border, .75f)) graphics.DrawPath(border, shape);
            if (!showHeader)
            {
                using (var grip = new Pen(Border, 2)) graphics.DrawLine(grip, size.Width / 2 - 9, 8, size.Width / 2 + 9, 8);
                return;
            }
            if (Skin == "outline") using (var line = new Pen(Border, .7f)) graphics.DrawLine(line, 14, HeaderHeight - 1, size.Width - 14, HeaderHeight - 1);
            using (var format = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            using (var titleFont = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var smallFont = new Font("Segoe UI", 10, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var ink = new SolidBrush(Text))
            using (var muted = new SolidBrush(Muted))
            {
                graphics.DrawString(name, titleFont, ink, new RectangleF(18, 0, Math.Max(1, size.Width - 108), HeaderHeight), format);
                format.Alignment = StringAlignment.Far;
                graphics.DrawString(string.IsNullOrEmpty(hint) ? count.ToString() : hint, smallFont, muted,
                    new RectangleF(Math.Max(18, size.Width - 110), 0, 64, HeaderHeight), format);
            }
            var more = new Rectangle(size.Width - 38, (HeaderHeight - 26) / 2, 26, 26);
            if (hover) using (var path = Rounded(more, 6)) using (var fill = new SolidBrush(Surface)) graphics.FillPath(fill, path);
            using (var ink = new SolidBrush(hover ? Text : Muted))
                for (int i = 0; i < 3; i++) graphics.FillEllipse(ink, more.X + 6 + i * 6, more.Y + 12, 2, 2);
        }

        internal static ContextMenuStrip Menu()
        {
            var menu = new ContextMenuStrip { Font = BodyFont, BackColor = Background, ForeColor = Text,
                ShowImageMargin = false, Padding = new Padding(5), DropShadowEnabled = true, Renderer = new MenuRenderer() };
            StyleItems(menu);
            menu.Opening += (sender, args) =>
            {
                menu.BackColor = Surface; menu.ForeColor = Text;
                using (var path = Rounded(new RectangleF(0, 0, menu.Width, menu.Height), 9))
                { var old = menu.Region; menu.Region = new Region(path); old?.Dispose(); }
            };
            return menu;
        }

        private static void StyleItems(ToolStrip strip)
        {
            strip.Font = BodyFont; strip.BackColor = Background; strip.ForeColor = Text;
            strip.Renderer = new MenuRenderer();
            strip.ItemAdded += (s, e) =>
            {
                e.Item.Padding = new Padding(12, 6, 12, 6);
                var child = e.Item as ToolStripMenuItem;
                if (child != null) StyleItems(child.DropDown);
            };
        }

        private sealed class MenuRenderer : ToolStripProfessionalRenderer
        {
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(Background); }
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using (var pen = new Pen(Border))
                using (var path = Rounded(new RectangleF(.5f, .5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 9)) e.Graphics.DrawPath(pen, path);
            }
            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected || !e.Item.Enabled) return;
                using (var path = Rounded(new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2), 6))
                using (var fill = new SolidBrush(Surface)) e.Graphics.FillPath(fill, path);
            }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = !e.Item.Enabled ? Muted : (string)e.Item.Tag == "danger" ? Skin == "paper" ? Color.FromArgb(177, 52, 66) : Color.FromArgb(240, 151, 160) : Text;
                base.OnRenderItemText(e);
            }
            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                using (var pen = new Pen(Surface)) e.Graphics.DrawLine(pen, 12, e.Item.Height / 2, e.Item.Width - 12, e.Item.Height / 2);
            }
            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Muted; base.OnRenderArrow(e); }
        }

        internal static void StyleDialog(Form form)
        {
            var icon = CreateIcon(); form.Icon = icon;
            form.Disposed += (s, e) => icon.Dispose();
            form.Font = BodyFont; form.BackColor = Background; form.ForeColor = Text;
            form.ShowInTaskbar = false;
            form.HandleCreated += (s, e) => { int enabled = Skin == "paper" ? 0 : 1; DwmSetWindowAttribute(form.Handle, 20, ref enabled, 4); };
        }

        internal static Button Button(string text, bool primary = false)
        {
            var button = new ModernButton { Text = text, Font = BodyFont, FlatStyle = FlatStyle.Flat, Height = 38, Width = 100,
                BackColor = primary ? Accent : Surface, ForeColor = primary ? Background : Text, Cursor = Cursors.Hand, Tag = primary ? "primary" : null };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = primary ? Accent : Surface;
            return button;
        }
    }

    internal sealed class ModernButton : Button
    {
        private bool hover;
        internal ModernButton() { SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Theme.Background : Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var shape = Theme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), 7 * DeviceDpi / 96f))
            using (var fill = new SolidBrush(Enabled && hover ? ControlPaint.Light(BackColor, .08f) : BackColor)) e.Graphics.FillPath(fill, shape);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
                using (var shape = Theme.Rounded(new RectangleF(2, 2, Width - 5, Height - 5), 6 * DeviceDpi / 96f))
                using (var pen = new Pen(ForeColor)) e.Graphics.DrawPath(pen, shape);
        }
    }

    internal sealed class ToggleSwitch : CheckBox
    {
        internal ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            AutoSize = false; Size = new Size(46, 26); Cursor = Cursors.Hand;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var shape = Theme.Rounded(new RectangleF(1, 1, Width - 2, Height - 2), Height / 2f))
            using (var track = new SolidBrush(Checked ? Theme.Accent : Theme.Border)) e.Graphics.FillPath(track, shape);
            float margin = Height * .17f, diameter = Height - margin * 2;
            using (var thumb = new SolidBrush(Checked ? Theme.Background : Theme.Text))
                e.Graphics.FillEllipse(thumb, Checked ? Width - margin - diameter : margin, margin, diameter, diameter);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, Theme.Accent, BackColor);
        }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    }
}
