using System.Runtime.InteropServices;
using System.Windows.Media;
using BacSiMayTinh.Controls;
using BacSiMayTinh.Core;
using Microsoft.Win32;

namespace BacSiMayTinh.Modules
{
    /// <summary>Một bản cập nhật Windows Update (phần mềm hoặc driver).</summary>
    public class WuUpdate : ObservableObject
    {
        internal object Raw;        // IUpdate (COM) – chỉ dùng trên luồng nền
        internal object Session;    // IUpdateSession đã tìm ra bản cập nhật này

        public string Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string KB { get; set; }
        public long Size { get; set; }
        public bool IsDriver { get; set; }
        public string DriverClass { get; set; }
        public string DriverModel { get; set; }
        public string DriverProvider { get; set; }
        public string HardwareId { get; set; }
        public DateTime? DriverDate { get; set; }
        public bool IsHidden { get; set; }

        // Thiết bị tương ứng trên máy
        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public string CurrentInf { get; set; }
        public string CurrentVersion { get; set; }
        public string CurrentDate { get; set; }

        public string SizeText => Size > 0 ? FileUtil.FormatSize(Size) : "";
        public string ClassText => DriverManager.ClassName(DriverClass);
        public string Detail
        {
            get
            {
                if (!IsDriver) return (string.IsNullOrEmpty(KB) ? "" : "KB" + KB + "  ·  ") + SizeText;
                var now = string.IsNullOrEmpty(CurrentVersion) ? L.T("chưa có driver") : "v" + CurrentVersion + (string.IsNullOrEmpty(CurrentDate) ? "" : " (" + CurrentDate + ")");
                return (string.IsNullOrEmpty(DeviceName) ? DriverModel : DeviceName) + "  ·  " + L.T("Hiện tại: ") + now + "  →  " +
                       L.T("Bản mới: ") + (DriverDate?.ToString("dd/MM/yyyy") ?? "?") + (Size > 0 ? "  ·  " + SizeText : "");
            }
        }

        private bool _checked = true;
        public bool IsChecked { get => _checked; set => Set(ref _checked, value); }

        private string _status = "";
        public string Status { get => _status; set { Set(ref _status, value); OnPropertyChanged(nameof(HasStatus)); } }
        public bool HasStatus => !string.IsNullOrEmpty(_status);

        private Brush _statusBrush = Pal.Gray;
        public Brush StatusBrush { get => _statusBrush; set => Set(ref _statusBrush, value); }
    }

    public class WuInstallResult
    {
        public bool Success { get; set; }
        public bool RebootRequired { get; set; }
        public string Message { get; set; }
    }

    /// <summary>Gọi Windows Update Agent (COM) – nguồn cập nhật chính thức của Microsoft.</summary>
    public static class Wua
    {
        private const string WuService = @"HKLM\SYSTEM\CurrentControlSet\Services\wuauserv";

        public static bool ServiceDisabled => RegistryHelper.ValueEquals(RegistryHelper.GetValue(WuService, "Start"), 4);

        public static void EnableService()
        {
            RegistryHelper.SetValue(WuService, "Start", 3, RegistryValueKind.DWord);
            Logger.Info("Bật lại dịch vụ Windows Update (Start=3)");
        }

        private static dynamic NewSession()
        {
            var t = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (t == null) throw new InvalidOperationException(L.T("Máy này không có Windows Update Agent."));
            dynamic s = Activator.CreateInstance(t);
            try { s.ClientApplicationID = AppInfo.Name; } catch { }
            return s;
        }

        /// <param name="windowsUpdate">true = hỏi thẳng máy chủ Windows Update (dùng cho driver), false = nguồn mặc định của máy (có thể là WSUS).</param>
        public static List<WuUpdate> Search(string criteria, bool windowsUpdate, CancellationToken ct)
        {
            if (ServiceDisabled) throw new WuServiceDisabledException();
            var list = new List<WuUpdate>();
            try
            {
                dynamic session = NewSession();
                dynamic searcher = session.CreateUpdateSearcher();
                if (windowsUpdate) searcher.ServerSelection = 2; // ssWindowsUpdate
                searcher.Online = true;
                Logger.Info("Windows Update: tìm '" + criteria + "'");
                dynamic result = searcher.Search(criteria);
                ct.ThrowIfCancellationRequested();
                dynamic ups = result.Updates;
                int n = ups.Count;
                for (int i = 0; i < n; i++)
                {
                    dynamic u = ups[i];
                    var w = new WuUpdate { Raw = u, Session = session };
                    try { w.Id = (string)u.Identity.UpdateID; } catch { }
                    try { w.Title = (string)u.Title; } catch { }
                    try { w.Description = (string)u.Description; } catch { }
                    try { w.Size = Convert.ToInt64(u.MaxDownloadSize); } catch { }
                    try { w.IsHidden = (bool)u.IsHidden; } catch { }
                    try { if (u.KBArticleIDs.Count > 0) w.KB = (string)u.KBArticleIDs[0]; } catch { }
                    try { w.IsDriver = (int)u.Type == 2; } catch { }
                    if (w.IsDriver)
                    {
                        try { w.DriverClass = (string)u.DriverClass; } catch { }
                        try { w.DriverModel = (string)u.DriverModel; } catch { }
                        try { w.DriverProvider = (string)u.DriverProvider; } catch { }
                        try { w.HardwareId = (string)u.DriverHardwareID; } catch { }
                        try { w.DriverDate = (DateTime)u.DriverVerDate; } catch { }
                    }
                    list.Add(w);
                }
                Logger.Info("Windows Update: " + list.Count + " bản cập nhật");
            }
            catch (COMException ex) { throw new InvalidOperationException(ErrorText(ex.HResult), ex); }
            return list;
        }

