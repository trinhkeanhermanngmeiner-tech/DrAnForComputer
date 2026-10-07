using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BacSiMayTinh.Core;
using Microsoft.Win32;

namespace BacSiMayTinh.Modules
{
    public static class IconHelper
    {
        private static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Lấy biểu tượng của tệp (có thể gọi từ luồng nền – ảnh đã được Freeze).</summary>
        public static ImageSource FromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            lock (Cache)
                if (Cache.TryGetValue(path, out var c)) return c;
            ImageSource img = null;
            try
            {
                if (File.Exists(path))
                    using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(path))
                        if (ico != null)
                        {
                            var bs = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                            bs.Freeze();
                            img = bs;
                        }
            }
            catch { }
            lock (Cache) Cache[path] = img;
            return img;
        }

        /// <summary>Tách đường dẫn tệp thực thi từ một dòng lệnh.</summary>
        public static string ExeFromCommand(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return null;
            cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
            if (cmd.StartsWith("\""))
            {
                int e = cmd.IndexOf('"', 1);
                return e > 1 ? cmd.Substring(1, e - 1) : cmd.Trim('"');
            }
            int i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (i > 0) return cmd.Substring(0, i + 4);
            int sp = cmd.IndexOf(' ');
            return sp > 0 ? cmd.Substring(0, sp) : cmd;
        }

        /// <summary>Đọc đích của tệp lối tắt .lnk.</summary>
        public static (string target, string args) ResolveShortcut(string lnk)
        {
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(t);
                dynamic sc = shell.CreateShortcut(lnk);
                string target = sc.TargetPath;
                string args = sc.Arguments;
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(sc);
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                return (target, args);
            }
            catch { return (null, null); }
        }
    }

    public class StartupItem : ObservableObject
    {
        public string Name { get; set; }
        public string Command { get; set; }
        public string ExePath { get; set; }
        public string Publisher { get; set; }
        public string Location { get; set; }
        public string SourceKey { get; set; }      // khóa Run hoặc thư mục Startup
        public string ApprovedKey { get; set; }    // khóa StartupApproved tương ứng
        public string ValueName { get; set; }      // tên giá trị registry hoặc tên tệp lối tắt
        public bool IsFolder { get; set; }
        public bool IsTask { get; set; }
        public string TaskPath { get; set; }

        private ImageSource _icon;
        public ImageSource Icon { get => _icon; set { Set(ref _icon, value); OnPropertyChanged(nameof(HasIcon)); } }
        public bool HasIcon => _icon != null;

        private bool _enabled;
        public bool IsEnabled { get => _enabled; set => Set(ref _enabled, value); }

        public string Detail => (string.IsNullOrEmpty(Publisher) ? "" : Publisher + "  ·  ") + (ExePath ?? Command);
    }

    /// <summary>Quản lý ứng dụng khởi động cùng Windows – tương thích với tab Startup của Task Manager.</summary>
    public static class StartupManager
    {
        private const string ApprovedBase = @"\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        public static List<StartupItem> GetItems()
        {
            var list = new List<StartupItem>();
            ReadRun(list, @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "HKCU" + ApprovedBase + "Run", L.T("Registry (người dùng)"));
            ReadRun(list, @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM" + ApprovedBase + "Run", L.T("Registry (mọi người dùng)"));
            if (Environment.Is64BitOperatingSystem)
                ReadRun(list, @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM" + ApprovedBase + "Run32", L.T("Registry (mọi người dùng)"));
            ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "HKCU" + ApprovedBase + "StartupFolder", L.T("Thư mục Startup (người dùng)"));
            ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "HKLM" + ApprovedBase + "StartupFolder", L.T("Thư mục Startup (mọi người dùng)"));
            ReadTasks(list);
            return list.OrderByDescending(i => i.IsEnabled).ThenBy(i => i.Name).ToList();
        }

        private static void ReadRun(List<StartupItem> list, string runKey, string approved, string location)
        {
            try
            {
                using (var b = RegistryHelper.OpenBase(runKey, out var sub))
                using (var k = b.OpenSubKey(sub))
                {
                    if (k == null) return;
                    foreach (var name in k.GetValueNames())
                    {
                        if (string.IsNullOrEmpty(name)) continue;
                        var cmd = k.GetValue(name)?.ToString();
                        if (string.IsNullOrWhiteSpace(cmd)) continue;
                        var exe = IconHelper.ExeFromCommand(cmd);
                        list.Add(Make(name, cmd, exe, location, runKey, approved, name, false));
                    }
                }
            }
            catch (Exception ex) { Logger.Warn("Đọc " + runKey + ": " + ex.Message); }
        }

        private static void ReadFolder(List<StartupItem> list, string dir, string approved, string location)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (var f in Directory.GetFiles(dir))
                {
                    var fn = Path.GetFileName(f);
                    if (fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    string exe = f, cmd = f;
                    if (f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        var (t, a) = IconHelper.ResolveShortcut(f);
                        if (!string.IsNullOrEmpty(t)) { exe = t; cmd = t + (string.IsNullOrEmpty(a) ? "" : " " + a); }
                    }
                    list.Add(Make(Path.GetFileNameWithoutExtension(f), cmd, exe, location, dir, approved, fn, true));
                }
            }
            catch (Exception ex) { Logger.Warn("Đọc thư mục Startup: " + ex.Message); }
        }

        private static StartupItem Make(string name, string cmd, string exe, string location, string source, string approved, string valueName, bool folder)
        {
            var item = new StartupItem
            {
                Name = name, Command = cmd, ExePath = exe, Location = location,
                SourceKey = source, ApprovedKey = approved, ValueName = valueName, IsFolder = folder
            };
            try
            {
                if (exe != null && File.Exists(exe))
                {
                    var vi = FileVersionInfo.GetVersionInfo(exe);
                    item.Publisher = vi.CompanyName?.Trim();
                    if (!string.IsNullOrWhiteSpace(vi.FileDescription) && vi.FileDescription.Length < 60) item.Name = vi.FileDescription.Trim();
                }
            }
            catch { }
            item.IsEnabled = ReadApproved(approved, valueName);
            return item;
        }


        // ---- Tác vụ Task Scheduler chạy khi đăng nhập/khởi động (ngoài thư mục \Microsoft của Windows) ----
        private static dynamic Scheduler()
        {
            dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            svc.Connect();
            return svc;
        }

        private static void ReadTasks(List<StartupItem> list)
        {
            try { WalkFolder(Scheduler().GetFolder("\\"), list); }
            catch (Exception ex) { Logger.Warn("Đọc Task Scheduler: " + ex.Message); }
        }

        private static void WalkFolder(dynamic folder, List<StartupItem> list)
        {
            string path = folder.Path;
            if (path.StartsWith("\\Microsoft", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("\\" + AppInfo.Id, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("\\" + AppInfo.OldId, StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                foreach (dynamic t in folder.GetTasks(1))
                {
                    try
                    {
                        dynamic def = t.Definition;
                        bool atStart = false;
                        foreach (dynamic trg in def.Triggers) { int type = trg.Type; if (type == 8 || type == 9) atStart = true; }
                        if (!atStart) continue;
                        string exe = null, args = null;
                        foreach (dynamic a in def.Actions) { if ((int)a.Type == 0) { exe = a.Path; args = a.Arguments; break; } }
                        if (string.IsNullOrWhiteSpace(exe)) continue;
                        exe = Environment.ExpandEnvironmentVariables(exe.Trim().Trim('"'));
                        var item = new StartupItem
                        {
                            Name = (string)t.Name, ExePath = exe, Command = exe + (string.IsNullOrEmpty(args) ? "" : " " + args),
                            Location = L.T("Task Scheduler"), IsTask = true, TaskPath = (string)t.Path, IsEnabled = (bool)t.Enabled
                        };
                        try { if (File.Exists(exe)) item.Publisher = FileVersionInfo.GetVersionInfo(exe).CompanyName?.Trim(); } catch { }
                        list.Add(item);
                    }
                    catch { }
                }
                foreach (dynamic sub in folder.GetFolders(0)) WalkFolder(sub, list);
            }
            catch { }
        }

        private static (dynamic folder, string name) TaskLocation(string taskPath)
        {
            int i = taskPath.LastIndexOf('\\');
            var folderPath = i <= 0 ? "\\" : taskPath.Substring(0, i);
            return (Scheduler().GetFolder(folderPath), taskPath.Substring(i + 1));
        }
        private static bool ReadApproved(string approvedKey, string valueName)
        {
            var v = RegistryHelper.GetValue(approvedKey, valueName) as byte[];
            if (v == null || v.Length == 0) return true;
            return (v[0] & 1) == 0; // 02/06 = bật, 03/07 = tắt
        }

        public static void SetEnabled(StartupItem item, bool enabled)
        {
            if (item.IsTask)
            {
                var (folder, name) = TaskLocation(item.TaskPath);
                folder.GetTask(name).Enabled = enabled;
                item.IsEnabled = enabled;
                Logger.Info((enabled ? "Bật" : "Tắt") + " tác vụ khởi động: " + item.TaskPath);
                return;
            }
            var data = new byte[12];
            if (enabled) data[0] = 0x02;
            else
            {
                data[0] = 0x03;
                var ft = BitConverter.GetBytes(DateTime.Now.ToFileTime());
                Array.Copy(ft, 0, data, 4, 8);
            }
            RegistryHelper.SetValue(item.ApprovedKey, item.ValueName, data, RegistryValueKind.Binary);
            item.IsEnabled = enabled;
            Logger.Info((enabled ? "Bật" : "Tắt") + " khởi động cùng Windows: " + item.Name);
        }

        public static void Delete(StartupItem item)
        {
            Logger.Info("Xóa mục khởi động: " + item.Name + " | " + item.Command);
            if (item.IsTask)
            {
                var (folder, name) = TaskLocation(item.TaskPath);
                folder.DeleteTask(name, 0);
                return;
            }
            if (item.IsFolder) FileUtil.SendToRecycleBin(Path.Combine(item.SourceKey, item.ValueName));
            else RegistryHelper.DeleteValue(item.SourceKey, item.ValueName);
            RegistryHelper.DeleteValue(item.ApprovedKey, item.ValueName);
        }
    }
}
