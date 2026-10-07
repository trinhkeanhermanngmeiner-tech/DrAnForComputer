using System.ServiceProcess;
using System.Windows.Media;
using BacSiMayTinh.Controls;
using BacSiMayTinh.Core;
using Microsoft.Win32;

namespace BacSiMayTinh.Modules
{
    public class ServiceTweak : ObservableObject
    {
        public string Name { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public Risk Risk { get; set; }
        public int DefaultStart { get; set; } = 3;
        public bool LowEnd { get; set; }
        public bool LowEndHddOnly { get; set; }

        public string RiskText => RiskInfo.Text(Risk);
        public Brush RiskFg => Pal.RiskFg(Risk);
        public Brush RiskBg => Pal.RiskBg(Risk);

        private string KeyPath => @"HKLM\SYSTEM\CurrentControlSet\Services\" + Name;

        public bool Exists => RegistryHelper.KeyExists(KeyPath) && RegistryHelper.GetValue(KeyPath, "Start") != null;

        private int _start;
        public int StartType { get => _start; set { Set(ref _start, value); OnPropertyChanged(nameof(StartText)); } }

        private bool _running;
        public bool IsRunning { get => _running; set { Set(ref _running, value); OnPropertyChanged(nameof(StateText)); OnPropertyChanged(nameof(StateBrush)); } }

        private bool _disabled;
        /// <summary>Công tắc: bật = dịch vụ đã bị tắt.</summary>
        public bool IsDisabled { get => _disabled; set => Set(ref _disabled, value); }

        private bool _busy;
        public bool IsBusy { get => _busy; set { Set(ref _busy, value); OnPropertyChanged(nameof(IsIdle)); } }
        public bool IsIdle => !_busy;

        public string StateText => IsRunning ? L.T("Đang chạy") : L.T("Đã dừng");
        public Brush StateBrush => IsRunning ? Pal.Green : Pal.Gray;

        public string StartText
        {
            get
            {
                switch (StartType)
                {
                    case 2:
                        var delayed = RegistryHelper.GetValue(KeyPath, "DelayedAutostart");
                        return RegistryHelper.ValueEquals(delayed, 1) ? L.T("Tự động (trễ)") : L.T("Tự động");
                    case 3: return L.T("Thủ công");
                    case 4: return L.T("Đã tắt");
                    default: return L.T("Hệ thống");
                }
            }
        }

        public void Refresh()
        {
            var v = RegistryHelper.GetValue(KeyPath, "Start");
            StartType = v is int i ? i : 0;
            IsDisabled = StartType == 4;
            try
            {
                using (var sc = new ServiceController(Name)) IsRunning = sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.StartPending;
            }
            catch { IsRunning = false; }
        }

        private void SetStart(int start)
        {
            try { RegistryHelper.SetValue(KeyPath, "Start", start, RegistryValueKind.DWord); }
            catch
            {
                var mode = start == 2 ? "auto" : start == 3 ? "demand" : "disabled";
                ProcessRunner.CmdAsync($"sc config \"{Name}\" start= {mode}").Wait();
            }
        }

        public void Disable()
        {
            var store = UndoStore.Current;
            var cur = RegistryHelper.GetValue(KeyPath, "Start");
            if (!store.Services.ContainsKey(Name) && cur is int c && c != 4) store.Services[Name] = c;
            UndoStore.SaveCurrent();
            SetStart(4);
            try
            {
                using (var sc = new ServiceController(Name))
                    if (sc.Status != ServiceControllerStatus.Stopped && sc.CanStop)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                    }
            }
            catch (Exception ex) { Logger.Warn("Dừng dịch vụ " + Name + ": " + ex.Message); }
            Logger.Info("Tắt dịch vụ " + Name);
        }

        public void Restore()
        {
            var store = UndoStore.Current;
            int start = store.Services.TryGetValue(Name, out var s) ? s : DefaultStart;
            store.Services.Remove(Name);
            UndoStore.SaveCurrent();
            SetStart(start);
            if (start == 2)
            {
                try
                {
                    using (var sc = new ServiceController(Name))
                        if (sc.Status == ServiceControllerStatus.Stopped) sc.Start();
                }
                catch (Exception ex) { Logger.Warn("Khởi động dịch vụ " + Name + ": " + ex.Message); }
            }
            Logger.Info("Khôi phục dịch vụ " + Name + " (Start=" + start + ")");
        }
    }

    public static class ServiceCatalog
    {
        private static ServiceTweak S(string name, string title, string desc, Risk risk, int def, bool lowEnd = false) =>
            new ServiceTweak { Name = name, Title = title, Description = desc, Risk = risk, DefaultStart = def, LowEnd = lowEnd };

