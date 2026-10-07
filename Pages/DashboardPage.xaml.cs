using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BacSiMayTinh.Controls;
using BacSiMayTinh.Core;
using BacSiMayTinh.Modules;
using BacSiMayTinh.UI;

namespace BacSiMayTinh.Pages
{
    public class Suggestion
    {
        public string Glyph { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        public Brush Fg { get; set; } = Pal.Blue;
        public Brush Bg { get; set; } = Pal.BlueBg;
        public string ActionText { get; set; }
        public Action Action { get; set; }
        public Visibility ActionVisibility => Action == null ? Visibility.Collapsed : Visibility.Visible;
    }

    public partial class DashboardPage : UserControl, IPage
    {
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private ulong _lastIdle, _lastTotal;
        private long _lastNet = -1;
        private DateTime _lastNetTime;
        private int _tick;
        private bool _analyzing, _analyzed;
        private static Result _last; // giữ kết quả khám khi trang được dựng lại (đổi ngôn ngữ)

        public DashboardPage()
        {
            InitializeComponent();
            _timer.Interval = TimeSpan.FromSeconds(MainWindow.ReduceEffects ? 2 : 1);
            _timer.Tick += (s, e) => Sample();
            var h = DateTime.Now.Hour;
            Greeting.Text = h < 11 ? L.T("Chào buổi sáng!") : h < 13 ? L.T("Chào buổi trưa!") : h < 18 ? L.T("Chào buổi chiều!") : L.T("Chào buổi tối!");
            CpuName.Text = SystemQuick.CpuName();
        }

        public void OnShown()
        {
            Sample();
            _timer.Start();
            if (!_analyzed && _last != null) { Show(_last); _analyzed = true; }
            if (!_analyzed) _ = Analyze();
        }

        public void OnHidden() => _timer.Stop();

        // ================= Theo dõi trực tiếp =================
        private void Sample()
        {
            _tick++;
            if (Native.GetSystemTimes(out var idle, out var kernel, out var user))
            {
                ulong total = kernel.Value + user.Value;
                if (_lastTotal != 0 && total > _lastTotal)
                {
                    double cpu = 100.0 * (1 - (double)(idle.Value - _lastIdle) / (total - _lastTotal));
                    cpu = Math.Max(0, Math.Min(100, cpu));
                    CpuValue.Text = cpu.ToString("0") + "%";
                    CpuSpark.Push(cpu);
                }
                _lastIdle = idle.Value; _lastTotal = total;
            }

            var m = Native.GetMemory();
            RamValue.Text = m.dwMemoryLoad + "%";
            RamDetail.Text = $"{FileUtil.FormatSize((long)(m.ullTotalPhys - m.ullAvailPhys))} / {FileUtil.FormatSize((long)m.ullTotalPhys)}";
            RamSpark.Push(m.dwMemoryLoad);

            try
            {
                long bytes = 0;
                foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var st = n.GetIPStatistics();
                    bytes += st.BytesReceived + st.BytesSent;
                }
                var now = DateTime.Now;
                if (_lastNet >= 0)
                {
                    double secs = Math.Max(0.2, (now - _lastNetTime).TotalSeconds);
                    double speed = Math.Max(0, (bytes - _lastNet) / secs);
                    NetValue.Text = FileUtil.FormatSize((long)speed) + "/s";
                    NetSpark.Push(speed);
                }
                _lastNet = bytes; _lastNetTime = now;
            }
            catch { }

            if (_tick % 10 == 1)
            {
                UptimeText.Text = L.T("Máy đã hoạt động: ") + SystemQuick.Uptime();
                try
                {
                    var d = new DriveInfo(FileUtil.SystemDrive);
                    double used = 100.0 * (d.TotalSize - d.TotalFreeSpace) / d.TotalSize;
                    DiskTitle.Text = L.T("Ổ ") + d.Name.TrimEnd('\\');
                    DiskValue.Text = used.ToString("0") + "%";
                    DiskBar.Value = used;
                    DiskBar.Foreground = used >= 90 ? (Brush)Pal.Red : used >= 80 ? Pal.Amber : (Brush)FindResource("B.AccentGrad");
                    DiskDetail.Text = L.F("Còn trống {0} / {1}", FileUtil.FormatSize(d.TotalFreeSpace), FileUtil.FormatSize(d.TotalSize));
                }
                catch { }
            }
        }