        public static WuInstallResult Install(WuUpdate w, Action<string> progress)
        {
            var res = new WuInstallResult();
            try
            {
                dynamic u = w.Raw;
                dynamic session = w.Session ?? NewSession();
                try { if (!(bool)u.EulaAccepted) u.AcceptEula(); } catch { }
                dynamic coll = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl"));
                coll.Add(u);

                bool downloaded = false;
                try { downloaded = (bool)u.IsDownloaded; } catch { }
                if (!downloaded)
                {
                    progress?.Invoke(L.T("Đang tải về…"));
                    dynamic dl = session.CreateUpdateDownloader();
                    dl.Updates = coll;
                    dynamic dr = dl.Download();
                    int dcode = (int)dr.ResultCode;
                    if (dcode != 2 && dcode != 3)
                    {
                        res.Message = L.T("Tải về thất bại: ") + ResultText(dcode);
                        return res;
                    }
                }
                progress?.Invoke(L.T("Đang cài đặt…"));
                dynamic inst = session.CreateUpdateInstaller();
                inst.Updates = coll;
                try { inst.ForceQuiet = true; } catch { }
                dynamic ir = inst.Install();
                int code = (int)ir.ResultCode;
                try { res.RebootRequired = (bool)ir.RebootRequired; } catch { }
                res.Success = code == 2 || code == 3;
                if (res.Success) res.Message = res.RebootRequired ? L.T("Đã cài – cần khởi động lại") : L.T("Đã cài thành công");
                else
                {
                    int hr = 0;
                    try { hr = (int)ir.GetUpdateResult(0).HResult; } catch { }
                    res.Message = L.T("Cài thất bại: ") + (hr != 0 ? ErrorText(hr) : ResultText(code));
                }
            }
            catch (COMException ex) { res.Message = L.T("Lỗi: ") + ErrorText(ex.HResult); }
            catch (Exception ex) { res.Message = L.T("Lỗi: ") + ex.Message; }
            return res;
        }

        /// <summary>Ẩn/hiện một bản cập nhật (Windows sẽ không tự cài bản đã ẩn).</summary>
        public static bool SetHidden(WuUpdate w, bool hidden)
        {
            try
            {
                dynamic u = w.Raw;
                u.IsHidden = hidden;
                w.IsHidden = hidden;
                Logger.Info((hidden ? "Ẩn" : "Bỏ ẩn") + " bản cập nhật: " + w.Title);
                return true;
            }
            catch (Exception ex) { Logger.Error("Ẩn bản cập nhật", ex); return false; }
        }

        /// <summary>Thời điểm gần nhất Windows cài thành công một bản cập nhật.</summary>
        public static DateTime? LastSuccessfulInstall()
        {
            try
            {
                dynamic session = NewSession();
                dynamic searcher = session.CreateUpdateSearcher();
                int total = (int)searcher.GetTotalHistoryCount();
                if (total <= 0) return null;
                dynamic hist = searcher.QueryHistory(0, Math.Min(total, 200));
                DateTime? best = null;
                int n = hist.Count;
                for (int i = 0; i < n; i++)
                {
                    dynamic e = hist[i];
                    try
                    {
                        if ((int)e.Operation != 1 || (int)e.ResultCode != 2) continue;
                        DateTime d = ((DateTime)e.Date).ToLocalTime();
                        if (best == null || d > best) best = d;
                    }
                    catch { }
                }
                return best;
            }
            catch (Exception ex) { Logger.Warn("Đọc lịch sử Windows Update: " + ex.Message); return null; }
        }

        public static string ResultText(int code)
        {
            switch (code)
            {
                case 2: return L.T("thành công");
                case 3: return L.T("thành công nhưng có lỗi nhỏ");
                case 4: return L.T("thất bại");
                case 5: return L.T("bị hủy");
                default: return L.F("mã {0}", code);
            }
        }

        public static string ErrorText(int hr)
        {
            switch ((uint)hr)
            {
                case 0x8024402C: case 0x80072EFD: case 0x80072EE7: case 0x80072EE2: case 0x8024401C:
                    return L.T("Không kết nối được máy chủ Windows Update – hãy kiểm tra Internet.");
                case 0x80070422: case 0x8024A000:
                    return L.T("Dịch vụ Windows Update đang bị tắt.");
                case 0x8024500C: case 0x80240438: case 0x8024002E:
                    return L.T("Windows Update đang bị chặn bởi chính sách (Group Policy) hoặc phần mềm khác.");
                case 0x80240016: return L.T("Windows Update đang bận cài bản khác – hãy thử lại sau ít phút.");
                case 0x80070005: return L.T("Không đủ quyền – hãy chạy phần mềm với quyền quản trị.");
                case 0x80240024: return L.T("Không có bản cập nhật nào.");
                default: return L.F("Mã lỗi 0x{0:X8}", hr);
            }
        }
    }

