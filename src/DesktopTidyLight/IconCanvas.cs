using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopTidyLight
{
    // This control owns pixels and selection, never filesystem contents. Only the controller commits membership.
    internal sealed class IconCanvas : Control
    {
        internal const string InternalFormat = "DesktopTidyLight.Members";
        private string[] ids = new string[0];
        private Dictionary<string, Point> desktopPositions;
        private Size desktopSpacing;
        private int desktopIconSize;
        private HashSet<string> selectionBeforeMarquee;
        private Rectangle? marquee;
        internal bool IsDesktopSurface { get { return desktopPositions != null; } }
        private int ImageSize { get { return IsDesktopSurface ? desktopIconSize : (int)(48 * ScaleFactor); } }
        private readonly Dictionary<string, ShellVisual> visuals = new Dictionary<string, ShellVisual>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Bitmap> cells = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private readonly ShellVisualQueue loading;
        private readonly HashSet<string> ready = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Timer loadTimer = new Timer { Interval = 33 };
        internal bool IsLoadingVisuals { get { return loading.HasWork; } }
        private bool initialRefresh;
        private int cachedDpi;
        private float resolutionScale = 1;
        private int cachedCellWidth;
        private readonly Timer refreshTimer = new Timer { Interval = 2000 };
        private HashSet<string> selection = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal Func<string[]> DesktopIds;
        internal event Action SelectionChanged;
        internal void ShareDesktopSelection(HashSet<string> selected) { selection = selected; }
        private IEnumerable<string> SelectedItems { get { return (IsDesktopSurface ? DesktopIds?.Invoke() ?? ids : ids).Where(selection.Contains); } }
        private readonly ToolTip tooltip = new ToolTip();
        internal static bool DragInProgress { get; private set; }
        private Point? desktopRelease;
        private DragBadge dragBadge;
        private Point down;
        private int scrollY, contentHeight, thumbDown, scrollBeforeThumb;
        private bool scrolling, scrollHovered;
        private int MaxScroll { get { return Math.Max(0, contentHeight - ClientSize.Height); } }
        internal event Action<string[], Point> DesktopDropRequested;
        private bool pressed, dropping;
        private int anchor, focusIndex;
        private string hovered;
        internal event Action<string[]> Dropped;
        internal event Action<string[]> RemoveRequested;
        internal event Action<string[]> ReorderRequested;
        internal event Action BackgroundMenuRequested;
        internal event Action<string[], Point> DesktopMenuRequested;
        internal event Action RefreshRequested;
        private float ScaleFactor { get { return DeviceDpi / 96f * resolutionScale; } }
        internal void SetResolutionScale(float scale)
        {
            if (Math.Abs(resolutionScale - scale) < 0.001f) return;
            resolutionScale = scale;
            RefreshVisuals(); Arrange();
        }
        private int Pad { get { return (int)(16 * ScaleFactor); } }
        private int CellW { get { return IsDesktopSurface ? desktopSpacing.Width : Math.Max(1, Math.Min((int)(100 * ScaleFactor), ClientSize.Width - 2 * Pad - (int)(10 * ScaleFactor))); } }
        private int CellH { get { return IsDesktopSurface ? desktopSpacing.Height : (int)(104 * ScaleFactor); } }
        private int Columns { get { return Math.Max(1, (ClientSize.Width - 2 * Pad - (int)(10 * ScaleFactor)) / CellW); } }

        internal IconCanvas(Func<string, int, ShellVisual> loader = null)
        {
            loading = new ShellVisualQueue(loader);
            DoubleBuffered = true; AllowDrop = true; TabStop = true;
            Cursor = Cursors.Default;
            SetStyle(ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background; ForeColor = Theme.Text;
            AccessibleName = "分组图标"; AccessibleRole = AccessibleRole.List;
            refreshTimer.Tick += (s, e) => { if (loading.HasWork || MouseButtons != MouseButtons.None || DragInProgress) return; RetryVisuals(); };
            loadTimer.Tick += (s, e) =>
            {
                bool changed = false;
                foreach (var result in loading.TakeCompleted())
                {
                    ShellVisual old;
                    if (!visuals.TryGetValue(result.Key, out old)) { result.Value.Dispose(); continue; }
                    // A temporary failure must not replace a previously valid icon with a generic one.
                    if (result.Value.IsFallback && !old.IsFallback) { result.Value.Dispose(); ready.Add(result.Key); continue; }
                    old.Dispose(); visuals[result.Key] = result.Value; ready.Add(result.Key);
                    if (result.Value.IsFallback && !refreshTimer.Enabled) { refreshTimer.Interval = 10000; refreshTimer.Start(); }
                    if (hovered == result.Key) tooltip.SetToolTip(this, result.Value.Name);
                    foreach (string key in new[] { "0:" + result.Key, "1:" + result.Key })
                    { Bitmap cell; if (cells.TryGetValue(key, out cell)) { cell.Dispose(); cells.Remove(key); } }
                    changed = true;
                }
                if (!loading.HasWork) loadTimer.Stop();
                if (changed) Invalidate();
            };
        }

        internal void SetMembers(IEnumerable<MemberData> members)
        {
            var next = members.Select(m => m.Id).ToArray();
            if (ids.SequenceEqual(next, StringComparer.OrdinalIgnoreCase)) return;
            if (IsDesktopSurface) selection.ExceptWith(ids.Except(next, StringComparer.OrdinalIgnoreCase));
            else selection.IntersectWith(next);
            ids = next;
            ClearCells();
            foreach (string stale in visuals.Keys.Where(k => !ids.Contains(k, StringComparer.OrdinalIgnoreCase)).ToArray())
            { visuals[stale].Dispose(); visuals.Remove(stale); ready.Remove(stale); }
            // Shell can briefly return a generic icon while a shortcut/application is updating.
            initialRefresh = true; refreshTimer.Stop(); refreshTimer.Interval = 2000; refreshTimer.Start();
            focusIndex = Math.Min(focusIndex, Math.Max(0, ids.Length - 1));
            anchor = Math.Min(anchor, Math.Max(0, ids.Length - 1));
            Arrange(); Invalidate();
        }

        internal void SetDesktopItems(IEnumerable<DesktopItem> items, Point offset, Size spacing, int iconSize)
        {
            var list = items.ToArray();
            var positions = list.ToDictionary(i => i.Id,
                i => new Point(i.Position.X + offset.X - GridLayout.LabelInset(spacing, iconSize), i.Position.Y + offset.Y),
                StringComparer.OrdinalIgnoreCase);
            bool geometryChanged = desktopPositions == null || desktopSpacing != spacing || desktopIconSize != iconSize ||
                positions.Count != desktopPositions.Count || positions.Any(p => !desktopPositions.ContainsKey(p.Key) || desktopPositions[p.Key] != p.Value);
            if (desktopSpacing != spacing || desktopIconSize != iconSize) ClearCells();
            desktopSpacing = spacing; desktopIconSize = iconSize; desktopPositions = positions;
            SetMembers(list.Select(i => new MemberData { Id = i.Id }));
            if (geometryChanged) { Arrange(); Invalidate(); }
        }

        internal Region ItemRegion()
        {
            var region = new Region(); region.MakeEmpty();
            for (int i = 0; i < ids.Length; i++) region.Union(Cell(i));
            if (marquee.HasValue && marquee.Value.Width > 0 && marquee.Value.Height > 0)
                using (var outline = new Region(marquee.Value))
                { var inner = Rectangle.Inflate(marquee.Value, -2, -2); if (inner.Width > 0 && inner.Height > 0) outline.Exclude(inner); region.Union(outline); }
            return region;
        }

        internal void BeginDesktopSelection(bool extend)
        { selectionBeforeMarquee = extend ? new HashSet<string>(selection, StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase); }

        internal void SetDesktopSelection(Rectangle? area, bool active)
        {
            marquee = active ? area : null;
            if (area.HasValue)
            {
                selection.ExceptWith(ids);
                if (selectionBeforeMarquee != null) selection.UnionWith(selectionBeforeMarquee.Where(id => ids.Contains(id, StringComparer.OrdinalIgnoreCase)));
                for (int i = 0; i < ids.Length; i++) if (area.Value.IntersectsWith(Cell(i))) selection.Add(ids[i]);
            }
            Invalidate();
        }

        internal void RetryVisuals()
        {
            if (initialRefresh)
            {
                initialRefresh = false; loading.Reset(); ready.Clear(); refreshTimer.Interval = 10000; Invalidate(); return;
            }
            bool retry = false;
            foreach (var item in visuals.Where(v => v.Value.IsFallback)) { ready.Remove(item.Key); retry = true; }
            if (retry) { refreshTimer.Interval = 10000; Invalidate(); }
            else refreshTimer.Stop();
        }
        private void ClearCells() { foreach (var cell in cells.Values) cell.Dispose(); cells.Clear(); }
        internal void RefreshVisuals()
        {
            loading.Reset(); ready.Clear(); ClearCells(); foreach (var visual in visuals.Values) visual.Dispose(); visuals.Clear(); Invalidate();
        }
        private ShellVisual Visual(string id)
        {
            ShellVisual visual;
            if (!visuals.TryGetValue(id, out visual)) visuals.Add(id, visual = ShellVisual.Placeholder(id));
            if (!ready.Contains(id)) { loading.Request(id, ImageSize); loadTimer.Start(); }
            return visual;
        }
        internal void RefreshTheme(bool transparent = false) { ClearCells(); BackColor = transparent ? Theme.TransparencyColor : Theme.Background; ForeColor = Theme.Text; Invalidate(); }
        private Bitmap CellImage(string id, bool selected)
        {
            string key = (selected ? "1:" : "0:") + id;
            Bitmap result; if (cells.TryGetValue(key, out result)) return result;
            string other = (selected ? "0:" : "1:") + id;
            if (cells.TryGetValue(other, out result)) { result.Dispose(); cells.Remove(other); }
            result = new Bitmap(CellW, CellH, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(result))
            using (var font = new Font("Segoe UI", 12 * ScaleFactor, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.Clear(BackColor);
                if (selected)
                    using (var fill = new SolidBrush(Theme.Surface))
                    using (var path = Theme.Rounded(Rectangle.Inflate(new Rectangle(Point.Empty, result.Size), -(int)(4 * ScaleFactor), -(int)(4 * ScaleFactor)), 9 * ScaleFactor)) g.FillPath(fill, path);
                var visual = Visual(id); int size = ImageSize;
                int top = IsDesktopSurface ? 0 : (int)(8 * ScaleFactor);
                g.DrawImage(visual.Image, new Rectangle((CellW - size) / 2, top, size, size));
                int labelTop = IsDesktopSurface ? size + (int)(4 * ScaleFactor) : (int)(62 * ScaleFactor);
                var label = new Rectangle((int)(6 * ScaleFactor), labelTop, CellW - (int)(12 * ScaleFactor), Math.Max(1, CellH - labelTop));
                using (var ink = new SolidBrush(IsDesktopSurface ? Color.White : Theme.Text))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.LineLimit })
                {
                    if (IsDesktopSurface)
                        using (var shadow = new SolidBrush(Color.Black))
                        { var shadowLabel = label; shadowLabel.Offset(1, 1); g.DrawString(visual.Name, font, shadow, shadowLabel, format); }
                    g.DrawString(visual.Name, font, ink, label, format);
                }
            }
            cells.Add(key, result); return result;
        }
        private void Arrange()
        {
            contentHeight = IsDesktopSurface ? 0 : Pad * 2 + ((ids.Length + Columns - 1) / Columns) * CellH;
            scrollY = Math.Min(scrollY, MaxScroll); Invalidate();
        }
        private Rectangle Thumb
        {
            get
            {
                int track = Math.Max(1, Height - Pad * 2);
                int height = Math.Min(track, Math.Max((int)(28 * ScaleFactor), (int)((long)track * Height / Math.Max(1, contentHeight))));
                return new Rectangle(Width - (int)(9 * ScaleFactor), Pad + (int)((long)(track - height) * scrollY / Math.Max(1, MaxScroll)), Math.Max(4, (int)(4 * ScaleFactor)), height);
            }
        }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); scrollY = Math.Max(0, Math.Min(MaxScroll, scrollY - e.Delta * CellH / 240)); Invalidate(); }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Arrange(); }
        private Rectangle Cell(int index)
        {
            if (IsDesktopSurface) return new Rectangle(desktopPositions[ids[index]], desktopSpacing);
            return new Rectangle(Pad + index % Columns * CellW, Pad + index / Columns * CellH - scrollY, CellW, CellH);
        }
        private int Hit(Point point)
        {
            if (IsDesktopSurface)
            {
                for (int i = ids.Length - 1; i >= 0; i--) if (Cell(i).Contains(point)) return i;
                return -1;
            }
            int x = point.X - Pad, y = point.Y + scrollY - Pad;
            if (x < 0 || y < 0 || x >= Columns * CellW) return -1;
            int index = y / CellH * Columns + x / CellW;
            return index < ids.Length ? index : -1;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (cachedCellWidth != CellW) { cachedCellWidth = CellW; ClearCells(); }
            if (cachedDpi != DeviceDpi)
            {
                cachedDpi = DeviceDpi; RefreshVisuals();
                Arrange();
            }
            int first = Math.Min(ids.Length, Math.Max(0, (scrollY - Pad) / CellH) * Columns);
            int end = Math.Min(ids.Length, Math.Max(0, (scrollY + Height - Pad) / CellH + 1) * Columns);
            if (IsDesktopSurface) { first = 0; end = ids.Length; }
            var visible = new HashSet<string>(ids.Skip(first).Take(end - first), StringComparer.OrdinalIgnoreCase);
            loading.Retain(visible);
            int cacheStart = Math.Max(0, first - (end - first));
            var nearby = new HashSet<string>(ids.Skip(cacheStart).Take(end + (end - first) - cacheStart), StringComparer.OrdinalIgnoreCase);
            foreach (string key in cells.Keys.Where(k => !visible.Contains(k.Substring(2))).ToArray()) { cells[key].Dispose(); cells.Remove(key); }
            foreach (string id in visuals.Keys.Where(id => !nearby.Contains(id)).ToArray()) { visuals[id].Dispose(); visuals.Remove(id); ready.Remove(id); }
            for (int i = first; i < end; i++)
            {
                var cell = Cell(i); if (!cell.IntersectsWith(ClientRectangle)) continue;
                Visual(ids[i]);
                var inner = Rectangle.Inflate(cell, -(int)(4 * ScaleFactor), -(int)(4 * ScaleFactor));
                e.Graphics.DrawImageUnscaled(CellImage(ids[i], selection.Contains(ids[i])), cell.Location);
                if (Focused && ShowFocusCues && i == focusIndex)
                    using (var path = Theme.Rounded(inner, 7 * ScaleFactor))
                    using (var pen = new Pen(Theme.Accent, ScaleFactor)) e.Graphics.DrawPath(pen, path);
            }
            if (ids.Length == 0 && !IsDesktopSurface)
                TextRenderer.DrawText(e.Graphics, "拖入桌面图标", Theme.BodyFont, ClientRectangle, Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            if (MaxScroll > 0)
                using (var path = Theme.Rounded(Thumb, 3 * ScaleFactor))
                using (var brush = new SolidBrush(Color.FromArgb(scrolling || scrollHovered ? 240 : 130, Theme.Accent))) e.Graphics.FillPath(brush, path);
            if (dropping) using (var pen = new Pen(Theme.Accent, 3)) e.Graphics.DrawRectangle(pen, 2, 2, Width - 5, Height - 5);
            if (marquee.HasValue && marquee.Value.Width > 0 && marquee.Value.Height > 0)
                using (var pen = new Pen(Theme.Accent, 2)) e.Graphics.DrawRectangle(pen, marquee.Value.X, marquee.Value.Y, marquee.Value.Width - 1, marquee.Value.Height - 1);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus();
            if (e.Button == MouseButtons.Left && MaxScroll > 0 && e.X >= Width - (int)(14 * ScaleFactor))
            {
                if (e.Y >= Thumb.Top && e.Y < Thumb.Bottom) { scrolling = true; thumbDown = e.Y; scrollBeforeThumb = scrollY; Capture = true; }
                else { scrollY = Math.Max(0, Math.Min(MaxScroll, scrollY + (e.Y < Thumb.Top ? -Height : Height))); Invalidate(); }
                return;
            }
            int index = Hit(e.Location);
            if (index < 0) { selection.Clear(); SelectionChanged?.Invoke(); Invalidate(); if (e.Button == MouseButtons.Right) BackgroundMenuRequested?.Invoke(); return; }
            focusIndex = index;
            if ((ModifierKeys & Keys.Shift) != 0)
            {
                selection.Clear(); for (int i = Math.Min(anchor, index); i <= Math.Max(anchor, index); i++) selection.Add(ids[i]);
            }
            else if ((ModifierKeys & Keys.Control) != 0)
            { if (!selection.Add(ids[index])) selection.Remove(ids[index]); anchor = index; }
            else if (!selection.Contains(ids[index])) { selection.Clear(); selection.Add(ids[index]); anchor = index; }
            Invalidate();
            SelectionChanged?.Invoke();
            if (e.Button == MouseButtons.Right) { ShowItemMenu(e.Location); return; }
            down = e.Location; pressed = e.Button == MouseButtons.Left;
        }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; scrolling = false; Capture = false; base.OnMouseUp(e); Invalidate(); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool overScroll = MaxScroll > 0 && e.X >= Width - (int)(14 * ScaleFactor);
            if (scrollHovered != overScroll) { scrollHovered = overScroll; Invalidate(new Rectangle(Math.Max(0, Width - (int)(14 * ScaleFactor)), 0, (int)(14 * ScaleFactor), Height)); }
            if (scrolling)
            {
                scrollY = Math.Max(0, Math.Min(MaxScroll, scrollBeforeThumb + (int)((long)(e.Y - thumbDown) * MaxScroll / Math.Max(1, Height - Pad * 2 - Thumb.Height))));
                Invalidate(); return;
            }
            int index = Hit(e.Location); string id = index < 0 ? null : ids[index];
            if (hovered != id) { hovered = id; tooltip.SetToolTip(this, id == null ? null : Visual(id).Name); }
            if (!pressed || e.Button != MouseButtons.Left || selection.Count == 0) return;
            if (Math.Abs(e.X - down.X) < SystemInformation.DragSize.Width && Math.Abs(e.Y - down.Y) < SystemInformation.DragSize.Height) return;
            pressed = false;
            var batch = SelectedItems.ToArray();
            // Group transfer uses OLE. A release over the desktop restores positions ourselves;
            // no FileDrop payload is handed to Explorer, so there is no filesystem move/copy.
            var data = new DataObject(); data.SetData(InternalFormat, false, batch);
            desktopRelease = null; DragInProgress = true;
            try { dragBadge = new DragBadge(batch.Length, Visual(batch[0]).Image); DoDragDrop(data, DragDropEffects.Move); }
            finally { dragBadge?.Dispose(); dragBadge = null; DragInProgress = false; pressed = false; }
            if (desktopRelease.HasValue) DesktopDropRequested?.Invoke(batch, desktopRelease.Value);
            desktopRelease = null;
        }
        protected override void OnMouseLeave(EventArgs e)
        { base.OnMouseLeave(e); if (scrollHovered) { scrollHovered = false; Invalidate(); } }
        protected override void OnQueryContinueDrag(QueryContinueDragEventArgs e)
        {
            base.OnQueryContinueDrag(e);
            if (e.EscapePressed || (e.KeyState & 2) != 0) { desktopRelease = null; e.Action = DragAction.Cancel; return; }
            if (DesktopDrop.ShouldReturn(e.EscapePressed, e.KeyState, DesktopDrop.IsDesktop(Cursor.Position)))
            { desktopRelease = Cursor.Position; e.Action = DragAction.Cancel; }
        }
        protected override void OnGiveFeedback(GiveFeedbackEventArgs e)
        {
            base.OnGiveFeedback(e);
            bool desktop = DesktopDrop.IsDesktop(Cursor.Position);
            dragBadge?.Follow(Cursor.Position, desktop, desktop || e.Effect != DragDropEffects.None);
            if (desktop) { e.UseDefaultCursors = false; Cursor.Current = Cursors.Arrow; }
            else e.UseDefaultCursors = true;
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { base.OnMouseDoubleClick(e); int i = Hit(e.Location); if (i >= 0) Open(ids[i]); }
        private void Open(string id)
        {
            try { Process.Start(new ProcessStartInfo(id.StartsWith("::", StringComparison.Ordinal) ? "shell:" + id : id) { UseShellExecute = true }); }
            catch (Exception error) { Dialogs.Message(error.Message, "无法打开项目", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }
        private void ShowItemMenu(Point at)
        {
            if (IsDesktopSurface)
            { DesktopMenuRequested?.Invoke(SelectedItems.ToArray(), PointToScreen(at)); return; }
            var menu = Theme.Menu();
            menu.Items.Add("打开", null, (s, e) => { foreach (var id in ids.Where(selection.Contains)) Open(id); });
            menu.Items.Add("在资源管理器中查看", null, (s, e) =>
            {
                string id = ids.FirstOrDefault(selection.Contains);
                if (id == null || id.StartsWith("::", StringComparison.Ordinal)) return;
                try { Process.Start("explorer.exe", "/select,\"" + id + "\""); }
                catch (Exception error) { Dialogs.Message(error.Message, "无法打开位置", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            });
            if (!IsDesktopSurface)
            {
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("移回普通桌面", null, (s, e) => RemoveRequested?.Invoke(ids.Where(selection.Contains).ToArray()));
            }
            menu.Closed += (s, e) => BeginInvoke(new Action(menu.Dispose)); menu.Show(this, at);
        }
        protected override bool IsInputKey(Keys keyData) { return (keyData & Keys.KeyCode) >= Keys.Left && (keyData & Keys.KeyCode) <= Keys.Down || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Control && e.KeyCode == Keys.A) { selection.UnionWith(IsDesktopSurface ? DesktopIds?.Invoke() ?? ids : ids); e.Handled = true; }
            else if (e.KeyCode == Keys.F5) { RefreshVisuals(); RefreshRequested?.Invoke(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { foreach (string id in SelectedItems) Open(id); e.Handled = true; }
            else if (e.KeyCode == Keys.Apps || e.Shift && e.KeyCode == Keys.F10) { ShowItemMenu(new Point(Pad, Pad)); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape) { selection.Clear(); e.Handled = true; }
            else if (ids.Length > 0 && e.KeyCode >= Keys.Left && e.KeyCode <= Keys.Down)
            {
                int delta = e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : e.KeyCode == Keys.Up ? -Columns : Columns;
                focusIndex = Math.Max(0, Math.Min(ids.Length - 1, focusIndex + delta));
                if (!e.Shift) selection.Clear(); selection.Add(ids[focusIndex]);
                var r = Cell(focusIndex); int scroll = scrollY;
                if (r.Top < 0) scrollY = Math.Max(0, scroll + r.Top - Pad);
                else if (r.Bottom > Height) scrollY = Math.Min(MaxScroll, scroll + r.Bottom - Height + Pad);
                e.Handled = true;
            }
            SelectionChanged?.Invoke(); Invalidate();
        }
        internal void AcceptLeave() { if (dropping) { dropping = false; Invalidate(); } }
        internal void AcceptDrag(DragEventArgs e) { Feedback(e); }
        internal void AcceptDrop(DragEventArgs e) { OnDragDrop(e); }
        private void Feedback(DragEventArgs e)
        {
            bool internalDrop = e.Data.GetDataPresent(InternalFormat);
            bool known = internalDrop || e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent("Shell IDList Array");
            e.Effect = !known ? DragDropEffects.None : internalDrop ? DragDropEffects.Move :
                (e.AllowedEffect & DragDropEffects.Link) != 0 ? DragDropEffects.Link : (e.AllowedEffect & DragDropEffects.Copy);
            bool next = e.Effect != DragDropEffects.None;
            if (dropping != next) { dropping = next; Invalidate(); }
        }
        protected override void OnDragEnter(DragEventArgs e) { base.OnDragEnter(e); Feedback(e); }
        protected override void OnDragOver(DragEventArgs e) { base.OnDragOver(e); Feedback(e); }
        protected override void OnDragLeave(EventArgs e) { base.OnDragLeave(e); AcceptLeave(); }
        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e); dropping = false; Invalidate();
            try
            {
                string[] paths = DropItems.Read(e.Data);
                if (paths.Length > 0 && IsDesktopSurface) DesktopDropRequested?.Invoke(paths, new Point(e.X, e.Y));
                else if (paths.Length > 0 && paths.All(p => ids.Contains(p, StringComparer.OrdinalIgnoreCase)))
                {
                    var reordered = ids.Except(paths, StringComparer.OrdinalIgnoreCase).ToList();
                    int index = Hit(PointToClient(new Point(e.X, e.Y)));
                    int insertion = index < 0 ? reordered.Count : ids.Take(index).Count(p => !paths.Contains(p, StringComparer.OrdinalIgnoreCase));
                    reordered.InsertRange(insertion, paths); ReorderRequested?.Invoke(reordered.ToArray());
                }
                else if (paths.Length > 0) Dropped?.Invoke(paths);
            }
            catch (Exception error) { Dialogs.Message(error.Message, "未加入分组", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { loading.Dispose(); loadTimer.Dispose(); refreshTimer.Dispose(); tooltip.Dispose(); ClearCells(); foreach (var v in visuals.Values) v.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class DragBadge : Form
    {
        private readonly int count;
        private readonly Bitmap image;
        private string label;
        internal DragBadge(int count, Bitmap image)
        {
            this.count = count; this.image = (Bitmap)image.Clone();
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; DoubleBuffered = true; TopMost = true; BackColor = Theme.Surface;
            Enabled = false; // WindowFromPoint skips disabled windows, including this moving drag preview.
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x80 | 0x20 | 0x08000000; return p; } }
        internal void Follow(Point point, bool desktop, bool accepted)
        {
            float scale = DeviceDpi / 96f; Size = new Size((int)(164 * scale), (int)(42 * scale));
            Location = new Point(point.X + (int)(20 * scale), point.Y + (int)(20 * scale));
            string next = accepted ? (desktop ? "移回桌面" : "移动图标") + " · " + count : "取消移动";
            if (label != next) { label = next; Invalidate(); }
            if (!Visible) Show();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            float scale = DeviceDpi / 96f;
            e.Graphics.DrawImage(image, new Rectangle((int)(9 * scale), (int)(9 * scale), (int)(24 * scale), (int)(24 * scale)));
            using (var font = new Font("Segoe UI", 11 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var ink = new SolidBrush(Theme.Text))
            using (var format = new StringFormat { LineAlignment = StringAlignment.Center })
                e.Graphics.DrawString(label, font, ink, new RectangleF(42 * scale, 0, Width - 42 * scale, Height), format);
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width <= 0 || Height <= 0) return;
            using (var path = Theme.Rounded(new RectangleF(0, 0, Width, Height), 8 * DeviceDpi / 96f))
            { var old = Region; Region = new Region(path); old?.Dispose(); }
        }
        protected override void Dispose(bool disposing) { if (disposing) image?.Dispose(); base.Dispose(disposing); }
    }

    // One bounded STA queue per active canvas. It owns all worker results until the UI takes them.
    // A slow Shell extension never blocks painting or disposal; idle workers exit automatically.
    internal sealed class ShellVisualQueue : IDisposable
    {
        private readonly object gate = new object();
        private readonly Queue<Tuple<string, int, int>> requests = new Queue<Tuple<string, int, int>>();
        private readonly Queue<KeyValuePair<string, ShellVisual>> completed = new Queue<KeyValuePair<string, ShellVisual>>();
        private readonly Dictionary<string, Tuple<string, int, int>> pending = new Dictionary<string, Tuple<string, int, int>>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<string, int, ShellVisual> loader;
        private int generation;
        private bool running, disposed;
        internal ShellVisualQueue(Func<string, int, ShellVisual> loader = null) { this.loader = loader ?? ShellVisual.Load; }
        internal bool HasWork { get { lock (gate) return pending.Count > 0; } }
        internal void Request(string id, int size)
        {
            lock (gate)
            {
                if (disposed || pending.Count >= 128 || pending.ContainsKey(id)) return;
                var request = Tuple.Create(id, size, generation); pending.Add(id, request);
                requests.Enqueue(request);
                if (running) return;
                running = true;
                var thread = new System.Threading.Thread(Work) { IsBackground = true, Name = "Desktop Tidy icon loader" };
                thread.SetApartmentState(System.Threading.ApartmentState.STA); thread.Start();
            }
        }
        private void Work()
        {
            while (true)
            {
                Tuple<string, int, int> request;
                lock (gate)
                {
                    if (disposed || requests.Count == 0) { running = false; return; }
                    request = requests.Dequeue();
                    Tuple<string, int, int> active;
                    if (!pending.TryGetValue(request.Item1, out active) || !ReferenceEquals(request, active)) continue;
                }
                ShellVisual result;
                try { result = loader(request.Item1, request.Item2); }
                catch (Exception) { result = ShellVisual.Placeholder(request.Item1); }
                lock (gate)
                {
                    Tuple<string, int, int> active;
                    if (disposed || request.Item3 != generation || !pending.TryGetValue(request.Item1, out active) || !ReferenceEquals(request, active)) result.Dispose();
                    else completed.Enqueue(new KeyValuePair<string, ShellVisual>(request.Item1, result));
                }
            }
        }
        internal KeyValuePair<string, ShellVisual>[] TakeCompleted()
        {
            lock (gate)
            {
                var result = completed.ToArray(); completed.Clear();
                foreach (var item in result) pending.Remove(item.Key);
                return result;
            }
        }
        internal void Retain(HashSet<string> visible)
        {
            lock (gate)
            {
                if (pending.Count == 0) return;
                foreach (var id in pending.Keys.Where(id => !visible.Contains(id)).ToArray()) pending.Remove(id);
                var queued = requests.Where(r => pending.ContainsKey(r.Item1)).ToArray(); requests.Clear();
                foreach (var request in queued) requests.Enqueue(request);
                var finished = completed.ToArray(); completed.Clear();
                foreach (var result in finished)
                    if (visible.Contains(result.Key)) completed.Enqueue(result); else result.Value.Dispose();
            }
        }
        internal void Reset()
        {
            lock (gate)
            {
                generation++; requests.Clear(); pending.Clear();
                foreach (var item in completed) item.Value.Dispose(); completed.Clear();
            }
        }
        public void Dispose() { lock (gate) { disposed = true; Reset(); } }
    }

    internal sealed class ShellVisual : IDisposable
    {
        internal string Name; internal Bitmap Image; internal bool IsFallback;
        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IImageFactory { [PreserveSig] int GetImage(Size size, int flags, out IntPtr bitmap); }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string path, IntPtr bind, ref Guid iid, out IImageFactory item);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr bitmap);
        [StructLayout(LayoutKind.Sequential)] private struct NativeBitmap { internal int Type, Width, Height, WidthBytes; internal ushort Planes, BitsPixel; internal IntPtr Bits; }
        [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { internal uint Size; internal int Width, Height; internal ushort Planes, Bits; internal uint Compression, ImageSize; internal int X, Y; internal uint Used, Important; }
        [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr bitmap, int size, out NativeBitmap info);
        [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint count, IntPtr pixels, ref BitmapInfo info, uint usage);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        private static Bitmap CopyAlphaBitmap(IntPtr handle)
        {
            NativeBitmap native; if (GetObject(handle, Marshal.SizeOf(typeof(NativeBitmap)), out native) == 0) throw new InvalidOperationException("无法读取图标。");
            var result = new Bitmap(native.Width, native.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var info = new BitmapInfo { Size = 40, Width = native.Width, Height = -native.Height, Planes = 1, Bits = 32 };
            var pixels = result.LockBits(new Rectangle(Point.Empty, result.Size), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            IntPtr dc = GetDC(IntPtr.Zero); bool success;
            try { success = GetDIBits(dc, handle, 0, (uint)native.Height, pixels.Scan0, ref info, 0) != 0; }
            finally { ReleaseDC(IntPtr.Zero, dc); result.UnlockBits(pixels); }
            if (!success) { result.Dispose(); throw new InvalidOperationException("无法读取图标像素。"); }
            return result;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, IntPtr context, out IntPtr pidl, uint attributes, out uint result);
        [DllImport("shell32.dll")] private static extern int SHGetNameFromIDList(IntPtr pidl, uint type, out IntPtr name);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHDefExtractIcon(string path, int index, uint flags, out IntPtr large, out IntPtr small, uint size);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        private static Bitmap ShortcutIcon(string id, int size)
        {
            if (!string.Equals(Path.GetExtension(id), ".lnk", StringComparison.OrdinalIgnoreCase) || !File.Exists(id)) return null;
            object shell = null, shortcut = null; IntPtr large = IntPtr.Zero, small = IntPtr.Zero;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                shortcut = ((dynamic)shell).CreateShortcut(id); // Read only: never call Save or Resolve.
                string location = (string)((dynamic)shortcut).IconLocation;
                int comma = location.LastIndexOf(','), index = 0;
                if (comma >= 0) { int.TryParse(location.Substring(comma + 1), out index); location = location.Substring(0, comma); }
                if (string.IsNullOrWhiteSpace(location)) location = (string)((dynamic)shortcut).TargetPath;
                location = Environment.ExpandEnvironmentVariables(location.Trim().Trim('"'));
                if (!File.Exists(location)) return null;
                if (SHDefExtractIcon(location, index, 0, out large, out small, (uint)size) == 0 && large != IntPtr.Zero)
                    using (var icon = Icon.FromHandle(large)) return icon.ToBitmap();
            }
            catch (COMException) { }
            finally
            {
                if (large != IntPtr.Zero) DestroyIcon(large); if (small != IntPtr.Zero) DestroyIcon(small);
                if (shortcut != null) Marshal.ReleaseComObject(shortcut); if (shell != null) Marshal.ReleaseComObject(shell);
            }
            return null;
        }
        private static string DisplayName(string id)
        {
            IntPtr pidl = IntPtr.Zero, name = IntPtr.Zero; uint attributes;
            try
            {
                if (SHParseDisplayName(id, IntPtr.Zero, out pidl, 0, out attributes) >= 0 &&
                    SHGetNameFromIDList(pidl, 0, out name) >= 0)
                {
                    string display = Marshal.PtrToStringUni(name);
                    if (!string.IsNullOrWhiteSpace(display)) return display;
                }
            }
            finally { if (name != IntPtr.Zero) Marshal.FreeCoTaskMem(name); if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
            if (id.StartsWith("::", StringComparison.Ordinal)) return "桌面项目";
            return string.Equals(Path.GetExtension(id), ".lnk", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(id) : Path.GetFileName(id);
        }
        internal static ShellVisual Placeholder(string id)
        {
            string name = id.StartsWith("::", StringComparison.Ordinal) ? "桌面项目" : Path.GetFileName(id);
            if (string.Equals(Path.GetExtension(name), ".lnk", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(name);
            return new ShellVisual { Name = name, Image = SystemIcons.Application.ToBitmap(), IsFallback = true };
        }
        internal static ShellVisual Load(string id, int size)
        {
            var result = new ShellVisual { Name = DisplayName(id), Image = ShortcutIcon(id, size) };
            if (result.Image != null) return result;
            IImageFactory factory = null; IntPtr handle = IntPtr.Zero;
            try
            {
                var iid = typeof(IImageFactory).GUID;
                if (SHCreateItemFromParsingName(id, IntPtr.Zero, ref iid, out factory) >= 0 && factory.GetImage(new Size(size, size), 0x4 | 0x1, out handle) >= 0)
                    result.Image = CopyAlphaBitmap(handle);
            }
            catch (COMException) { }
            finally { if (handle != IntPtr.Zero) DeleteObject(handle); if (factory != null) Marshal.ReleaseComObject(factory); }
            if (result.Image == null) { result.Image = SystemIcons.Application.ToBitmap(); result.IsFallback = true; }
            return result;
        }
        public void Dispose() { Image.Dispose(); }
    }

    internal static class DropItems
    {
        [DllImport("shell32.dll")] private static extern IntPtr ILCombine(IntPtr parent, IntPtr child);
        [DllImport("shell32.dll")] private static extern int SHGetNameFromIDList(IntPtr pidl, uint type, out IntPtr name);
        internal static string[] Read(IDataObject data)
        {
            if (data.GetDataPresent(IconCanvas.InternalFormat)) return data.GetData(IconCanvas.InternalFormat) as string[] ?? new string[0];
            var stream = data.GetDataPresent("Shell IDList Array") ? data.GetData("Shell IDList Array") as MemoryStream : null;
            if (stream == null) return data.GetData(DataFormats.FileDrop) as string[] ?? new string[0];
            byte[] bytes = stream.ToArray(); if (bytes.Length < 8) return new string[0];
            int count = BitConverter.ToInt32(bytes, 0);
            if (count < 1 || count > 10000 || 4L + (count + 1L) * 4 > bytes.Length) throw new InvalidDataException("拖入的数据无效。");
            int[] offsets = Enumerable.Range(0, count + 1).Select(i => BitConverter.ToInt32(bytes, 4 + i * 4)).ToArray();
            foreach (int offset in offsets)
            {
                if (offset < 4 + (count + 1) * 4 || offset >= bytes.Length - 1) throw new InvalidDataException("拖入的数据无效。");
                int at = offset;
                while (true) { if (at + 2 > bytes.Length) throw new InvalidDataException(); int n = BitConverter.ToUInt16(bytes, at); if (n == 0) break; if (n < 2) throw new InvalidDataException(); at += n; }
            }
            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned); var paths = new List<string>();
            try
            {
                for (int i = 1; i <= count; i++)
                {
                    IntPtr pidl = ILCombine(IntPtr.Add(pin.AddrOfPinnedObject(), offsets[0]), IntPtr.Add(pin.AddrOfPinnedObject(), offsets[i]));
                    IntPtr name = IntPtr.Zero;
                    try { Marshal.ThrowExceptionForHR(SHGetNameFromIDList(pidl, 0x80028000, out name)); paths.Add(Marshal.PtrToStringUni(name)); }
                    finally { Marshal.FreeCoTaskMem(name); Marshal.FreeCoTaskMem(pidl); }
                }
            }
            finally { pin.Free(); }
            return paths.ToArray();
        }
    }
}
