using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Media;
using BacSiMayTinh.Controls;
using BacSiMayTinh.Core;
using Microsoft.Win32;

namespace BacSiMayTinh.Modules
{
    // =====================================================================
    //  Driver: liệt kê, phát hiện thiết bị lỗi, sao lưu / phục hồi,
    //  cập nhật qua Windows Update (có sao lưu & quay lại), dọn driver cũ.
    // =====================================================================

    [DataContract]
    public class DriverInfo
    {
        [DataMember] public string DeviceName { get; set; }
        [DataMember] public string DeviceClass { get; set; }
        [DataMember] public string Provider { get; set; }
        [DataMember] public string Manufacturer { get; set; }
        [DataMember] public string Version { get; set; }
        [DataMember] public string Date { get; set; }      // yyyy-MM-dd
        [DataMember] public string InfName { get; set; }
        [DataMember] public string DeviceId { get; set; }
        [DataMember] public bool IsSigned { get; set; }

        public DateTime? DateValue => DateTime.TryParse(Date, out var d) ? d : (DateTime?)null;
        public bool IsThirdParty => (InfName ?? "").StartsWith("oem", StringComparison.OrdinalIgnoreCase);
        public double AgeYears => DateValue.HasValue ? (DateTime.Now - DateValue.Value).TotalDays / 365.25 : 0;
        public bool IsOld => IsThirdParty && AgeYears >= 5;
        public string DateText => DateValue?.ToString("dd/MM/yyyy") ?? "";
        public string ClassText => DriverManager.ClassName(DeviceClass);
        public string Detail => (string.IsNullOrEmpty(Provider) ? "" : Provider + "  ·  ") + "v" + Version + "  ·  " + DateText + "  ·  " + InfName;
        public string AgeText => IsOld ? L.F("Cũ {0:0} năm", AgeYears) : "";
        public bool HasAge => IsOld;

        public bool IsChecked { get; set; }
    }

    [DataContract]
    public class DriverInventory
    {
        [DataMember] public List<DriverInfo> Items { get; set; } = new List<DriverInfo>();
    }

    public class ProblemDevice
    {
        public string Name { get; set; }
        public string DeviceId { get; set; }
        public int Code { get; set; }
        public string ClassText { get; set; }
        public string CodeText => DriverManager.ProblemText(Code);
        public bool MissingDriver => Code == 28 || Code == 1 || Code == 18;
    }

