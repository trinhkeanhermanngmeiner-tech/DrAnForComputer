using System.Diagnostics;
using System.Xml.Linq;
using BacSiMayTinh.Core;

namespace BacSiMayTinh.Modules
{
    public class WifiProfile
    {
        public string Ssid { get; set; }
        public string Password { get; set; }
        public string Security { get; set; }
        public string PasswordText => string.IsNullOrEmpty(Password) ? L.T("(không có mật khẩu)") : Password;
    }

    public static class WifiPasswords
    {
        /// <summary>Xuất hồ sơ Wi-Fi ra XML tạm (không phụ thuộc ngôn ngữ Windows) rồi đọc mật khẩu.</summary>
        public static async Task<List<WifiProfile>> GetAsync()
        {
            var list = new List<WifiProfile>();
            var tmp = Path.Combine(Path.GetTempPath(), "bsmt_wifi_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                await ProcessRunner.CmdAsync($"netsh wlan export profile key=clear folder=\"{tmp}\"").ConfigureAwait(false);
                foreach (var f in Directory.GetFiles(tmp, "*.xml"))
                {
                    try
                    {
                        var x = XDocument.Load(f);
                        XNamespace ns = x.Root.Name.Namespace;
                        var name = x.Root.Element(ns + "SSIDConfig")?.Element(ns + "SSID")?.Element(ns + "name")?.Value
                                   ?? x.Root.Element(ns + "name")?.Value;
                        var sec = x.Root.Element(ns + "MSM")?.Element(ns + "security");
                        var auth = sec?.Element(ns + "authEncryption")?.Element(ns + "authentication")?.Value;
                        var key = sec?.Element(ns + "sharedKey")?.Element(ns + "keyMaterial")?.Value;
                        list.Add(new WifiProfile { Ssid = name, Password = key, Security = auth });
                    }
                    catch { }
                }
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }
            return list.OrderBy(w => w.Ssid).ToList();
        }
    }

    public static class HostsFile
    {
        public static string PathName => Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts");

        // Nội dung mặc định tự soạn (tương đương tệp hosts trống của Windows).
        public const string Default =
            "# Windows hosts file\r\n" +
            "# Each line: <IP address>  <host name>   (lines starting with # are comments)\r\n" +
            "# Example:\r\n" +
            "#   127.0.0.1    localhost\r\n" +
            "#   ::1          localhost\r\n";

        public static string Read()
        {
            try { return File.ReadAllText(PathName); } catch (Exception ex) { return "# " + L.T("Không đọc được tệp hosts:") + " " + ex.Message; }
        }

        public static void Save(string content)
        {
            var backup = Path.Combine(AppInfo.BackupDir, "hosts_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak");
            try { File.Copy(PathName, backup, true); } catch { }
            var attr = File.GetAttributes(PathName);
            if ((attr & FileAttributes.ReadOnly) != 0) File.SetAttributes(PathName, attr & ~FileAttributes.ReadOnly);
            File.WriteAllText(PathName, content, new UTF8Encoding(false));
            Logger.Info("Lưu tệp hosts (sao lưu: " + backup + ")");
            ProcessRunner.CmdAsync("ipconfig /flushdns");
        }
    }

    public class DnsOption
    {
        public string Name { get; set; }
        public string Servers { get; set; }
        public string Description { get; set; }
        public override string ToString() => Name;
    }

    public static class DnsChanger
    {
        public static List<DnsOption> Options => new List<DnsOption>
        {
            new DnsOption { Name = L.T("Tự động (theo nhà mạng)"), Servers = "", Description = L.T("Dùng DNS do nhà mạng/modem cấp.") },
            new DnsOption { Name = "Cloudflare (1.1.1.1)", Servers = "1.1.1.1,1.0.0.1", Description = L.T("Nhanh, bảo mật, không ghi nhật ký.") },
            new DnsOption { Name = "Google (8.8.8.8)", Servers = "8.8.8.8,8.8.4.4", Description = L.T("Ổn định, phổ biến nhất.") },
            new DnsOption { Name = L.T("Cloudflare chặn mã độc (1.1.1.2)"), Servers = "1.1.1.2,1.0.0.2", Description = L.T("Tự động chặn các trang lừa đảo, mã độc.") },
            new DnsOption { Name = L.T("Cloudflare gia đình (1.1.1.3)"), Servers = "1.1.1.3,1.0.0.3", Description = L.T("Chặn mã độc và nội dung người lớn – phù hợp máy cho trẻ em.") },
            new DnsOption { Name = "Quad9 (9.9.9.9)", Servers = "9.9.9.9,149.112.112.112", Description = L.T("Chặn tên miền độc hại, bảo vệ quyền riêng tư.") },
        };

        public static Task<RunResult> ApplyAsync(DnsOption o)
        {
            string action = string.IsNullOrEmpty(o.Servers)
                ? "Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ResetServerAddresses"
                : $"Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ServerAddresses {o.Servers}";
            Logger.Info("Đổi DNS: " + o.Name);
            return ProcessRunner.PowerShellAsync(
                "Get-NetAdapter | Where-Object { $_.Status -eq 'Up' } | ForEach-Object { " + action + " }; Clear-DnsClientCache; 'OK'");
        }
    }

    public class ProcItem : Core.ObservableObject
    {
        public int Pid { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        public long Memory { get; set; }
        public string MemoryText => FileUtil.FormatSize(Memory);
        public bool HasWindow { get; set; }

        private bool _checked;
        public bool IsChecked { get => _checked; set => Set(ref _checked, value); }
    }

    /// <summary>Tăng tốc chơi game: đóng ứng dụng nền, dồn RAM, bật chế độ nguồn hiệu suất cao tạm thời.</summary>
    public static class GameBooster
    {
        private static readonly HashSet<string> Protected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "explorer", "dwm",
            "fontdrvhost", "sihost", "ctfmon", "RuntimeBroker", "SearchHost", "SearchUI", "StartMenuExperienceHost",
            "ShellExperienceHost", "TextInputHost", "SecurityHealthService", "SecurityHealthSystray", "MsMpEng", "NisSrv",
            "audiodg", "conhost", "dllhost", "spoolsv", "taskhostw", "Memory Compression", "Registry", "WmiPrvSE",
            "BacSiMayTinh", "DrAnForComputer", "UniKeyNT", "EVKey64", "EVKey", "Unikey", "steam", "steamwebhelper", "EpicGamesLauncher",
            "Battle.net", "RiotClientServices", "vgc", "vgtray", "nvcontainer", "NVDisplay.Container", "amdow", "RadeonSoftware",
            "igfxEM", "atiesrxx", "atieclxx", "WUDFHost", "smartscreen", "SgrmBroker", "LsaIso", "ApplicationFrameHost",
            "SearchApp", "SearchIndexer", "ShellHost", "Widgets", "WidgetService", "LockApp", "SystemSettings", "UserOOBEBroker",
        };

        public static bool Active { get; private set; }

        public static List<ProcItem> Candidates()
        {
            var list = new List<ProcItem>();
            int self = Process.GetCurrentProcess().Id;
            int session = Process.GetCurrentProcess().SessionId;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == self || p.SessionId != session || Protected.Contains(p.ProcessName)) continue;
                    long mem = p.WorkingSet64;
                    if (mem < 40L * 1024 * 1024) continue;
                    string title = "";
                    try { title = p.MainWindowTitle; } catch { }
                    list.Add(new ProcItem { Pid = p.Id, Name = p.ProcessName, Title = title, Memory = mem, HasWindow = !string.IsNullOrEmpty(title) });
                }
                catch { }
                finally { p.Dispose(); }
            }
            // Gộp các tiến trình cùng tên (ví dụ Chrome có nhiều tiến trình).
            return list.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ProcItem
                {
                    Pid = g.First().Pid, Name = g.Key, Memory = g.Sum(x => x.Memory),
                    Title = g.Select(x => x.Title).FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? "",
                    HasWindow = g.Any(x => x.HasWindow)
                })
                .OrderByDescending(x => x.Memory).ToList();
        }

        public static async Task<(int closed, long freed)> StartAsync(IEnumerable<ProcItem> toClose)
        {
            int closed = 0;
            foreach (var item in toClose)
            {
                foreach (var p in Process.GetProcessesByName(item.Name))
                {
                    try
                    {
                        if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow() && p.WaitForExit(3000)) { closed++; continue; }
                        p.Kill();
                        closed++;
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            long freed = 0;
            await Task.Run(() =>
            {
                if (!PowerPlan.IsHighPerformance())
                {
                    PowerPlan.ApplyHighPerformance();
                    UndoStore.Current.Misc["gameboost_power"] = "1";
                    UndoStore.SaveCurrent();
                }
                freed = MemoryCleaner.Free();
            });
            Active = true;
            Logger.Info($"Bật tăng tốc game: đóng {closed} tiến trình, giải phóng {FileUtil.FormatSize(freed)}");
            return (closed, freed);
        }

        public static async Task StopAsync()
        {
            await Task.Run(() =>
            {
                if (UndoStore.Current.Misc.Remove("gameboost_power")) PowerPlan.Restore();
                UndoStore.SaveCurrent();
            });
            Active = false;
            Logger.Info("Tắt tăng tốc game");
        }
    }

    /// <summary>Đăng ký các tác vụ Task Scheduler cho ứng dụng.</summary>
    public static class Scheduler
    {
        private const string Folder = AppInfo.TaskName;

        public static async Task SetStartupAsync(bool enabled, string exe = null)
        {
            exe = exe ?? AppInfo.ExePath;
            var name = Folder + "\\KhoiDong";
            if (enabled)
                await ProcessRunner.CmdAsync($"schtasks /create /tn \"{name}\" /tr \"\\\"{exe}\\\" --tray\" /sc onlogon /rl highest /f").ConfigureAwait(false);
            else
                await ProcessRunner.CmdAsync($"schtasks /delete /tn \"{name}\" /f").ConfigureAwait(false);
        }

        public static async Task SetAutoCleanAsync(string mode, string exe = null)
        {
            exe = exe ?? AppInfo.ExePath;
            var name = Folder + "\\DonRacTuDong";
            await ProcessRunner.CmdAsync($"schtasks /delete /tn \"{name}\" /f").ConfigureAwait(false);
            if (mode == "daily")
                await ProcessRunner.CmdAsync($"schtasks /create /tn \"{name}\" /tr \"\\\"{exe}\\\" --autoclean\" /sc daily /st 12:00 /rl highest /f").ConfigureAwait(false);
            else if (mode == "weekly")
                await ProcessRunner.CmdAsync($"schtasks /create /tn \"{name}\" /tr \"\\\"{exe}\\\" --autoclean\" /sc weekly /d SUN /st 12:00 /rl highest /f").ConfigureAwait(false);
        }

        /// <summary>Xóa mọi tác vụ của ứng dụng (cả tên cũ của bản 1.0).</summary>
        public static async Task RemoveAllAsync()
        {
            foreach (var folder in new[] { Folder, AppInfo.OldId })
            {
                await ProcessRunner.CmdAsync($"schtasks /delete /tn \"{folder}\\KhoiDong\" /f").ConfigureAwait(false);
                await ProcessRunner.CmdAsync($"schtasks /delete /tn \"{folder}\\DonRacTuDong\" /f").ConfigureAwait(false);
                try
                {
                    dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
                    svc.Connect();
                    svc.GetFolder("\\").DeleteFolder(folder, 0);
                }
                catch { }
            }
        }

        /// <summary>Bản 1.1 đổi tên ứng dụng: chuyển các tác vụ cũ sang tên mới (chạy một lần).</summary>
        public static async Task MigrateAsync()
        {
            var s = AppSettings.Current;
            if (s.TasksMigrated) return;
            var oldStart = await ProcessRunner.CmdAsync($"schtasks /query /tn \"{AppInfo.OldId}\\KhoiDong\"").ConfigureAwait(false);
            var oldClean = await ProcessRunner.CmdAsync($"schtasks /query /tn \"{AppInfo.OldId}\\DonRacTuDong\"").ConfigureAwait(false);
            if (oldStart.Ok || oldClean.Ok)
            {
                foreach (var folder in new[] { AppInfo.OldId })
                {
                    await ProcessRunner.CmdAsync($"schtasks /delete /tn \"{folder}\\KhoiDong\" /f").ConfigureAwait(false);
                    await ProcessRunner.CmdAsync($"schtasks /delete /tn \"{folder}\\DonRacTuDong\" /f").ConfigureAwait(false);
                    try
                    {
                        dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
                        svc.Connect();
                        svc.GetFolder("\\").DeleteFolder(folder, 0);
                    }
                    catch { }
                }
                if (s.StartWithWindows) await SetStartupAsync(true).ConfigureAwait(false);
                if (s.AutoClean != "off") await SetAutoCleanAsync(s.AutoClean).ConfigureAwait(false);
            }
            s.TasksMigrated = true;
            AppSettings.SaveCurrent();
        }
    }
}