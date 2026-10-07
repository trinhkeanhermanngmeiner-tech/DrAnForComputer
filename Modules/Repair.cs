using System.Diagnostics;
using System.Windows.Media;
using BacSiMayTinh.Controls;
using BacSiMayTinh.Core;
using Microsoft.Win32;

namespace BacSiMayTinh.Modules
{
    public class RepairContext
    {
        public Action<string> Log;
        public Action<string> Progress;
        public string Parameter;
        public CancellationToken Token;

        private void OnLine(string line, bool progress)
        {
            if (progress) Progress?.Invoke(line.Trim());
            else Log?.Invoke(line);
        }

        public async Task<RunResult> Cmd(string command)
        {
            Log?.Invoke("› " + command);
            return await ProcessRunner.CmdAsync(command, OnLine, Token);
        }

        public async Task<RunResult> Run(string exe, string args, Encoding enc = null)
        {
            Log?.Invoke("› " + Path.GetFileName(exe) + " " + args);
            return await ProcessRunner.RunAsync(exe, args, OnLine, enc, Token);
        }

        public async Task<RunResult> Ps(string label, string script)
        {
            Log?.Invoke("› " + label);
            return await ProcessRunner.PowerShellAsync(script, OnLine, Token);
        }

        public void Info(string text) => Log?.Invoke(text);
    }

    public class RepairFix : ObservableObject
    {
        public string Id { get; set; }
        public string Glyph { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Duration { get; set; } = L.T("Dưới 1 phút");
        public Risk Risk { get; set; }
        public bool NeedsRestart { get; set; }
        public bool NeedsDrive { get; set; }
        public Func<RepairContext, Task> Action { get; set; }

        public string RiskText => RiskInfo.Text(Risk);
        public Brush RiskFg => Pal.RiskFg(Risk);
        public Brush RiskBg => Pal.RiskBg(Risk);
        public string Meta => "⏱ " + Duration + (NeedsRestart ? L.T("  ·  Cần khởi động lại") : "");

        private string _state;
        public string State { get => _state; set { Set(ref _state, value); OnPropertyChanged(nameof(HasState)); } }
        public bool HasState => !string.IsNullOrEmpty(_state);

        private Brush _stateBrush = Pal.Green;
        public Brush StateBrush { get => _stateBrush; set => Set(ref _stateBrush, value); }

        private List<string> _drives = new List<string>();
        public List<string> Drives { get => _drives; set => Set(ref _drives, value); }

        private string _drive;
        public string SelectedDrive { get => _drive; set => Set(ref _drive, value); }
    }

