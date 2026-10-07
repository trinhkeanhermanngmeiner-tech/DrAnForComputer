using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Principal;
using BacSiMayTinh.Core;
using Microsoft.Win32;

namespace BacSiMayTinh.Modules
{
    public class PathSpec
    {
        public string Dir;
        public string Pattern = "*";
        public bool Recursive = true;
        public int OlderThanDays;

        public PathSpec(string dir, string pattern = "*", bool recursive = true, int olderThanDays = 0)
        {
            Dir = FileUtil.Expand(dir);
            Pattern = pattern;
            Recursive = recursive;
            OlderThanDays = olderThanDays;
        }
    }

    public enum CleanKind { Files, RecycleBin, WindowsOld }

    public class CleanItem : ObservableObject
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Warning { get; set; }
        public bool HasWarning => !string.IsNullOrEmpty(Warning);
        public string[] Processes { get; set; } = new string[0];
        public List<PathSpec> Paths { get; } = new List<PathSpec>();
        public CleanKind Kind { get; set; } = CleanKind.Files;
        public bool OneClick { get; set; }
        public CleanGroup Group { get; set; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (Set(ref _isChecked, value)) Group?.Refresh(); }
        }

        private long _size;
        public long Size
        {
            get => _size;
            set { if (Set(ref _size, value)) { OnPropertyChanged(nameof(SizeText)); Group?.Refresh(); } }
        }

        private bool _scanned;
        public bool Scanned
        {
            get => _scanned;
            set { if (Set(ref _scanned, value)) OnPropertyChanged(nameof(SizeText)); }
        }

        public int FileCount { get; set; }

        private string _status;
        public string Status { get => _status; set => Set(ref _status, value); }

        public string SizeText => Scanned ? (Size > 0 ? FileUtil.FormatSize(Size) : L.T("Sạch")) : "";

        public bool IsAppRunning()
        {
            foreach (var p in Processes)
            {
                try
                {
                    var procs = Process.GetProcessesByName(p);
                    bool any = procs.Length > 0;
                    foreach (var x in procs) x.Dispose();
                    if (any) return true;
                }
                catch { }
            }
            return false;
        }
    }

    public class CleanGroup : ObservableObject
    {
        public string Name { get; set; }
        public string Glyph { get; set; }
        public ObservableCollection<CleanItem> Items { get; } = new ObservableCollection<CleanItem>();

        public long TotalChecked => Items.Where(i => i.IsChecked).Sum(i => i.Size);
        public string TotalText => Items.Any(i => i.Scanned) ? FileUtil.FormatSize(TotalChecked) : "";

        private bool _suppress;
        public bool? AllChecked
        {
            get
            {
                if (Items.All(i => i.IsChecked)) return true;
                if (Items.All(i => !i.IsChecked)) return false;
                return null;
            }
            set
            {
                _suppress = true;
                bool v = value ?? false;
                foreach (var i in Items) i.IsChecked = v;
                _suppress = false;
                Refresh();
            }
        }

        public void Refresh()
        {
            if (_suppress) return;
            OnPropertyChanged(nameof(TotalText));
            OnPropertyChanged(nameof(AllChecked));
        }
    }

    public static class CleanerCatalog
    {
        private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        private static string Win => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        private static string ProgData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        public static List<CleanGroup> Build()
        {
            var groups = new List<CleanGroup> { BuildSystem(), BuildBrowsers(), BuildApps() };
            foreach (var g in groups)
                foreach (var i in g.Items) i.Group = g;
            return groups.Where(g => g.Items.Count > 0).ToList();
        }

        private static CleanItem Item(string id, string name, string desc, bool check, bool oneClick, params PathSpec[] paths)
        {
            var it = new CleanItem { Id = id, Name = name, Description = desc, OneClick = oneClick };
            it.Paths.AddRange(paths);
            it.IsChecked = check;
            return it;
        }

        private static CleanGroup BuildSystem()
        {
            var g = new CleanGroup { Name = L.T("Hệ thống Windows"), Glyph = "" };
            g.Items.Add(Item("user_temp", L.T("Tệp tạm của người dùng"), L.T("Tệp tạm do các phần mềm tạo ra (cũ hơn 1 ngày)."), true, true,
                new PathSpec(Path.GetTempPath(), olderThanDays: 1)));
            g.Items.Add(Item("win_temp", L.T("Tệp tạm của Windows"), L.T("Thư mục Windows\\Temp (cũ hơn 1 ngày)."), true, true,
                new PathSpec(Path.Combine(Win, "Temp"), olderThanDays: 1)));
            g.Items.Add(Item("wu_cache", L.T("Bộ đệm Windows Update"), L.T("Các gói cập nhật đã tải về và cài xong."), true, false,
                new PathSpec(Path.Combine(Win, @"SoftwareDistribution\Download"))));
            g.Items.Add(Item("do_cache", L.T("Bộ đệm Delivery Optimization"), L.T("Tệp cập nhật được lưu để chia sẻ cho máy khác."), true, true,
                new PathSpec(Path.Combine(Win, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"))));
            g.Items.Add(Item("wer", L.T("Báo cáo lỗi Windows"), L.T("Báo cáo sự cố đã gửi hoặc chờ gửi về Microsoft."), true, true,
                new PathSpec(Path.Combine(ProgData, @"Microsoft\Windows\WER\ReportArchive")),
                new PathSpec(Path.Combine(ProgData, @"Microsoft\Windows\WER\ReportQueue")),
                new PathSpec(Path.Combine(ProgData, @"Microsoft\Windows\WER\Temp")),
                new PathSpec(Path.Combine(Local, @"Microsoft\Windows\WER"))));
            g.Items.Add(Item("dumps", L.T("Tệp kết xuất khi lỗi (dump)"), L.T("Tệp ghi bộ nhớ khi máy hoặc phần mềm bị lỗi."), true, true,
                new PathSpec(Path.Combine(Win, "Minidump")),
                new PathSpec(Win, "MEMORY.DMP", false),
                new PathSpec(Path.Combine(Win, "LiveKernelReports"), "*.dmp"),
                new PathSpec(Path.Combine(Local, "CrashDumps"))));
            g.Items.Add(Item("logs", L.T("Nhật ký hệ thống cũ"), L.T("Tệp log của Windows cũ hơn 7 ngày."), true, true,
                new PathSpec(Path.Combine(Win, "Logs"), "*.log", true, 7),
                new PathSpec(Path.Combine(Win, "Logs"), "*.etl", true, 7),
                new PathSpec(Path.Combine(Win, @"Logs\CBS"), "*.cab", false, 7)));
            g.Items.Add(new CleanItem
            {
                Id = "recycle", Name = L.T("Thùng rác"), Description = L.T("Các tệp bạn đã xóa vào Thùng rác."),
                Kind = CleanKind.RecycleBin, IsChecked = true
            });
            var thumbs = Item("thumbs", L.T("Bộ đệm ảnh thu nhỏ"), L.T("Ảnh xem trước của tệp. Windows sẽ tự tạo lại khi cần."), false, false,
                new PathSpec(Path.Combine(Local, @"Microsoft\Windows\Explorer"), "thumbcache_*.db", false));
            g.Items.Add(thumbs);
            var shader = Item("shader", L.T("Bộ đệm shader DirectX/GPU"), L.T("Dữ liệu đồ họa đã biên dịch của game và ứng dụng."), false, false,
                new PathSpec(Path.Combine(Local, "D3DSCache")),
                new PathSpec(Path.Combine(Local, @"NVIDIA\DXCache")),
                new PathSpec(Path.Combine(Local, @"NVIDIA\GLCache")),
                new PathSpec(Path.Combine(Local, @"AMD\DxCache")),
                new PathSpec(Path.Combine(Local, @"AMD\DxcCache")),
                new PathSpec(Path.Combine(Local, @"AMD\VkCache")));
            shader.Warning = L.T("Game có thể hơi giật ở lần chạy đầu tiên sau khi dọn.");
            g.Items.Add(shader);
            var recent = Item("recent", L.T("Danh sách tệp mở gần đây"), L.T("Lịch sử tệp/thư mục đã mở (Quick Access, Jump List)."), false, false,
                new PathSpec(Path.Combine(Roaming, @"Microsoft\Windows\Recent"), "*.lnk", false),
                new PathSpec(Path.Combine(Roaming, @"Microsoft\Windows\Recent\AutomaticDestinations")),
                new PathSpec(Path.Combine(Roaming, @"Microsoft\Windows\Recent\CustomDestinations")));
            g.Items.Add(recent);

            var winOld = Path.Combine(FileUtil.SystemDrive, "Windows.old");
            if (Directory.Exists(winOld))
            {
                var it = new CleanItem
                {
                    Id = "winold", Name = L.T("Bản Windows cũ (Windows.old)"), Kind = CleanKind.WindowsOld,
                    Description = L.T("Bản sao Windows trước khi nâng cấp – thường chiếm 10–30 GB."),
                    Warning = L.T("Sau khi xóa sẽ không thể quay lại phiên bản Windows trước.")
                };
                it.Paths.Add(new PathSpec(winOld));
                g.Items.Add(it);
            }
            return g;
        }

        private class BrowserDef
        {
            public string Name, Process, UserData, LocalCacheRoot;
            public bool SingleProfile;
        }

        private static CleanGroup BuildBrowsers()
        {
            var g = new CleanGroup { Name = L.T("Trình duyệt web"), Glyph = "" };
            var defs = new[]
            {
                new BrowserDef { Name = "Google Chrome", Process = "chrome", UserData = Path.Combine(Local, @"Google\Chrome\User Data") },
                new BrowserDef { Name = "Microsoft Edge", Process = "msedge", UserData = Path.Combine(Local, @"Microsoft\Edge\User Data") },
                new BrowserDef { Name = L.T("Cốc Cốc"), Process = "browser", UserData = Path.Combine(Local, @"CocCoc\Browser\User Data") },
                new BrowserDef { Name = "Brave", Process = "brave", UserData = Path.Combine(Local, @"BraveSoftware\Brave-Browser\User Data") },
                new BrowserDef { Name = "Vivaldi", Process = "vivaldi", UserData = Path.Combine(Local, @"Vivaldi\User Data") },
                new BrowserDef { Name = "Opera", Process = "opera", UserData = Path.Combine(Roaming, @"Opera Software\Opera Stable"),
                    LocalCacheRoot = Path.Combine(Local, @"Opera Software\Opera Stable"), SingleProfile = true },
                new BrowserDef { Name = "Opera GX", Process = "opera", UserData = Path.Combine(Roaming, @"Opera Software\Opera GX Stable"),
                    LocalCacheRoot = Path.Combine(Local, @"Opera Software\Opera GX Stable"), SingleProfile = true },
            };

            foreach (var b in defs)
            {
                if (!Directory.Exists(b.UserData)) continue;
                var profiles = new List<string>();
                if (b.SingleProfile) profiles.Add(b.UserData);
                else
                {
                    try
                    {
                        profiles.AddRange(Directory.GetDirectories(b.UserData)
                            .Where(d => File.Exists(Path.Combine(d, "Preferences"))));
                    }
                    catch { }
                }
                if (profiles.Count == 0) continue;
                string id = b.Name.ToLowerInvariant().Replace(" ", "");

                var cache = new CleanItem
                {
                    Id = id + "_cache", Name = b.Name + L.T(" – Bộ nhớ đệm"),
                    Description = L.T("Ảnh, script đã tải về khi lướt web. Không ảnh hưởng mật khẩu hay dấu trang."),
                    Processes = new[] { b.Process }, OneClick = true
                };
                foreach (var p in profiles)
                    foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache" })
                        cache.Paths.Add(new PathSpec(Path.Combine(p, sub)));
                foreach (var sub in new[] { "ShaderCache", "GrShaderCache", "GraphiteDawnCache" })
                    cache.Paths.Add(new PathSpec(Path.Combine(b.UserData, sub)));
                if (b.LocalCacheRoot != null) cache.Paths.Add(new PathSpec(Path.Combine(b.LocalCacheRoot, "Cache")));
                cache.IsChecked = true;
                g.Items.Add(cache);

                var hist = new CleanItem
                {
                    Id = id + "_history", Name = b.Name + L.T(" – Lịch sử duyệt web"),
                    Description = L.T("Danh sách trang đã truy cập. Dấu trang và mật khẩu được giữ nguyên."),
                    Processes = new[] { b.Process }, Warning = L.T("Hãy đóng trình duyệt trước khi dọn.")
                };
                foreach (var p in profiles)
                    foreach (var f in new[] { "History", "History-journal", "Visited Links", "Top Sites", "Top Sites-journal", "Shortcuts", "Shortcuts-journal" })
                        hist.Paths.Add(new PathSpec(p, f, false));
                g.Items.Add(hist);

                var cookies = new CleanItem
                {
                    Id = id + "_cookies", Name = b.Name + L.T(" – Cookie"),
                    Description = L.T("Dữ liệu đăng nhập của các trang web."),
                    Processes = new[] { b.Process }, Warning = L.T("Bạn sẽ bị đăng xuất khỏi các trang web (Facebook, Gmail…).")
                };
                foreach (var p in profiles)
                {
                    cookies.Paths.Add(new PathSpec(Path.Combine(p, "Network"), "Cookies*", false));
                    cookies.Paths.Add(new PathSpec(p, "Cookies*", false));
                }
                g.Items.Add(cookies);
            }

            // Firefox
            var ffLocal = Path.Combine(Local, @"Mozilla\Firefox\Profiles");
            var ffRoam = Path.Combine(Roaming, @"Mozilla\Firefox\Profiles");
            if (Directory.Exists(ffLocal) || Directory.Exists(ffRoam))
            {
                var cache = new CleanItem
                {
                    Id = "firefox_cache", Name = L.T("Firefox – Bộ nhớ đệm"), Processes = new[] { "firefox" }, OneClick = true,
                    Description = L.T("Ảnh, script đã tải về khi lướt web.")
                };
                try
                {
                    if (Directory.Exists(ffLocal))
                        foreach (var p in Directory.GetDirectories(ffLocal))
                            foreach (var sub in new[] { "cache2", "startupCache", "thumbnails" })
                                cache.Paths.Add(new PathSpec(Path.Combine(p, sub)));
                }
                catch { }
                cache.IsChecked = true;
                g.Items.Add(cache);

                if (Directory.Exists(ffRoam))
                {
                    var cookies = new CleanItem
                    {
                        Id = "firefox_cookies", Name = L.T("Firefox – Cookie"), Processes = new[] { "firefox" },
                        Description = L.T("Dữ liệu đăng nhập của các trang web."),
                        Warning = L.T("Bạn sẽ bị đăng xuất khỏi các trang web.")
                    };
                    try
                    {
                        foreach (var p in Directory.GetDirectories(ffRoam))
                            cookies.Paths.Add(new PathSpec(p, "cookies.sqlite*", false));
                    }
                    catch { }
                    g.Items.Add(cookies);
                }
            }
            return g;
        }

        private static CleanGroup BuildApps()
        {
            var g = new CleanGroup { Name = L.T("Ứng dụng"), Glyph = "" };
            void Electron(string id, string name, string proc, string root, params string[] extra)
            {
                if (!Directory.Exists(root)) return;
                var it = new CleanItem
                {
                    Id = id, Name = name + L.T(" – Bộ nhớ đệm"), Processes = new[] { proc }, OneClick = true,
                    Description = L.T("Tệp đệm tạm của ") + name + L.T(". Tin nhắn và tệp của bạn không bị ảnh hưởng.")
                };
                foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache" }.Concat(extra))
                    it.Paths.Add(new PathSpec(Path.Combine(root, sub)));
                it.IsChecked = true;
                g.Items.Add(it);
            }

            Electron("zalo", "Zalo", "Zalo", Path.Combine(Roaming, "ZaloData"));
            Electron("discord", "Discord", "Discord", Path.Combine(Roaming, "discord"));
            Electron("teams", L.T("Microsoft Teams (cổ điển)"), "Teams", Path.Combine(Roaming, @"Microsoft\Teams"), "tmp");
            Electron("vscode", "Visual Studio Code", "Code", Path.Combine(Roaming, "Code"), "CachedData", "CachedExtensionVSIXs");
            Electron("slack", "Slack", "slack", Path.Combine(Roaming, "Slack"));
            Electron("skype", "Skype", "Skype", Path.Combine(Roaming, @"Microsoft\Skype for Desktop"));

            var spotify = Path.Combine(Local, @"Spotify\Data");
            if (Directory.Exists(spotify))
            {
                var it = Item("spotify", L.T("Spotify – Bộ nhớ đệm nhạc"), L.T("Nhạc đã nghe được lưu tạm (không phải nhạc tải về ngoại tuyến)."), true, true,
                    new PathSpec(spotify));
                it.Processes = new[] { "Spotify" };
                g.Items.Add(it);
            }

            var steamHtml = Path.Combine(Local, @"Steam\htmlcache");
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var steamHttp = Path.Combine(pf86, @"Steam\appcache\httpcache");
            if (Directory.Exists(steamHtml) || Directory.Exists(steamHttp))
            {
                var it = Item("steam", L.T("Steam – Bộ nhớ đệm web"), L.T("Bộ đệm trang cửa hàng và thư viện Steam."), true, true,
                    new PathSpec(steamHtml), new PathSpec(steamHttp));
                it.Processes = new[] { "steam" };
                g.Items.Add(it);
            }
            return g;
        }
    }

    public static class CleanerEngine
    {
        private static bool Match(FileInfo f, PathSpec s, DateTime now)
        {
            if (s.OlderThanDays <= 0) return true;
            try { return (now - f.LastWriteTime).TotalDays >= s.OlderThanDays; } catch { return false; }
        }

        private static IEnumerable<string> RecycleDirs()
        {
            string sid;
            try { using (var id = WindowsIdentity.GetCurrent()) sid = id.User?.Value; } catch { yield break; }
            if (sid == null) yield break;
            foreach (var d in DriveInfo.GetDrives())
            {
                bool ok;
                try { ok = d.DriveType == DriveType.Fixed && d.IsReady; } catch { ok = false; }
                if (!ok) continue;
                var p = Path.Combine(d.RootDirectory.FullName, "$Recycle.Bin", sid);
                if (Directory.Exists(p)) yield return p;
            }
        }

        public static void Scan(CleanItem item, CancellationToken ct = default)
        {
            long size = 0; int count = 0;
            var now = DateTime.Now;
            if (item.Kind == CleanKind.RecycleBin)
            {
                foreach (var dir in RecycleDirs())
                    foreach (var f in FileUtil.EnumerateFiles(dir))
                    {
                        if (ct.IsCancellationRequested) return;
                        if (f.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                        try { size += f.Length; if (f.Name.StartsWith("$R")) count++; } catch { }
                    }
            }
            else
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var spec in item.Paths)
                {
                    if (!Directory.Exists(spec.Dir)) continue;
                    foreach (var f in FileUtil.EnumerateFiles(spec.Dir, spec.Pattern, spec.Recursive))
                    {
                        if (ct.IsCancellationRequested) return;
                        if (!Match(f, spec, now) || !seen.Add(f.FullName)) continue;
                        try { size += f.Length; count++; } catch { }
                    }
                }
            }
            item.FileCount = count;
            item.Size = size;
            item.Scanned = true;
        }

        /// <summary>Dọn một mục, trả về số byte giải phóng được.</summary>
        public static long Clean(CleanItem item, CancellationToken ct = default)
        {
            long freed = 0;
            var now = DateTime.Now;
            try
            {
                switch (item.Kind)
                {
                    case CleanKind.RecycleBin:
                        Scan(item);
                        freed = item.Size;
                        Native.SHEmptyRecycleBin(IntPtr.Zero, null, Native.SHERB_NOCONFIRMATION | Native.SHERB_NOPROGRESSUI | Native.SHERB_NOSOUND);
                        break;

                    case CleanKind.WindowsOld:
                        freed = CleanWindowsOld(item);
                        break;

                    default:
                        foreach (var spec in item.Paths)
                        {
                            if (!Directory.Exists(spec.Dir)) continue;
                            foreach (var f in FileUtil.EnumerateFiles(spec.Dir, spec.Pattern, spec.Recursive).ToList())
                            {
                                if (ct.IsCancellationRequested) break;
                                if (!Match(f, spec, now)) continue;
                                long len = 0;
                                try { len = f.Length; } catch { }
                                if (FileUtil.TryDeleteFile(f)) freed += len;
                            }
                            if (spec.Recursive && spec.Pattern == "*") FileUtil.DeleteEmptySubdirs(spec.Dir);
                        }
                        break;
                }
            }
            catch (Exception ex) { Logger.Error("Dọn " + item.Name, ex); }
            Logger.Info($"Dọn '{item.Name}': giải phóng {FileUtil.FormatSize(freed)}");
            Scan(item);
            return freed;
        }

        private static long CleanWindowsOld(CleanItem item)
        {
            var dir = item.Paths[0].Dir;
            long before = FileUtil.DirectorySize(dir);
            // Dùng Disk Cleanup (cleanmgr) – cách chính thức, tự xử lý quyền sở hữu tệp.
            const string key = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches\Previous Installations";
            try
            {
                RegistryHelper.SetValue(key, "StateFlags0099", 2, RegistryValueKind.DWord);
                var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cleanmgr.exe"), "/sagerun:99") { UseShellExecute = false });
                p?.WaitForExit(60 * 60 * 1000);
            }
            catch (Exception ex) { Logger.Error("cleanmgr Windows.old", ex); }
            finally { RegistryHelper.DeleteValue(key, "StateFlags0099"); }
            long after = Directory.Exists(dir) ? FileUtil.DirectorySize(dir) : 0;
            return Math.Max(0, before - after);
        }

        /// <summary>Dọn nhanh các mục an toàn (dùng cho nút "Tối ưu ngay" và dọn tự động theo lịch).</summary>
        public static long QuickClean()
        {
            long total = 0;
            foreach (var g in CleanerCatalog.Build())
                foreach (var i in g.Items.Where(i => i.OneClick && i.IsChecked))
                    total += Clean(i);
            return total;
        }

        /// <summary>Ước lượng dung lượng rác (các mục mặc định được chọn).</summary>
        public static long Estimate()
        {
            long total = 0;
            foreach (var g in CleanerCatalog.Build())
                foreach (var i in g.Items.Where(i => i.IsChecked))
                {
                    Scan(i);
                    total += i.Size;
                }
            return total;
        }
    }
}