    public static class DriverManager
    {
        /// <summary>Danh sách driver đang dùng cho các thiết bị có mặt (Win32_PnPSignedDriver).</summary>
        public static List<DriverInfo> List(bool includeInbox)
        {
            var list = new List<DriverInfo>();
            foreach (var o in Wmi.Query("SELECT DeviceName, DeviceClass, Manufacturer, DriverProviderName, DriverVersion, DriverDate, InfName, DeviceID, IsSigned FROM Win32_PnPSignedDriver"))
            {
                var inf = Wmi.Str(o, "InfName");
                var name = Wmi.Str(o, "DeviceName");
                if (string.IsNullOrEmpty(inf) || string.IsNullOrEmpty(name)) continue;
                var d = new DriverInfo
                {
                    DeviceName = name, DeviceClass = Wmi.Str(o, "DeviceClass"), Provider = Wmi.Str(o, "DriverProviderName"),
                    Manufacturer = Wmi.Str(o, "Manufacturer"), Version = Wmi.Str(o, "DriverVersion"),
                    Date = Wmi.Date(o, "DriverDate")?.ToString("yyyy-MM-dd"), InfName = inf, DeviceId = Wmi.Str(o, "DeviceID"),
                    IsSigned = string.Equals(Wmi.Str(o, "IsSigned"), "True", StringComparison.OrdinalIgnoreCase)
                };
                if (!includeInbox && !d.IsThirdParty) continue;
                list.Add(d);
            }
            return list.OrderBy(d => d.ClassText, StringComparer.CurrentCultureIgnoreCase).ThenBy(d => d.DeviceName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Thiết bị đang báo lỗi trong Device Manager (thiếu driver, lỗi khởi động…).</summary>
        public static List<ProblemDevice> Problems()
        {
            var list = new List<ProblemDevice>();
            foreach (var o in Wmi.Query("SELECT Name, DeviceID, ConfigManagerErrorCode, PNPClass FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0"))
            {
                int code = (int)Wmi.Long(o, "ConfigManagerErrorCode");
                if (code == 22 || code == 45) continue; // 22 = người dùng tự tắt, 45 = thiết bị đang không cắm
                var name = Wmi.Str(o, "Name");
                list.Add(new ProblemDevice
                {
                    Name = string.IsNullOrEmpty(name) ? Wmi.Str(o, "DeviceID") : name, DeviceId = Wmi.Str(o, "DeviceID"),
                    Code = code, ClassText = ClassName(Wmi.Str(o, "PNPClass"))
                });
            }
            return list;
        }

        public static string ProblemText(int code)
        {
            switch (code)
            {
                case 1: case 18: case 28: return L.F("Chưa cài driver (mã {0})", code);
                case 3: return L.T("Driver hỏng hoặc thiếu bộ nhớ (mã 3)");
                case 10: return L.T("Thiết bị không khởi động được (mã 10)");
                case 12: return L.T("Xung đột tài nguyên (mã 12)");
                case 14: return L.T("Cần khởi động lại máy (mã 14)");
                case 19: return L.T("Thông tin cấu hình trong registry bị hỏng (mã 19)");
                case 21: return L.T("Windows đang gỡ thiết bị (mã 21)");
                case 24: return L.T("Thiết bị không có mặt hoặc driver chưa đầy đủ (mã 24)");
                case 31: return L.T("Windows không nạp được driver (mã 31)");
                case 39: return L.T("Driver bị hỏng hoặc thiếu (mã 39)");
                case 43: return L.T("Thiết bị báo lỗi và đã bị dừng (mã 43)");
                case 52: return L.T("Driver không có chữ ký số hợp lệ (mã 52)");
                default: return L.F("Thiết bị gặp sự cố (mã {0})", code);
            }
        }

        public static string ClassName(string cls)
        {
            switch ((cls ?? "").ToUpperInvariant())
            {
                case "DISPLAY": return L.T("Card màn hình");
                case "MEDIA": return L.T("Âm thanh");
                case "AUDIOENDPOINT": return L.T("Âm thanh");
                case "NET": return L.T("Mạng");
                case "BLUETOOTH": return "Bluetooth";
                case "USB": return "USB";
                case "HIDCLASS": case "KEYBOARD": case "MOUSE": return L.T("Bàn phím, chuột & cảm ứng");
                case "SYSTEM": return L.T("Thiết bị hệ thống");
                case "HDC": case "SCSIADAPTER": case "DISKDRIVE": return L.T("Ổ đĩa & bộ điều khiển");
                case "CAMERA": case "IMAGE": return L.T("Camera & máy quét");
                case "PRINTER": case "PRINTQUEUE": return L.T("Máy in");
                case "BIOMETRIC": return L.T("Vân tay & khuôn mặt");
                case "MONITOR": return L.T("Màn hình");
                case "BATTERY": return L.T("Pin & nguồn");
                case "FIRMWARE": return "Firmware";
                case "SOFTWARECOMPONENT": return L.T("Thành phần phần mềm");
                case "EXTENSION": return L.T("Phần mở rộng driver");
                case "SECURITYDEVICES": return L.T("Bảo mật (TPM)");
                case "": return L.T("Khác");
                default: return cls;
            }
        }

        // ---------------- Sao lưu & phục hồi ----------------
        public static async Task<int> ExportAsync(IEnumerable<string> infs, string target, Action<string> progress)
        {
            Directory.CreateDirectory(target);
            int n = 0;
            var all = infs?.ToList();
            if (all == null)
            {
                await ProcessRunner.CmdAsync($"pnputil /export-driver * \"{target}\"", (line, p) =>
                {
                    if (line.IndexOf(".inf", StringComparison.OrdinalIgnoreCase) >= 0) { n++; progress?.Invoke(line.Trim()); }
                }).ConfigureAwait(false);
            }
            else
            {
                foreach (var inf in all.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    progress?.Invoke(inf);
                    var r = await ProcessRunner.CmdAsync($"pnputil /export-driver {inf} \"{target}\"").ConfigureAwait(false);
                    if (r.Ok) n++;
                }
            }
            try { JsonFile.Save(Path.Combine(target, "drivers.json"), new DriverInventory { Items = List(true) }); } catch { }
            Logger.Info("Sao lưu driver vào " + target + ": " + n + " gói");
            return n;
        }

        /// <summary>Nạp & cài tất cả driver (*.inf) trong thư mục (kể cả thư mục con).</summary>
        public static (bool ok, int count) InstallFromFolder(string folder, Action<string> progress)
        {
            int n = 0;
            var r = ProcessRunner.CmdAsync($"pnputil /add-driver \"{Path.Combine(folder, "*.inf")}\" /subdirs /install", (line, p) =>
            {
                if (line.IndexOf(".inf", StringComparison.OrdinalIgnoreCase) >= 0) { n++; progress?.Invoke(line.Trim()); }
            }).Result;
            // pnputil trả mã 259 (không có gì để cập nhật) hoặc 3010 (cần khởi động lại) vẫn là thành công.
            bool ok = r.ExitCode == 0 || r.ExitCode == 259 || r.ExitCode == 3010;
            Logger.Info("Cài driver từ " + folder + ": mã " + r.ExitCode + ", " + n + " gói");
            return (ok, n);
        }

        public static Task ScanHardwareAsync() => ProcessRunner.CmdAsync("pnputil /scan-devices");

        /// <summary>Tên các gói driver bên thứ ba đang có trong Driver Store (oemNN.inf).</summary>
        public static HashSet<string> PublishedInfs()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var f in Directory.GetFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF"), "oem*.inf"))
                    set.Add(Path.GetFileName(f));
            }
            catch { }
            return set;
        }

