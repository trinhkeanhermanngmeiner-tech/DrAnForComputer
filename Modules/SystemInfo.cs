using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Media;
using BacSiMayTinh.Controls;
using BacSiMayTinh.Core;

namespace BacSiMayTinh.Modules
{
    public class InfoRow
    {
        public string Label { get; set; }
        public string Value { get; set; }
        public bool IsHeader { get; set; }
        public Brush ValueBrush { get; set; }
        public bool HasBrush => ValueBrush != null;
    }

    public class InfoBar
    {
        public string Label { get; set; }
        public string Detail { get; set; }
        public double Percent { get; set; }
        public Brush Brush => Percent >= 90 ? Pal.Red : Percent >= 75 ? Pal.Amber : null;
        public bool Warn => Percent >= 75;
    }

    public class InfoSection
    {
        public string Title { get; set; }
        public string Glyph { get; set; }
        public List<InfoRow> Rows { get; } = new List<InfoRow>();
        public List<InfoBar> Bars { get; } = new List<InfoBar>();
        public bool HasBars => Bars.Count > 0;

        public InfoSection Add(string label, string value, Brush brush = null)
        {
            if (!string.IsNullOrWhiteSpace(value)) Rows.Add(new InfoRow { Label = label, Value = value, ValueBrush = brush });
            return this;
        }

        public InfoSection Header(string text)
        {
            Rows.Add(new InfoRow { Label = text, IsHeader = true });
            return this;
        }
    }

    /// <summary>Thông tin nhanh dùng cho trang Tổng quan và chẩn đoán.</summary>
    public static class SystemQuick
    {
        private static string _diskType;
        private static string _cpu;
        private static string _os;

        public static string SystemDiskType()
        {
            if (_diskType != null) return _diskType;
            string result = "Unknown";
            try
            {
                char letter = char.ToUpperInvariant(FileUtil.SystemDrive[0]);
                const string scope = @"root\Microsoft\Windows\Storage";
                var part = Wmi.Query("SELECT DriveLetter, DiskNumber FROM MSFT_Partition", scope)
                    .FirstOrDefault(p => { try { return char.ToUpperInvariant(Convert.ToChar(p["DriveLetter"])) == letter; } catch { return false; } });
                if (part != null)
                {
                    var num = Wmi.Long(part, "DiskNumber").ToString();
                    var disk = Wmi.Query("SELECT DeviceId, MediaType, BusType FROM MSFT_PhysicalDisk", scope)
                        .FirstOrDefault(d => Wmi.Str(d, "DeviceId") == num);
                    if (disk != null) result = Classify(Wmi.Long(disk, "MediaType"), Wmi.Long(disk, "BusType"));
                }
            }
            catch { }
            return _diskType = result;
        }

        public static string Classify(long media, long bus)
        {
            if (bus == 17) return "NVMe SSD";
            if (media == 4) return "SSD";
            if (media == 3) return "HDD";
            return "Unknown";
        }

        public static string Display(string diskType) => diskType == "Unknown" ? L.T("Không rõ") : diskType;

        public static string CpuName()
        {
            if (_cpu != null) return _cpu;
            var n = RegistryHelper.GetValue(@"HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString") as string;
            return _cpu = (n ?? L.T("Không rõ")).Trim();
        }

