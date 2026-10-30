using System.Management;
using Microsoft.Win32;

namespace BacSiMayTinh.Core
{
    public class RestorePointInfo
    {
        public int Sequence { get; set; }
        public string Description { get; set; }
        public DateTime? Created { get; set; }
        public string CreatedText => Created?.ToString("dd/MM/yyyy HH:mm") ?? "";
    }

    /// <summary>Tạo / liệt kê điểm khôi phục hệ thống (System Restore).</summary>
    public static class RestorePointService
    {
        private static bool _createdThisSession;

        public static Task<bool> CreateAsync(string description) => Task.Run(() => Create(description));

        public static bool Create(string description)
        {
            try
            {
                // Windows mặc định chỉ cho tạo 1 điểm khôi phục mỗi 24 giờ – bỏ giới hạn này.
                RegistryHelper.SetValue(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore",
                    "SystemRestorePointCreationFrequency", 0, RegistryValueKind.DWord);
            }
            catch { }

            if (TryCreate(description)) return true;

            // Có thể System Protection đang tắt – thử bật cho ổ hệ thống rồi tạo lại.
            Logger.Warn("Tạo điểm khôi phục thất bại, thử bật System Protection cho ổ hệ thống.");
            ProcessRunner.PowerShellAsync("Enable-ComputerRestore -Drive \"$env:SystemDrive\\\"").Wait();
            return TryCreate(description);
        }

        private static bool TryCreate(string description)
        {
            try
            {
                using (var cls = new ManagementClass(@"\\.\root\default", "SystemRestore", null))
                using (var inParams = cls.GetMethodParameters("CreateRestorePoint"))
                {
                    inParams["Description"] = description;
                    inParams["RestorePointType"] = 12;   // MODIFY_SETTINGS
                    inParams["EventType"] = 100;         // BEGIN_SYSTEM_CHANGE
                    using (var outParams = cls.InvokeMethod("CreateRestorePoint", inParams, null))
                    {
                        var rc = Convert.ToInt32(outParams["ReturnValue"]);
                        Logger.Info("Tạo điểm khôi phục '" + description + "' → mã " + rc);
                        if (rc == 0) _createdThisSession = true;
                        return rc == 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Tạo điểm khôi phục", ex);
                return false;
            }
        }

        /// <summary>Tạo điểm khôi phục một lần trong mỗi phiên làm việc (nếu bật trong Cài đặt).</summary>
        public static async Task EnsureForSessionAsync()
        {
            // Bản sao lưu cấu hình nhanh (vài giây) – phục hồi được kể cả khi System Restore bị tắt.
            if (AppSettings.Current.AutoBackupBeforeChange && !Modules.ConfigBackup.SessionSnapshotDone)
            {
                var m = UI.MainWindow.Instance;
                m?.ShowBusy(L.T("Đang tự sao lưu cấu hình hiện tại…"));
                try { await Modules.ConfigBackup.EnsureSessionSnapshotAsync(); }
                finally { m?.HideBusy(); }
            }
            if (_createdThisSession || !AppSettings.Current.AutoRestorePoint) return;
            var main = UI.MainWindow.Instance;
            main?.ShowBusy(L.T("Đang tạo điểm khôi phục hệ thống…\nViệc này giúp bạn quay lại trạng thái cũ nếu cần."));
            try
            {
                var ok = await CreateAsync(AppInfo.Name + " - before optimizing");
                _createdThisSession = true; // không hỏi lại trong phiên này
                if (!ok) main?.Toast(L.T("Không tạo được điểm khôi phục (System Restore có thể bị tắt). Vẫn tiếp tục – mọi thay đổi đều có thể hoàn tác."), ToastKind.Warning);
            }
            finally { main?.HideBusy(); }
        }

        public static List<RestorePointInfo> List()
        {
            return Wmi.Query("SELECT * FROM SystemRestore", @"root\default")
                .Select(o => new RestorePointInfo
                {
                    Sequence = (int)Wmi.Long(o, "SequenceNumber"),
                    Description = Wmi.Str(o, "Description"),
                    Created = Wmi.Date(o, "CreationTime")
                })
                .OrderByDescending(r => r.Sequence)
                .ToList();
        }
    }

    public enum ToastKind { Info, Success, Warning, Error }
}