        // ---------------- Công cụ của hãng ----------------
        public static List<VendorTool> VendorTools()
        {
            var list = new List<VendorTool>();
            string gpus = string.Join(" | ", Wmi.Query("SELECT Name FROM Win32_VideoController").Select(o => Wmi.Str(o, "Name"))).ToUpperInvariant();
            string cpu = Wmi.Str(Wmi.First("SELECT Manufacturer FROM Win32_Processor"), "Manufacturer").ToUpperInvariant();
            string maker = Wmi.Str(Wmi.First("SELECT Manufacturer FROM Win32_ComputerSystem"), "Manufacturer").ToUpperInvariant();
            if (gpus.Contains("NVIDIA"))
                list.Add(new VendorTool { Name = "NVIDIA App", Detail = L.T("Driver card đồ họa NVIDIA GeForce/RTX"), Url = "https://www.nvidia.com/en-us/software/nvidia-app/" });
            if (gpus.Contains("AMD") || gpus.Contains("RADEON") || cpu.Contains("AMD"))
                list.Add(new VendorTool { Name = "AMD Software (Adrenalin)", Detail = L.T("Driver card đồ họa & chipset AMD Ryzen/Radeon"), Url = "https://www.amd.com/en/support/download/drivers.html" });
            if (gpus.Contains("INTEL") || cpu.Contains("INTEL"))
                list.Add(new VendorTool { Name = "Intel Driver & Support Assistant", Detail = L.T("Tự tìm driver Intel (đồ họa, Wi-Fi, Bluetooth, chipset)"), Url = "https://www.intel.com/content/www/us/en/support/detect.html" });
            if (maker.Contains("DELL"))
                list.Add(new VendorTool { Name = "Dell SupportAssist", Detail = L.T("Driver & BIOS chính hãng cho máy Dell"), Url = "https://www.dell.com/support/home" });
            else if (maker.Contains("HP") || maker.Contains("HEWLETT"))
                list.Add(new VendorTool { Name = "HP Support Assistant", Detail = L.T("Driver & BIOS chính hãng cho máy HP"), Url = "https://support.hp.com/drivers" });
            else if (maker.Contains("LENOVO"))
                list.Add(new VendorTool { Name = "Lenovo Vantage / Lenovo Support", Detail = L.T("Driver & BIOS chính hãng cho máy Lenovo"), Url = "https://pcsupport.lenovo.com" });
            else if (maker.Contains("ASUS"))
                list.Add(new VendorTool { Name = "ASUS Download Center", Detail = L.T("Driver & BIOS chính hãng cho máy ASUS"), Url = "https://www.asus.com/support/download-center/" });
            else if (maker.Contains("ACER"))
                list.Add(new VendorTool { Name = "Acer Drivers and Manuals", Detail = L.T("Driver & BIOS chính hãng cho máy Acer"), Url = "https://www.acer.com/us-en/support/drivers-and-manuals" });
            else if (maker.Contains("MICRO-STAR") || maker.Contains("MSI"))
                list.Add(new VendorTool { Name = "MSI Download", Detail = L.T("Driver & BIOS chính hãng cho máy MSI"), Url = "https://www.msi.com/support/download" });
            return list;
        }
    }