        public static string OsName()
        {
            if (_os != null) return _os;
            var cap = Wmi.Str(Wmi.First("SELECT Caption FROM Win32_OperatingSystem"), "Caption").Replace("Microsoft ", "");
            var dv = RegistryHelper.GetValue(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion") as string;
            return _os = (string.IsNullOrEmpty(cap) ? "Windows" : cap) + (string.IsNullOrEmpty(dv) ? "" : " " + dv);
        }

        public static double TotalRamGb() => Native.GetMemory().ullTotalPhys / 1024.0 / 1024 / 1024;

        public static (int used, int total) RamSlots()
        {
            int total = (int)Wmi.Query("SELECT MemoryDevices, Use FROM Win32_PhysicalMemoryArray")
                .Where(a => Wmi.Long(a, "Use") == 3 || Wmi.Long(a, "Use") == 0).Sum(a => Wmi.Long(a, "MemoryDevices"));
            int used = Wmi.Query("SELECT Capacity FROM Win32_PhysicalMemory").Count;
            return (used, total);
        }

        public static string Uptime()
        {
            var t = TimeSpan.FromMilliseconds(Native.GetTickCount64());
            if (t.TotalDays >= 1) return L.F("{0} ngày {1} giờ", (int)t.TotalDays, t.Hours);
            if (t.TotalHours >= 1) return L.F("{0} giờ {1} phút", t.Hours, t.Minutes);
            return L.F("{0} phút", t.Minutes);
        }

        public static double UptimeDays => Native.GetTickCount64() / 1000.0 / 86400;

        public static bool IsLaptop()
        {
            var cs = Wmi.First("SELECT PCSystemType FROM Win32_ComputerSystem");
            return Wmi.Long(cs, "PCSystemType") == 2 || PowerPlan.HasBattery();
        }
    }

    public static class SystemInfoCollector
    {
        private static string Gb(double bytes) => (bytes / 1024 / 1024 / 1024).ToString(bytes >= 100L * 1024 * 1024 * 1024 ? "0" : "0.#") + " GB";

        public static List<InfoSection> Collect()
        {
            // Chạy song song các mục để giảm thời gian chờ (WMI/PowerShell có thể chậm trên máy yếu).
            var funcs = new Func<InfoSection>[] { Computer, OperatingSystem, Cpu, Ram, Gpu, Board, Disks, Partitions, Battery, Network, Displays };
            var tasks = funcs.Select(f => Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try { return f(); }
                catch (Exception ex) { Logger.Warn("Thu thập thông tin: " + ex.Message); return null; }
                finally { if (sw.ElapsedMilliseconds > 1500) Logger.Info($"Thu thập {f.Method.Name} mất {sw.ElapsedMilliseconds} ms"); }
            })).ToArray();
            Task.WaitAll(tasks);
            return tasks.Select(t => t.Result).Where(s => s != null && (s.Rows.Count > 0 || s.Bars.Count > 0)).ToList();
        }

        private static InfoSection Computer()
        {
            var cs = Wmi.First("SELECT * FROM Win32_ComputerSystem");
            var bios = Wmi.First("SELECT SerialNumber FROM Win32_BIOS");
            var s = new InfoSection { Title = L.T("Tổng quan máy"), Glyph = "" };
            s.Add(L.T("Tên máy"), Environment.MachineName)
             .Add(L.T("Hãng sản xuất"), Wmi.Str(cs, "Manufacturer"))
             .Add("Model", Wmi.Str(cs, "Model"))
             .Add(L.T("Loại máy"), SystemQuick.IsLaptop() ? "Laptop" : L.T("Máy tính để bàn"))
             .Add(L.T("Số serial"), Wmi.Str(bios, "SerialNumber"))
             .Add(L.T("Người dùng"), Environment.UserName);
            return s;
        }