        // ================= Khám tổng quát =================
        private class Result
        {
            public long Junk;
            public int StartupOn, StartupTotal;
            public string DiskType;
            public double FreePct;
            public long FreeBytes;
            public double RamGb;
            public int SlotsUsed, SlotsTotal;
            public int TweaksPending;
            public bool DiskBad;
            public double UptimeDays;
            public uint RamLoad;
        }

        public async Task Analyze()
        {
            if (_analyzing) return;
            _analyzing = true;
            RescanBtn.IsEnabled = false;
            ScoreText.Text = "--";
            ScoreLabel.Text = L.T("Đang khám…");
            HealthText.Text = L.T("Đang kiểm tra rác, khởi động, ổ đĩa, RAM…");
            Gauge.Value = 0;
            try
            {
                var r = await Task.Run(() =>
                {
                    var res = new Result();
                    try { res.Junk = CleanerEngine.Estimate(); } catch { }
                    try
                    {
                        var st = StartupManager.GetItems();
                        res.StartupOn = st.Count(i => i.IsEnabled);
                        res.StartupTotal = st.Count;
                    }
                    catch { }
                    res.DiskType = SystemQuick.SystemDiskType();
                    try
                    {
                        var d = new DriveInfo(FileUtil.SystemDrive);
                        res.FreeBytes = d.TotalFreeSpace;
                        res.FreePct = 100.0 * d.TotalFreeSpace / d.TotalSize;
                    }
                    catch { res.FreePct = 100; }
                    res.RamGb = SystemQuick.TotalRamGb();
                    try { (res.SlotsUsed, res.SlotsTotal) = SystemQuick.RamSlots(); } catch { }
                    try { res.TweaksPending = TweakCatalog.Build().Count(t => t.Recommended && !t.Check()); } catch { }
                    try
                    {
                        res.DiskBad = Wmi.Query("SELECT HealthStatus FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage")
                            .Any(d => Wmi.Long(d, "HealthStatus") != 0);
                    }
                    catch { }
                    res.UptimeDays = SystemQuick.UptimeDays;
                    res.RamLoad = Native.GetMemory().dwMemoryLoad;
                    return res;
                });
                _last = r;
                Show(r);
                _analyzed = true;
                MainWindow.TrimMemory();
            }
            finally
            {
                _analyzing = false;
                RescanBtn.IsEnabled = true;
            }
        }

