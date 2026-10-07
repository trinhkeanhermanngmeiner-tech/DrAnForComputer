using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Media;
using BacSiMayTinh.Core;
using Microsoft.Win32;

namespace BacSiMayTinh.Modules
{
    public class InstalledApp : ObservableObject
    {
        public string Name { get; set; }
        public string Publisher { get; set; }
        public string Version { get; set; }
        public DateTime? InstallDate { get; set; }
        public long SizeBytes { get; set; }
        public string UninstallString { get; set; }
        public string QuietUninstallString { get; set; }
        public string InstallLocation { get; set; }
        public string IconPath { get; set; }
        public string RegistryKey { get; set; }

        private ImageSource _icon;
        public ImageSource Icon { get => _icon; set { Set(ref _icon, value); OnPropertyChanged(nameof(HasIcon)); } }
        public bool HasIcon => _icon != null;

        public string SizeText => SizeBytes > 0 ? FileUtil.FormatSize(SizeBytes) : "";
        public string DateText => InstallDate?.ToString("dd/MM/yyyy") ?? "";
        public string Detail => string.Join("  ·  ", new[] { Publisher, string.IsNullOrEmpty(Version) ? null : "v" + Version }.Where(x => !string.IsNullOrEmpty(x)));
    }

    public class Leftover : ObservableObject
    {
        public string Path { get; set; }
        public bool IsRegistry { get; set; }
        public string Kind => IsRegistry ? "Registry" : L.T("Thư mục");
        public long Size { get; set; }
        public string SizeText => IsRegistry ? "" : FileUtil.FormatSize(Size);

        private bool _checked = true;
        public bool IsChecked { get => _checked; set => Set(ref _checked, value); }
    }

    public static class UninstallerService
    {
        private static readonly string[] Roots =
        {
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall",
        };

