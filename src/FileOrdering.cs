using System;
using System.Runtime.InteropServices;

namespace WSLBox
{
    internal static class FileOrdering
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string left, string right);
        public static bool SameEntries(Entry[] first, Entry[] second)
        {
            if (first == null || second == null || first.Length != second.Length) return false;
            for (int index = 0; index < first.Length; index++)
            {
                Entry a = first[index], b = second[index];
                if (a.Name != b.Name || a.DisplayName != b.DisplayName || a.Path != b.Path || a.TrashId != b.TrashId
                    || a.IsDirectory != b.IsDirectory || a.IsLink != b.IsLink
                    || a.ContentType != b.ContentType || a.IconKey != b.IconKey
                    || a.Size != b.Size || a.Modified != b.Modified || a.CanThumbnail != b.CanThumbnail) return false;
            }
            return true;
        }
        public static int Compare(Entry left, Entry right, string key, bool descending)
        {
            if (left.IsDirectory != right.IsDirectory) return left.IsDirectory ? -1 : 1;
            int order;
            if (key == "size" && !left.IsDirectory) order = left.Size.CompareTo(right.Size);
            else if (key == "modified") order = left.Modified.CompareTo(right.Modified);
            else if (key == "type") order = StrCmpLogicalW(left.ContentType ?? "", right.ContentType ?? "");
            else order = StrCmpLogicalW(left.DisplayName ?? left.Name, right.DisplayName ?? right.Name);
            if (order != 0) return descending ? -Math.Sign(order) : Math.Sign(order);
            order = StrCmpLogicalW(left.DisplayName ?? left.Name, right.DisplayName ?? right.Name);
            if (order != 0) return Math.Sign(order);
            return StringComparer.Ordinal.Compare(left.Path, right.Path);
        }
    }
}