        private static InfoSection OperatingSystem()
        {
            var os = Wmi.First("SELECT * FROM Win32_OperatingSystem");
            const string cv = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            var build = RegistryHelper.GetValue(cv, "CurrentBuild") as string;
            var ubr = RegistryHelper.GetValue(cv, "UBR");
            var s = new InfoSection { Title = L.T("Hệ điều hành"), Glyph = "" };
            s.Add(L.T("Phiên bản"), Wmi.Str(os, "Caption").Replace("Microsoft ", ""))
             .Add(L.T("Bản cập nhật"), RegistryHelper.GetValue(cv, "DisplayVersion") as string)
             .Add("Build", build + (ubr != null ? "." + ubr : ""))
             .Add(L.T("Kiến trúc"), Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")
             .Add(L.T("Ngày cài đặt"), Wmi.Date(os, "InstallDate")?.ToString("dd/MM/yyyy"))
             .Add(L.T("Lần khởi động gần nhất"), Wmi.Date(os, "LastBootUpTime")?.ToString("dd/MM/yyyy HH:mm"))
             .Add(L.T("Thời gian hoạt động"), SystemQuick.Uptime());

            var lic = Wmi.Query("SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL");
            if (lic.Count > 0)
            {
                bool active = lic.Any(l => Wmi.Long(l, "LicenseStatus") == 1);
                s.Add(L.T("Bản quyền"), active ? L.T("Đã kích hoạt") : L.T("Chưa kích hoạt"), active ? Pal.Green : Pal.Amber);
            }
            return s;
        }

        private static InfoSection Cpu()
        {
            var s = new InfoSection { Title = L.T("Bộ vi xử lý (CPU)"), Glyph = "" };
            foreach (var c in Wmi.Query("SELECT * FROM Win32_Processor"))
            {
                s.Add(L.T("Tên"), Wmi.Str(c, "Name"))
                 .Add(L.T("Số nhân / luồng"), L.F("{0} nhân / {1} luồng", Wmi.Long(c, "NumberOfCores"), Wmi.Long(c, "NumberOfLogicalProcessors")))
                 .Add(L.T("Xung nhịp cơ bản"), (Wmi.Long(c, "MaxClockSpeed") / 1000.0).ToString("0.00") + " GHz")
                 .Add(L.T("Bộ nhớ đệm L2"), Wmi.Long(c, "L2CacheSize") > 0 ? (Wmi.Long(c, "L2CacheSize") / 1024.0).ToString("0.#") + " MB" : null)
                 .Add(L.T("Bộ nhớ đệm L3"), Wmi.Long(c, "L3CacheSize") > 0 ? (Wmi.Long(c, "L3CacheSize") / 1024.0).ToString("0.#") + " MB" : null)
                 .Add("Socket", Wmi.Str(c, "SocketDesignation"));
                var virt = c["VirtualizationFirmwareEnabled"];
                if (virt != null) s.Add(L.T("Ảo hóa (VT-x/AMD-V)"), (bool)virt ? L.T("Đã bật") : L.T("Đang tắt"));
            }
            return s;
        }

        private static string MemType(long t)
        {
            switch (t)
            {
                case 20: return "DDR";
                case 21: return "DDR2";
                case 24: return "DDR3";
                case 26: return "DDR4";
                case 27: return "LPDDR";
                case 28: return "LPDDR2";
                case 29: return "LPDDR3";
                case 30: return "LPDDR4";
                case 34: return "DDR5";
                case 35: return "LPDDR5";
                default: return "";
            }
        }

        private static InfoSection Ram()
        {
            var m = Native.GetMemory();
            var s = new InfoSection { Title = L.T("Bộ nhớ RAM"), Glyph = "" };
            s.Add(L.T("Tổng dung lượng"), Gb(m.ullTotalPhys))
             .Add(L.T("Đang sử dụng"), $"{Gb(m.ullTotalPhys - m.ullAvailPhys)} ({m.dwMemoryLoad}%)");
            var sticks = Wmi.Query("SELECT * FROM Win32_PhysicalMemory");
            var (used, total) = SystemQuick.RamSlots();
            if (total > 0) s.Add(L.T("Số khe RAM"), L.F("{0} / {1} khe đã dùng", used, total) + (total > used ? L.F(" – còn {0} khe trống", total - used) : ""), total > used ? Pal.Green : null);
            var arr = Wmi.First("SELECT MaxCapacity, MaxCapacityEx FROM Win32_PhysicalMemoryArray");
            long maxKb = Wmi.Long(arr, "MaxCapacityEx");
            if (maxKb == 0) maxKb = Wmi.Long(arr, "MaxCapacity");
            if (maxKb > 0) s.Add(L.T("Hỗ trợ tối đa"), Gb(maxKb * 1024.0));
            foreach (var st in sticks)
            {
                var type = MemType(Wmi.Long(st, "SMBIOSMemoryType"));
                long speed = Wmi.Long(st, "ConfiguredClockSpeed");
                if (speed == 0) speed = Wmi.Long(st, "Speed");
                var slot = Wmi.Str(st, "DeviceLocator");
                s.Header((string.IsNullOrEmpty(slot) ? "Thanh RAM" : slot) + ": " + Gb(Wmi.Long(st, "Capacity")) + " " + type + (speed > 0 ? $" {speed} MHz" : ""));
                s.Add(L.T("Hãng"), Wmi.Str(st, "Manufacturer"))
                 .Add(L.T("Mã sản phẩm"), Wmi.Str(st, "PartNumber"))
                 .Add(L.T("Dạng"), Wmi.Long(st, "FormFactor") == 12 ? "SO-DIMM (laptop)" : Wmi.Long(st, "FormFactor") == 8 ? L.T("DIMM (máy bàn)") : null);
            }
            return s;
        }

        private static InfoSection Gpu()
        {
            var s = new InfoSection { Title = L.T("Card đồ họa (GPU)"), Glyph = "" };
            var vram = ReadVramFromRegistry();
            foreach (var g in Wmi.Query("SELECT * FROM Win32_VideoController"))
            {
                var name = Wmi.Str(g, "Name");
                s.Header(name);
                long mem = vram.TryGetValue(name, out var v) ? v : Wmi.Long(g, "AdapterRAM");
                if (mem > 0) s.Add(L.T("Bộ nhớ (VRAM)"), mem >= 1024L * 1024 * 1024 ? Gb(mem) : (mem / 1024 / 1024) + " MB");
                s.Add(L.T("Phiên bản driver"), Wmi.Str(g, "DriverVersion"))
                 .Add(L.T("Ngày driver"), Wmi.Date(g, "DriverDate")?.ToString("dd/MM/yyyy"));
                var w = Wmi.Long(g, "CurrentHorizontalResolution");
                if (w > 0) s.Add(L.T("Độ phân giải"), L.F("{0} × {1} @ {2} Hz", w, Wmi.Long(g, "CurrentVerticalResolution"), Wmi.Long(g, "CurrentRefreshRate")));
            }
            return s;
        }

        private static Dictionary<string, long> ReadVramFromRegistry()
        {
            var d = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            try
            {
                const string cls = @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
                using (var b = RegistryHelper.OpenBase(cls, out var sub))
                using (var k = b.OpenSubKey(sub))
                {
                    if (k == null) return d;
                    foreach (var n in k.GetSubKeyNames().Where(x => x.Length == 4 && x.All(char.IsDigit)))
                        using (var sk = k.OpenSubKey(n))
                        {
                            var desc = sk?.GetValue("DriverDesc") as string;
                            var q = sk?.GetValue("HardwareInformation.qwMemorySize");
                            if (desc == null || q == null) continue;
                            long val = q is long l ? l : q is byte[] bytes && bytes.Length >= 4 ? (bytes.Length >= 8 ? BitConverter.ToInt64(bytes, 0) : BitConverter.ToUInt32(bytes, 0)) : Convert.ToInt64(q);
                            if (val > 0) d[desc] = val;
                        }
                }
            }
            catch { }
            return d;
        }

        private static InfoSection Board()
        {
            var bb = Wmi.First("SELECT * FROM Win32_BaseBoard");
            var bios = Wmi.First("SELECT * FROM Win32_BIOS");
            var s = new InfoSection { Title = L.T("Bo mạch chủ & BIOS"), Glyph = "" };
            s.Add(L.T("Hãng bo mạch"), Wmi.Str(bb, "Manufacturer"))
             .Add(L.T("Model bo mạch"), Wmi.Str(bb, "Product"))
             .Add(L.T("Phiên bản BIOS"), Wmi.Str(bios, "SMBIOSBIOSVersion"))
             .Add(L.T("Ngày BIOS"), Wmi.Date(bios, "ReleaseDate")?.ToString("dd/MM/yyyy"));
            var fw = RegistryHelper.GetValue(@"HKLM\SYSTEM\CurrentControlSet\Control", "PEFirmwareType");
            if (fw is int f) s.Add(L.T("Chế độ khởi động"), f == 2 ? "UEFI" : "Legacy BIOS");
            var sb = RegistryHelper.GetValue(@"HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
            if (sb is int sbv) s.Add("Secure Boot", sbv == 1 ? L.T("Đang bật") : L.T("Đang tắt"));
            var tpm = Wmi.First("SELECT SpecVersion, IsEnabled_InitialValue FROM Win32_Tpm", @"root\cimv2\Security\MicrosoftTpm");
            if (tpm != null)
            {
                var ver = Wmi.Str(tpm, "SpecVersion").Split(',')[0].Trim();
                s.Add("TPM", L.T("Phiên bản ") + ver + (ver.StartsWith("2") ? L.T(" (đủ điều kiện Windows 11)") : ""));
            }
            else s.Add("TPM", L.T("Không phát hiện"));
            return s;
        }

        private static string BusName(long b)
        {
            switch (b)
            {
                case 3: return "ATA";
                case 7: return "USB";
                case 8: return "RAID";
                case 11: return "SATA";
                case 12: return "SD";
                case 17: return "NVMe";
                case 10: return "SAS";
                default: return L.T("Khác");
            }
        }

        private static InfoSection Disks()
        {
            const string scope = @"root\Microsoft\Windows\Storage";
            var s = new InfoSection { Title = L.T("Ổ cứng"), Glyph = "" };
            var rel = new Dictionary<string, string[]>();
            try
            {
                var r = ProcessRunner.PowerShellAsync("Get-PhysicalDisk | ForEach-Object { $r = $_ | Get-StorageReliabilityCounter -ErrorAction SilentlyContinue; \"$($_.DeviceId)|$($r.Temperature)|$($r.Wear)|$($r.PowerOnHours)\" }").Result;
                foreach (var line in (r.Output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var p = line.Split('|');
                    if (p.Length >= 4) rel[p[0].Trim()] = p;
                }
            }
            catch { }

            foreach (var d in Wmi.Query("SELECT * FROM MSFT_PhysicalDisk", scope).OrderBy(x => Wmi.Long(x, "DeviceId")))
            {
                var id = Wmi.Str(d, "DeviceId");
                s.Header(Wmi.Str(d, "FriendlyName"));
                s.Add(L.T("Loại ổ"), SystemQuick.Display(SystemQuick.Classify(Wmi.Long(d, "MediaType"), Wmi.Long(d, "BusType"))))
                 .Add(L.T("Dung lượng"), Gb(Wmi.Long(d, "Size")))
                 .Add(L.T("Giao tiếp"), BusName(Wmi.Long(d, "BusType")));
                long h = Wmi.Long(d, "HealthStatus");
                s.Add(L.T("Tình trạng sức khỏe"), h == 0 ? L.T("Tốt") : h == 1 ? L.T("Cảnh báo – nên sao lưu dữ liệu!") : L.T("Hỏng – sao lưu dữ liệu ngay!"),
                    h == 0 ? Pal.Green : h == 1 ? Pal.Amber : Pal.Red);
                if (rel.TryGetValue(id, out var p))
                {
                    if (int.TryParse(p[1], out var temp) && temp > 0) s.Add(L.T("Nhiệt độ"), temp + " °C", temp >= 60 ? Pal.Red : temp >= 50 ? Pal.Amber : null);
                    if (int.TryParse(p[2], out var wear) && wear > 0) s.Add(L.T("Độ hao mòn"), wear + "%", wear >= 80 ? Pal.Red : wear >= 50 ? Pal.Amber : null);
                    if (int.TryParse(p[3], out var hours) && hours > 0) s.Add(L.T("Thời gian đã chạy"), FileUtil.FormatNumber(hours) + L.T(" giờ"));
                }
            }
            return s;
        }

        private static InfoSection Partitions()
        {
            var s = new InfoSection { Title = L.T("Phân vùng ổ đĩa"), Glyph = "" };
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady || (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable)) continue;
                    double used = d.TotalSize - d.TotalFreeSpace;
                    var label = string.IsNullOrEmpty(d.VolumeLabel) ? (d.DriveType == DriveType.Removable ? "USB" : L.T("Ổ đĩa")) : d.VolumeLabel;
                    s.Bars.Add(new InfoBar
                    {
                        Label = $"{label} ({d.Name.TrimEnd('\\')})",
                        Detail = L.F("Trống {0} / {1}  ·  {2}", FileUtil.FormatSize(d.TotalFreeSpace), FileUtil.FormatSize(d.TotalSize), d.DriveFormat),
                        Percent = d.TotalSize > 0 ? used * 100 / d.TotalSize : 0
                    });
                }
                catch { }
            }
            return s;
        }

