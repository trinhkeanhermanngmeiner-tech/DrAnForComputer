using System.Runtime.InteropServices;
using System.Windows.Media;
using BacSiMayTinh.Core;
using Microsoft.Win32;

namespace BacSiMayTinh.Modules
{
    public class ContextMenuItem : ObservableObject
    {
        public string Name { get; set; }
        public string Detail { get; set; }
        public string Location { get; set; }
        public string Kind { get; set; }
        public string FilePath { get; set; }
        public bool IsHandler { get; set; }
        public string RegPath { get; set; }   // khóa lệnh (verb) – dùng LegacyDisable
        public string Clsid { get; set; }     // tiện ích mở rộng – dùng danh sách Blocked

        private ImageSource _icon;
        public ImageSource Icon { get => _icon; set { Set(ref _icon, value); OnPropertyChanged(nameof(HasIcon)); } }
        public bool HasIcon => _icon != null;

        private bool _enabled;
        public bool IsEnabled { get => _enabled; set => Set(ref _enabled, value); }
    }

    /// <summary>Bật/tắt các mục do phần mềm khác thêm vào menu chuột phải (không xóa, có thể bật lại).</summary>
    public static class ContextMenuManager
    {
        private const string Blocked = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int SHLoadIndirectString(string source, StringBuilder outBuf, int cch, IntPtr reserved);

        private static string Resolve(string s)
        {
            if (string.IsNullOrEmpty(s) || !s.StartsWith("@")) return s;
            var sb = new StringBuilder(512);
            return SHLoadIndirectString(s, sb, sb.Capacity, IntPtr.Zero) == 0 ? sb.ToString() : null;
        }

        private static bool IsSystemFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            // Tên trần (không có đường dẫn) như "cmd.exe", "powershell.exe" là công cụ có sẵn của Windows – phần mềm khác luôn ghi đường dẫn đầy đủ.
            if (path.IndexOf('\\') < 0) return true;
            if (path.IndexOf(@"\Windows Defender\", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return path.StartsWith(win, StringComparison.OrdinalIgnoreCase)
                   || path.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Clean(string s) => (s ?? "").Replace("&", "").Trim();

        public static List<ContextMenuItem> GetItems()
        {
            var list = new List<ContextMenuItem>();
            var verbRoots = new[]
            {
                ("*\\shell", L.T("Tệp")), ("Directory\\shell", L.T("Thư mục")),
                ("Directory\\Background\\shell", L.T("Nền thư mục")), ("Drive\\shell", L.T("Ổ đĩa")),
            };
            foreach (var (root, loc) in verbRoots)
            {
                try
                {
                    using (var b = RegistryHelper.OpenBase("HKCR\\" + root, out var sub))
                    using (var k = b.OpenSubKey(sub))
                    {
                        if (k == null) continue;
                        foreach (var verb in k.GetSubKeyNames())
                        {
                            using (var vk = k.OpenSubKey(verb))
                            {
                                var cmd = vk?.OpenSubKey("command")?.GetValue("") as string;
                                if (string.IsNullOrWhiteSpace(cmd)) continue;
                                var exe = IconHelper.ExeFromCommand(cmd);
                                if (IsSystemFile(exe)) continue;
                                var name = Resolve(vk.GetValue("MUIVerb") as string) ?? Resolve(vk.GetValue("") as string);
                                list.Add(new ContextMenuItem
                                {
                                    Name = Clean(string.IsNullOrWhiteSpace(name) ? verb : name),
                                    Detail = exe, FilePath = exe, Location = loc, Kind = L.T("Lệnh"),
                                    RegPath = "HKCR\\" + root + "\\" + verb,
                                    IsEnabled = vk.GetValue("LegacyDisable") == null
                                });
                            }
                        }
                    }
                }
                catch (Exception ex) { Logger.Warn("Đọc menu chuột phải " + root + ": " + ex.Message); }
            }

            var handlerRoots = new[]
            {
                ("*\\shellex\\ContextMenuHandlers", L.T("Tệp")), ("AllFilesystemObjects\\shellex\\ContextMenuHandlers", L.T("Tệp")),
                ("Directory\\shellex\\ContextMenuHandlers", L.T("Thư mục")), ("Folder\\shellex\\ContextMenuHandlers", L.T("Thư mục")),
                ("Directory\\Background\\shellex\\ContextMenuHandlers", L.T("Nền thư mục")), ("Drive\\shellex\\ContextMenuHandlers", L.T("Ổ đĩa")),
            };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (root, loc) in handlerRoots)
            {
                try
                {
                    using (var b = RegistryHelper.OpenBase("HKCR\\" + root, out var sub))
                    using (var k = b.OpenSubKey(sub))
                    {
                        if (k == null) continue;
                        foreach (var hname in k.GetSubKeyNames())
                        {
                            string clsid;
                            using (var hk = k.OpenSubKey(hname)) clsid = hk?.GetValue("") as string;
                            if (string.IsNullOrEmpty(clsid) && hname.StartsWith("{")) clsid = hname;
                            if (string.IsNullOrEmpty(clsid) || !clsid.StartsWith("{")) continue;
                            var dll = RegistryHelper.GetValue(@"HKCR\CLSID\" + clsid + @"\InprocServer32", "") as string;
                            dll = string.IsNullOrEmpty(dll) ? null : Environment.ExpandEnvironmentVariables(dll.Trim('"'));
                            if (IsSystemFile(dll)) continue;
                            if (!seen.Add(clsid)) continue;
                            var name = RegistryHelper.GetValue(@"HKCR\CLSID\" + clsid, "") as string;
                            list.Add(new ContextMenuItem
                            {
                                Name = Clean(string.IsNullOrWhiteSpace(name) ? hname : name),
                                Detail = dll, FilePath = dll, Location = loc, Kind = L.T("Tiện ích mở rộng"),
                                IsHandler = true, Clsid = clsid,
                                IsEnabled = !RegistryHelper.ValueExists(Blocked, clsid)
                            });
                        }
                    }
                }
                catch (Exception ex) { Logger.Warn("Đọc menu chuột phải " + root + ": " + ex.Message); }
            }
            return list.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static void SetEnabled(ContextMenuItem item, bool enabled)
        {
            if (item.IsHandler)
            {
                if (enabled) RegistryHelper.DeleteValue(Blocked, item.Clsid);
                else RegistryHelper.SetValue(Blocked, item.Clsid, item.Name, RegistryValueKind.String);
            }
            else
            {
                if (enabled) RegistryHelper.DeleteValue(item.RegPath, "LegacyDisable");
                else RegistryHelper.SetValue(item.RegPath, "LegacyDisable", "", RegistryValueKind.String);
            }
            item.IsEnabled = enabled;
            Logger.Info((enabled ? "Bật" : "Tắt") + " mục menu chuột phải: " + item.Name);
        }
    }
}