        private void Show(Result r)
        {
            int score = 100;
            const long MB = 1024L * 1024;
            if (r.Junk >= 5 * 1024 * MB) score -= 15; else if (r.Junk >= 1024 * MB) score -= 10; else if (r.Junk >= 300 * MB) score -= 5;
            if (r.StartupOn > 15) score -= 15; else if (r.StartupOn > 10) score -= 10; else if (r.StartupOn > 6) score -= 5;
            if (r.FreePct < 10) score -= 20; else if (r.FreePct < 20) score -= 10;
            if (r.RamLoad > 85) score -= 10; else if (r.RamLoad > 70) score -= 5;
            if (r.TweaksPending >= 8) score -= 10; else if (r.TweaksPending >= 3) score -= 5;
            if (r.DiskBad) score -= 25;
            if (r.UptimeDays > 7) score -= 5;
            score = Math.Max(5, score);

            Brush color = score >= 80 ? Pal.Green : score >= 55 ? Pal.Amber : Pal.Red;
            Gauge.ValueBrush = color;
            AnimateScore(score);
            ScoreLabel.Text = score >= 85 ? L.T("Rất khỏe") : score >= 70 ? L.T("Khá khỏe") : score >= 50 ? L.T("Cần chăm sóc") : L.T("Cần chữa trị");
            HealthText.Text = score >= 85 ? L.T("Máy tính của bạn đang khỏe mạnh!")
                : score >= 70 ? L.T("Máy ổn, nhưng còn vài chỗ có thể tối ưu thêm.")
                : score >= 50 ? L.T("Máy cần được chăm sóc – hãy làm theo lời khuyên bên dưới.")
                : L.T("Máy đang \"ốm\" – để Bác sĩ chữa trị ngay nhé!");

            if (r.DiskType != "Unknown")
            {
                DiskTypeBadge.Visibility = Visibility.Visible;
                DiskTypeText.Text = r.DiskType;
            }

            var main = MainWindow.Instance;
            var list = new List<Suggestion>();
            if (r.DiskBad)
                list.Add(new Suggestion { Glyph = "", Fg = Pal.Red, Bg = Pal.RedBg, Title = L.T("Ổ cứng có dấu hiệu hỏng!"),
                    Detail = L.T("Hãy sao lưu dữ liệu quan trọng ngay và cân nhắc thay ổ cứng mới."), ActionText = L.T("Chi tiết"), Action = () => main.Navigate("sysinfo") });
            if (r.Junk >= 50 * MB)
                list.Add(new Suggestion { Glyph = "", Fg = r.Junk >= 1024 * MB ? Pal.Amber : Pal.Blue, Bg = r.Junk >= 1024 * MB ? Pal.AmberBg : Pal.BlueBg,
                    Title = L.F("Có {0} tệp rác có thể dọn", FileUtil.FormatSize(r.Junk)),
                    Detail = L.T("Tệp tạm, bộ đệm trình duyệt, bộ đệm cập nhật, thùng rác…"), ActionText = L.T("Dọn ngay"),
                    Action = () => main.GetPage<CleanerPage>("cleaner")?.StartScan() });
            if (r.FreePct < 15)
                list.Add(new Suggestion { Glyph = "", Fg = Pal.Red, Bg = Pal.RedBg, Title = L.F("Ổ hệ thống sắp đầy (chỉ còn {0})", FileUtil.FormatSize(r.FreeBytes)),
                    Detail = L.T("Ổ C đầy làm Windows chậm và lỗi cập nhật. Hãy tìm và xóa tệp lớn không cần thiết."), ActionText = L.T("Phân tích"), Action = () => main.Navigate("disk") });
            if (r.StartupOn > 5)
                list.Add(new Suggestion { Glyph = "", Fg = r.StartupOn > 10 ? Pal.Amber : Pal.Blue, Bg = r.StartupOn > 10 ? Pal.AmberBg : Pal.BlueBg,
                    Title = L.F("{0} ứng dụng tự chạy khi bật máy", r.StartupOn),
                    Detail = L.T("Tắt bớt các ứng dụng không cần thiết để máy khởi động nhanh hơn."), ActionText = L.T("Quản lý"),
                    Action = () => main.GetPage<OptimizerPage>("optimizer")?.ShowTab("startup") });
            if (r.TweaksPending > 0)
                list.Add(new Suggestion { Glyph = "", Title = L.F("{0} tinh chỉnh khuyến nghị chưa được áp dụng", r.TweaksPending),
                    Detail = L.T("Tắt quảng cáo, theo dõi, hiệu ứng thừa… Mọi thay đổi đều có thể hoàn tác."), ActionText = L.T("Tối ưu"),
                    Action = () => main.GetPage<OptimizerPage>("optimizer")?.ShowTab("tweaks") });
            if (r.RamLoad > 80)
                list.Add(new Suggestion { Glyph = "", Fg = Pal.Amber, Bg = Pal.AmberBg, Title = L.F("RAM đang dùng tới {0}%", r.RamLoad),
                    Detail = L.T("Đóng bớt ứng dụng hoặc giải phóng RAM để máy mượt hơn."), ActionText = L.T("Giải phóng"), Action = () => FreeRam_Click(null, null) });
            if (r.DiskType == "HDD")
                list.Add(new Suggestion { Glyph = "", Fg = Pal.Purple, Bg = Pal.PurpleBg, Title = L.T("Máy đang dùng ổ cứng HDD"),
                    Detail = L.T("Nâng cấp lên SSD là cách tăng tốc hiệu quả nhất: khởi động và mở ứng dụng nhanh hơn 5–10 lần.") });
            if (r.RamGb > 0 && r.RamGb <= 4.5)
            {
                var slot = r.SlotsTotal > r.SlotsUsed ? L.F(" Máy còn {0} khe RAM trống – rất dễ nâng cấp.", r.SlotsTotal - r.SlotsUsed) : "";
                list.Add(new Suggestion { Glyph = "", Fg = Pal.Purple, Bg = Pal.PurpleBg, Title = L.F("RAM chỉ có {0:0.#} GB", r.RamGb),
                    Detail = L.T("Windows 10/11 chạy mượt nhất với 8 GB RAM trở lên.") + slot + L.T(" Trong lúc chờ, hãy bật Chế độ máy yếu."),
                    ActionText = L.T("Chế độ máy yếu"), Action = () => QA_LowEnd(null, null) });
            }
            if (r.UptimeDays > 7)
                list.Add(new Suggestion { Glyph = "", Title = L.F("Máy chưa khởi động lại {0:0} ngày", r.UptimeDays),
                    Detail = L.T("Khởi động lại định kỳ giúp Windows dọn bộ nhớ và hoàn tất cập nhật.") });
            if (list.Count == 0)
                list.Add(new Suggestion { Glyph = "", Fg = Pal.Green, Bg = Pal.GreenBg, Title = L.T("Mọi thứ đều ổn!"),
                    Detail = L.T("Không phát hiện vấn đề nào. Hãy khám lại định kỳ mỗi tuần nhé.") });
            SuggestionList.ItemsSource = list;
        }

