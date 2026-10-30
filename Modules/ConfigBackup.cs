using System.Net.NetworkInformation;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using BacSiMayTinh.Core;
using Microsoft.Win32;

namespace BacSiMayTinh.Modules
{
    // =====================================================================
    //  Sao lưu & phục hồi cấu hình Windows (tinh chỉnh, dịch vụ, khởi động,
    //  menu chuột phải, gói điện năng, hosts, DNS, Wi-Fi, driver).
    //  Mỗi bản sao lưu là một thư mục độc lập có manifest.json – chép sang
    //  ổ khác/USB vẫn phục hồi được sau khi cài lại Windows.
    // =====================================================================

    [DataContract]
    public class StartupState
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public string Kind { get; set; }        // run | folder | task
        [DataMember] public string SourceKey { get; set; }
        [DataMember] public string ApprovedKey { get; set; }
        [DataMember] public string ValueName { get; set; }
        [DataMember] public string Command { get; set; }
        [DataMember] public string TaskPath { get; set; }
        [DataMember] public bool Enabled { get; set; }
    }

    [DataContract]
    public class ContextState
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public bool IsHandler { get; set; }
        [DataMember] public string RegPath { get; set; }
        [DataMember] public string Clsid { get; set; }
        [DataMember] public bool Enabled { get; set; }
    }

    [DataContract]
    public class DnsState
    {
        [DataMember] public string Adapter { get; set; }
        [DataMember] public string Id { get; set; }
        [DataMember] public string Servers { get; set; }     // rỗng = tự động (DHCP)
    }

    [DataContract]
    public class KeyState
    {
        [DataMember] public string Path { get; set; }
        [DataMember] public bool Existed { get; set; }
    }

    [DataContract]
    public class BackupManifest
    {
        [DataMember] public int Format { get; set; } = 1;
        [DataMember] public string AppVersion { get; set; }
        [DataMember] public string Machine { get; set; }
        [DataMember] public string User { get; set; }
        [DataMember] public int WindowsBuild { get; set; }
        [DataMember] public string Created { get; set; }
        [DataMember] public string Kind { get; set; }        // manual | auto | before-change | before-restore | before-driver
        [DataMember] public string Note { get; set; }
        [DataMember] public List<string> Components { get; set; } = new List<string>();
        [DataMember] public List<RegBackup> Registry { get; set; } = new List<RegBackup>();
        [DataMember] public List<KeyState> Keys { get; set; } = new List<KeyState>();
        [DataMember] public List<RegBackup> ServiceValues { get; set; } = new List<RegBackup>();
        [DataMember] public List<StartupState> Startup { get; set; } = new List<StartupState>();
        [DataMember] public List<ContextState> ContextMenu { get; set; } = new List<ContextState>();
        [DataMember] public string PowerScheme { get; set; }
        [DataMember] public bool? Hibernate { get; set; }
        [DataMember] public List<DnsState> Dns { get; set; } = new List<DnsState>();
        [DataMember] public int WifiCount { get; set; }
        [DataMember] public int DriverCount { get; set; }
        [DataMember] public bool RestorePoint { get; set; }
        [DataMember] public long Size { get; set; }

        [OnDeserialized]
        private void Fix(StreamingContext c)
        {
            if (Components == null) Components = new List<string>();
            if (Registry == null) Registry = new List<RegBackup>();
            if (Keys == null) Keys = new List<KeyState>();
            if (ServiceValues == null) ServiceValues = new List<RegBackup>();
            if (Startup == null) Startup = new List<StartupState>();
            if (ContextMenu == null) ContextMenu = new List<ContextState>();
            if (Dns == null) Dns = new List<DnsState>();
        }

        public DateTime CreatedTime => DateTime.TryParse(Created, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : DateTime.MinValue;
    }

    /// <summary>Một bản sao lưu cấu hình hiển thị trong danh sách.</summary>
    public class BackupSet : ObservableObject
    {
        public string Folder { get; set; }
        public BackupManifest Manifest { get; set; }
        public string Title => BackupSetText.KindText(Manifest.Kind) + (string.IsNullOrEmpty(Manifest.Note) ? "" : " – " + Manifest.Note);
        public string CreatedText => Manifest.CreatedTime == DateTime.MinValue ? "" : Manifest.CreatedTime.ToString("dd/MM/yyyy HH:mm");
        public string ComponentsText => string.Join(" · ", Manifest.Components.Select(BackupSetText.ComponentName));
        public string Detail =>
            CreatedText + "  ·  " + FileUtil.FormatSize(Manifest.Size) + "  ·  " + Manifest.Machine +
            (IsOtherMachine ? L.T(" (máy khác)") : "") + "  ·  " + Folder;
        public bool IsOtherMachine => !string.Equals(Manifest.Machine, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        public string Glyph => Manifest.Kind == "manual" ? "" : "";
    }

    public static class BackupSetText
    {
        public static string KindText(string kind)
        {
            switch (kind)
            {
                case "manual": return L.T("Sao lưu thủ công");
                case "auto": return L.T("Sao lưu tự động theo lịch");
                case "before-change": return L.T("Tự sao lưu trước khi tối ưu");
                case "before-restore": return L.T("Tự sao lưu trước khi phục hồi");
                case "before-driver": return L.T("Tự sao lưu trước khi cập nhật driver");
                default: return L.T("Bản sao lưu");
            }
        }

        public static string ComponentName(string id)
        {
            switch (id)
            {
                case "tweaks": return L.T("Tinh chỉnh Windows");
                case "services": return L.T("Dịch vụ");
                case "startup": return L.T("Ứng dụng khởi động");
                case "context": return L.T("Menu chuột phải");
                case "power": return L.T("Gói điện năng");
                case "hosts": return L.T("Tệp hosts");
                case "network": return L.T("DNS & Wi-Fi");
                case "drivers": return L.T("Driver");
                case "restorepoint": return L.T("Điểm khôi phục");
                default: return id;
            }
        }
    }

    public class BackupOptions
    {
        public HashSet<string> Components { get; set; } = new HashSet<string>(ConfigBackup.QuickComponents);
        public string Kind { get; set; } = "manual";
        public string Note { get; set; }
        public string Root { get; set; }
        public bool WifiPasswords { get; set; } = true;
    }

    public class RestoreReport
    {
        public List<string> Lines { get; } = new List<string>();
        public int Errors { get; set; }
        public bool NeedsRestart { get; set; }
        public void Ok(string s) => Lines.Add("✓ " + s);
        public void Warn(string s) => Lines.Add("! " + s);
        public void Fail(string s) { Lines.Add("✗ " + s); Errors++; }
    }

    public static class ConfigBackup
    {
        public static readonly string[] AllComponents = { "tweaks", "services", "startup", "context", "power", "hosts", "network", "drivers", "restorepoint" };
        /// <summary>Các phần sao lưu nhanh (vài giây) – dùng cho bản tự động trước khi tối ưu.</summary>
        public static readonly string[] QuickComponents = { "tweaks", "services", "startup", "context", "power", "hosts", "network" };
        public const string ManifestName = "manifest.json";

        private static readonly string[] ExtraServices = { "wuauserv", "BITS", "UsoSvc", "WSearch", "SysMain", "DiagTrack", "dmwappushservice", "Spooler", "WinDefend", "mpssvc" };

        public static string DefaultRoot => Path.Combine(AppInfo.BackupDir, "BanSaoLuu");

        public static string Root
        {
            get
            {
                var r = AppSettings.Current.BackupRoot;
                return string.IsNullOrWhiteSpace(r) ? DefaultRoot : r;
            }
        }

        public static bool RootOnSystemDrive =>
            string.Equals(Path.GetPathRoot(Path.GetFullPath(Root)), FileUtil.SystemDrive, StringComparison.OrdinalIgnoreCase);

        // ------------------------------------------------------------------
        //  Các khóa registry ngoài danh mục tinh chỉnh mà phần mềm có thể đổi
        //  (bảo mật, Windows Update) – được sao lưu cùng phần "tinh chỉnh".
        // ------------------------------------------------------------------
        public static IEnumerable<(string path, string name)> ExtraValues()
        {
            foreach (var v in SecurityCheck.ManagedValues) yield return v;
            foreach (var v in WuManager.ManagedValues) yield return v;
            yield return (@"HKCU\Control Panel\Mouse", "MouseSpeed");
        }

        // ================== TẠO BẢN SAO LƯU ==================
        public static Task<BackupSet> CreateAsync(BackupOptions o, Action<string> progress = null, CancellationToken ct = default)
            => Task.Run(() => Create(o, progress, ct));

        public static BackupSet Create(BackupOptions o, Action<string> progress = null, CancellationToken ct = default)
        {
            var root = string.IsNullOrWhiteSpace(o.Root) ? Root : o.Root;
            Directory.CreateDirectory(root);
            var stamp = DateTime.Now;
            var folder = Path.Combine(root, SafeName(Environment.MachineName) + "_" + stamp.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + o.Kind);
            Directory.CreateDirectory(folder);
            var m = new BackupManifest
            {
                AppVersion = AppInfo.Version, Machine = Environment.MachineName, User = Environment.UserName,
                WindowsBuild = AppInfo.WindowsBuild, Created = stamp.ToString("s"), Kind = o.Kind, Note = o.Note
            };
            void P(string s) { progress?.Invoke(s); }
            Logger.Info("Bắt đầu sao lưu cấu hình (" + o.Kind + "): " + string.Join(",", o.Components) + " → " + folder);

            try
            {
                if (o.Components.Contains("tweaks"))
                {
                    P(L.T("Đang sao lưu các tinh chỉnh Windows…"));
                    CaptureTweaks(m);
                    JsonFile.Save(Path.Combine(folder, "hoantac.json"), UndoStore.Current);
                    m.Components.Add("tweaks");
                }
                ct.ThrowIfCancellationRequested();
                if (o.Components.Contains("services"))
                {
                    P(L.T("Đang sao lưu chế độ khởi động của dịch vụ…"));
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    try { foreach (var s in ServiceCatalog.Build()) names.Add(s.Name); } catch { }
                    foreach (var s in ExtraServices) names.Add(s);
                    foreach (var n in names)
                    {
                        var key = @"HKLM\SYSTEM\CurrentControlSet\Services\" + n;
                        if (!RegistryHelper.KeyExists(key)) continue;
                        m.ServiceValues.Add(UndoStore.Capture(key, "Start"));
                        m.ServiceValues.Add(UndoStore.Capture(key, "DelayedAutostart"));
                    }
                    m.Components.Add("services");
                }
                ct.ThrowIfCancellationRequested();
                if (o.Components.Contains("startup"))
                {
                    P(L.T("Đang sao lưu danh sách ứng dụng khởi động…"));
                    CaptureStartup(m, folder);
                    m.Components.Add("startup");
                }
                if (o.Components.Contains("context"))
                {
                    P(L.T("Đang sao lưu menu chuột phải…"));
                    try
                    {
                        m.ContextMenu = ContextMenuManager.GetItems().Select(i => new ContextState
                        {
                            Name = i.Name, IsHandler = i.IsHandler, RegPath = i.RegPath, Clsid = i.Clsid, Enabled = i.IsEnabled
                        }).ToList();
                    }
                    catch (Exception ex) { Logger.Warn("Sao lưu menu chuột phải: " + ex.Message); }
                    m.Components.Add("context");
                }
                ct.ThrowIfCancellationRequested();
                if (o.Components.Contains("power"))
                {
                    P(L.T("Đang sao lưu gói điện năng…"));
                    m.PowerScheme = PowerPlan.ActiveScheme();
                    if (!string.IsNullOrEmpty(m.PowerScheme))
                        ProcessRunner.CmdAsync($"powercfg /export \"{Path.Combine(folder, "power.pow")}\" {m.PowerScheme}").Wait();
                    var h = RegistryHelper.GetValue(@"HKLM\SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled");
                    if (h is int hi) m.Hibernate = hi != 0;
                    m.Components.Add("power");
                }
                if (o.Components.Contains("hosts"))
                {
                    try { File.Copy(HostsFile.PathName, Path.Combine(folder, "hosts"), true); m.Components.Add("hosts"); }
                    catch (Exception ex) { Logger.Warn("Sao lưu hosts: " + ex.Message); }
                }
                ct.ThrowIfCancellationRequested();
                if (o.Components.Contains("network"))
                {
                    P(L.T("Đang sao lưu DNS và hồ sơ Wi-Fi…"));
                    m.Dns = CaptureDns();
                    if (o.WifiPasswords)
                    {
                        var wdir = Path.Combine(folder, "WiFi");
                        Directory.CreateDirectory(wdir);
                        ProcessRunner.CmdAsync($"netsh wlan export profile key=clear folder=\"{wdir}\"").Wait();
                        m.WifiCount = Directory.GetFiles(wdir, "*.xml").Length;
                        if (m.WifiCount == 0) TryDeleteDir(wdir);
                    }
                    m.Components.Add("network");
                }
                ct.ThrowIfCancellationRequested();
                if (o.Components.Contains("drivers"))
                {
                    P(L.T("Đang sao lưu driver… (có thể mất vài phút)"));
                    var ddir = Path.Combine(folder, "Drivers");
                    Directory.CreateDirectory(ddir);
                    int n = 0;
                    ProcessRunner.CmdAsync($"pnputil /export-driver * \"{ddir}\"", (line, pr) =>
                    {
                        if (line.IndexOf(".inf", StringComparison.OrdinalIgnoreCase) >= 0) { n++; P(L.T("Đang sao lưu driver: ") + line.Trim()); }
                    }, ct).Wait();
                    try { JsonFile.Save(Path.Combine(folder, "drivers.json"), new DriverInventory { Items = DriverManager.List(true) }); } catch { }
                    m.DriverCount = Directory.Exists(ddir) ? Directory.GetFiles(ddir, "*.inf", SearchOption.AllDirectories).Length : 0;
                    m.Components.Add("drivers");
                }
                ct.ThrowIfCancellationRequested();
                if (o.Components.Contains("restorepoint"))
                {
                    P(L.T("Đang tạo điểm khôi phục hệ thống…"));
                    m.RestorePoint = RestorePointService.Create(AppInfo.Name + " - " + BackupSetText.KindText(o.Kind) + " " + stamp.ToString("dd/MM/yyyy HH:mm"));
                    if (m.RestorePoint) m.Components.Add("restorepoint");
                }
            }
            catch (OperationCanceledException)
            {
                TryDeleteDir(folder);
                throw;
            }
            catch (AggregateException ae) when (ae.InnerException is OperationCanceledException)
            {
                TryDeleteDir(folder);
                throw ae.InnerException;
            }

            m.Size = FileUtil.DirectorySize(folder) + 4096;
            JsonFile.Save(Path.Combine(folder, ManifestName), m);   // manifest ghi sau cùng = bản sao lưu hoàn chỉnh
            Logger.Info("Sao lưu cấu hình xong: " + folder + " (" + FileUtil.FormatSize(m.Size) + ")");
            if (o.Kind != "manual") ApplyRetention(root, o.Kind);
            RememberRoot(root);
            return new BackupSet { Folder = folder, Manifest = m };
        }

        private static void CaptureTweaks(BackupManifest m)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string path, string name)
            {
                if (seen.Add(path + "|" + name)) m.Registry.Add(UndoStore.Capture(path, name));
            }
            foreach (var t in TweakCatalog.Build())
            {
                foreach (var s in t.Sets) Add(s.Path, s.Name);
                if (t.RevertDeleteKey != null && !m.Keys.Any(k => string.Equals(k.Path, t.RevertDeleteKey, StringComparison.OrdinalIgnoreCase)))
                    m.Keys.Add(new KeyState { Path = t.RevertDeleteKey, Existed = RegistryHelper.KeyExists(t.RevertDeleteKey) });
            }
            foreach (var (path, name) in ExtraValues()) Add(path, name);
        }

        private static void CaptureStartup(BackupManifest m, string folder)
        {
            List<StartupItem> items;
            try { items = StartupManager.GetItems(); }
            catch (Exception ex) { Logger.Warn("Sao lưu khởi động: " + ex.Message); return; }
            var lnkDir = Path.Combine(folder, "Startup");
            foreach (var i in items)
            {
                var s = new StartupState
                {
                    Name = i.Name, Enabled = i.IsEnabled, SourceKey = i.SourceKey, ApprovedKey = i.ApprovedKey,
                    ValueName = i.ValueName, Command = i.Command, TaskPath = i.TaskPath,
                    Kind = i.IsTask ? "task" : i.IsFolder ? "folder" : "run"
                };
                if (i.IsFolder)
                {
                    try
                    {
                        var src = Path.Combine(i.SourceKey, i.ValueName);
                        if (File.Exists(src))
                        {
                            var sub = Path.Combine(lnkDir, i.ApprovedKey.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase) ? "AllUsers" : "User");
                            Directory.CreateDirectory(sub);
                            File.Copy(src, Path.Combine(sub, i.ValueName), true);
                        }
                    }
                    catch { }
                }
                m.Startup.Add(s);
            }
        }

        private static List<DnsState> CaptureDns()
        {
            var list = new List<DnsState>();
            try
            {
                foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (n.NetworkInterfaceType == NetworkInterfaceType.Loopback || n.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    var ns = RegistryHelper.GetValue(@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + n.Id, "NameServer") as string;
                    if (n.OperationalStatus != OperationalStatus.Up && string.IsNullOrWhiteSpace(ns)) continue;
                    list.Add(new DnsState { Adapter = n.Name, Id = n.Id, Servers = (ns ?? "").Replace(' ', ',').Trim(',') });
                }
            }
            catch (Exception ex) { Logger.Warn("Đọc DNS: " + ex.Message); }
            return list;
        }

        // ================== DANH SÁCH ==================
        public static List<BackupSet> List()
        {
            var roots = new List<string> { Root, DefaultRoot };
            roots.AddRange(AppSettings.Current.BackupRoots ?? new List<string>());
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<BackupSet>();
            foreach (var r in roots)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(r) || !seen.Add(Path.GetFullPath(r).TrimEnd('\\'))) continue;
                    if (!Directory.Exists(r)) continue;
                    foreach (var d in Directory.GetDirectories(r))
                    {
                        var s = Load(d);
                        if (s != null) list.Add(s);
                    }
                }
                catch (Exception ex) { Logger.Warn("Đọc thư mục sao lưu " + r + ": " + ex.Message); }
            }
            return list.OrderByDescending(s => s.Manifest.CreatedTime).ToList();
        }

        public static BackupSet Load(string folder)
        {
            var f = Path.Combine(folder, ManifestName);
            if (!File.Exists(f)) return null;
            var m = JsonFile.Load<BackupManifest>(f);
            if (m == null || string.IsNullOrEmpty(m.Created)) return null;
            return new BackupSet { Folder = folder, Manifest = m };
        }

        public static void RememberRoot(string root)
        {
            var s = AppSettings.Current;
            if (s.BackupRoots == null) s.BackupRoots = new List<string>();
            if (string.Equals(root, Root, StringComparison.OrdinalIgnoreCase) || string.Equals(root, DefaultRoot, StringComparison.OrdinalIgnoreCase)) return;
            if (s.BackupRoots.Any(r => string.Equals(r, root, StringComparison.OrdinalIgnoreCase))) return;
            s.BackupRoots.Add(root);
            AppSettings.SaveCurrent();
        }

        public static void Delete(BackupSet s)
        {
            Logger.Info("Xóa bản sao lưu " + s.Folder);
            if (!FileUtil.SendToRecycleBin(s.Folder)) Directory.Delete(s.Folder, true);
        }

        /// <summary>Giữ lại N bản mới nhất của mỗi loại tự động (bản thủ công không bao giờ bị xóa).</summary>
        public static void ApplyRetention(string root, string kind)
        {
            int keep = Math.Max(1, AppSettings.Current.AutoBackupKeep);
            try
            {
                var old = Directory.GetDirectories(root).Select(Load).Where(s => s != null && s.Manifest.Kind == kind)
                    .OrderByDescending(s => s.Manifest.CreatedTime).Skip(keep).ToList();
                foreach (var s in old)
                {
                    try { Directory.Delete(s.Folder, true); Logger.Info("Xóa bản sao lưu cũ " + s.Folder); }
                    catch (Exception ex) { Logger.Warn("Xóa bản sao lưu cũ: " + ex.Message); }
                }
            }
            catch { }
        }

        // ================== TỰ ĐỘNG ==================
        private static bool _sessionSnapshot;
        public static bool SessionSnapshotDone => _sessionSnapshot;

        /// <summary>Tự sao lưu nhanh một lần mỗi phiên trước khi phần mềm thay đổi hệ thống.</summary>
        public static async Task EnsureSessionSnapshotAsync(string kind = "before-change")
        {
            if (_sessionSnapshot || !AppSettings.Current.AutoBackupBeforeChange) return;
            _sessionSnapshot = true;
            try { await CreateAsync(new BackupOptions { Kind = kind, WifiPasswords = false }).ConfigureAwait(false); }
            catch (Exception ex) { Logger.Error("Tự sao lưu trước khi thay đổi", ex); }
        }

        /// <summary>Chạy từ Task Scheduler (--autobackup).</summary>
        public static void RunScheduled()
        {
            var s = AppSettings.Current;
            var comps = new HashSet<string>(QuickComponents);
            if (s.AutoBackupDrivers) comps.Add("drivers");
            try
            {
                var set = Create(new BackupOptions { Kind = "auto", Components = comps });
                s.LastAutoBackup = DateTime.Now.ToString("s");
                AppSettings.SaveCurrent();
                Logger.Info("Sao lưu tự động theo lịch xong: " + set.Folder);
            }
            catch (Exception ex) { Logger.Error("Sao lưu tự động theo lịch", ex); }

            if (s.AutoBackupData && !string.IsNullOrWhiteSpace(s.DataBackupDest))
            {
                try
                {
                    var r = DataBackup.RunAsync(DataBackup.SelectedSources(), s.DataBackupDest, null, CancellationToken.None).Result;
                    Logger.Info("Sao lưu dữ liệu cá nhân theo lịch: " + r.Files + " tệp, " + FileUtil.FormatSize(r.Bytes) + ", lỗi " + r.Errors);
                }
                catch (Exception ex) { Logger.Error("Sao lưu dữ liệu theo lịch", ex); }
            }
        }

        public static BackupSet Latest() => List().FirstOrDefault();

        // ================== PHỤC HỒI ==================
        public static Task<RestoreReport> RestoreAsync(BackupSet set, HashSet<string> comps, Action<string> progress = null)
            => Task.Run(() => Restore(set, comps, progress));

        public static RestoreReport Restore(BackupSet set, HashSet<string> comps, Action<string> progress = null)
        {
            var m = set.Manifest;
            var r = new RestoreReport();
            void P(string s) { progress?.Invoke(s); }
            Logger.Info("Phục hồi từ " + set.Folder + ": " + string.Join(",", comps));

            // Sao lưu trạng thái hiện tại trước → việc phục hồi cũng có thể quay lại.
            if (AppSettings.Current.AutoBackupBeforeChange && !comps.SetEquals(new[] { "drivers" }))
            {
                P(L.T("Đang sao lưu trạng thái hiện tại (để có thể quay lại)…"));
                try { Create(new BackupOptions { Kind = "before-restore", WifiPasswords = false }); r.Ok(L.T("Đã sao lưu trạng thái hiện tại trước khi phục hồi")); }
                catch (Exception ex) { r.Warn(L.T("Không sao lưu được trạng thái hiện tại: ") + ex.Message); }
            }

            if (comps.Contains("tweaks") && m.Components.Contains("tweaks"))
            {
                P(L.T("Đang phục hồi tinh chỉnh Windows…"));
                int ok = 0, fail = 0;
                bool mouseChanged = false;
                foreach (var b in m.Registry)
                {
                    try
                    {
                        var cur = RegistryHelper.GetKind(b.Path, b.Name);
                        if (!b.Existed && cur == null) continue;
                        if (b.Existed && cur != null && cur.Value.ToString() == b.Kind &&
                            RegistryHelper.Encode(RegistryHelper.GetValue(b.Path, b.Name), cur.Value) == b.Data) continue; // đã giống
                        if (b.Path.EndsWith(@"Control Panel\Mouse", StringComparison.OrdinalIgnoreCase)) mouseChanged = true;
                        UndoStore.Restore(b);
                        ok++;
                    }
                    catch (Exception ex) { fail++; Logger.Warn("Phục hồi " + b.Path + "\\" + b.Name + ": " + ex.Message); }
                }
                foreach (var k in m.Keys)
                    if (!k.Existed && RegistryHelper.KeyExists(k.Path)) RegistryHelper.DeleteKeyTree(k.Path);
                var undoFile = Path.Combine(set.Folder, "hoantac.json");
                if (File.Exists(undoFile))
                {
                    try
                    {
                        // Dữ liệu hoàn tác phải khớp với registry vừa phục hồi.
                        var saved = JsonFile.Load<UndoStore>(undoFile);
                        var cur = UndoStore.Current;
                        cur.Tweaks = saved.Tweaks;
                        cur.Misc = saved.Misc;
                        UndoStore.SaveCurrent();
                    }
                    catch { }
                }
                if (mouseChanged)
                    try { MouseAccel.Set(!string.Equals(RegistryHelper.GetValue(@"HKCU\Control Panel\Mouse", "MouseSpeed") as string, "0")); } catch { }
                if (m.Hibernate.HasValue)
                {
                    var h = RegistryHelper.GetValue(@"HKLM\SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled");
                    bool now = h is int hi && hi != 0;
                    if (now != m.Hibernate.Value) ProcessRunner.CmdAsync("powercfg /h " + (m.Hibernate.Value ? "on" : "off")).Wait();
                }
                if (fail == 0) r.Ok(ok == 0 ? L.T("Tinh chỉnh Windows: không có gì thay đổi so với bản sao lưu") : L.F("Tinh chỉnh Windows: phục hồi {0} giá trị", ok));
                else r.Fail(L.F("Tinh chỉnh Windows: {0} giá trị phục hồi, {1} lỗi", ok, fail));
                if (ok > 0) r.NeedsRestart = true;
            }

            if (comps.Contains("services") && m.Components.Contains("services"))
            {
                P(L.T("Đang phục hồi dịch vụ…"));
                int ok = 0, fail = 0;
                foreach (var b in m.ServiceValues)
                {
                    if (!RegistryHelper.KeyExists(b.Path)) continue;
                    try
                    {
                        if (!b.Existed && !RegistryHelper.ValueExists(b.Path, b.Name)) continue;
                        if (b.Name == "Start" && !b.Existed) continue;
                        if (b.Existed && RegistryHelper.Encode(RegistryHelper.GetValue(b.Path, b.Name), RegistryValueKind.DWord) == b.Data) continue;
                        UndoStore.Restore(b);
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        // Một số dịch vụ được Windows bảo vệ quyền ghi registry → dùng sc config.
                        if (b.Name == "Start" && b.Existed && int.TryParse(b.Data, out var st))
                        {
                            var svc = b.Path.Substring(b.Path.LastIndexOf('\\') + 1);
                            var mode = st == 2 ? "auto" : st == 3 ? "demand" : st == 4 ? "disabled" : null;
                            if (mode != null && ProcessRunner.CmdAsync($"sc config \"{svc}\" start= {mode}").Result.Ok) { ok++; continue; }
                        }
                        fail++; Logger.Warn("Phục hồi dịch vụ " + b.Path + ": " + ex.Message);
                    }
                }
                if (fail == 0) r.Ok(L.F("Dịch vụ: phục hồi {0} thiết lập", ok));
                else r.Fail(L.F("Dịch vụ: {0} thiết lập phục hồi, {1} lỗi", ok, fail));
                if (ok > 0) r.NeedsRestart = true;
            }

            if (comps.Contains("startup") && m.Components.Contains("startup"))
            {
                P(L.T("Đang phục hồi ứng dụng khởi động…"));
                RestoreStartup(set, r);
            }

            if (comps.Contains("context") && m.Components.Contains("context"))
            {
                P(L.T("Đang phục hồi menu chuột phải…"));
                int changed = 0;
                try
                {
                    foreach (var cur in ContextMenuManager.GetItems())
                    {
                        var saved = m.ContextMenu.FirstOrDefault(c => c.IsHandler == cur.IsHandler &&
                            (cur.IsHandler ? string.Equals(c.Clsid, cur.Clsid, StringComparison.OrdinalIgnoreCase)
                                           : string.Equals(c.RegPath, cur.RegPath, StringComparison.OrdinalIgnoreCase)));
                        if (saved != null && saved.Enabled != cur.IsEnabled) { ContextMenuManager.SetEnabled(cur, saved.Enabled); changed++; }
                    }
                    r.Ok(L.F("Menu chuột phải: {0} mục được đưa về như lúc sao lưu", changed));
                }
                catch (Exception ex) { r.Fail(L.T("Menu chuột phải: ") + ex.Message); }
            }

            if (comps.Contains("power") && m.Components.Contains("power") && !string.IsNullOrEmpty(m.PowerScheme))
            {
                P(L.T("Đang phục hồi gói điện năng…"));
                var g = m.PowerScheme;
                var ok = ProcessRunner.CmdAsync("powercfg /setactive " + g).Result.Ok;
                var pow = Path.Combine(set.Folder, "power.pow");
                if (!ok && File.Exists(pow))
                {
                    ProcessRunner.CmdAsync($"powercfg /import \"{pow}\" {g}").Wait();
                    ok = ProcessRunner.CmdAsync("powercfg /setactive " + g).Result.Ok;
                }
                if (ok) r.Ok(L.T("Gói điện năng đã được phục hồi"));
                else r.Fail(L.T("Không phục hồi được gói điện năng"));
            }

            if (comps.Contains("hosts") && m.Components.Contains("hosts"))
            {
                try
                {
                    var src = Path.Combine(set.Folder, "hosts");
                    var content = File.ReadAllText(src);
                    if (content != HostsFile.Read()) HostsFile.Save(content);
                    r.Ok(L.T("Tệp hosts đã được phục hồi"));
                }
                catch (Exception ex) { r.Fail(L.T("Tệp hosts: ") + ex.Message); }
            }

            if (comps.Contains("network") && m.Components.Contains("network"))
            {
                P(L.T("Đang phục hồi DNS và Wi-Fi…"));
                RestoreNetwork(set, r);
            }

            if (comps.Contains("drivers") && m.Components.Contains("drivers"))
            {
                var ddir = Path.Combine(set.Folder, "Drivers");
                if (Directory.Exists(ddir))
                {
                    P(L.T("Đang cài lại driver từ bản sao lưu… (có thể mất vài phút)"));
                    var res = DriverManager.InstallFromFolder(ddir, s => P(L.T("Đang cài driver: ") + s));
                    if (res.ok) r.Ok(L.F("Driver: đã nạp {0} gói driver", res.count));
                    else r.Fail(L.F("Driver: nạp được {0} gói, một số gói lỗi (xem nhật ký)", res.count));
                    r.NeedsRestart = true;
                }
            }

            Logger.Info("Phục hồi xong: " + string.Join(" | ", r.Lines));
            return r;
        }

        private static void RestoreStartup(BackupSet set, RestoreReport r)
        {
            var m = set.Manifest;
            int changed = 0, recreated = 0, missing = 0;
            List<StartupItem> current;
            try { current = StartupManager.GetItems(); }
            catch (Exception ex) { r.Fail(L.T("Ứng dụng khởi động: ") + ex.Message); return; }

            foreach (var s in m.Startup)
            {
                try
                {
                    var cur = current.FirstOrDefault(c => s.Kind == "task"
                        ? string.Equals(c.TaskPath, s.TaskPath, StringComparison.OrdinalIgnoreCase)
                        : string.Equals(c.SourceKey, s.SourceKey, StringComparison.OrdinalIgnoreCase) && string.Equals(c.ValueName, s.ValueName, StringComparison.OrdinalIgnoreCase));
                    if (cur != null)
                    {
                        if (cur.IsEnabled != s.Enabled) { StartupManager.SetEnabled(cur, s.Enabled); changed++; }
                        continue;
                    }
                    // Mục đã bị xóa sau khi sao lưu → tạo lại (nếu chương trình vẫn còn trên máy).
                    if (s.Kind == "run" && !string.IsNullOrEmpty(s.Command))
                    {
                        var exe = IconHelper.ExeFromCommand(s.Command);
                        if (!string.IsNullOrEmpty(exe) && !File.Exists(Environment.ExpandEnvironmentVariables(exe))) { missing++; continue; }
                        RegistryHelper.SetValue(s.SourceKey, s.ValueName, s.Command, RegistryValueKind.String);
                        if (!s.Enabled) StartupManager.SetEnabled(new StartupItem { ApprovedKey = s.ApprovedKey, ValueName = s.ValueName, Name = s.Name }, false);
                        recreated++;
                    }
                    else if (s.Kind == "folder")
                    {
                        var sub = Path.Combine(set.Folder, "Startup", s.ApprovedKey.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase) ? "AllUsers" : "User");
                        var src = Path.Combine(sub, s.ValueName);
                        if (File.Exists(src) && Directory.Exists(s.SourceKey))
                        {
                            File.Copy(src, Path.Combine(s.SourceKey, s.ValueName), true);
                            if (!s.Enabled) StartupManager.SetEnabled(new StartupItem { ApprovedKey = s.ApprovedKey, ValueName = s.ValueName, Name = s.Name }, false);
                            recreated++;
                        }
                        else missing++;
                    }
                    else missing++;
                }
                catch (Exception ex) { Logger.Warn("Phục hồi khởi động " + s.Name + ": " + ex.Message); missing++; }
            }
            r.Ok(L.F("Ứng dụng khởi động: {0} mục đổi trạng thái, {1} mục được tạo lại", changed, recreated));
            if (missing > 0) r.Warn(L.F("{0} mục khởi động không phục hồi được (phần mềm đã gỡ hoặc tác vụ đã xóa)", missing));
        }

        private static void RestoreNetwork(BackupSet set, RestoreReport r)
        {
            var m = set.Manifest;
            int dnsOk = 0;
            List<NetworkInterface> nics;
            try { nics = NetworkInterface.GetAllNetworkInterfaces().ToList(); } catch { nics = new List<NetworkInterface>(); }
            foreach (var d in m.Dns)
            {
                var nic = nics.FirstOrDefault(n => string.Equals(n.Id, d.Id, StringComparison.OrdinalIgnoreCase))
                          ?? nics.FirstOrDefault(n => string.Equals(n.Name, d.Adapter, StringComparison.OrdinalIgnoreCase));
                if (nic == null) continue;
                var alias = nic.Name.Replace("'", "''");
                var servers = (d.Servers ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(s => Regex.IsMatch(s, @"^[0-9a-fA-F:.]+$")).ToList();
                var cmd = servers.Count == 0
                    ? $"Set-DnsClientServerAddress -InterfaceAlias '{alias}' -ResetServerAddresses; 'OK'"
                    : $"Set-DnsClientServerAddress -InterfaceAlias '{alias}' -ServerAddresses {string.Join(",", servers)}; 'OK'";
                var res = ProcessRunner.PowerShellAsync(cmd).Result;
                if (res.Output != null && res.Output.Contains("OK")) dnsOk++;
            }
            if (m.Dns.Count > 0)
            {
                ProcessRunner.CmdAsync("ipconfig /flushdns").Wait();
                r.Ok(L.F("DNS: phục hồi cho {0} card mạng", dnsOk));
            }
            var wdir = Path.Combine(set.Folder, "WiFi");
            if (Directory.Exists(wdir))
            {
                int w = 0;
                foreach (var f in Directory.GetFiles(wdir, "*.xml"))
                    if (ProcessRunner.CmdAsync($"netsh wlan add profile filename=\"{f}\" user=all").Result.Ok) w++;
                r.Ok(L.F("Wi-Fi: nạp lại {0} mạng đã lưu", w));
            }
        }

        // ================== Tiện ích ==================
        public static string SafeName(string s) => Regex.Replace(s ?? "PC", @"[^\w\-]", "_");

        private static void TryDeleteDir(string d)
        {
            try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
        }

        /// <summary>Số ngày kể từ bản sao lưu gần nhất (null = chưa có).</summary>
        public static double? DaysSinceLastBackup()
        {
            try
            {
                var last = List().Where(s => s.Manifest.Kind != "before-change" && s.Manifest.Kind != "before-restore").FirstOrDefault();
                return last == null ? (double?)null : (DateTime.Now - last.Manifest.CreatedTime).TotalDays;
            }
            catch { return null; }
        }
    }
}