    public static class RepairCatalog
    {
        private static string Win => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        public static List<RepairFix> Build() => new List<RepairFix>
        {
            new RepairFix
            {
                Id = "net_quick", Glyph = "", Title = L.T("Có Wi-Fi nhưng không vào được web"),
                Description = L.T("Xóa bộ đệm DNS, gỡ proxy lạ do phần mềm/virus cài, xin lại địa chỉ IP mới."),
                Risk = Risk.Safe, Action = NetQuick
            },
            new RepairFix
            {
                Id = "net_reset", Glyph = "", Title = L.T("Đặt lại mạng nâng cao (Winsock, TCP/IP)"),
                Description = L.T("Sửa lỗi mạng sâu do phần mềm VPN, game hoặc virus làm hỏng. Nếu bạn đặt IP tĩnh, cần đặt lại sau khi sửa."),
                Risk = Risk.Moderate, NeedsRestart = true, Action = NetReset
            },
            new RepairFix
            {
                Id = "time_sync", Glyph = "", Title = L.T("Sai ngày giờ – web báo \"Kết nối không riêng tư\""),
                Description = L.T("Đồng bộ lại đồng hồ với máy chủ thời gian. Ngày giờ sai làm các trang HTTPS báo lỗi bảo mật."),
                Risk = Risk.Safe, Action = TimeSync
            },
            new RepairFix
            {
                Id = "wu_reset", Glyph = "", Title = L.T("Windows Update bị lỗi hoặc kẹt"),
                Description = L.T("Đặt lại các thành phần Windows Update (SoftwareDistribution, catroot2) và khởi động lại dịch vụ."),
                Risk = Risk.Moderate, Duration = L.T("1–3 phút"), Action = WindowsUpdateReset
            },
            new RepairFix
            {
                Id = "sys_files", Glyph = "", Title = L.T("Quét & sửa tệp hệ thống bị hỏng"),
                Description = L.T("Chạy DISM RestoreHealth rồi SFC /scannow để sửa lỗi màn hình xanh, ứng dụng Windows tự tắt, lỗi DLL…"),
                Risk = Risk.Safe, Duration = L.T("10–30 phút"), Action = SystemFiles
            },
            new RepairFix
            {
                Id = "explorer", Glyph = "", Title = L.T("Taskbar / Start menu bị đơ, không bấm được"),
                Description = L.T("Đăng ký lại Start menu, Shell và khởi động lại Windows Explorer."),
                Risk = Risk.Safe, Action = ExplorerFix
            },
            new RepairFix
            {
                Id = "iconcache", Glyph = "", Title = L.T("Biểu tượng, ảnh thu nhỏ hiển thị sai"),
                Description = L.T("Xóa và tạo lại bộ đệm biểu tượng (icon cache) và ảnh thu nhỏ (thumbnail cache)."),
                Risk = Risk.Safe, Action = IconCache
            },
            new RepairFix
            {
                Id = "usb_virus", Glyph = "", Title = L.T("USB bị virus làm ẩn hết dữ liệu"),
                Description = L.T("Hiện lại các tệp/thư mục bị ẩn và xóa lối tắt (shortcut) độc hại do virus tạo ra trên USB."),
                Risk = Risk.Safe, NeedsDrive = true, Duration = L.T("Tùy dung lượng USB"), Action = UsbVirus
            },
            new RepairFix
            {
                Id = "policies", Glyph = "", Title = L.T("Task Manager, Regedit, CMD bị khóa (do virus)"),
                Description = L.T("Gỡ các chính sách do virus đặt để chặn Task Manager, Registry Editor, CMD, Folder Options, hiện tệp ẩn."),
                Risk = Risk.Safe, Action = Policies
            },
            new RepairFix
            {
                Id = "exe_assoc", Glyph = "", Title = L.T("Không mở được tệp .exe hoặc lối tắt"),
                Description = L.T("Khôi phục liên kết mặc định của tệp .exe và .lnk khi virus hoặc phần mềm lạ làm hỏng."),
                Risk = Risk.Safe, Action = ExeAssoc
            },
            new RepairFix
            {
                Id = "audio", Glyph = "", Title = L.T("Mất âm thanh, loa báo dấu X đỏ"),
                Description = L.T("Khởi động lại dịch vụ âm thanh Windows Audio và Audio Endpoint Builder."),
                Risk = Risk.Safe, Action = Audio
            },
            new RepairFix
            {
                Id = "printer", Glyph = "", Title = L.T("Lệnh in bị kẹt, máy in không in"),
                Description = L.T("Xóa hàng đợi in bị kẹt và khởi động lại dịch vụ Print Spooler."),
                Risk = Risk.Safe, Action = Printer
            },
            new RepairFix
            {
                Id = "vnkey", Glyph = "", Title = L.T("Không gõ được tiếng Việt / bàn phím lỗi"),
                Description = L.T("Khởi động lại bộ xử lý nhập liệu của Windows (ctfmon) – thường sửa được lỗi Unikey/EVKey không gõ được dấu."),
                Risk = Risk.Safe, Action = Keyboard
            },
            new RepairFix
            {
                Id = "store", Glyph = "", Title = L.T("Microsoft Store không mở hoặc không tải được"),
                Description = L.T("Xóa bộ đệm Store (wsreset) và đăng ký lại ứng dụng Microsoft Store."),
                Risk = Risk.Safe, Duration = L.T("1–2 phút"), Action = Store
            },
            new RepairFix
            {
                Id = "search", Glyph = "", Title = L.T("Tìm kiếm Windows không ra kết quả"),
                Description = L.T("Đặt lại và xây dựng lại chỉ mục tìm kiếm. Windows sẽ lập chỉ mục lại trong nền."),
                Risk = Risk.Moderate, Action = SearchFix
            },
            new RepairFix
            {
                Id = "disk100", Glyph = "", Title = L.T("Ổ cứng luôn 100% (Disk 100%)"),
                Description = L.T("Tắt các dịch vụ gây tải ổ cứng nặng (Telemetry, SysMain trên ổ HDD). Có thể hoàn tác trong mục Tối ưu › Dịch vụ."),
                Risk = Risk.Moderate, Action = Disk100
            },
            new RepairFix
            {
                Id = "chkdsk", Glyph = "", Title = L.T("Kiểm tra lỗi ổ đĩa hệ thống"),
                Description = L.T("Quét lỗi hệ thống tệp của ổ C ngay khi máy đang chạy (không cần khởi động lại)."),
                Risk = Risk.Safe, Duration = L.T("2–10 phút"), Action = ChkDsk
            },
            new RepairFix
            {
                Id = "recycle", Glyph = "", Title = L.T("Thùng rác bị hỏng (Recycle Bin is corrupted)"),
                Description = L.T("Tạo lại thư mục Thùng rác trên các ổ đĩa. Lưu ý: các tệp trong Thùng rác sẽ bị xóa vĩnh viễn."),
                Risk = Risk.Moderate, Action = RecycleBin
            },
            new RepairFix
            {
                Id = "net_full", Glyph = "", Title = L.T("Đặt lại toàn bộ card mạng"),
                Description = L.T("Gỡ và cài lại tất cả card mạng (giống \"Network reset\" trong Cài đặt). Mạng VPN sẽ cần cài lại."),
                Risk = Risk.Advanced, NeedsRestart = true, Action = NetFull
            },
        };

