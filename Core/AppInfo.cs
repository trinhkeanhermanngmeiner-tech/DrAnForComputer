using System.Diagnostics;
using System.Security.Principal;

namespace BacSiMayTinh.Core
{
    public static class AppInfo
    {
        public const string Name = "DrAn For Computer";
        public const string Author = "BS. Trịnh Kế An";
        public const string Version = "1.1.0";
        public static string Edition => L.T("Bản miễn phí");
        public static string Slogan => L.T("Bác sĩ chăm sóc máy tính của bạn");
        public const string Id = "DrAnForComputer";
        public const string TaskName = Id;
        public const string OldId = "BacSiMayTinh"; // tên thư mục/tác vụ của bản 1.0

        // Thông tin ủng hộ tác giả
        public static string BankName => L.T("Ngân hàng TMCP Bản Việt (BVBank)");
        public const string BankAccount = "9021642814421";

        // Báo lỗi, góp ý tính năng
        public const string ContactEmail = "bstrinhkean@gmail.com";

        /// <summary>Địa chỉ tệp JSON thông tin phiên bản mới: {"version":"1.2.0","url":"…","notes":"…"}. Để trống = tắt kiểm tra cập nhật.</summary>
        public const string UpdateUrl = "";

        private static string _dataDir;
        public static string DataDir
        {
            get
            {
                if (_dataDir == null)
                {
                    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    _dataDir = Path.Combine(local, Id);
                    Directory.CreateDirectory(_dataDir);
                    MigrateFromV1(Path.Combine(local, OldId), _dataDir);
                }
                return _dataDir;
            }
        }

        public static string LogDir => Ensure(Path.Combine(DataDir, "NhatKy"));
        public static string BackupDir => Ensure(Path.Combine(DataDir, "SaoLuu"));

        public static string ExePath
        {
            get
            {
                using (var p = Process.GetCurrentProcess()) return p.MainModule.FileName;
            }
        }

        public static bool IsAdmin
        {
            get
            {
                try
                {
                    using (var id = WindowsIdentity.GetCurrent())
                        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch { return false; }
            }
        }

        private static int? _build;
        public static int WindowsBuild
        {
            get
            {
                if (_build == null)
                {
                    int b = Environment.OSVersion.Version.Build;
                    var s = RegistryHelper.GetValue(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber") as string;
                    if (int.TryParse(s, out var rb)) b = rb;
                    _build = b;
                }
                return _build.Value;
            }
        }

        public static bool IsWindows11 => WindowsBuild >= 22000;

        private static string Ensure(string dir) { Directory.CreateDirectory(dir); return dir; }

        /// <summary>
        /// Chép dữ liệu của bản 1.0 (tên cũ "Bác Sĩ Máy Tính") sang thư mục mới – chỉ một lần.
        /// Dùng sao chép thay vì di chuyển vì bản cũ có thể đang chạy và giữ thư mục.
        /// </summary>
        private static void MigrateFromV1(string oldDir, string newDir)
        {
            var marker = Path.Combine(newDir, ".migrated_v1");
            if (!Directory.Exists(oldDir) || File.Exists(marker)) return;
            try
            {
                foreach (var src in Directory.GetFiles(oldDir, "*", SearchOption.AllDirectories))
                {
                    var rel = src.Substring(oldDir.Length).TrimStart('\\');
                    var dst = Path.Combine(newDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    bool isState = rel.Equals("caidat.json", StringComparison.OrdinalIgnoreCase)
                                   || rel.Equals(@"SaoLuu\hoantac.json", StringComparison.OrdinalIgnoreCase);
                    // Tệp trạng thái (cài đặt, sao lưu hoàn tác) của bản cũ là dữ liệu thật của người dùng → luôn ưu tiên,
                    // trừ khi bản mới đã có dữ liệu hoàn tác thật (lớn hơn tệp rỗng).
                    if (!File.Exists(dst) || (isState && (rel.StartsWith("caidat") || new FileInfo(dst).Length <= 64)))
                        File.Copy(src, dst, true);
                }
                File.WriteAllText(marker, DateTime.Now.ToString("s"));
            }
            catch { }
        }
    }
}