    public class VendorTool
    {
        public string Name { get; set; }
        public string Detail { get; set; }
        public string Url { get; set; }
    }

    // =====================================================================
    //  Lịch sử cập nhật driver – để quay lại bản cũ.
    // =====================================================================
    [DataContract]
    public class DriverChange
    {
        [DataMember] public string Date { get; set; }
        [DataMember] public string Title { get; set; }
        [DataMember] public string DeviceName { get; set; }
        [DataMember] public string DeviceId { get; set; }
        [DataMember] public string OldInf { get; set; }
        [DataMember] public string OldVersion { get; set; }
        [DataMember] public string OldDate { get; set; }
        [DataMember] public List<string> NewInfs { get; set; } = new List<string>();
        [DataMember] public string BackupDir { get; set; }
        [DataMember] public bool RolledBack { get; set; }

        [OnDeserialized] private void Fix(StreamingContext c) { if (NewInfs == null) NewInfs = new List<string>(); }

        public string DateText => DateTime.TryParse(Date, out var d) ? d.ToString("dd/MM/yyyy HH:mm") : Date;
        public string Detail => DateText + "  ·  " + (string.IsNullOrEmpty(DeviceName) ? "" : DeviceName + "  ·  ") +
                                (string.IsNullOrEmpty(OldVersion) ? L.T("bản cũ: driver có sẵn của Windows") : L.F("bản cũ: v{0} ({1})", OldVersion, OldDate));
        public bool CanRollback => !RolledBack && NewInfs.Count > 0;
        public string StateText => RolledBack ? L.T("Đã quay lại bản cũ") : NewInfs.Count == 0 ? L.T("Không có gói mới để gỡ") : "";
    }

    [DataContract]
    public class DriverHistory
    {
        [DataMember] public List<DriverChange> Items { get; set; } = new List<DriverChange>();
        [OnDeserialized] private void Fix(StreamingContext c) { if (Items == null) Items = new List<DriverChange>(); }

        private static string FilePath => Path.Combine(AppInfo.BackupDir, "lichsu_driver.json");
        public static DriverHistory Load() => JsonFile.Load<DriverHistory>(FilePath);
        public void Save() => JsonFile.Save(FilePath, this);
    }

    // =====================================================================
    //  Cập nhật driver qua Windows Update (driver chính thức, có chữ ký số)
    // =====================================================================
    public static class DriverUpdater
    {
        public static List<WuUpdate> Search(CancellationToken ct)
        {
            using (DriverPolicy.AllowDuringScan())
            {
                var list = Wua.Search("IsInstalled=0 and Type='Driver' and IsHidden=0", true, ct);
                MatchDevices(list);
                return list;
            }
        }