        private void AnimateScore(int target)
        {
            if (MainWindow.ReduceEffects) { Gauge.Value = target; ScoreText.Text = target.ToString(); return; }
            int cur = 0;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            t.Tick += (s, e) =>
            {
                cur = Math.Min(target, cur + Math.Max(1, (target - cur) / 6));
                Gauge.Value = cur;
                ScoreText.Text = cur.ToString();
                if (cur >= target) t.Stop();
            };
            t.Start();
        }

        // ================= Hành động =================
        private async void Rescan_Click(object sender, RoutedEventArgs e) => await Analyze();

        private async void Quick_Click(object sender, RoutedEventArgs e)
        {
            QuickBtn.IsEnabled = false;
            try
            {
                QuickStatus.Text = L.T("Đang dọn tệp rác an toàn…");
                long freed = await Task.Run(() => CleanerEngine.QuickClean());
                QuickStatus.Text = L.T("Đang giải phóng RAM…");
                long ram = await Task.Run(() => MemoryCleaner.Free());
                QuickStatus.Text = L.T("Đang làm mới bộ đệm DNS…");
                await ProcessRunner.CmdAsync("ipconfig /flushdns");
                QuickStatus.Text = L.F("Xong! Đã dọn {0} rác, giải phóng {1} RAM.", FileUtil.FormatSize(freed), FileUtil.FormatSize(ram));
                MainWindow.Instance.Toast(L.F("Tối ưu xong: dọn {0} rác, giải phóng {1} RAM.", FileUtil.FormatSize(freed), FileUtil.FormatSize(ram)));
                await Analyze();
            }
            finally { QuickBtn.IsEnabled = true; }
        }

        private async void FreeRam_Click(object sender, RoutedEventArgs e)
        {
            var main = MainWindow.Instance;
            main.ShowBusy(L.T("Đang giải phóng RAM…"));
            try
            {
                var freed = await Task.Run(() => MemoryCleaner.Free());
                main.Toast(L.F("Đã giải phóng {0} RAM.", FileUtil.FormatSize(freed)));
            }
            finally { main.HideBusy(); }
        }

        private void Suggestion_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is Suggestion s) s.Action?.Invoke();
        }

        private void QA_Clean(object sender, RoutedEventArgs e) => MainWindow.Instance.GetPage<CleanerPage>("cleaner")?.StartScan();
        private void QA_Startup(object sender, RoutedEventArgs e) => MainWindow.Instance.GetPage<OptimizerPage>("optimizer")?.ShowTab("startup");
        private void QA_Repair(object sender, RoutedEventArgs e) => MainWindow.Instance.Navigate("repair");
        private void QA_LowEnd(object sender, RoutedEventArgs e)
        {
            var p = MainWindow.Instance.GetPage<OptimizerPage>("optimizer");
            p?.RunLowEndPreset();
        }
    }
}