        public static List<InstalledApp> List()
        {
            var apps = new List<InstalledApp>();
            foreach (var root in Roots)
            {
                try
                {
                    using (var b = RegistryHelper.OpenBase(root, out var sub))
                    using (var k = b.OpenSubKey(sub))
                    {
                        if (k == null) continue;
                        foreach (var n in k.GetSubKeyNames())
                        {
                            try
                            {
                                using (var a = k.OpenSubKey(n))
                                {
                                    if (a == null) continue;
                                    var name = a.GetValue("DisplayName") as string;
                                    var un = a.GetValue("UninstallString") as string;
                                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(un)) continue;
                                    if (RegistryHelper.ValueEquals(a.GetValue("SystemComponent"), 1)) continue;
                                    if (a.GetValue("ParentKeyName") != null) continue;
                                    var rt = a.GetValue("ReleaseType") as string;
                                    if (rt != null && (rt.Contains("Update") || rt.Contains("Hotfix"))) continue;

                                    var app = new InstalledApp
                                    {
                                        Name = name.Trim(),
                                        Publisher = (a.GetValue("Publisher") as string)?.Trim(),
                                        Version = (a.GetValue("DisplayVersion") as string)?.Trim(),
                                        UninstallString = un,
                                        QuietUninstallString = a.GetValue("QuietUninstallString") as string,
                                        InstallLocation = (a.GetValue("InstallLocation") as string)?.Trim().Trim('"'),
                                        IconPath = a.GetValue("DisplayIcon") as string,
                                        RegistryKey = root + "\\" + n,
                                    };
                                    var size = a.GetValue("EstimatedSize");
                                    if (size is int kb) app.SizeBytes = kb * 1024L;
                                    var d = a.GetValue("InstallDate") as string;
                                    if (d != null && DateTime.TryParseExact(d, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) app.InstallDate = dt;
                                    apps.Add(app);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            return apps.GroupBy(a => a.Name + "|" + a.Version, StringComparer.OrdinalIgnoreCase)
                       .Select(g => g.First())
                       .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                       .ToList();
        }

        public static string IconFile(InstalledApp app)
        {
            var p = app.IconPath;
            if (!string.IsNullOrWhiteSpace(p))
            {
                p = p.Trim();
                int comma = p.LastIndexOf(',');
                if (comma > 2 && !p.Substring(comma).Contains("\\")) p = p.Substring(0, comma);
                p = Environment.ExpandEnvironmentVariables(p.Trim('"'));
                if (File.Exists(p) && !p.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) return p;
            }
            var exe = IconHelper.ExeFromCommand(app.UninstallString);
            if (exe != null && File.Exists(exe) && !exe.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase)) return exe;
            return null;
        }

        public static bool StillInstalled(InstalledApp app) => RegistryHelper.KeyExists(app.RegistryKey);

        /// <summary>Chạy trình gỡ cài đặt của phần mềm.</summary>
        public static Process StartUninstall(InstalledApp app)
        {
            var (file, args) = ParseCommand(app.UninstallString);
            Logger.Info($"Gỡ cài đặt '{app.Name}': {file} {args}");
            return Process.Start(new ProcessStartInfo(Environment.ExpandEnvironmentVariables(file), args) { UseShellExecute = true });
        }

        /// <summary>Tách dòng lệnh gỡ cài đặt thành (tệp, tham số); MsiExec /I được đổi thành /X.</summary>
        public static (string file, string args) ParseCommand(string uninstallString)
        {
            var cmd = uninstallString.Trim();
            string file, args;
            var msi = Regex.Match(cmd, @"msiexec(\.exe)?\s+(.*)", RegexOptions.IgnoreCase);
            if (msi.Success)
            {
                file = Path.Combine(Environment.SystemDirectory, "msiexec.exe");
                args = Regex.Replace(msi.Groups[2].Value, @"/I\s*\{", "/X{", RegexOptions.IgnoreCase);
            }
            else if (cmd.StartsWith("\""))
            {
                int e = cmd.IndexOf('"', 1);
                file = cmd.Substring(1, e - 1);
                args = cmd.Substring(e + 1).Trim();
            }
            else
            {
                int i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (i > 0) { file = cmd.Substring(0, i + 4); args = cmd.Substring(i + 4).Trim(); }
                else
                {
                    int sp = cmd.IndexOf(' ');
                    file = sp > 0 ? cmd.Substring(0, sp) : cmd;
                    args = sp > 0 ? cmd.Substring(sp + 1) : "";
                }
            }
            return (file, args);
        }

        private static string Norm(string s) => Regex.Replace((s ?? "").ToLowerInvariant(), @"[^a-z0-9]", "");

        private static readonly string[] ProtectedNames =
        {
            "microsoft", "windows", "common files", "windowsapps", "internet explorer", "windows defender", "system32",
            "packages", "temp", "programs", "nvidia corporation", "intel", "amd", "google", "mozilla", "realtek"
        };

        /// <summary>Tìm thư mục và khóa registry còn sót lại sau khi gỡ phần mềm.</summary>
        public static List<Leftover> ScanLeftovers(InstalledApp app)
        {
            var result = new List<Leftover>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string nameNorm = Norm(Regex.Replace(app.Name, @"\s*[\(\[]?v?\d+(\.\d+)+.*$", ""));
            string fullNorm = Norm(app.Name);
            string pubNorm = Norm(app.Publisher);
            if (nameNorm.Length < 3) nameNorm = fullNorm;
            if (nameNorm.Length < 3) return result;

            // 2 = trùng khớp chính xác (được chọn sẵn), 1 = gần giống (để người dùng tự quyết), 0 = không khớp
            int Score(string folder)
            {
                var f = Norm(folder);
                if (f.Length < 3) return 0;
                if (f == nameNorm || f == fullNorm) return 2;
                return f.StartsWith(nameNorm) && f.Length <= nameNorm.Length + 6 ? 1 : 0;
            }

            void AddDir(string path, bool check)
            {
                try
                {
                    if (!Directory.Exists(path) || !seen.Add(path)) return;
                    var leaf = Path.GetFileName(path.TrimEnd('\\')).ToLowerInvariant();
                    if (ProtectedNames.Contains(leaf)) return;
                    if (path.TrimEnd('\\').Count(c => c == '\\') < 2) return;
                    result.Add(new Leftover { Path = path, Size = FileUtil.DirectorySize(path), IsChecked = check });
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(app.InstallLocation)) AddDir(app.InstallLocation, true);

            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
                Path.Combine(Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) ?? "", "LocalLow"),
            }.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)).Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var root in roots)
            {
                try
                {
                    foreach (var d in Directory.GetDirectories(root))
                    {
                        var leaf = Path.GetFileName(d);
                        int sc = Score(leaf);
                        if (sc > 0) AddDir(d, sc == 2);
                        else if (pubNorm.Length >= 3 && Norm(leaf) == pubNorm)
                        {
                            try
                            {
                                foreach (var d2 in Directory.GetDirectories(d))
                                {
                                    int s2 = Score(Path.GetFileName(d2));
                                    if (s2 > 0) AddDir(d2, s2 == 2);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            // Registry
            foreach (var soft in new[] { @"HKCU\Software", @"HKLM\SOFTWARE", @"HKLM\SOFTWARE\WOW6432Node" })
            {
                try
                {
                    using (var b = RegistryHelper.OpenBase(soft, out var sub))
                    using (var k = b.OpenSubKey(sub))
                    {
                        if (k == null) continue;
                        foreach (var n in k.GetSubKeyNames())
                        {
                            if (ProtectedNames.Contains(n.ToLowerInvariant()) || n.Equals("Classes", StringComparison.OrdinalIgnoreCase) || n.Equals("Policies", StringComparison.OrdinalIgnoreCase)) continue;
                            int sc = Score(n);
                            if (sc > 0) { var p = soft + "\\" + n; if (seen.Add(p)) result.Add(new Leftover { Path = p, IsRegistry = true, IsChecked = Norm(n) == fullNorm }); }
                            else if (pubNorm.Length >= 3 && Norm(n) == pubNorm)
                            {
                                using (var pk = k.OpenSubKey(n))
                                    if (pk != null)
                                        foreach (var n2 in pk.GetSubKeyNames())
                                        {
                                            int s2 = Score(n2);
                                            if (s2 > 0) { var p = soft + "\\" + n + "\\" + n2; if (seen.Add(p)) result.Add(new Leftover { Path = p, IsRegistry = true, IsChecked = Norm(n2) == fullNorm }); }
                                        }
                            }
                        }
                    }
                }
                catch { }
            }
            return result;
        }

        /// <summary>Xóa phần còn sót: thư mục vào Thùng rác, registry được sao lưu (.reg) trước khi xóa.</summary>
        public static int DeleteLeftovers(IEnumerable<Leftover> items)
        {
            int n = 0;
            foreach (var l in items)
            {
                try
                {
                    if (l.IsRegistry)
                    {
                        var file = Path.Combine(AppInfo.BackupDir, "reg_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + n + ".reg");
                        var full = l.Path.Replace("HKCU\\", "HKEY_CURRENT_USER\\").Replace("HKLM\\", "HKEY_LOCAL_MACHINE\\");
                        ProcessRunner.RunAsync(Path.Combine(Environment.SystemDirectory, "reg.exe"), $"export \"{full}\" \"{file}\" /y").Wait();
                        RegistryHelper.DeleteKeyTree(l.Path);
                        Logger.Info("Xóa khóa còn sót " + l.Path + " (sao lưu: " + file + ")");
                        n++;
                    }
                    else if (FileUtil.SendToRecycleBin(l.Path))
                    {
                        Logger.Info("Xóa thư mục còn sót vào Thùng rác: " + l.Path);
                        n++;
                    }
                }
                catch (Exception ex) { Logger.Error("Xóa phần còn sót " + l.Path, ex); }
            }
            return n;
        }
    }
}