        /// <summary>Ghép bản cập nhật với thiết bị đang có để hiện phiên bản hiện tại.</summary>
        private static void MatchDevices(List<WuUpdate> list)
        {
            if (list.Count == 0) return;
            var drivers = DriverManager.List(true);
            var hw = new List<(string devId, string[] ids)>();
            foreach (var o in Wmi.Query("SELECT DeviceID, HardwareID, CompatibleID FROM Win32_PnPEntity"))
            {
                var ids = new List<string>();
                try { if (o["HardwareID"] is string[] a) ids.AddRange(a); } catch { }
                try { if (o["CompatibleID"] is string[] b) ids.AddRange(b); } catch { }
                hw.Add((Wmi.Str(o, "DeviceID"), ids.ToArray()));
            }
            foreach (var u in list)
            {
                if (string.IsNullOrEmpty(u.HardwareId)) continue;
                var dev = hw.FirstOrDefault(h => h.ids.Any(i => string.Equals(i, u.HardwareId, StringComparison.OrdinalIgnoreCase)));
                if (dev.devId == null) continue;
                u.DeviceId = dev.devId;
                var d = drivers.FirstOrDefault(x => string.Equals(x.DeviceId, dev.devId, StringComparison.OrdinalIgnoreCase));
                if (d != null)
                {
                    u.DeviceName = d.DeviceName;
                    u.CurrentInf = d.InfName;
                    u.CurrentVersion = d.Version;
                    u.CurrentDate = d.DateText;
                }
            }
        }

        /// <summary>Cài một bản cập nhật driver: sao lưu driver cũ → cài → ghi lịch sử để quay lại.</summary>
        public static WuInstallResult Install(WuUpdate u, Action<string> progress)
        {
            var change = new DriverChange
            {
                Date = DateTime.Now.ToString("s"), Title = u.Title, DeviceName = u.DeviceName ?? u.DriverModel, DeviceId = u.DeviceId,
                OldInf = u.CurrentInf, OldVersion = u.CurrentVersion, OldDate = u.CurrentDate
            };
            if (!string.IsNullOrEmpty(u.CurrentInf) && u.CurrentInf.StartsWith("oem", StringComparison.OrdinalIgnoreCase))
            {
                progress?.Invoke(L.T("Đang sao lưu driver hiện tại…"));
                var dir = Path.Combine(ConfigBackup.Root, "DriverTruocCapNhat",
                    DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + ConfigBackup.SafeName(u.DeviceName ?? u.CurrentInf));
                Directory.CreateDirectory(dir);
                var r = ProcessRunner.CmdAsync($"pnputil /export-driver {u.CurrentInf} \"{dir}\"").Result;
                if (r.Ok) change.BackupDir = dir;
                else Logger.Warn("Không sao lưu được " + u.CurrentInf + ": " + r.Output);
            }
            var before = DriverManager.PublishedInfs();
            WuInstallResult res;
            using (DriverPolicy.AllowDuringScan())
                res = Wua.Install(u, progress);
            change.NewInfs = DriverManager.PublishedInfs().Where(i => !before.Contains(i)).ToList();
            if (res.Success)
            {
                var h = DriverHistory.Load();
                h.Items.Insert(0, change);
                h.Save();
            }
            Logger.Info("Cập nhật driver '" + u.Title + "': " + res.Message + " | gói mới: " + string.Join(",", change.NewInfs));
            return res;
        }

        /// <summary>Quay lại driver cũ: gỡ gói driver mới cài, thiết bị tự dùng lại bản cũ (cài lại từ bản sao lưu nếu cần).</summary>
        public static RestoreReport Rollback(DriverChange c, Action<string> progress)
        {
            var r = new RestoreReport();
            foreach (var inf in c.NewInfs)
            {
                progress?.Invoke(L.T("Đang gỡ driver mới: ") + inf);
                var res = ProcessRunner.CmdAsync($"pnputil /delete-driver {inf} /uninstall /force").Result;
                if (res.Ok) r.Ok(L.T("Đã gỡ gói driver mới ") + inf);
                else r.Fail(L.F("Không gỡ được {0}: {1}", inf, (res.Output ?? "").Trim()));
            }
            var published = DriverManager.PublishedInfs();
            if (!string.IsNullOrEmpty(c.BackupDir) && Directory.Exists(c.BackupDir) &&
                (string.IsNullOrEmpty(c.OldInf) || !published.Contains(c.OldInf)))
            {
                progress?.Invoke(L.T("Đang cài lại driver cũ từ bản sao lưu…"));
                var (ok, n) = DriverManager.InstallFromFolder(c.BackupDir, s => progress?.Invoke(s));
                if (ok) r.Ok(L.T("Đã cài lại driver cũ từ bản sao lưu"));
                else r.Fail(L.T("Cài lại driver cũ chưa thành công – hãy dùng Device Manager › Roll Back Driver"));
            }
            progress?.Invoke(L.T("Đang quét lại phần cứng…"));
            DriverManager.ScanHardwareAsync().Wait();
            r.NeedsRestart = true;
            if (r.Errors == 0)
            {
                var h = DriverHistory.Load();
                var item = h.Items.FirstOrDefault(x => x.Date == c.Date && x.Title == c.Title);
                if (item != null) { item.RolledBack = true; h.Save(); }
                c.RolledBack = true;
            }
            Logger.Info("Quay lại driver '" + c.Title + "': " + string.Join(" | ", r.Lines));
            return r;
        }
    }