    public class WuServiceDisabledException : Exception
    {
        public WuServiceDisabledException() : base(L.T("Dịch vụ Windows Update đang bị tắt.")) { }
    }

    /// <summary>Tạm dừng cập nhật, giờ hoạt động – dùng đúng các khóa mà trang Cài đặt Windows sử dụng.</summary>
    public static class WuManager
    {
        private const string Ux = @"HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
        private static readonly string[] PauseNames =
        {
            "PauseUpdatesStartTime", "PauseUpdatesExpiryTime",
            "PauseFeatureUpdatesStartTime", "PauseFeatureUpdatesEndTime",
            "PauseQualityUpdatesStartTime", "PauseQualityUpdatesEndTime",
        };

        /// <summary>Các giá trị registry trang này thay đổi (được sao lưu cùng phần tinh chỉnh).</summary>
        public static IEnumerable<(string path, string name)> ManagedValues
        {
            get
            {
                foreach (var n in PauseNames) yield return (Ux, n);
                yield return (Ux, "ActiveHoursStart");
                yield return (Ux, "ActiveHoursEnd");
                yield return (Ux, "SmartActiveHoursState");
            }
        }

        public static DateTime? PausedUntil
        {
            get
            {
                var s = RegistryHelper.GetValue(Ux, "PauseUpdatesExpiryTime") as string;
                if (string.IsNullOrEmpty(s)) return null;
                if (!DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d)) return null;
                var local = d.ToLocalTime();
                return local > DateTime.Now ? local : (DateTime?)null;
            }
        }

        public static void Pause(int days)
        {
            days = Math.Max(1, Math.Min(35, days));
            var now = DateTime.UtcNow;
            var end = now.AddDays(days);
            string F(DateTime d) => d.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
            RegistryHelper.SetValue(Ux, "PauseUpdatesStartTime", F(now), RegistryValueKind.String);
            RegistryHelper.SetValue(Ux, "PauseUpdatesExpiryTime", F(end), RegistryValueKind.String);
            RegistryHelper.SetValue(Ux, "PauseFeatureUpdatesStartTime", F(now), RegistryValueKind.String);
            RegistryHelper.SetValue(Ux, "PauseFeatureUpdatesEndTime", F(end), RegistryValueKind.String);
            RegistryHelper.SetValue(Ux, "PauseQualityUpdatesStartTime", F(now), RegistryValueKind.String);
            RegistryHelper.SetValue(Ux, "PauseQualityUpdatesEndTime", F(end), RegistryValueKind.String);
            Logger.Info("Tạm dừng Windows Update " + days + " ngày (đến " + end.ToLocalTime().ToString("dd/MM/yyyy") + ")");
        }

        public static void Resume()
        {
            foreach (var n in PauseNames) RegistryHelper.DeleteValue(Ux, n);
            Logger.Info("Tiếp tục Windows Update");
        }

        public static (int start, int end, bool manual) ActiveHours
        {
            get
            {
                int s = RegistryHelper.GetValue(Ux, "ActiveHoursStart") is int a ? a : 8;
                int e = RegistryHelper.GetValue(Ux, "ActiveHoursEnd") is int b ? b : 17;
                bool manual = !RegistryHelper.ValueEquals(RegistryHelper.GetValue(Ux, "SmartActiveHoursState"), 1);
                return (s, e, manual);
            }
        }

        public static int Span(int start, int end) => ((end - start) % 24 + 24) % 24;

        /// <summary>Giờ hoạt động: Windows không tự khởi động lại máy trong khoảng này (tối đa 18 giờ).</summary>
        public static void SetActiveHours(int start, int end)
        {
            if (Span(start, end) == 0 || Span(start, end) > 18) throw new ArgumentException(L.T("Khoảng giờ hoạt động phải từ 1 đến 18 tiếng."));
            RegistryHelper.SetValue(Ux, "ActiveHoursStart", start, RegistryValueKind.DWord);
            RegistryHelper.SetValue(Ux, "ActiveHoursEnd", end, RegistryValueKind.DWord);
            RegistryHelper.SetValue(Ux, "SmartActiveHoursState", 0, RegistryValueKind.DWord);
            Logger.Info($"Đặt giờ hoạt động {start}h–{end}h");
        }

        public static List<WuUpdate> PendingSoftware(CancellationToken ct) => Wua.Search("IsInstalled=0 and Type='Software' and IsHidden=0", false, ct);
        public static List<WuUpdate> Hidden(CancellationToken ct) => Wua.Search("IsInstalled=0 and IsHidden=1", false, ct);
    }
}
