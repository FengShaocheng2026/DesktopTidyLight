using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace DesktopTidyLight
{
    internal sealed partial class LayoutController
    {
        internal void SetLocked(string id, bool locked)
        { var next = State.Copy(); next.Groups.Single(g => g.Id == id).Locked = locked; Commit(next); }

        internal void EnableUnified()
        {
            if (State.SessionActive || State.PendingPositions != null) throw new InvalidOperationException("请先恢复旧版桌面布局。");
            var next = State.Copy(); next.UnifiedIcons = true; Commit(next);
        }

        private void Commit(Layout next) { save(next); State = next; }

        private void StartUnified(IDesktopView desktop, Rectangle[] screens)
        {
            Recover(desktop);
            var next = State.Copy(); var items = desktop.Items(); MatchMembers(next, items);
            if (!next.SessionActive)
                foreach (var m in next.Members)
                {
                    var item = items.First(i => string.Equals(i.Id, m.Id, StringComparison.OrdinalIgnoreCase));
                    m.OriginalX = item.Position.X + desktop.Origin.X; m.OriginalY = item.Position.Y + desktop.Origin.Y;
                }
            if (next.Groups.Count > 0)
            {
                if (!next.OriginalFolderFlags.HasValue) next.OriginalFolderFlags = desktop.PositioningFlags;
                if (!next.OriginalIconsVisible.HasValue) next.OriginalIconsVisible = desktop.IconsVisible;
                next.SessionActive = true;
            }
            Apply(desktop, next, new Dictionary<string, Point>(), next.Groups.Count > 0 ? 0 : desktop.PositioningFlags,
                finalVisibility: next.Groups.Count > 0 ? (bool?)false : null);
        }

        private void CreateUnified(IDesktopView desktop, GroupData group, Rectangle[] screens)
        {
            var next = State.Copy(); next.Groups.Add(group);
            if (!next.OriginalFolderFlags.HasValue) next.OriginalFolderFlags = desktop.PositioningFlags;
            if (!next.OriginalIconsVisible.HasValue) next.OriginalIconsVisible = desktop.IconsVisible;
            next.SessionActive = true;
            Apply(desktop, next, new Dictionary<string, Point>(), 0, finalVisibility: false);
        }

        private bool SyncUnified(IDesktopView desktop)
        {
            var next = State.Copy(); var items = desktop.Items(); MatchMembers(next, items);
            bool changed = desktop.IconsVisible || !next.Members.Select(m => m.Id).SequenceEqual(State.Members.Select(m => m.Id));
            if (changed) Apply(desktop, next, new Dictionary<string, Point>(), 0, finalVisibility: false);
            return changed;
        }

        internal void AddMembers(IDesktopView desktop, string groupId, IEnumerable<string> ids)
        {
            var next = State.Copy();
            if (!next.Groups.Any(g => g.Id == groupId)) throw new InvalidOperationException("目标分组已不存在。");
            var items = desktop.Items(); MatchMembers(next, items);
            // Resolve the entire drop before committing, so a multi-selection is all or nothing.
            var batch = ids.Distinct(StringComparer.OrdinalIgnoreCase).Select(id => items.FirstOrDefault(i =>
                string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (batch.Length == 0 || batch.Any(i => i == null || i.Identity == null))
                throw new InvalidOperationException("请拖入桌面上的图标；部分项目暂时无法识别，整批未加入，文件没有移动。");
            foreach (var item in batch)
            {
                var m = next.Members.FirstOrDefault(x => string.Equals(x.Id, item.Id, StringComparison.OrdinalIgnoreCase));
                if (m == null) next.Members.Add(new MemberData { Id = item.Id, Identity = item.Identity, GroupId = groupId,
                    OriginalX = item.Position.X + desktop.Origin.X, OriginalY = item.Position.Y + desktop.Origin.Y });
                else m.GroupId = groupId;
            }
            Apply(desktop, next, new Dictionary<string, Point>(), 0, finalVisibility: false);
        }

        internal void SaveGroup(string id, Rectangle bounds, string name, DisplayInfo[] monitors = null)
        {
            var next = State.Copy(); var group = next.Groups.Single(g => g.Id == id);
            group.X = bounds.X; group.Y = bounds.Y; group.Width = bounds.Width; group.Height = bounds.Height; group.Name = name;
            if (monitors != null) DisplayLayout.Remember(group, monitors);
            Commit(next);
        }

        internal void SaveMonitors(DisplayInfo[] monitors, string movedGroup = null)
        {
            var next = State.Copy();
            foreach (var group in next.Groups)
            {
                if (group.MonitorId == null || group.Id == movedGroup) DisplayLayout.Remember(group, monitors);
            }
            DisplayLayout.ResolveAll(next, monitors);
            Commit(next);
        }

        internal void SavePreferences(string skin, bool autoHide, int? transparency = null)
        {
            var next = State.Copy(); next.Skin = skin; next.AutoHideTitle = autoHide;
            if (transparency.HasValue) next.BackgroundTransparency = Math.Max(0, Math.Min(100, transparency.Value));
            Commit(next);
        }

        internal void Reorder(string groupId, string[] ids)
        {
            var next = State.Copy(); var members = next.Members.Where(m => m.GroupId == groupId).ToList();
            if (ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != members.Count || ids.Any(id => !members.Any(m => m.Id == id)))
                throw new InvalidOperationException("分组成员已变化，请重试。");
            next.Members.RemoveAll(m => m.GroupId == groupId);
            next.Members.AddRange(ids.Select(id => members.Single(m => m.Id == id)));
            Commit(next);
        }

        internal void RemoveMembers(IDesktopView desktop, IEnumerable<string> ids, Rectangle[] screens, Point? dropPoint = null)
        {
            var next = State.Copy(); var items = desktop.Items(); MatchMembers(next, items);
            var ordered = ids.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var selected = new HashSet<string>(ordered, StringComparer.OrdinalIgnoreCase);
            var members = next.Members.Where(m => selected.Contains(m.Id)).ToList();
            if (dropPoint.HasValue)
            {
                var batch = ordered.Select(id => items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase))).ToArray();
                if (batch.Any(i => i == null || i.Identity == null))
                    throw new InvalidOperationException("部分桌面项目已变化，请重新拖动。");
                members = batch.Select(i => new MemberData { Id = i.Id, Identity = i.Identity }).ToList();
            }
            var visibleItems = items.Where(i => selected.Contains(i.Id) || !next.Members.Any(m => string.Equals(m.Id, i.Id, StringComparison.OrdinalIgnoreCase))).ToList();
            var targets = dropPoint.HasValue
                ? DesktopDrop.Targets(next, members, items, desktop.Origin, desktop.Spacing, desktop.IconSize, screens, dropPoint.Value)
                : GridLayout.RestoreTargets(members, visibleItems, desktop.Origin, desktop.Spacing, screens, desktop.IconSize);
            next.Members.RemoveAll(m => selected.Contains(m.Id));
            Apply(desktop, next, targets, 0, finalVisibility: false);
        }
    }
}