    /// <summary>
    /// Nếu đang bật chính sách "Không cài driver qua Windows Update" (tinh chỉnh wu_nodrivers),
    /// tạm cho phép trong lúc người dùng chủ động tìm/cài driver rồi trả lại như cũ.
    /// </summary>
    public sealed class DriverPolicy : IDisposable
    {
        private const string Key = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
        private const string Name = "ExcludeWUDriversInQualityUpdate";
        private readonly bool _lifted;

        private DriverPolicy()
        {
            try
            {
                if (RegistryHelper.ValueEquals(RegistryHelper.GetValue(Key, Name), 1))
                {
                    RegistryHelper.SetValue(Key, Name, 0, RegistryValueKind.DWord);
                    _lifted = true;
                    Logger.Info("Tạm cho phép tìm driver qua Windows Update");
                }
            }
            catch { }
        }

        public static DriverPolicy AllowDuringScan() => new DriverPolicy();

        public void Dispose()
        {
            if (!_lifted) return;
            try { RegistryHelper.SetValue(Key, Name, 1, RegistryValueKind.DWord); } catch { }
        }
    }

    // =====================================================================
    //  Dọn driver cũ trùng lặp trong Driver Store
    // =====================================================================
    public class StorePackage : ObservableObject
    {
        public string PublishedName { get; set; }   // oemNN.inf
        public string OriginalName { get; set; }    // tên inf gốc
        public string PackageId { get; set; }
        public string Provider { get; set; }
        public string ClassName { get; set; }
        public string Version { get; set; }
        public DateTime? Date { get; set; }
        public long Size { get; set; }
        public bool InUse { get; set; }
        public string NewerVersion { get; set; }

        public string SizeText => FileUtil.FormatSize(Size);
        public string Title => (string.IsNullOrEmpty(Provider) ? "" : Provider + " – ") + OriginalName;
        public string Detail => L.F("v{0} ({1})  ·  {2}  ·  đã có bản mới hơn v{3}", Version, Date?.ToString("dd/MM/yyyy") ?? "?", PublishedName, NewerVersion);

        private bool _checked = true;
        public bool IsChecked { get => _checked; set => Set(ref _checked, value); }
    }