        // ================= Các bước sửa lỗi =================

        private static async Task NetQuick(RepairContext c)
        {
            await c.Cmd("ipconfig /flushdns");
            await c.Cmd("netsh winhttp reset proxy");
            c.Info(L.T("› Tắt proxy trong Internet Options"));
            const string inet = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings";
            RegistryHelper.SetValue(inet, "ProxyEnable", 0, RegistryValueKind.DWord);
            RegistryHelper.DeleteValue(inet, "AutoConfigURL");
            await c.Cmd("ipconfig /release");
            await c.Cmd("ipconfig /renew");
            c.Info(L.T("Đã làm mới kết nối mạng."));
        }

        private static async Task NetReset(RepairContext c)
        {
            await c.Cmd("netsh winsock reset");
            await c.Cmd("netsh int ip reset");
            await c.Cmd("netsh int ipv6 reset");
            await c.Cmd("ipconfig /flushdns");
            c.Info(L.T("Hãy khởi động lại máy để hoàn tất."));
        }

        private static async Task TimeSync(RepairContext c)
        {
            RegistryHelper.SetValue(@"HKLM\SYSTEM\CurrentControlSet\Services\W32Time\Parameters", "Type", "NTP", RegistryValueKind.String);
            await c.Cmd("sc config w32time start= auto");
            await c.Cmd("net start w32time");
            await c.Cmd("w32tm /config /manualpeerlist:\"time.windows.com,0x9 time.google.com,0x9 pool.ntp.org,0x9\" /syncfromflags:manual /reliable:no /update");
            await c.Cmd("w32tm /resync /force");
            c.Info(L.T("Giờ hiện tại: ") + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
            c.Info(L.T("Mẹo: nếu cứ tắt máy là giờ lại sai, pin CMOS trên bo mạch chủ đã hết – nên thay pin (loại CR2032)."));
        }

        private static async Task WindowsUpdateReset(RepairContext c)
        {
            foreach (var s in new[] { "wuauserv", "bits", "cryptsvc", "msiserver" }) await c.Cmd($"net stop {s} /y");
            var sd = Path.Combine(Win, "SoftwareDistribution");
            var cr = Path.Combine(Environment.SystemDirectory, "catroot2");
            await c.Cmd($"if exist \"{sd}.old\" rd /s /q \"{sd}.old\"");
            await c.Cmd($"if exist \"{cr}.old\" rd /s /q \"{cr}.old\"");
            await c.Cmd($"ren \"{sd}\" SoftwareDistribution.old");
            await c.Cmd($"ren \"{cr}\" catroot2.old");
            foreach (var s in new[] { "cryptsvc", "bits", "wuauserv", "msiserver" }) await c.Cmd($"net start {s}");
            c.Info(L.T("Đã đặt lại Windows Update. Hãy vào Cài đặt › Windows Update và bấm \"Kiểm tra bản cập nhật\"."));
        }

        private static async Task SystemFiles(RepairContext c)
        {
            c.Info(L.T("Bước 1/2: DISM kiểm tra và sửa kho thành phần Windows (cần mạng Internet)…"));
            await c.Cmd("DISM /Online /Cleanup-Image /RestoreHealth");
            c.Info(L.T("Bước 2/2: SFC quét và sửa tệp hệ thống…"));
            await c.Run(Path.Combine(Environment.SystemDirectory, "sfc.exe"), "/scannow", Encoding.Unicode);
            c.Info(L.T("Hoàn tất. Nếu SFC báo đã sửa lỗi, hãy khởi động lại máy."));
        }

        private static async Task RestartExplorer(RepairContext c)
        {
            await c.Cmd("taskkill /f /im explorer.exe");
            await Task.Delay(2500);
            if (Process.GetProcessesByName("explorer").Length == 0)
            {
                c.Info(L.T("› Khởi động lại Explorer"));
                ProcessRunner.Open(Path.Combine(Win, "explorer.exe"));
            }
        }

        private static async Task ExplorerFix(RepairContext c)
        {
            await c.Ps(L.T("Đăng ký lại Start menu và Shell"),
                "Get-AppxPackage -AllUsers Microsoft.Windows.ShellExperienceHost, Microsoft.Windows.StartMenuExperienceHost | " +
                "ForEach-Object { Add-AppxPackage -DisableDevelopmentMode -Register \"$($_.InstallLocation)\\AppXManifest.xml\" -ErrorAction SilentlyContinue }; 'Xong.'");
            await RestartExplorer(c);
            c.Info(L.T("Đã khởi động lại Explorer."));
        }

        private static async Task IconCache(RepairContext c)
        {
            await c.Run(Path.Combine(Environment.SystemDirectory, "ie4uinit.exe"), "-show");
            await c.Cmd("taskkill /f /im explorer.exe");
            await Task.Delay(1000);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            int n = 0;
            var files = new List<string> { Path.Combine(local, "IconCache.db") };
            var exp = Path.Combine(local, @"Microsoft\Windows\Explorer");
            if (Directory.Exists(exp))
            {
                files.AddRange(Directory.GetFiles(exp, "iconcache_*.db"));
                files.AddRange(Directory.GetFiles(exp, "thumbcache_*.db"));
            }
            foreach (var f in files)
                if (File.Exists(f) && FileUtil.TryDeleteFile(new FileInfo(f))) n++;
            c.Info(L.F("Đã xóa {0} tệp bộ đệm.", n));
            await Task.Delay(1500);
            if (Process.GetProcessesByName("explorer").Length == 0) ProcessRunner.Open(Path.Combine(Win, "explorer.exe"));
            c.Info(L.T("Explorer đã khởi động lại. Biểu tượng sẽ được tạo lại dần."));
        }

        private static async Task UsbVirus(RepairContext c)
        {
            var drive = c.Parameter;
            if (string.IsNullOrEmpty(drive) || !Directory.Exists(drive)) { c.Info(L.T("Chưa chọn ổ USB hợp lệ.")); return; }
            var root = drive.TrimEnd('\\') + "\\";
            await c.Cmd($"attrib -h -s -r /s /d \"{root}*.*\"");
            int removed = 0;
            var suspicious = new[] { "cmd", "wscript", "cscript", "rundll32", "powershell", "mshta", ".vbs", ".js", ".bat", "%" };
            foreach (var lnk in Directory.GetFiles(root, "*.lnk"))
            {
                var (t, a) = IconHelper.ResolveShortcut(lnk);
                var all = ((t ?? "") + " " + (a ?? "")).ToLowerInvariant();
                if (suspicious.Any(s => all.Contains(s)))
                {
                    c.Info(L.T("Xóa lối tắt độc hại: ") + Path.GetFileName(lnk) + "  →  " + all.Trim());
                    if (FileUtil.TryDeleteFile(new FileInfo(lnk))) removed++;
                }
            }
            var autorun = Path.Combine(root, "autorun.inf");
            if (File.Exists(autorun) && FileUtil.TryDeleteFile(new FileInfo(autorun))) { c.Info(L.T("Xóa autorun.inf")); removed++; }
            var exes = Directory.GetFiles(root).Where(f => new[] { ".exe", ".vbs", ".js", ".bat", ".cmd", ".scr" }
                .Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();
            if (exes.Count > 0)
            {
                c.Info(L.T("Các tệp thực thi đáng ngờ ở thư mục gốc USB (hãy kiểm tra bằng phần mềm diệt virus):"));
                foreach (var e in exes) c.Info("   • " + Path.GetFileName(e));
            }
            c.Info(L.F("Hoàn tất. Đã hiện lại dữ liệu và xóa {0} mục độc hại.", removed));
        }

        private static Task Policies(RepairContext c)
        {
            var sysPol = @"\Software\Microsoft\Windows\CurrentVersion\Policies\System";
            var expPol = @"\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
            foreach (var hive in new[] { "HKCU", "HKLM" })
            {
                foreach (var v in new[] { "DisableTaskMgr", "DisableRegistryTools", "DisableCMD" })
                    RegistryHelper.DeleteValue(hive + sysPol, v);
                foreach (var v in new[] { "NoFolderOptions", "NoRun", "NoControlPanel", "NoViewContextMenu", "NoDrives", "NoViewOnDrive", "NoFind" })
                    RegistryHelper.DeleteValue(hive + expPol, v);
                RegistryHelper.DeleteValue(hive + @"\Software\Policies\Microsoft\Windows\System", "DisableCMD");
            }
            RegistryHelper.SetValue(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced\Folder\Hidden\SHOWALL", "CheckedValue", 1, RegistryValueKind.DWord);
            c.Info(L.T("Đã gỡ các chính sách chặn: Task Manager, Registry Editor, CMD, Folder Options, Run, Control Panel."));
            c.Info(L.T("Đã khôi phục tùy chọn \"Hiện tệp ẩn\"."));
            c.Info(L.T("Lưu ý: nên quét virus toàn bộ máy bằng Windows Security để xóa tận gốc."));
            Native.BroadcastSettingChange();
            return Task.CompletedTask;
        }

        private static Task ExeAssoc(RepairContext c)
        {
            RegistryHelper.SetValue(@"HKLM\SOFTWARE\Classes\.exe", "", "exefile", RegistryValueKind.String);
            RegistryHelper.SetValue(@"HKLM\SOFTWARE\Classes\exefile\shell\open\command", "", "\"%1\" %*", RegistryValueKind.String);
            RegistryHelper.SetValue(@"HKLM\SOFTWARE\Classes\.lnk", "", "lnkfile", RegistryValueKind.String);
            foreach (var ext in new[] { ".exe", ".lnk" })
            {
                RegistryHelper.DeleteKeyTree(@"HKCU\Software\Classes\" + ext);
                RegistryHelper.DeleteKeyTree(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\" + ext + @"\UserChoice");
            }
            c.Info(L.T("Đã khôi phục liên kết tệp .exe và .lnk về mặc định của Windows."));
            Native.BroadcastSettingChange();
            return Task.CompletedTask;
        }

        private static async Task Audio(RepairContext c)
        {
            await c.Cmd("sc config AudioEndpointBuilder start= auto");
            await c.Cmd("sc config Audiosrv start= auto");
            await c.Cmd("net stop Audiosrv /y");
            await c.Cmd("net stop AudioEndpointBuilder /y");
            await c.Cmd("net start AudioEndpointBuilder");
            await c.Cmd("net start Audiosrv");
            c.Info(L.T("Đã khởi động lại dịch vụ âm thanh. Nếu vẫn mất tiếng, hãy kiểm tra driver âm thanh trong Device Manager."));
        }

        private static async Task Printer(RepairContext c)
        {
            await c.Cmd("net stop spooler /y");
            var dir = Path.Combine(Environment.SystemDirectory, @"spool\PRINTERS");
            int n = 0;
            foreach (var f in FileUtil.EnumerateFiles(dir).ToList()) if (FileUtil.TryDeleteFile(f)) n++;
            c.Info(L.F("Đã xóa {0} lệnh in bị kẹt.", n));
            await c.Cmd("sc config spooler start= auto");
            await c.Cmd("net start spooler");
        }

        private static async Task Keyboard(RepairContext c)
        {
            await c.Cmd("taskkill /f /im ctfmon.exe");
            await Task.Delay(500);
            ProcessRunner.Open(Path.Combine(Environment.SystemDirectory, "ctfmon.exe"));
            c.Info(L.T("› Đã khởi động lại ctfmon.exe"));
            await c.Cmd("sc config TabletInputService start= demand");
            await c.Cmd("net start TabletInputService");
            c.Info(L.T("Mẹo khi dùng Unikey/EVKey:"));
            c.Info(L.T("   • Nếu không gõ được trong phần mềm chạy quyền Admin, hãy chạy Unikey/EVKey bằng \"Run as administrator\"."));
            c.Info(L.T("   • Kiểm tra bảng mã Unicode và kiểu gõ Telex/VNI trong Unikey."));
        }

        private static async Task Store(RepairContext c)
        {
            c.Info("› wsreset.exe");
            await Task.Run(() =>
            {
                try
                {
                    using (var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "wsreset.exe")) { UseShellExecute = false, CreateNoWindow = true }))
                        p?.WaitForExit(90000);
                }
                catch (Exception ex) { c.Info(L.T("Lỗi: ") + ex.Message); }
            });
            await c.Ps(L.T("Đăng ký lại Microsoft Store"),
                "Get-AppxPackage -AllUsers Microsoft.WindowsStore | ForEach-Object { Add-AppxPackage -DisableDevelopmentMode -Register \"$($_.InstallLocation)\\AppXManifest.xml\" -ErrorAction SilentlyContinue }; 'Xong.'");
            c.Info(L.T("Đã đặt lại Microsoft Store."));
        }

        private static async Task SearchFix(RepairContext c)
        {
            await c.Cmd("net stop wsearch /y");
            RegistryHelper.SetValue(@"HKLM\SOFTWARE\Microsoft\Windows Search", "SetupCompletedSuccessfully", 0, RegistryValueKind.DWord);
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Search\Data\Applications\Windows");
            int n = 0;
            foreach (var f in FileUtil.EnumerateFiles(data).ToList()) if (FileUtil.TryDeleteFile(f)) n++;
            c.Info(L.F("Đã xóa {0} tệp chỉ mục cũ.", n));
            await c.Cmd("sc config wsearch start= delayed-auto");
            await c.Cmd("net start wsearch");
            c.Info(L.T("Windows sẽ lập chỉ mục lại trong nền (có thể mất vài giờ, máy hơi chậm trong lúc này)."));
        }

        private static async Task Disk100(RepairContext c)
        {
            var media = SystemQuick.SystemDiskType();
            c.Info(L.T("Loại ổ hệ thống: ") + SystemQuick.Display(media));
            var services = ServiceCatalog.Build();
            foreach (var name in new[] { "DiagTrack", "dmwappushservice" })
            {
                var s = services.FirstOrDefault(x => x.Name == name);
                if (s != null && !s.IsDisabled) { c.Info(L.T("› Tắt dịch vụ ") + s.Title); await Task.Run(() => s.Disable()); }
            }
            if (media != "SSD" && media != "NVMe SSD")
            {
                var s = services.FirstOrDefault(x => x.Name == "SysMain");
                if (s != null && !s.IsDisabled) { c.Info(L.T("› Tắt dịch vụ SysMain (Superfetch)")); await Task.Run(() => s.Disable()); }
            }
            c.Info(L.T("Đã xong. Nếu ổ vẫn thường xuyên 100%:"));
            c.Info(L.T("   • Vào Tối ưu › Khởi động, tắt bớt ứng dụng khởi động cùng Windows."));
            c.Info(L.T("   • Kiểm tra sức khỏe ổ cứng ở mục Thông tin máy."));
            if (media == "HDD") c.Info(L.T("   • Giải pháp triệt để: nâng cấp lên ổ SSD (nhanh hơn 5–10 lần, chỉ từ vài trăm nghìn đồng)."));
        }

        private static async Task ChkDsk(RepairContext c)
        {
            var drive = FileUtil.SystemDrive.TrimEnd('\\');
            var r = await c.Cmd($"chkdsk {drive} /scan");
            c.Info(r.ExitCode == 0
                ? L.T("Không phát hiện lỗi hệ thống tệp.")
                : L.T("Phát hiện lỗi! Hãy mở CMD (Admin) và chạy: chkdsk ") + drive + L.T(" /f – rồi khởi động lại máy để sửa."));
        }

        private static async Task RecycleBin(RepairContext c)
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                bool ok;
                try { ok = d.DriveType == DriveType.Fixed && d.IsReady; } catch { ok = false; }
                if (!ok) continue;
                var p = Path.Combine(d.RootDirectory.FullName, "$Recycle.Bin");
                if (Directory.Exists(p)) await c.Cmd($"rd /s /q \"{p}\"");
            }
            c.Info(L.T("Đã tạo lại Thùng rác. Windows sẽ tự tạo thư mục mới khi bạn xóa tệp."));
        }

        private static async Task NetFull(RepairContext c)
        {
            await c.Cmd("netsh winsock reset");
            await c.Cmd("netcfg -d");
            c.Info(L.T("Hãy khởi động lại máy. Sau đó kết nối lại Wi-Fi (cần nhập lại mật khẩu)."));
        }
    }
}