        private static InfoSection Battery()
        {
            var bat = Wmi.Query("SELECT * FROM Win32_Battery");
            if (bat.Count == 0) return null;
            var s = new InfoSection { Title = "Pin", Glyph = "" };
            var b = bat[0];
            s.Add(L.T("Mức pin hiện tại"), Wmi.Long(b, "EstimatedChargeRemaining") + "%")
             .Add(L.T("Trạng thái"), Wmi.Long(b, "BatteryStatus") == 2 ? L.T("Đang cắm sạc") : L.T("Đang dùng pin"));
            long design = Wmi.Query("SELECT DesignedCapacity FROM BatteryStaticData", @"root\WMI").Select(x => Wmi.Long(x, "DesignedCapacity")).FirstOrDefault();
            long full = Wmi.Query("SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", @"root\WMI").Select(x => Wmi.Long(x, "FullChargedCapacity")).FirstOrDefault();
            long cycles = Wmi.Query("SELECT CycleCount FROM BatteryCycleCount", @"root\WMI").Select(x => Wmi.Long(x, "CycleCount")).FirstOrDefault();
            if (design > 0 && full > 0)
            {
                double health = Math.Min(100, full * 100.0 / design);
                s.Add(L.T("Dung lượng thiết kế"), FileUtil.FormatNumber(design) + " mWh")
                 .Add(L.T("Dung lượng sạc đầy"), FileUtil.FormatNumber(full) + " mWh")
                 .Add(L.T("Sức khỏe pin"), $"{health:0}% (chai {100 - health:0}%)", health >= 80 ? Pal.Green : health >= 60 ? Pal.Amber : Pal.Red);
            }
            if (cycles > 0) s.Add(L.T("Số lần sạc"), FileUtil.FormatNumber(cycles));
            return s;
        }