    public static class DriverStore
    {
        private static readonly Regex DriverVer = new Regex(@"^\s*DriverVer\s*=\s*([^,;\r\n]*)\s*,\s*([0-9.]+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        private static readonly Regex ProviderRx = new Regex(@"^\s*Provider\s*=\s*(.+?)\s*(;.*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        private static readonly Regex ClassRx = new Regex(@"^\s*Class\s*=\s*(.+?)\s*(;.*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        /// <summary>Tìm các gói driver cũ: cùng tên inf gốc & hãng, đã có bản mới hơn, và không thiết bị nào đang dùng.</summary>
        public static List<StorePackage> FindOldDuplicates()
        {
            var inUse = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in Wmi.Query("SELECT InfName FROM Win32_PnPSignedDriver")) inUse.Add(Wmi.Str(o, "InfName"));

            var infDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF");
            var repo = Path.Combine(Environment.SystemDirectory, @"DriverStore\FileRepository");
            var all = new List<StorePackage>();
            foreach (var pub in DriverManager.PublishedInfs())
            {
                try
                {
                    var pkgs = RegistryHelper.GetValue(@"HKLM\SYSTEM\DriverDatabase\DriverInfFiles\" + pub, "") as string[];
                    var pkgId = pkgs?.FirstOrDefault();
                    if (string.IsNullOrEmpty(pkgId)) continue;
                    var orig = pkgId;
                    var m = Regex.Match(pkgId, @"^(.+?\.inf)_", RegexOptions.IgnoreCase);
                    if (m.Success) orig = m.Groups[1].Value;
                    var text = ReadInf(Path.Combine(infDir, pub));
                    var dv = DriverVer.Match(text);
                    var p = new StorePackage
                    {
                        PublishedName = pub, OriginalName = orig, PackageId = pkgId,
                        Provider = Resolve(text, ProviderRx.Match(text).Groups[1].Value),
                        ClassName = ClassRx.Match(text).Groups[1].Value.Trim(),
                        Version = dv.Success ? dv.Groups[2].Value.Trim() : "",
                        Date = dv.Success && DateTime.TryParse(dv.Groups[1].Value.Trim(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt) ? dt : (DateTime?)null,
                        InUse = inUse.Contains(pub)
                    };
                    var dir = Path.Combine(repo, pkgId);
                    p.Size = Directory.Exists(dir) ? FileUtil.DirectorySize(dir) : 0;
                    all.Add(p);
                }
                catch (Exception ex) { Logger.Warn("Đọc gói driver " + pub + ": " + ex.Message); }
            }

            var result = new List<StorePackage>();
            foreach (var g in all.GroupBy(p => (p.OriginalName + "|" + p.Provider + "|" + p.ClassName).ToLowerInvariant()))
            {
                if (g.Count() < 2) continue;
                var ordered = g.OrderByDescending(p => ParseVersion(p.Version)).ThenByDescending(p => p.Date ?? DateTime.MinValue).ToList();
                var newest = ordered[0];
                foreach (var p in ordered.Skip(1))
                {
                    if (p.InUse) continue;
                    if (ParseVersion(p.Version) >= ParseVersion(newest.Version) && (p.Date ?? DateTime.MinValue) >= (newest.Date ?? DateTime.MinValue)) continue;
                    p.NewerVersion = newest.Version;
                    result.Add(p);
                }
            }
            return result.OrderByDescending(p => p.Size).ToList();
        }

        /// <summary>Xóa gói driver (không dùng /force: Windows sẽ từ chối nếu gói đang được dùng).</summary>
        public static bool Delete(StorePackage p)
        {
            var r = ProcessRunner.CmdAsync($"pnputil /delete-driver {p.PublishedName}").Result;
            Logger.Info("Xóa gói driver cũ " + p.PublishedName + " (" + p.OriginalName + " v" + p.Version + "): mã " + r.ExitCode);
            return r.Ok;
        }

        private static string ReadInf(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes);
            if (bytes.Length >= 2 && bytes[1] == 0 && bytes[0] != 0) return Encoding.Unicode.GetString(bytes);
            return Encoding.Default.GetString(bytes);
        }

        /// <summary>Provider dạng %ten% → tra trong mục [Strings].</summary>
        private static string Resolve(string text, string v)
        {
            v = (v ?? "").Trim().Trim('"');
            var m = Regex.Match(v, @"^%(.+)%$");
            if (!m.Success) return v;
            var s = Regex.Match(text, @"^\s*" + Regex.Escape(m.Groups[1].Value) + @"\s*=\s*""?([^""\r\n;]+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            return s.Success ? s.Groups[1].Value.Trim() : v;
        }

        public static Version ParseVersion(string v)
        {
            var parts = (v ?? "").Split('.').Select(x => int.TryParse(x, out var n) ? n : 0).ToList();
            while (parts.Count < 4) parts.Add(0);
            try { return new Version(parts[0], parts[1], parts[2], parts[3]); } catch { return new Version(0, 0); }
        }
    }
}
