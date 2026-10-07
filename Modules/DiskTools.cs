using System.Collections.ObjectModel;
using System.Security.Cryptography;
using BacSiMayTinh.Core;

namespace BacSiMayTinh.Modules
{
    public class DirNode
    {
        public string Name;
        public string FullPath;
        public long Size;
        public long Files;
        public DirNode Parent;
        public List<DirNode> Children = new List<DirNode>();
        public long LooseFiles; // dung lượng tệp nằm trực tiếp trong thư mục này
    }

    public class BigFile : ObservableObject
    {
        public string Path { get; set; }
        public string Name => System.IO.Path.GetFileName(Path);
        public string Folder => System.IO.Path.GetDirectoryName(Path);
        public long Size { get; set; }
        public DateTime Modified { get; set; }
        public string SizeText => FileUtil.FormatSize(Size);
        public string DateText => Modified.ToString("dd/MM/yyyy");

        private bool _checked;
        public bool IsChecked { get => _checked; set => Set(ref _checked, value); }

        private bool _deleted;
        public bool IsDeleted { get => _deleted; set => Set(ref _deleted, value); }
    }

    public class DuplicateGroup
    {
        public long Size { get; set; }
        public ObservableCollection<BigFile> Files { get; } = new ObservableCollection<BigFile>();
        public string Title => L.F("{0} bản giống nhau  ·  {1} mỗi tệp  ·  lãng phí {2}", Files.Count, FileUtil.FormatSize(Size), FileUtil.FormatSize(Size * (Files.Count - 1)));
        public string FileName => Files.FirstOrDefault()?.Name;
    }

    public class ScanProgress
    {
        public long Files;
        public long Bytes;
        public string Current;
    }

    /// <summary>Quét ổ đĩa tốc độ cao bằng FindFirstFileEx.</summary>
    public static class DiskScanner
    {
        public static DirNode Scan(string root, List<BigFile> topFiles, int topCount, ScanProgress progress, CancellationToken ct)
        {
            var node = new DirNode { Name = root, FullPath = root.TrimEnd('\\') + "\\" };
            var heap = new SortedSet<(long size, string path)>();
            ScanDir(node, heap, topCount, progress, ct);
            topFiles.Clear();
            foreach (var (size, path) in heap.Reverse())
            {
                DateTime mod = DateTime.MinValue;
                try { mod = File.GetLastWriteTime(path); } catch { }
                topFiles.Add(new BigFile { Path = path, Size = size, Modified = mod });
            }
            return node;
        }

        private static string LongPath(string p) => p.StartsWith(@"\\") ? p : @"\\?\" + p;

        private static void ScanDir(DirNode node, SortedSet<(long, string)> heap, int topCount, ScanProgress progress, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;
            progress.Current = node.FullPath;
            var dir = node.FullPath.TrimEnd('\\') + "\\";
            var h = Native.FindFirstFileEx(LongPath(dir + "*"), 1 /* Basic */, out var fd, 0, IntPtr.Zero, 2 /* LargeFetch */);
            if (h == Native.INVALID_HANDLE_VALUE) return;
            try
            {
                do
                {
                    var name = fd.cFileName;
                    if (name == "." || name == "..") continue;
                    bool isDir = (fd.dwFileAttributes & FileAttributes.Directory) != 0;
                    bool reparse = (fd.dwFileAttributes & FileAttributes.ReparsePoint) != 0;
                    if (isDir)
                    {
                        if (reparse) continue;
                        var child = new DirNode { Name = name, FullPath = dir + name, Parent = node };
                        node.Children.Add(child);
                    }
                    else
                    {
                        long size = ((long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow;
                        node.LooseFiles += size;
                        node.Size += size;
                        node.Files++;
                        progress.Files++;
                        progress.Bytes += size;
                        if (size > 0 && (heap.Count < topCount || size > heap.Min.Item1))
                        {
                            heap.Add((size, dir + name));
                            if (heap.Count > topCount) heap.Remove(heap.Min);
                        }
                    }
                } while (Native.FindNextFile(h, out fd));
            }
            finally { Native.FindClose(h); }

            foreach (var c in node.Children)
            {
                if (ct.IsCancellationRequested) return;
                ScanDir(c, heap, topCount, progress, ct);
                node.Size += c.Size;
                node.Files += c.Files;
            }
            node.Children.Sort((a, b) => b.Size.CompareTo(a.Size));
        }
    }

    public static class DuplicateFinder
    {
        private static readonly string[] SkipDirs =
        {
            @"\Windows\", @"\Program Files\", @"\Program Files (x86)\", @"\ProgramData\", @"\$Recycle.Bin\",
            @"\System Volume Information\", @"\AppData\Local\Microsoft\", @"\AppData\Local\Packages\", @"\node_modules\", @"\.git\"
        };

        public static List<DuplicateGroup> Find(string root, long minSize, ScanProgress progress, CancellationToken ct)
        {
            var bySize = new Dictionary<long, List<string>>();
            foreach (var f in FileUtil.EnumerateFiles(root))
            {
                if (ct.IsCancellationRequested) return new List<DuplicateGroup>();
                long len;
                try { len = f.Length; } catch { continue; }
                progress.Files++;
                if (len < minSize) continue;
                var full = f.FullName;
                if (SkipDirs.Any(s => full.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                progress.Current = f.DirectoryName;
                if (!bySize.TryGetValue(len, out var l)) bySize[len] = l = new List<string>();
                l.Add(full);
            }

            var groups = new List<DuplicateGroup>();
            foreach (var kv in bySize.Where(k => k.Value.Count > 1).OrderByDescending(k => k.Key))
            {
                if (ct.IsCancellationRequested) break;
                // Lọc nhanh bằng 64KB đầu, sau đó so sánh toàn bộ tệp.
                foreach (var partial in kv.Value.GroupBy(p => Hash(p, 64 * 1024)).Where(g => g.Key != null && g.Count() > 1))
                {
                    var candidates = kv.Key <= 64 * 1024 ? partial.GroupBy(p => partial.Key) : partial.GroupBy(p => Hash(p, long.MaxValue));
                    foreach (var full in candidates.Where(g => g.Key != null && g.Count() > 1))
                    {
                        progress.Current = full.First();
                        var dg = new DuplicateGroup { Size = kv.Key };
                        var files = full.Select(p =>
                        {
                            DateTime m = DateTime.MinValue;
                            try { m = File.GetLastWriteTime(p); } catch { }
                            return new BigFile { Path = p, Size = kv.Key, Modified = m };
                        }).OrderBy(b => b.Modified).ToList();
                        // Giữ lại bản cũ nhất, đánh dấu sẵn các bản sao còn lại.
                        for (int i = 1; i < files.Count; i++) files[i].IsChecked = true;
                        foreach (var b in files) dg.Files.Add(b);
                        groups.Add(dg);
                    }
                }
            }
            return groups.OrderByDescending(g => g.Size * (g.Files.Count - 1)).ToList();
        }

        private static string Hash(string path, long limit)
        {
            try
            {
                using (var md5 = MD5.Create())
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16))
                {
                    if (limit == long.MaxValue) return Convert.ToBase64String(md5.ComputeHash(fs));
                    var buf = new byte[limit];
                    int read = fs.Read(buf, 0, buf.Length);
                    return Convert.ToBase64String(md5.ComputeHash(buf, 0, read));
                }
            }
            catch { return null; }
        }
    }
}
