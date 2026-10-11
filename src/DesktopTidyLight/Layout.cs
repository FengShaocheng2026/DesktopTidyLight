using System;
using System.Collections.Generic;
using System.IO;
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
    }

    [DataContract]
    internal sealed class MemberData
    {
        [DataMember] internal string Id;
        [DataMember] internal string GroupId;
        [DataMember] internal int OriginalX;
        [DataMember] internal int OriginalY;
    }

    [DataContract]
    internal sealed class PositionData
    {
        [DataMember] internal string Id;
        [DataMember] internal int X;
        [DataMember] internal int Y;
    }

    [DataContract]
    internal sealed class Layout
    {
        [DataMember] internal int Version = 1;
        [DataMember] internal List<GroupData> Groups = new List<GroupData>();
        [DataMember] internal List<MemberData> Members = new List<MemberData>();
        [DataMember] internal List<PositionData> Snapshots = new List<PositionData>();
        [DataMember] internal int? OriginalFolderFlags;
    }

    internal static class LayoutStore
    {
        internal static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTidyLight");
        internal static readonly string Pathname = Path.Combine(Folder, "layout.json");
        private static readonly string Backup = Pathname + ".bak";

        internal static Layout Load()
        {
            foreach (string candidate in new[] { Pathname, Backup })
            {
                if (!File.Exists(candidate)) continue;
                try
                {
                    Layout data;
                    using (var file = File.OpenRead(candidate))
                    {
                        data = (Layout)new DataContractJsonSerializer(typeof(Layout)).ReadObject(file);
                        if (data.Version != 1 || data.Groups == null || data.Members == null)
                            throw new InvalidDataException("不支持的布局版本。");
                        if (data.Snapshots == null) data.Snapshots = new List<PositionData>();
                    }
                    if (candidate == Backup) File.Copy(Backup, Pathname, true);
                    return data;
                }
                catch (Exception error) when (error is IOException || error is SerializationException || error is InvalidDataException)
                {
                    // Try the last known good copy.
                }
            }
            if (File.Exists(Pathname) || File.Exists(Backup))
                throw new InvalidDataException("布局文件无法读取；请保留文件并使用恢复入口。");
            return new Layout();
        }

        internal static void Save(Layout data)
        {
            Directory.CreateDirectory(Folder);
            string temporary = Pathname + ".tmp";
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                new DataContractJsonSerializer(typeof(Layout)).WriteObject(file, data);
                file.Flush(true);
            }
            if (File.Exists(Pathname)) File.Replace(temporary, Pathname, Backup);
            else File.Move(temporary, Pathname);
        }
    }
}