        public static List<ServiceTweak> Build()
        {
            var all = new List<ServiceTweak>
            {
                S("DiagTrack", L.T("Đo lường từ xa (Telemetry)"), L.T("Thu thập và gửi dữ liệu sử dụng về Microsoft. Tắt an toàn, giảm tải CPU và mạng."), Risk.Safe, 2, true),
                S("dmwappushservice", L.T("Định tuyến WAP Push"), L.T("Dịch vụ phụ trợ của Telemetry. Tắt an toàn."), Risk.Safe, 3, true),
                new ServiceTweak { Name = "SysMain", Title = "SysMain (Superfetch)", DefaultStart = 2, Risk = Risk.Moderate, LowEndHddOnly = true,
                    Description = L.T("Nạp trước ứng dụng vào RAM. Nên TẮT nếu máy dùng ổ HDD và hay bị Disk 100%. Máy dùng SSD nên để bật.") },
                S("WSearch", L.T("Windows Search (lập chỉ mục)"), L.T("Lập chỉ mục để tìm kiếm nhanh. Tắt giúp giảm tải ổ cứng trên máy yếu, nhưng tìm kiếm trong Start/Explorer sẽ chậm hơn."), Risk.Moderate, 2),
                S("Spooler", L.T("Print Spooler (máy in)"), L.T("Quản lý hàng đợi in. Chỉ tắt nếu bạn KHÔNG dùng máy in."), Risk.Moderate, 2),
                S("Fax", "Fax", L.T("Gửi/nhận fax. Hầu như không ai dùng."), Risk.Safe, 3, true),
                S("MapsBroker", L.T("Quản lý bản đồ ngoại tuyến"), L.T("Tự cập nhật bản đồ đã tải về. Tắt an toàn."), Risk.Safe, 2, true),
                S("RetailDemo", L.T("Chế độ trưng bày cửa hàng"), L.T("Chỉ dùng cho máy trưng bày tại cửa hàng."), Risk.Safe, 3, true),
                S("WerSvc", L.T("Báo cáo lỗi Windows"), L.T("Gửi báo cáo sự cố về Microsoft."), Risk.Safe, 3, true),
                S("TrkWks", L.T("Theo dõi liên kết phân tán"), L.T("Theo dõi tệp di chuyển giữa các máy trong mạng nội bộ. Máy cá nhân không cần."), Risk.Safe, 2, true),
                S("WpcMonSvc", L.T("Kiểm soát của phụ huynh"), L.T("Chỉ cần nếu bạn dùng Microsoft Family Safety."), Risk.Safe, 3),
                S("wisvc", L.T("Chương trình Windows Insider"), L.T("Chỉ cần nếu bạn tham gia thử nghiệm Windows Insider."), Risk.Safe, 3),
                S("WalletService", L.T("Ví (Wallet)"), L.T("Dịch vụ ví điện tử của Microsoft, không dùng tại Việt Nam."), Risk.Safe, 3),
                S("diagnosticshub.standardcollector.service", L.T("Thu thập chẩn đoán"), L.T("Dùng cho công cụ phát triển phần mềm."), Risk.Safe, 3),
                S("lfsvc", L.T("Định vị (Geolocation)"), L.T("Cung cấp vị trí cho ứng dụng. Tắt nếu không dùng Bản đồ/Thời tiết theo vị trí."), Risk.Moderate, 3),
                S("stisvc", L.T("Thu nhận hình ảnh (máy scan)"), L.T("Chỉ tắt nếu bạn không dùng máy quét/scan hoặc webcam đời cũ."), Risk.Moderate, 3),
                S("icssvc", L.T("Điểm phát Wi-Fi di động"), L.T("Chỉ cần khi bạn phát Wi-Fi từ máy tính."), Risk.Moderate, 3),
                S("PcaSvc", L.T("Trợ lý tương thích chương trình"), L.T("Theo dõi và cảnh báo lỗi tương thích phần mềm cũ."), Risk.Moderate, 2),
                S("XblAuthManager", L.T("Xbox Live – Xác thực"), L.T("Tắt nếu không dùng ứng dụng Xbox / Game Pass."), Risk.Moderate, 3),
                S("XblGameSave", L.T("Xbox Live – Lưu game"), L.T("Tắt nếu không dùng ứng dụng Xbox / Game Pass."), Risk.Moderate, 3),
                S("XboxNetApiSvc", L.T("Xbox Live – Mạng"), L.T("Tắt nếu không dùng ứng dụng Xbox / Game Pass."), Risk.Moderate, 3),
                S("XboxGipSvc", L.T("Phụ kiện Xbox"), L.T("Tắt nếu không dùng tay cầm Xbox."), Risk.Moderate, 3),
            };
            var list = all.Where(s => s.Exists).ToList();
            foreach (var s in list) s.Refresh();
            return list;
        }
    }
}
