using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using BacSiMayTinh.Core;

namespace BacSiMayTinh.Modules
{
    /// <summary>Một nguồn dữ liệu cá nhân (thư mục) để sao lưu.</summary>
    public class DataSource : ObservableObject
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Glyph { get; set; }
        public bool IsCustom { get; set; }
        public string[] Processes { get; set; }   // ứng dụng nên đóng trước khi chép
        public List<DataPart> Parts { get; } = new List<DataPart>();

        public string PathText => string.Join("; ", Parts.Select(p => p.Source));
        public bool Exists => Parts.Any(p => Directory.Exists(p.Source));

        private long _size = -1;
        public long Size { get => _size; set { Set(ref _size, value); OnPropertyChanged(nameof(SizeText)); } }
        public string SizeText => _size < 0 ? L.T("đang tính…") : FileUtil.FormatSize(_size);

        private bool _checked;
        public bool IsChecked { get => _checked; set => Set(ref _checked, value); }
    }

    public class DataPart
    {
        public string Source { get; set; }
        public string SubFolder { get; set; }        // tên thư mục con trong bản sao lưu
        public string FilePattern { get; set; }      // null = mọi tệp
        public string[] ExcludeDirs { get; set; } = new string[0];
    }

    public class DataProgress
    {
        public long Files;
        public long Bytes;
        public int Errors;
        public string Current;
    }

    public class DataBackupResult
    {
        public long Files { get; set; }
        public long Bytes { get; set; }
        public int Errors { get; set; }
        public string Target { get; set; }
    }

    [DataContract]
    public class DataBackupEntry
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public string SubFolder { get; set; }
        [DataMember] public string OriginalPath { get; set; }
        [DataMember] public string FilePattern { get; set; }
    }

    [DataContract]
    public class DataBackupManifest
    {
        [DataMember] public string Created { get; set; }
        [DataMember] public string Machine { get; set; }
        [DataMember] public string User { get; set; }
        [DataMember] public List<DataBackupEntry> Entries { get; set; } = new List<DataBackupEntry>();
        [OnDeserialized] private void Fix(StreamingContext c) { if (Entries == null) Entries = new List<DataBackupEntry>(); }
    }

    /// <summary>
    /// Sao lưu dữ liệu cá nhân (Desktop, Tài liệu, Ảnh, Zalo, dấu trang trình duyệt…) sang ổ khác/USB bằng robocopy:
    /// chép tăng dần (lần sau chỉ chép tệp mới/đã sửa), không bao giờ xóa tệp ở nơi lưu.
    /// </summary>
    public static class DataBackup
    {
        public const string ManifestName = "DrAn_DuLieu.json";
        private static readonly string[] CacheDirs = { "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "Crashpad", "logs", "blob_storage", "Service Worker" };

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        public static string DownloadsFolder
        {
            get
            {
                try
                {
                    if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out var p) == 0)
                    {
                        var s = Marshal.PtrToStringUni(p);
                        Marshal.FreeCoTaskMem(p);
                        return s;
                    }
                }
                catch { }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
        }

        private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        /// <summary>Đường dẫn hiện tại của một thư mục đã biết (theo máy đang chạy).</summary>
        public static string KnownPath(string id)
        {
            switch (id)
            {
                case "desktop": return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                case "documents": return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                case "pictures": return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                case "videos": return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                case "music": return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                case "downloads": return DownloadsFolder;
                case "zalo": return Path.Combine(Roaming, "ZaloData");
                case "bm_chrome": return Path.Combine(Local, @"Google\Chrome\User Data");
                case "bm_edge": return Path.Combine(Local, @"Microsoft\Edge\User Data");
                case "bm_coccoc": return Path.Combine(Local, @"CocCoc\Browser\User Data");
                default: return null;
            }
        }

        public static List<DataSource> Sources()
        {
            var list = new List<DataSource>();
            DataSource Folder(string id, string name, string glyph)
            {
                var s = new DataSource { Id = id, Name = name, Glyph = glyph };
                s.Parts.Add(new DataPart { Source = KnownPath(id), SubFolder = id });
                return s;
            }
            list.Add(Folder("desktop", L.T("Màn hình nền (Desktop)"), "\uE7F4"));
            list.Add(Folder("documents", L.T("Tài liệu (Documents)"), "\uE8A5"));
            list.Add(Folder("pictures", L.T("Ảnh (Pictures)"), "\uE91B"));
            list.Add(Folder("videos", L.T("Video (Videos)"), "\uE714"));
            list.Add(Folder("music", L.T("Nhạc (Music)"), "\uE8D6"));
            list.Add(Folder("downloads", L.T("Tệp đã tải về (Downloads)"), "\uE896"));

            var zalo = new DataSource { Id = "zalo", Name = L.T("Dữ liệu Zalo PC (tin nhắn, ảnh, tệp)"), Glyph = "\uE8BD", Processes = new[] { "Zalo" } };
            zalo.Parts.Add(new DataPart { Source = KnownPath("zalo"), SubFolder = "zalo", ExcludeDirs = CacheDirs });
            if (zalo.Exists) list.Add(zalo);

            var bm = new DataSource { Id = "bookmarks", Name = L.T("Dấu trang trình duyệt (Chrome, Edge, Cốc Cốc)"), Glyph = "\uE728", Processes = new[] { "chrome", "msedge", "browser" } };
            foreach (var b in new[] { "bm_chrome", "bm_edge", "bm_coccoc" })
                if (Directory.Exists(KnownPath(b))) bm.Parts.Add(new DataPart { Source = KnownPath(b), SubFolder = b, FilePattern = "Bookmarks" });
            if (bm.Parts.Count > 0) list.Add(bm);

            foreach (var c in AppSettings.Current.DataBackupCustom ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(c)) continue;
                var s = new DataSource { Id = "custom:" + c, Name = Path.GetFileName(c.TrimEnd('\\')), Glyph = "\uE8B7", IsCustom = true };
                s.Parts.Add(new DataPart { Source = c, SubFolder = "khac_" + ConfigBackup.SafeName(Path.GetFileName(c.TrimEnd('\\'))) + "_" + StableHash(c) });
                list.Add(s);
            }

            var sel = AppSettings.Current.DataBackupSources;
            foreach (var s in list)
                s.IsChecked = sel == null ? (s.Id == "desktop" || s.Id == "documents" || s.Id == "pictures" || s.Id == "zalo" || s.Id == "bookmarks" || s.IsCustom)
                                          : sel.Contains(s.Id) || s.IsCustom && sel.Contains("custom");
            return list;
        }

        /// <summary>Mã băm ổn định (không đổi giữa các lần chạy/máy) để đặt tên thư mục con.</summary>
        public static string StableHash(string s)
        {
            uint h = 2166136261;
            foreach (var ch in (s ?? "").ToLowerInvariant().TrimEnd('\\')) { h ^= ch; h *= 16777619; }
            return (h % 100000).ToString("00000");
        }

        public static List<DataSource> SelectedSources() => Sources().Where(s => s.IsChecked).ToList();

        public static void SaveSelection(IEnumerable<DataSource> list)
        {
            var ids = list.Where(s => s.IsChecked && !s.IsCustom).Select(s => s.Id).ToList();
            if (list.Any(s => s.IsCustom && s.IsChecked)) ids.Add("custom");
            AppSettings.Current.DataBackupSources = ids;
            AppSettings.SaveCurrent();
        }

        public static long MeasureSize(DataSource s)
        {
            long total = 0;
            foreach (var p in s.Parts)
            {
                if (!Directory.Exists(p.Source)) continue;
                foreach (var f in FileUtil.EnumerateFiles(p.Source, p.FilePattern ?? "*"))
                {
                    try
                    {
                        if (p.ExcludeDirs.Length > 0 && p.ExcludeDirs.Any(x => f.FullName.IndexOf("\\" + x + "\\", StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                        total += f.Length;
                    }
                    catch { }
                }
            }
            return total;
        }

        public static string TargetFolder(string destRoot) =>
            Path.Combine(destRoot, "DrAn_DuLieu_" + ConfigBackup.SafeName(Environment.MachineName) + "_" + ConfigBackup.SafeName(Environment.UserName));

        public static List<string> RunningApps(IEnumerable<DataSource> sources)
        {
            var names = new List<string>();
            foreach (var p in sources.Where(s => s.Processes != null).SelectMany(s => s.Processes).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var ps = Process.GetProcessesByName(p);
                if (ps.Length > 0) names.Add(p);
                foreach (var x in ps) x.Dispose();
            }
            return names;
        }

        private static string Robocopy => Path.Combine(Environment.SystemDirectory, "robocopy.exe");

        /// <summary>Chạy robocopy và đếm số tệp/dung lượng đã chép từ kết quả.</summary>
        private static async Task<int> CopyAsync(string src, string dst, string pattern, string[] excludeDirs, bool onlyMissing, DataProgress prog, Action<DataProgress> report, CancellationToken ct)
        {
            var sb = new StringBuilder();
            sb.Append('"').Append(src.TrimEnd('\\')).Append("\" \"").Append(dst.TrimEnd('\\')).Append('"');
            if (!string.IsNullOrEmpty(pattern)) sb.Append(" \"").Append(pattern).Append('"');
            sb.Append(" /E /XJ /R:1 /W:1 /MT:8 /NP /NDL /NJH /NJS /FP /BYTES /UNICODE /XA:O");
            if (onlyMissing) sb.Append(" /XC /XN /XO");
            if (excludeDirs != null && excludeDirs.Length > 0)
            {
                sb.Append(" /XD");
                foreach (var d in excludeDirs) sb.Append(" \"").Append(d).Append('"');
            }
            var errRx = new Regex(@"\(0x[0-9A-Fa-f]{8}\)");
            var r = await ProcessRunner.RunAsync(Robocopy, sb.ToString(), (line, p) =>
            {
                if (errRx.IsMatch(line)) { Interlocked.Increment(ref prog.Errors); return; }
                var parts = line.Split('\t').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
                if (parts.Length >= 2 && (parts[parts.Length - 1].IndexOf(":\\", StringComparison.Ordinal) > 0 || parts[parts.Length - 1].StartsWith("\\\\")))
                {
                    if (long.TryParse(parts[parts.Length - 2], out var size)) Interlocked.Add(ref prog.Bytes, size);
                    Interlocked.Increment(ref prog.Files);
                    prog.Current = parts[parts.Length - 1];
                    report?.Invoke(prog);
                }
            }, Encoding.Unicode, ct).ConfigureAwait(false);
            return r.ExitCode;
        }

        public static async Task<DataBackupResult> RunAsync(List<DataSource> sources, string destRoot, Action<DataProgress> report, CancellationToken ct)
        {
            var target = TargetFolder(destRoot);
            Directory.CreateDirectory(target);
            var manifestPath = Path.Combine(target, ManifestName);
            var man = File.Exists(manifestPath) ? JsonFile.Load<DataBackupManifest>(manifestPath) : new DataBackupManifest();
            var prog = new DataProgress();
            Logger.Info("Sao lưu dữ liệu cá nhân → " + target + ": " + string.Join(", ", sources.Select(s => s.Id)));
            foreach (var s in sources)
            {
                foreach (var p in s.Parts)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!Directory.Exists(p.Source)) continue;
                    var dst = Path.Combine(target, p.SubFolder);
                    var code = await CopyAsync(p.Source, dst, p.FilePattern, p.ExcludeDirs, false, prog, report, ct).ConfigureAwait(false);
                    if (code >= 8) prog.Errors = Math.Max(prog.Errors, 1);
                    man.Entries.RemoveAll(e => e.SubFolder == p.SubFolder);
                    man.Entries.Add(new DataBackupEntry { Id = s.IsCustom ? "custom" : (p.SubFolder.StartsWith("bm_") ? p.SubFolder : s.Id), Name = s.Name, SubFolder = p.SubFolder, OriginalPath = p.Source, FilePattern = p.FilePattern });
                }
            }
            man.Created = DateTime.Now.ToString("s");
            man.Machine = Environment.MachineName;
            man.User = Environment.UserName;
            JsonFile.Save(manifestPath, man);
            AppSettings.Current.LastDataBackup = man.Created;
            AppSettings.SaveCurrent();
            Logger.Info($"Sao lưu dữ liệu xong: {prog.Files} tệp, {FileUtil.FormatSize(prog.Bytes)}, lỗi {prog.Errors}");
            return new DataBackupResult { Files = prog.Files, Bytes = prog.Bytes, Errors = prog.Errors, Target = target };
        }

        public static DataBackupManifest LoadBackup(string folder)
        {
            var f = Path.Combine(folder, ManifestName);
            return File.Exists(f) ? JsonFile.Load<DataBackupManifest>(f) : null;
        }

        /// <summary>Chép dữ liệu từ bản sao lưu về đúng thư mục trên máy hiện tại.</summary>
        public static async Task<DataBackupResult> RestoreAsync(string folder, DataBackupManifest man, bool onlyMissing, Action<DataProgress> report, CancellationToken ct)
        {
            var prog = new DataProgress();
            Logger.Info("Phục hồi dữ liệu cá nhân từ " + folder);
            foreach (var e in man.Entries)
            {
                ct.ThrowIfCancellationRequested();
                var src = Path.Combine(folder, e.SubFolder);
                if (!Directory.Exists(src)) continue;
                var dst = e.Id == "custom" ? e.OriginalPath : KnownPath(e.Id) ?? e.OriginalPath;
                if (string.IsNullOrEmpty(dst)) continue;
                // Dấu trang: luôn lấy bản mới hơn (không ghi đè dấu trang mới hơn trên máy).
                bool missing = e.FilePattern == null ? onlyMissing : false;
                if (e.FilePattern != null)
                {
                    Directory.CreateDirectory(dst);
                    var code = await CopyBookmarksAsync(src, dst, prog, report, ct).ConfigureAwait(false);
                    if (code >= 8) prog.Errors = Math.Max(prog.Errors, 1);
                    continue;
                }
                var c = await CopyAsync(src, dst, null, null, missing, prog, report, ct).ConfigureAwait(false);
                if (c >= 8) prog.Errors = Math.Max(prog.Errors, 1);
            }
            Logger.Info($"Phục hồi dữ liệu xong: {prog.Files} tệp, {FileUtil.FormatSize(prog.Bytes)}, lỗi {prog.Errors}");
            return new DataBackupResult { Files = prog.Files, Bytes = prog.Bytes, Errors = prog.Errors, Target = folder };
        }

        private static async Task<int> CopyBookmarksAsync(string src, string dst, DataProgress prog, Action<DataProgress> report, CancellationToken ct)
        {
            // /XO: không chép đè nếu tệp trên máy mới hơn.
            var args = $"\"{src.TrimEnd('\\')}\" \"{dst.TrimEnd('\\')}\" Bookmarks /S /XJ /XO /R:1 /W:1 /NP /NDL /NJH /NJS /FP /BYTES /UNICODE";
            var r = await ProcessRunner.RunAsync(Robocopy, args, (line, p) =>
            {
                if (line.IndexOf(":\\", StringComparison.Ordinal) > 0) { Interlocked.Increment(ref prog.Files); report?.Invoke(prog); }
            }, Encoding.Unicode, ct).ConfigureAwait(false);
            return r.ExitCode;
        }
    }
}
