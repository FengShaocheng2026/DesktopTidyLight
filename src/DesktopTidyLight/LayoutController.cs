using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace DesktopTidyLight
{
    internal sealed class RecoveryRequiredException : Exception
    {
        internal bool CanRetry { get; private set; }
        internal RecoveryRequiredException(string message, Exception inner) : base(message, inner) { }
        internal RecoveryRequiredException(string message, Exception inner, bool canRetry) : base(message, inner) { CanRetry = canRetry; }
    }

    // All layout edits are staged. A durable rollback record precedes every Shell mutation.
    internal sealed partial class LayoutController
    {
        private readonly Action<Layout> save;
        internal Layout State { get; private set; }
        internal string Notice { get; private set; }
        private DragState drag;

        private sealed class DragState
        {
            internal Layout Next;
            internal string GroupId;
            internal List<DesktopItem> Items;
            internal Point Origin;
            internal Size Spacing;
            internal int IconSize;
            internal Dictionary<string, Point> Positions;
        }

        internal LayoutController(Layout state, Action<Layout> save)
        {
            state.Validate();
            State = state;
            this.save = save;
        }

        private static List<PositionData> Snapshot(IEnumerable<DesktopItem> items)
        {
            return items.Select(i => new PositionData { Id = i.Id, Identity = i.Identity, X = i.Position.X, Y = i.Position.Y }).ToList();
        }

        private void MatchMembers(Layout next, IList<DesktopItem> items)
        {
            Notice = null;
            foreach (var member in next.Members.ToArray())
            {
                var atPath = items.FirstOrDefault(i => string.Equals(i.Id, member.Id, StringComparison.OrdinalIgnoreCase));
                if (atPath != null && atPath.Identity == null)
                    throw new InvalidOperationException("暂时无法核对某个分组项目的身份，已停止布局；请检查文件访问权限或 OneDrive 状态。");
                var matches = member.Identity == null ? new List<DesktopItem>() : items.Where(i => i.Identity == member.Identity).ToList();
                var match = matches.FirstOrDefault(i => string.Equals(i.Id, member.Id, StringComparison.OrdinalIgnoreCase)) ??
                    (matches.Count == 1 ? matches[0] : null);
                if (match == null)
                {
                    next.Members.Remove(member);
                    Notice = "已移除不存在或身份无法确认的成员记录，未操作对应文件；旧版成员可重新拖入分组。";
                }
                else member.Id = match.Id;
            }
        }

        private static void CheckPositions(IDesktopView desktop, IDictionary<string, Point> targets, IList<DesktopItem> expected)
        {
            var current = desktop.Items().ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var target in targets)
            {
                DesktopItem item;
                var original = expected.First(i => string.Equals(i.Id, target.Key, StringComparison.OrdinalIgnoreCase));
                if (!current.TryGetValue(target.Key, out item) || original.Identity == null || item.Identity != original.Identity || item.Position != target.Value)
                    throw new InvalidOperationException("部分图标未到达目标位置，或文件身份已变化。");
            }
        }

        private static void MoveChecked(IDesktopView desktop, IDictionary<string, Point> targets, IList<DesktopItem> expected)
        {
            if (targets.Count == 0) return;
            var current = desktop.Items().ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var id in targets.Keys)
            {
                DesktopItem item;
                var original = expected.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
                if (original == null || original.Identity == null || !current.TryGetValue(id, out item) || item.Identity != original.Identity)
                    throw new InvalidOperationException("桌面项目已变化，已停止定位，避免操作其他文件。");
            }
            desktop.SetPositions(targets);
            CheckPositions(desktop, targets, expected);
        }

        private void Apply(IDesktopView desktop, Layout next, IDictionary<string, Point> targets,
            int finalFlags, IDictionary<string, Point> rollbackOverrides = null)
        {
            if (State.PendingPositions != null) throw new InvalidOperationException("请先恢复上次未完成的操作。");
            var items = desktop.Items();
            var ids = new HashSet<string>(targets.Keys, StringComparer.OrdinalIgnoreCase);
            if (rollbackOverrides != null) ids.UnionWith(rollbackOverrides.Keys);
            var journal = State.Copy();
            journal.PendingPositions = Snapshot(items.Where(i => ids.Contains(i.Id)));
            if (journal.PendingPositions.Count != ids.Count || journal.PendingPositions.Any(p => p.Identity == null))
                throw new InvalidOperationException("部分桌面项目无法核对身份，布局未修改。");
            if (rollbackOverrides != null)
                foreach (var point in journal.PendingPositions)
                {
                    Point value;
                    if (rollbackOverrides.TryGetValue(point.Id, out value)) { point.X = value.X; point.Y = value.Y; }
                }
            journal.PendingFlags = desktop.PositioningFlags;
            try { save(journal); }
            catch (Exception error) { throw new RecoveryRequiredException("保存恢复记录失败，已停止修改桌面。", error); }
            State = journal;
            try
            {
                if (targets.Count > 0 && desktop.PositioningFlags != 0) desktop.SetPositioningFlags(0);
                MoveChecked(desktop, targets, items);
                if (desktop.PositioningFlags != finalFlags) desktop.SetPositioningFlags(finalFlags);
                next.Snapshots = Snapshot(desktop.Items());
                next.PendingPositions = null;
                next.PendingFlags = null;
                save(next);
                State = next;
            }
            catch (Exception error)
            {
                try { Recover(desktop); }
                catch (Exception rollbackError)
                {
                    throw new RecoveryRequiredException("操作和回退未完成，自动整理已暂停；恢复记录仍在。" + rollbackError.Message, error);
                }
                bool retry = error is InvalidOperationException || error is System.Runtime.InteropServices.COMException;
                throw new RecoveryRequiredException(retry ? "桌面正在调整，已回退，将自动重试。" + error.Message :
                    "操作未完成，已退回调整前，自动整理已暂停。" + error.Message, error, retry);
            }
        }

        internal void Recover(IDesktopView desktop)
        {
            if (State.PendingPositions == null) return;
            (desktop as DesktopShell)?.RecoverPreviewDrawing();
            var items = desktop.Items();
            var targets = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
            foreach (var point in State.PendingPositions)
            {
                var item = items.FirstOrDefault(i => string.Equals(i.Id, point.Id, StringComparison.OrdinalIgnoreCase));
                if (item != null && item.Identity == null) throw new InvalidOperationException("恢复项目暂时无法访问，记录已保留。");
                if (item == null || item.Identity != point.Identity) continue;
                targets.Add(item.Id, new Point(point.X, point.Y));
            }
            // If an unrelated icon moved into a rollback slot, retain the journal instead of overlapping it.
            foreach (var target in targets)
                if (items.Any(i => !targets.ContainsKey(i.Id) && GridLayout.ItemBounds(i.Position, desktop.Spacing, desktop.IconSize)
                    .IntersectsWith(GridLayout.ItemBounds(target.Value, desktop.Spacing, desktop.IconSize))))
                    throw new InvalidOperationException("恢复位置已被其他图标占用，请移开后重试。");
            if (targets.Count > 0 && desktop.PositioningFlags != 0) desktop.SetPositioningFlags(0);
            MoveChecked(desktop, targets, items);
            if (State.PendingFlags.HasValue && desktop.PositioningFlags != State.PendingFlags.Value)
                desktop.SetPositioningFlags(State.PendingFlags.Value);
            var recovered = State.Copy();
            recovered.PendingPositions = null;
            recovered.PendingFlags = null;
            recovered.Snapshots = Snapshot(desktop.Items());
            save(recovered);
            State = recovered;
        }

        internal void Start(IDesktopView desktop, Rectangle[] screens)
        {
            if (State.UnifiedIcons) { StartUnified(desktop, screens); return; }
            Recover(desktop);
            var next = State.Copy();
            var items = desktop.Items();
            MatchMembers(next, items);
            if (!next.SessionActive)
                foreach (var member in next.Members)
                {
                    var item = items.First(i => string.Equals(i.Id, member.Id, StringComparison.OrdinalIgnoreCase));
                    member.OriginalX = item.Position.X + desktop.Origin.X; member.OriginalY = item.Position.Y + desktop.Origin.Y;
                }
            GridLayout.FitGroups(next, screens, desktop.Spacing, items, desktop.Origin, desktop.IconSize);
            var targets = GridLayout.Arrange(next, items, next.Groups.Select(g => g.Id), desktop.Origin, desktop.Spacing, desktop.IconSize);
            if (next.Groups.Count > 0)
            {
                if (!next.OriginalFolderFlags.HasValue) next.OriginalFolderFlags = desktop.PositioningFlags;
                next.SessionActive = true;
            }
            int flags = next.Groups.Count > 0 ? 0 : next.OriginalFolderFlags ?? desktop.PositioningFlags;
            if (next.Groups.Count == 0) { next.OriginalFolderFlags = null; next.SessionActive = false; }
            Apply(desktop, next, targets, flags);
        }

        internal void Create(IDesktopView desktop, GroupData group, Rectangle[] screens)
        {
            if (State.UnifiedIcons) { CreateUnified(desktop, group, screens); return; }
            var next = State.Copy();
            next.Groups.Add(group);
            GridLayout.FitGroups(next, screens, desktop.Spacing, desktop.Items(), desktop.Origin, desktop.IconSize);
            if (!next.OriginalFolderFlags.HasValue) next.OriginalFolderFlags = desktop.PositioningFlags;
            next.SessionActive = true;
            Apply(desktop, next, GridLayout.Arrange(next, desktop.Items(), next.Groups.Select(g => g.Id), desktop.Origin, desktop.Spacing, desktop.IconSize), 0);
        }

        internal void ChangeGroup(IDesktopView desktop, string groupId, Rectangle bounds, string name)
        {
            if (State.UnifiedIcons) { SaveGroup(groupId, bounds, name); return; }
            var next = State.Copy();
            var group = next.Groups.Single(g => g.Id == groupId);
            if (next.Groups.Any(g => g.Id != groupId && GridLayout.Bounds(g).IntersectsWith(bounds)))
                throw new InvalidOperationException("分组不能重叠，请换一个位置。");
            group.X = bounds.X; group.Y = bounds.Y; group.Width = bounds.Width; group.Height = bounds.Height; group.Name = name;
            var items = desktop.Items();
            MatchMembers(next, items);
            var targets = GridLayout.Arrange(next, items, new[] { groupId }, desktop.Origin, desktop.Spacing, desktop.IconSize);
            Apply(desktop, next, targets, 0);
        }

        internal void BeginDrag(IDesktopView desktop, string groupId)
        {
            if (drag != null || State.PendingPositions != null) throw new InvalidOperationException("请先完成或恢复上次操作。");
            var items = desktop.Items();
            var next = State.Copy();
            MatchMembers(next, items);
            var ids = new HashSet<string>(next.Members.Where(m => m.GroupId == groupId).Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
            var journal = State.Copy();
            journal.PendingPositions = Snapshot(items.Where(i => ids.Contains(i.Id)));
            journal.PendingFlags = desktop.PositioningFlags;
            var origin = desktop.Origin; var spacing = desktop.Spacing; int iconSize = desktop.IconSize;
            try { save(journal); }
            catch (Exception error) { throw new RecoveryRequiredException("无法保存拖动恢复记录，操作已取消。", error); }
            State = journal;
            drag = new DragState { Next = next, GroupId = groupId, Items = items, Origin = origin,
                Spacing = spacing, IconSize = iconSize,
                Positions = items.Where(i => ids.Contains(i.Id)).ToDictionary(i => i.Id, i => i.Position, StringComparer.OrdinalIgnoreCase) };
        }

        internal void PreviewDrag(IDesktopView desktop, Rectangle bounds)
        {
            if (drag == null) throw new InvalidOperationException("没有正在进行的拖动。");
            if (drag.Next.Groups.Any(g => g.Id != drag.GroupId && GridLayout.Bounds(g).IntersectsWith(bounds)))
                throw new InvalidOperationException("分组不能重叠");
            var group = drag.Next.Groups.Single(g => g.Id == drag.GroupId);
            var previousBounds = GridLayout.Bounds(group);
            group.X = bounds.X; group.Y = bounds.Y; group.Width = bounds.Width; group.Height = bounds.Height;
            Dictionary<string, Point> targets;
            try { targets = GridLayout.Arrange(drag.Next, drag.Items, new[] { drag.GroupId }, drag.Origin, drag.Spacing, drag.IconSize); }
            catch
            {
                group.X = previousBounds.X; group.Y = previousBounds.Y; group.Width = previousBounds.Width; group.Height = previousBounds.Height;
                throw;
            }
            var changed = targets.Where(p => !drag.Positions.ContainsKey(p.Key) || drag.Positions[p.Key] != p.Value)
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            try { if (changed.Count > 0) desktop.SetPositions(changed); }
            catch (Exception error) { throw new RecoveryRequiredException("实时移动失败，将恢复拖动前的布局。", error); }
            drag.Positions = targets;
        }

        internal void EndDrag(IDesktopView desktop, bool cancel)
        {
            var current = drag;
            drag = null;
            if (current == null) return;
            if (cancel) { Recover(desktop); return; }
            try
            {
                MoveChecked(desktop, current.Positions, current.Items);
                current.Next.Snapshots = Snapshot(desktop.Items());
                save(current.Next);
                State = current.Next;
            }
            catch (Exception error)
            {
                try { Recover(desktop); }
                catch (Exception restoreError) { throw new RecoveryRequiredException("拖动恢复未完成，记录已保留。" + restoreError.Message, error); }
                throw new RecoveryRequiredException("拖动未能保存，已恢复原布局。", error);
            }
        }

        internal bool Sync(IDesktopView desktop, Point? releasePoint = null)
        {
            if (State.UnifiedIcons) return SyncUnified(desktop);
            var items = desktop.Items();
            var next = State.Copy();
            MatchMembers(next, items);
            var affected = new HashSet<string>();
            foreach (var member in State.Members)
                if (!next.Members.Any(m => m.Id == member.Id && m.Identity == member.Identity)) affected.Add(member.GroupId);
            var rollback = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
            var before = State.Snapshots.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
            var moved = items.Where(item => before.ContainsKey(item.Id) && before[item.Id].Identity != null &&
                before[item.Id].Identity == item.Identity && (before[item.Id].X != item.Position.X || before[item.Id].Y != item.Position.Y)).ToList();
            var selected = moved.Count == 0 ? new List<string>() : desktop.SelectedIds();
            var anchor = selected.Select(id => moved.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(i => i != null);
            GroupData batchTarget = anchor == null ? null : next.Groups.FirstOrDefault(g => GridLayout.Bounds(g).Contains(
                new Point(anchor.Position.X + desktop.Origin.X, anchor.Position.Y + desktop.Origin.Y)));
            if (releasePoint.HasValue && anchor != null)
            {
                batchTarget = next.Groups.FirstOrDefault(g => GridLayout.Bounds(g).Contains(releasePoint.Value));
                // Include the whole selection, even when an item's own anchor did not move.
                var movedIds = new HashSet<string>(moved.Select(i => i.Id), StringComparer.OrdinalIgnoreCase);
                foreach (var item in items.Where(i => selected.Contains(i.Id) && !movedIds.Contains(i.Id) &&
                    before.ContainsKey(i.Id) && before[i.Id].Identity != null && before[i.Id].Identity == i.Identity)) moved.Add(item);
            }
            foreach (var item in moved.OrderBy(i => i.Position.Y).ThenBy(i => i.Position.X))
            {
                var old = before[item.Id];
                var member = next.Members.FirstOrDefault(m => string.Equals(m.Id, item.Id, StringComparison.OrdinalIgnoreCase));
                var screenPosition = new Point(item.Position.X + desktop.Origin.X, item.Position.Y + desktop.Origin.Y);
                var target = next.Groups.FirstOrDefault(g => GridLayout.Bounds(g).Contains(screenPosition));
                if (anchor != null && selected.Contains(item.Id)) target = batchTarget;
                if (member == null && target == null) continue;
                rollback.Add(item.Id, new Point(old.X, old.Y));
                if (member != null) affected.Add(member.GroupId);
                if (target == null) next.Members.Remove(member);
                else
                {
                    affected.Add(target.Id);
                    if (member == null)
                        next.Members.Add(new MemberData { Id = item.Id, Identity = item.Identity, GroupId = target.Id,
                            OriginalX = old.X + desktop.Origin.X, OriginalY = old.Y + desktop.Origin.Y });
                    else member.GroupId = target.Id;
                }
            }
            Dictionary<string, Point> targets;
            try { targets = GridLayout.Arrange(next, items, affected, desktop.Origin, desktop.Spacing, desktop.IconSize); }
            catch (InvalidOperationException)
            {
                if (rollback.Count > 0) Apply(desktop, State.Copy(), rollback, 0, rollback);
                throw;
            }
            bool unchanged = affected.Count == 0 && items.Count == before.Count && items.All(i =>
                before.ContainsKey(i.Id) && before[i.Id].Identity == i.Identity && before[i.Id].X == i.Position.X && before[i.Id].Y == i.Position.Y);
            if (!unchanged) Apply(desktop, next, targets, 0, rollback);
            return moved.Count > 0;
        }

        internal void Restore(IDesktopView desktop, Rectangle[] screens, string deleteGroup = null)
        {
            Recover(desktop);
            var next = State.Copy();
            var items = desktop.Items();
            MatchMembers(next, items);
            var members = next.Members.Where(m => deleteGroup == null || m.GroupId == deleteGroup).ToList();
            var targets = State.SessionActive || State.OriginalFolderFlags.HasValue
                ? GridLayout.RestoreTargets(members, items, desktop.Origin, desktop.Spacing, screens, desktop.IconSize)
                : new Dictionary<string, Point>();
            if (deleteGroup != null)
            {
                next.Members.RemoveAll(m => m.GroupId == deleteGroup);
                next.Groups.RemoveAll(g => g.Id == deleteGroup);
            }
            bool finish = deleteGroup == null || next.Groups.Count == 0;
            int flags = finish ? next.OriginalFolderFlags ?? desktop.PositioningFlags :
                State.SessionActive || State.OriginalFolderFlags.HasValue ? 0 : desktop.PositioningFlags;
            if (finish) { next.OriginalFolderFlags = null; next.SessionActive = false; }
            Apply(desktop, next, targets, flags);
        }
    }
}
