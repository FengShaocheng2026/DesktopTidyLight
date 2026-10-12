using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Linq;

namespace DesktopTidyLight
{
    internal sealed class SettingsForm : Form
    {
        internal SettingsForm(bool autoStart, Action<bool> setAutoStart, bool autoArrange)
            : this(autoStart, setAutoStart, "mist", false, (skin, hide) => { }) { }

        internal SettingsForm(bool autoStart, Action<bool> setAutoStart, string skin, bool hideTitle, Action<string, bool> save)
            : this(autoStart, setAutoStart, skin, hideTitle, (s, h, t) => save(s, h), 0) { }

        internal SettingsForm(bool autoStart, Action<bool> setAutoStart, string skin, bool hideTitle, Action<string, bool, int> save, int transparency)
        {
            SuspendLayout();
            Text = "设置 · Desktop Tidy"; StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi; Theme.StyleDialog(this); ClientSize = new Size(584, 476);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 14, 24, 18), ColumnCount = 1, RowCount = 6, AutoScroll = false };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);
            root.Controls.Add(new Label { Text = "外观", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 4, 0, 0) }, 0, 0);
            var choices = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 0, 0, 18) };
            var keys = new[] { "mist", "paper", "outline", "plum" }; var names = new[] { "石墨", "瓷白", "线性", "暮色" };
            var cards = new AppearanceCard[4]; string selected = skin; bool hiding = hideTitle, reverting = false;
            Action refresh = () =>
            {
                BackColor = Theme.Background; ForeColor = Theme.Text;
                foreach (var card in cards) if (card != null) card.Chosen = card.StyleKey == selected;
                Recolor(this); Invalidate(true);
            };
            for (int i = 0; i < 4; i++)
            {
                int index = i; choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
                var card = new AppearanceCard(keys[i], names[i]) { Dock = DockStyle.Fill, Margin = new Padding(i == 0 ? 0 : 5, 0, i == 3 ? 0 : 5, 0) };
                cards[i] = card;
                card.Click += (s, e) =>
                {
                    try { save(keys[index], hiding, transparency); selected = keys[index]; Theme.Apply(selected); refresh(); }
                    catch (Exception error) { Dialogs.Message(error.Message, "设置未保存", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                };
                choices.Controls.Add(card, i, 0);
            }
            root.Controls.Add(choices, 0, 1);
            var opacity = new TransparencySlider { Dock = DockStyle.Fill, Margin = Padding.Empty, Value = transparency };
            opacity.ValueChanged += (s, e) =>
            {
                if (reverting) return;
                try { save(selected, hiding, opacity.Value); transparency = opacity.Value; }
                catch (Exception error) { reverting = true; opacity.Value = transparency; reverting = false; Dialogs.Message(error.Message, "设置未保存", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            root.Controls.Add(opacity, 0, 2);
            var titleRow = new PreferenceRow("自动隐藏标题", "悬停顶部时显示", hideTitle);
            titleRow.Toggle.CheckedChanged += (s, e) =>
            {
                if (reverting) return;
                try { save(selected, titleRow.Toggle.Checked, transparency); hiding = titleRow.Toggle.Checked; }
                catch (Exception error) { reverting = true; titleRow.Toggle.Checked = hiding; reverting = false; Dialogs.Message(error.Message, "设置未保存", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            root.Controls.Add(titleRow, 0, 3);
            var startup = new PreferenceRow("登录时启动", "自动恢复桌面分组", autoStart);
            startup.Toggle.CheckedChanged += (s, e) =>
            {
                if (reverting) return;
                try { setAutoStart(startup.Toggle.Checked); }
                catch (Exception error) { reverting = true; startup.Toggle.Checked = !startup.Toggle.Checked; reverting = false; Dialogs.Message(error.Message, "设置未保存", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            root.Controls.Add(startup, 0, 4);
            var footer = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 14, 0, 0) };
            var done = Theme.Button("完成", true); done.Size = new Size(84, 32); done.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            footer.Controls.Add(done); footer.Resize += (s, e) => done.Location = new Point(footer.Width - done.Width, Math.Max(0, footer.Height - done.Height));
            done.Click += (s, e) => Close(); root.Controls.Add(footer, 0, 5); AcceptButton = done; CancelButton = done;
            refresh(); AutoScaleDimensions = new SizeF(96, 96); ResumeLayout(true);
        }

        private static void Recolor(Control control)
        {
            control.BackColor = Theme.Background; control.ForeColor = control is Label ? Theme.Muted : Theme.Text;
            if (control is Button && !(control is AppearanceCard))
            { control.BackColor = (string)control.Tag == "primary" ? Theme.Accent : Theme.Surface; control.ForeColor = (string)control.Tag == "primary" ? Theme.Background : Theme.Text; }
            foreach (Control child in control.Controls) Recolor(child);
            control.Invalidate();
        }
    }

    internal sealed class TransparencySlider : Control
    {
        private int value;
        internal int Value { get { return value; } set { this.value = Math.Max(0, Math.Min(100, value)); Invalidate(); } }
        internal event EventHandler ValueChanged;
        private readonly Timer throttle = new Timer { Interval = 90 };
        private bool dirty;
        internal TransparencySlider()
        {
            DoubleBuffered = true; TabStop = true; Cursor = Cursors.Hand; AccessibleName = "背景透明度"; AccessibleRole = AccessibleRole.Slider;
            throttle.Tick += (s, e) => Flush();
        }
        private void Flush() { throttle.Stop(); if (dirty) { dirty = false; ValueChanged?.Invoke(this, EventArgs.Empty); } }
        private void SetFromMouse(int x)
        {
            float padding = 6 * DeviceDpi / 96f;
            int next = Math.Max(0, Math.Min(100, (int)Math.Round(100d * (x - padding) / Math.Max(1, Width - padding * 2))));
            if (next == Value) return;
            Value = next; dirty = true;
            if (!throttle.Enabled) throttle.Start();
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Focus(); Capture = true; SetFromMouse(e.X); } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture && e.Button == MouseButtons.Left) SetFromMouse(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left) { Capture = false; Flush(); } }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) Flush(); }
        protected override bool IsInputKey(Keys key) { return key == Keys.Left || key == Keys.Right || key == Keys.Home || key == Keys.End || base.IsInputKey(key); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Home || e.KeyCode == Keys.End)
            {
                int next = Math.Max(0, Math.Min(100, e.KeyCode == Keys.Home ? 0 : e.KeyCode == Keys.End ? 100 : Value + (e.KeyCode == Keys.Left ? -5 : 5)));
                if (next != Value) { Value = next; dirty = true; Flush(); }
                e.Handled = true;
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); float scale = DeviceDpi / 96f; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var font = new Font("Segoe UI", 12 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var ink = new SolidBrush(Theme.Text))
            using (var muted = new SolidBrush(Theme.Muted))
            using (var right = new StringFormat { Alignment = StringAlignment.Far })
            {
                e.Graphics.DrawString("背景透明度", font, ink, 0, 5 * scale);
                e.Graphics.DrawString(Value + "%", font, muted, new RectangleF(Width - 70 * scale, 5 * scale, 70 * scale, 24 * scale), right);
            }
            float padding = 6 * scale, y = Height - 15 * scale, x = padding + (Width - padding * 2) * Value / 100f;
            using (var track = new Pen(Theme.Surface, 6 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round }) e.Graphics.DrawLine(track, padding, y, Width - padding, y);
            using (var track = new Pen(Theme.Accent, 6 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round }) e.Graphics.DrawLine(track, padding, y, x, y);
            using (var thumb = new SolidBrush(Theme.Accent)) e.Graphics.FillEllipse(thumb, x - 5 * scale, y - 5 * scale, 10 * scale, 10 * scale);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
        }
        protected override void Dispose(bool disposing) { if (disposing) { Flush(); throttle.Dispose(); } base.Dispose(disposing); }
    }

    internal sealed class PreferenceRow : Control
    {
        internal readonly ToggleSwitch Toggle;
        private readonly string description;
        internal PreferenceRow(string title, string description, bool value)
        {
            Text = title; this.description = description; Dock = DockStyle.Fill; Margin = Padding.Empty; DoubleBuffered = true;
            Toggle = new ToggleSwitch { Checked = value, AccessibleName = title };
            Controls.Add(Toggle);
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e); if (Toggle == null) return;
            float scale = DeviceDpi / 96f; Toggle.Size = new Size((int)(36 * scale), (int)(20 * scale));
            Toggle.Location = new Point(Width - Toggle.Width, (Height - Toggle.Height) / 2);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); float scale = DeviceDpi / 96f;
            using (var font = new Font("Segoe UI", 12 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var small = new Font("Segoe UI", 10 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var ink = new SolidBrush(Theme.Text))
            using (var muted = new SolidBrush(Theme.Muted))
            using (var line = new Pen(Theme.Border))
            {
                e.Graphics.DrawString(Text, font, ink, 0, 14 * scale);
                e.Graphics.DrawString(description, small, muted, 0, 34 * scale);
                e.Graphics.DrawLine(line, 0, Height - 1, Width, Height - 1);
            }
        }
    }

    internal sealed class AppearanceCard : Button
    {
        internal readonly string StyleKey;
        internal bool Chosen;
        internal AppearanceCard(string key, string label)
        { StyleKey = key; Text = label; AccessibleName = label + "外观"; Cursor = Cursors.Hand; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            float scale = DeviceDpi / 96f; var g = e.Graphics; g.Clear(Theme.Background); g.ScaleTransform(scale, scale); g.SmoothingMode = SmoothingMode.AntiAlias;
            float width = Width / scale, height = Height / scale;
            Color surface = StyleKey == "paper" ? Color.FromArgb(239,240,237) : StyleKey == "plum" ? Color.FromArgb(46,39,56) : StyleKey == "outline" ? Color.FromArgb(23,24,26) : Color.FromArgb(36,38,43);
            Color ink = StyleKey == "paper" ? Color.FromArgb(101,105,116) : Color.FromArgb(176,180,192);
            float radius = StyleKey == "outline" ? 3 : StyleKey == "plum" ? 14 : 9;
            using (var shape = Theme.Rounded(new RectangleF(1, 1, width - 2, height - 29), radius))
            using (var fill = new SolidBrush(surface))
            using (var border = new Pen(Chosen ? Theme.Accent : Theme.Border, Chosen ? 1.5f : .75f))
            { g.FillPath(fill, shape); g.DrawPath(border, shape); }
            using (var pen = new Pen(Color.FromArgb(110, ink), 2)) { g.DrawLine(pen, 14, 17, width / 2, 17); }
            if (StyleKey == "outline") using (var line = new Pen(Color.FromArgb(55, ink))) g.DrawLine(line, 12, 27, width - 12, 27);
            for (int i = 0; i < 3; i++)
            {
                float x = 14 + i * (width - 37) / 3;
                using (var shape = Theme.Rounded(new RectangleF(x, 38, 16, 16), StyleKey == "outline" ? 2 : 5))
                using (var fill = new SolidBrush(Color.FromArgb(StyleKey == "paper" ? 95 : 140, ink))) g.FillPath(fill, shape);
                using (var line = new Pen(Color.FromArgb(75, ink))) g.DrawLine(line, x, 61, x + 16, 61);
            }
            using (var font = new Font("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var inkBrush = new SolidBrush(Chosen ? Theme.Text : Theme.Muted))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(Text, font, inkBrush, new RectangleF(0, height - 26, width, 26), format);
            if (Chosen)
            {
                using (var fill = new SolidBrush(Theme.Accent)) g.FillEllipse(fill, width - 21, 9, 12, 12);
                using (var pen = new Pen(Theme.Background, 1.5f)) g.DrawLines(pen, new[] { new PointF(width - 18, 15), new PointF(width - 15, 18), new PointF(width - 11, 12) });
            }
            if (Focused && ShowFocusCues) using (var line = new Pen(Theme.Accent)) g.DrawLine(line, width / 2 - 10, height - 1, width / 2 + 10, height - 1);
        }
    }
}