        private static InfoSection Network()
        {
            var s = new InfoSection { Title = L.T("Mạng"), Glyph = "" };
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != OperationalStatus.Up) continue;
                if (n.NetworkInterfaceType == NetworkInterfaceType.Loopback || n.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                s.Header(n.Name);
                s.Add(L.T("Thiết bị"), n.Description)
                 .Add(L.T("Loại"), n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? L.T("Dây mạng (Ethernet)") : n.NetworkInterfaceType.ToString())
                 .Add(L.T("Tốc độ liên kết"), n.Speed > 0 ? (n.Speed >= 1_000_000_000 ? (n.Speed / 1e9).ToString("0.#") + " Gbps" : (n.Speed / 1e6).ToString("0") + " Mbps") : null)
                 .Add(L.T("Địa chỉ MAC"), string.Join(":", n.GetPhysicalAddress().GetAddressBytes().Select(x => x.ToString("X2"))));
                try
                {
                    var ip = n.GetIPProperties();
                    s.Add(L.T("Địa chỉ IPv4"), string.Join(", ", ip.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString())))
                     .Add("DNS", string.Join(", ", ip.DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString())));
                }
                catch { }
            }
            return s;
        }

        private static InfoSection Displays()
        {
            var s = new InfoSection { Title = L.T("Màn hình"), Glyph = "" };
            int i = 1;
            foreach (var sc in System.Windows.Forms.Screen.AllScreens)
            {
                s.Add(L.T("Màn hình ") + i++ + (sc.Primary ? L.T(" (chính)") : ""), L.F("{0} × {1}", sc.Bounds.Width, sc.Bounds.Height));
            }
            return s;
        }

        // ============ Xuất báo cáo HTML ============
        public static string ExportHtml(List<InfoSection> sections)
        {
            string E(string x) => WebUtility.HtmlEncode(x ?? "");
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang='" + L.Lang + "'><head><meta charset='utf-8'><title>" + E(L.T("Báo cáo cấu hình máy tính")) + "</title><style>");
            sb.Append("body{font-family:Segoe UI,Arial,sans-serif;background:#f2f5f9;color:#0f1b2a;margin:0;padding:32px}");
            sb.Append(".wrap{max-width:920px;margin:auto}.hero{background:linear-gradient(135deg,#0fa97c,#1279c4);color:#fff;border-radius:16px;padding:24px 28px;margin-bottom:20px}");
            sb.Append(".hero h1{margin:0 0 4px;font-size:24px}.hero p{margin:0;opacity:.9}.card{background:#fff;border:1px solid #dfe6ee;border-radius:12px;padding:18px 22px;margin-bottom:14px}");
            sb.Append("h2{font-size:16px;margin:0 0 10px}table{width:100%;border-collapse:collapse}td{padding:6px 0;border-bottom:1px solid #eef2f6;font-size:14px;vertical-align:top}");
            sb.Append("td.l{color:#4a5b70;width:38%}tr.h td{font-weight:600;padding-top:12px;color:#0e9f76}.bar{background:#e3e9f0;border-radius:4px;height:8px;margin:4px 0 2px}");
            sb.Append(".bar i{display:block;height:8px;border-radius:4px;background:linear-gradient(90deg,#0fa97c,#1279c4)}.foot{text-align:center;color:#90a0b3;font-size:13px;margin-top:24px}</style></head><body><div class='wrap'>");
            sb.Append("<div class='hero'><h1>" + E(L.F("Báo cáo cấu hình máy tính – {0}", Environment.MachineName)) + "</h1><p>" + E(L.F("Tạo lúc {0:HH:mm dd/MM/yyyy} bởi {1} v{2}", DateTime.Now, AppInfo.Name, AppInfo.Version)) + "</p></div>");
            foreach (var s in sections)
            {
                sb.Append($"<div class='card'><h2>{E(s.Title)}</h2>");
                if (s.Rows.Count > 0)
                {
                    sb.Append("<table>");
                    foreach (var r in s.Rows)
                        sb.Append(r.IsHeader ? $"<tr class='h'><td colspan='2'>{E(r.Label)}</td></tr>" : $"<tr><td class='l'>{E(r.Label)}</td><td>{E(r.Value)}</td></tr>");
                    sb.Append("</table>");
                }
                foreach (var b in s.Bars)
                    sb.Append($"<div style='margin-top:8px'><b>{E(b.Label)}</b> – {E(b.Detail)}<div class='bar'><i style='width:{b.Percent:0}%'></i></div></div>");
                sb.Append("</div>");
            }
            sb.Append("<div class='foot'>" + E(L.F("{0} – Tác giả: {1}", AppInfo.Name, AppInfo.Author)) + "</div></div></body></html>");

            var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                (L.IsEnglish ? "PC_Report_" : "BaoCao_MayTinh_") + $"{Environment.MachineName}_{DateTime.Now:yyyyMMdd_HHmm}.html");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
            return file;
        }
    }
}
