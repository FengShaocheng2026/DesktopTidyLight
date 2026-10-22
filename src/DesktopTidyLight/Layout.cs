using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DesktopTidyLight
{
    [DataContract]
    internal sealed class GroupData
    {
        [DataMember] internal string Id;
        [DataMember] internal string Name;
        [DataMember] internal int X;
        [DataMember] internal int Y;
        [DataMember] internal int Width;
        [DataMember] internal int Height;
        [DataMember] internal bool Locked;
        [DataMember] internal string MonitorId;
        [DataMember] internal int MonitorX, MonitorY, MonitorWidth, MonitorHeight, MonitorDpi;
        [DataMember] internal int SavedX, SavedY, SavedWidth, SavedHeight;
    }

    [DataContract]
    internal sealed class MemberData
    {
        [DataMember] internal string Id;
        [DataMember] internal string GroupId;
        [DataMember] internal int OriginalX;
        [DataMember] internal int OriginalY;
        [DataMember] internal string Identity;
    }

    [DataContract]
    internal sealed class PositionData
    {
        [DataMember] internal string Id;
        [DataMember] internal int X;
        [DataMember] internal int Y;
        [DataMember] internal string Identity;
    }

    [DataContract]
    internal sealed class Layout
    {
        [DataMember] internal int Version = 3;
        [DataMember] internal bool UnifiedIcons;
        [DataMember] internal string Skin = "mist";
        [DataMember] internal bool AutoHideTitle;
        [DataMember] internal int BackgroundTransparency;
        [DataMember] internal List<GroupData> Groups = new List<GroupData>();
        [DataMember] internal List<MemberData> Members = new List<MemberData>();
        [DataMember] internal List<PositionData> Snapshots = new List<PositionData>();
        [DataMember] internal int? OriginalFolderFlags;
        [DataMember] internal bool SessionActive;
        [DataMember] internal List<PositionData> PendingPositions;
        [DataMember] internal int? PendingFlags;
        [DataMember] internal bool? OriginalIconsVisible;
        [DataMember] internal bool? PendingIconsVisible;

        internal Layout Copy()
        {
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(Layout));
                serializer.WriteObject(stream, this);
                stream.Position = 0;
                return (Layout)serializer.ReadObject(stream);
            }
        }

        internal void Validate()
        {
            if ((Version != 1 && Version != 2 && Version != 3) || Groups == null || Members == null)
                throw new InvalidDataException("不支持的布局格式。");
            if (Groups.Any(g => g == null || string.IsNullOrEmpty(g.Id) || g.Width < 1 || g.Height < 1 ||
                g.Width > 100000 || g.Height > 100000 || Math.Abs((long)g.X) > 1000000 || Math.Abs((long)g.Y) > 1000000) ||
                Groups.Select(g => g.Id).Distinct().Count() != Groups.Count ||
                Members.Any(m => m == null || string.IsNullOrEmpty(m.Id) || !Groups.Any(g => g.Id == m.GroupId)) ||
                Members.Select(m => m.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Members.Count)
                throw new InvalidDataException("布局包含无效或重复的分组、成员；已停止修改桌面。");
            if (Snapshots == null) Snapshots = new List<PositionData>();
            if (Snapshots.Any(p => p == null || string.IsNullOrEmpty(p.Id)) ||
                Snapshots.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Snapshots.Count ||
                (PendingPositions != null && PendingPositions.Any(p => p == null || string.IsNullOrEmpty(p.Id))))
                throw new InvalidDataException("位置恢复记录无效。");
            if (OriginalFolderFlags.HasValue && (OriginalFolderFlags.Value & ~5) != 0 ||
                PendingFlags.HasValue && (PendingFlags.Value & ~5) != 0)
                throw new InvalidDataException("桌面排列设置记录无效。");
            if (Version == 1 && OriginalFolderFlags.HasValue) SessionActive = true;
            if (Skin != "mist" && Skin != "paper" && Skin != "outline" && Skin != "plum") Skin = "mist";
            BackgroundTransparency = Math.Max(0, Math.Min(100, BackgroundTransparency));
            Version = 3;
        }
    }

    internal static class LayoutStore
    {
        internal static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTidyLight");
        internal static readonly string Pathname = Path.Combine(Folder, "layout.json");

        internal static Layout Load(string pathname = null)
        {
            pathname = pathname ?? Pathname;
            string backup = pathname + ".bak";
            foreach (string candidate in new[] { pathname, backup })
            {
                if (!File.Exists(candidate)) continue;
                try
                {
                    Layout data;
                    using (var file = File.OpenRead(candidate))
                    {
                        data = (Layout)new DataContractJsonSerializer(typeof(Layout)).ReadObject(file);
                        if (data == null) throw new InvalidDataException("布局为空。");
                        data.Validate();
                    }
                    if (candidate == backup) File.Copy(backup, pathname, true);
                    return data;
                }
                catch (Exception error) when (error is IOException || error is SerializationException || error is InvalidDataException)
                {
                    // Try the last known good copy.
                }
            }
            if (File.Exists(pathname) || File.Exists(backup))
                throw new InvalidDataException("布局文件无法读取；请保留文件并使用恢复入口。");
            return new Layout();
        }

        internal static void Save(Layout data, string pathname = null)
        {
            data.Validate();
            pathname = pathname ?? Pathname;
            Directory.CreateDirectory(Path.GetDirectoryName(pathname));
            string temporary = pathname + ".tmp";
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                new DataContractJsonSerializer(typeof(Layout)).WriteObject(file, data);
                file.Flush(true);
            }
            if (File.Exists(pathname)) File.Replace(temporary, pathname, pathname + ".bak");
            else File.Move(temporary, pathname);
        }
    }
}
