using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using BacSiMayTinh.Core;
using BacSiMayTinh.Modules;
using BacSiMayTinh.UI;

namespace BacSiMayTinh.Pages
{
    public class ToolTile
    {
        public string Glyph { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public Action Action { get; set; }
    }

    public partial class ToolsPage : UserControl, IPage, ISnapSub
    {
        private List<ProcItem> _procs;
        private bool _restoreLoaded, _hostsLoaded;

        public ToolsPage()
        {
            InitializeComponent();
            BuildTiles();
            DnsBox.ItemsSource = DnsChanger.Options;
            DnsBox.SelectedIndex = 1;
        }

        public void OnShown() { }
        public void OnHidden() { }

        public void SnapSub(string key)
        {
            foreach (var rb in FindTabs()) if ((string)rb.Tag == key) rb.IsChecked = true;
        }

        private IEnumerable<RadioButton> FindTabs()
        {
            var bar = (Border)((Grid)Content).Children[1];
            return ((StackPanel)bar.Child).Children.OfType<RadioButton>();
        }

        private async void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (MiscPanel == null) return;
            var tag = (string)((RadioButton)sender).Tag;
            MiscPanel.Visibility = tag == "misc" ? Visibility.Visible : Visibility.Collapsed;
            GamePanel.Visibility = tag == "game" ? Visibility.Visible : Visibility.Collapsed;
            NetPanel.Visibility = tag == "net" ? Visibility.Visible : Visibility.Collapsed;
            RestorePanel.Visibility = tag == "restore" ? Visibility.Visible : Visibility.Collapsed;
            HostsPanel.Visibility = tag == "hosts" ? Visibility.Visible : Visibility.Collapsed;
            ExtPanel.Visibility = tag == "ext" ? Visibility.Visible : Visibility.Collapsed;
            if (tag == "ext" && ExtList.ItemsSource == null) await LoadExtensions();
            if (tag == "game" && _procs == null) await LoadProcs();
            if (tag == "restore" && !_restoreLoaded) await LoadRestorePoints();
            if (tag == "hosts" && !_hostsLoaded) { HostsText.Text = HostsFile.Read(); _hostsLoaded = true; }
            if (tag == "net") ShowCurrentDns();
        }

        // ================= Ô công cụ =================
        private void BuildTiles()
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            SystemTiles.ItemsSource = new List<ToolTile>
            {
                new ToolTile { Glyph = "", Title = L.T("Báo cáo sức khỏe pin"), Description = L.T("Tạo báo cáo chi tiết độ chai, lịch sử sạc pin laptop."), Action = async () =>
                {
                    if (!PowerPlan.HasBattery()) { MainWindow.Instance.Toast(L.T("Máy này không có pin."), ToastKind.Info); return; }
                    var file = Path.Combine(desktop, "BaoCaoPin.html");
                    await ProcessRunner.CmdAsync($"powercfg /batteryreport /output \"{file}\"");
                    if (File.Exists(file)) { ProcessRunner.Open(file); MainWindow.Instance.Toast(L.T("Đã lưu báo cáo pin ra Desktop.")); }
                } },
                new ToolTile { Glyph = "", Title = L.T("Sao lưu driver"), Description = L.T("Lưu toàn bộ driver đang dùng để cài lại sau khi cài Win."), Action = BackupDrivers },
                new ToolTile { Glyph = "", Title = L.T("Kiểm tra lỗi RAM"), Description = L.T("Khởi động lại và kiểm tra RAM bằng Windows Memory Diagnostic."), Action = () =>
                {
                    if (Dialog.Confirm(L.T("Kiểm tra RAM?"), L.T("Công cụ Windows Memory Diagnostic sẽ mở ra. Chọn « Restart now » để khởi động lại và kiểm tra (mất 10–20 phút). Hãy lưu công việc trước."), L.T("Mở công cụ"), kind: ToastKind.Info))
                        ProcessRunner.Open("mdsched.exe");
                } },
                new ToolTile { Glyph = "", Title = L.T("Vào BIOS / UEFI"), Description = L.T("Khởi động lại thẳng vào màn hình cài đặt BIOS (máy UEFI)."), Action = async () =>
                {
                    if (!Dialog.Confirm(L.T("Khởi động lại vào BIOS?"), L.T("Máy sẽ khởi động lại ngay và vào thẳng cài đặt UEFI/BIOS. Hãy lưu công việc trước."), L.T("Khởi động lại"), danger: true)) return;
                    var r = await ProcessRunner.CmdAsync("shutdown /r /fw /t 3");
                    if (!r.Ok) Dialog.Info(L.T("Không hỗ trợ"), L.T("Máy này không hỗ trợ khởi động thẳng vào BIOS (máy dùng Legacy BIOS). Hãy nhấn phím Del/F2 khi bật máy."), ToastKind.Warning);
                } },
                new ToolTile { Glyph = "", Title = L.T("Khởi động nâng cao"), Description = L.T("Vào Safe Mode, sửa lỗi khởi động, khôi phục hệ thống…"), Action = async () =>
                {
                    if (!Dialog.Confirm(L.T("Khởi động nâng cao?"), L.T("Máy sẽ khởi động lại vào menu Khởi động nâng cao (Troubleshoot › Advanced options › Startup Settings để vào Safe Mode)."), L.T("Khởi động lại"), danger: true)) return;
                    await ProcessRunner.CmdAsync("shutdown /r /o /t 3");
                } },
                new ToolTile { Glyph = "", Title = L.T("Thư mục nhật ký & sao lưu"), Description = L.T("Xem nhật ký thao tác và các bản sao lưu của DrAn For Computer."), Action = () => ProcessRunner.Open(AppInfo.DataDir) },
            };

            ToolTile W(string glyph, string title, string desc, string cmd, string args = null) =>
                new ToolTile { Glyph = glyph, Title = title, Description = desc, Action = () => ProcessRunner.Open(cmd, args) };
            WindowsTiles.ItemsSource = new List<ToolTile>
            {
                W("", "Task Manager", L.T("Xem và tắt ứng dụng đang chạy."), "taskmgr.exe"),
                W("", "Device Manager", L.T("Quản lý driver và thiết bị phần cứng."), "devmgmt.msc"),
                W("", L.T("Quản lý ổ đĩa"), L.T("Chia, gộp, định dạng phân vùng ổ cứng."), "diskmgmt.msc"),
                W("", "Services", L.T("Quản lý tất cả dịch vụ Windows."), "services.msc"),
                W("", "Reliability Monitor", L.T("Lịch sử lỗi và sự cố của máy theo ngày."), "perfmon.exe", "/rel"),
                W("", "Event Viewer", L.T("Nhật ký sự kiện chi tiết của Windows."), "eventvwr.msc"),
                W("", "System Configuration", L.T("msconfig – chế độ khởi động, Safe Mode."), "msconfig.exe"),
                W("", "Programs and Features", L.T("Gỡ cài đặt kiểu Control Panel cũ."), "appwiz.cpl"),
                W("", "Network Connections", L.T("Quản lý card mạng, IP tĩnh."), "ncpa.cpl"),
                W("", "Power Options", L.T("Tùy chỉnh gói điện năng."), "powercfg.cpl"),
                W("", "System Properties", L.T("Tên máy, bộ nhớ ảo, System Restore."), "SystemPropertiesAdvanced.exe"),
                W("", "Control Panel", L.T("Bảng điều khiển cổ điển."), "control.exe"),
            };
        }

        private void Tile_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ToolTile t)
            {
                try { t.Action?.Invoke(); }
                catch (Exception ex) { MainWindow.Instance.Toast(L.T("Lỗi: ") + ex.Message, ToastKind.Error); }
            }
        }

        private async void BackupDrivers()
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = L.T("Chọn nơi lưu bản sao lưu driver (nên chọn ổ D hoặc USB)") })
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                var target = Path.Combine(dlg.SelectedPath, "SaoLuuDriver_" + Environment.MachineName + "_" + DateTime.Now.ToString("yyyyMMdd"));
                Directory.CreateDirectory(target);
                var main = MainWindow.Instance;
                main.ShowBusy(L.T("Đang sao lưu driver…\nCó thể mất vài phút."));
                int n = 0;
                try
                {
                    await ProcessRunner.CmdAsync($"pnputil /export-driver * \"{target}\"", (line, p) =>
                    {
                        if (line.IndexOf(".inf", StringComparison.OrdinalIgnoreCase) >= 0) { n++; main.SetBusyText(L.T("Đang sao lưu driver…\n") + line.Trim()); }
                    });
                }
                finally { main.HideBusy(); }
                main.Toast(L.F("Đã sao lưu driver vào {0}", target));
                ProcessRunner.Open(target);
            }
        }

        // ================= Tăng tốc game =================
        private async Task LoadProcs()
        {
            _procs = await Task.Run(() => GameBooster.Candidates());
            ProcList.ItemsSource = _procs;
            UpdateGameUi();
        }

        private async void GameRefresh_Click(object sender, RoutedEventArgs e) => await LoadProcs();

        private void UpdateGameUi()
        {
            GameBtnText.Text = GameBooster.Active ? L.T("Tắt tăng tốc") : L.T("Bật tăng tốc");
            GameTitle.Text = GameBooster.Active ? L.T("Tăng tốc game đang BẬT") : L.T("Tăng tốc chơi game");
        }

        private async void Game_Click(object sender, RoutedEventArgs e)
        {
            var main = MainWindow.Instance;
            if (GameBooster.Active)
            {
                await GameBooster.StopAsync();
                UpdateGameUi();
                GameStatus.Text = L.T("Đã trở lại chế độ bình thường.");
                main.Toast(L.T("Đã tắt tăng tốc game."));
                return;
            }
            var sel = _procs?.Where(p => p.IsChecked).ToList() ?? new List<ProcItem>();
            if (sel.Count > 0 && !Dialog.Confirm(L.T("Đóng ") + sel.Count + L.T(" ứng dụng?"),
                "• " + string.Join("\n• ", sel.Select(p => p.Name)) + L.T("\n\nDữ liệu chưa lưu trong các ứng dụng này có thể bị mất."), L.T("Đóng & tăng tốc"), danger: true)) return;
            main.ShowBusy(L.T("Đang tăng tốc cho game…"));
            try
            {
                var (closed, freed) = await GameBooster.StartAsync(sel);
                GameStatus.Text = L.F("Đã đóng {0} tiến trình, giải phóng {1} RAM và bật chế độ Hiệu suất cao. Chúc bạn chơi game vui vẻ!", closed, FileUtil.FormatSize(freed));
                main.Toast(L.T("Tăng tốc game đã bật!"));
            }
            finally { main.HideBusy(); }
            UpdateGameUi();
            await LoadProcs();
        }

        // ================= Mạng & Wi-Fi =================
        private void Dns_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (DnsBox.SelectedItem is DnsOption o) DnsDesc.Text = o.Description + (string.IsNullOrEmpty(o.Servers) ? "" : "  (" + o.Servers.Replace(",", ", ") + ")");
        }

        private void ShowCurrentDns()
        {
            try
            {
                var dns = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(n => n.GetIPProperties().DnsAddresses)
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.ToString()).Distinct().ToList();
                DnsCurrent.Text = L.T("DNS hiện tại: ") + (dns.Count > 0 ? string.Join(", ", dns) : L.T("không xác định"));
            }
            catch { }
        }

        private async void DnsApply_Click(object sender, RoutedEventArgs e)
        {
            if (!(DnsBox.SelectedItem is DnsOption o)) return;
            var main = MainWindow.Instance;
            main.ShowBusy(L.T("Đang đổi DNS…"));
            RunResult r;
            try { r = await DnsChanger.ApplyAsync(o); }
            finally { main.HideBusy(); }
            ShowCurrentDns();
            if (r.Output != null && r.Output.Contains("OK")) main.Toast(L.T("Đã đổi DNS sang ") + o.Name);
            else main.Toast(L.T("Không đổi được DNS: ") + r.Output, ToastKind.Error);
        }

        private async void Wifi_Click(object sender, RoutedEventArgs e)
        {
            var list = await WifiPasswords.GetAsync();
            WifiList.ItemsSource = list;
            if (list.Count == 0) MainWindow.Instance.Toast(L.T("Không có mạng Wi-Fi nào được lưu trên máy này."), ToastKind.Info);
        }

        private void WifiCopy_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is WifiProfile w && !string.IsNullOrEmpty(w.Password))
            {
                try { Clipboard.SetText(w.Password); MainWindow.Instance.Toast(L.T("Đã sao chép mật khẩu Wi-Fi ") + w.Ssid, ToastKind.Info); } catch { }
            }
        }

        // ================= Điểm khôi phục =================
        private async Task LoadRestorePoints()
        {
            var list = await Task.Run(() => RestorePointService.List());
            RestoreList.ItemsSource = list;
            RestoreEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _restoreLoaded = true;
        }

        private async void CreateRestore_Click(object sender, RoutedEventArgs e)
        {
            var main = MainWindow.Instance;
            main.ShowBusy(L.T("Đang tạo điểm khôi phục…\nViệc này có thể mất 1–2 phút."));
            bool ok;
            try { ok = await RestorePointService.CreateAsync(L.T("DrAn For Computer - tạo thủ công ") + DateTime.Now.ToString("dd/MM/yyyy HH:mm")); }
            finally { main.HideBusy(); }
            if (ok) main.Toast(L.T("Đã tạo điểm khôi phục."));
            else Dialog.Info(L.T("Không tạo được điểm khôi phục"), L.T("System Restore có thể đang bị tắt hoặc ổ đĩa không đủ dung lượng. Hãy mở System Properties › System Protection để bật lại."), ToastKind.Warning);
            await LoadRestorePoints();
        }

        private void OpenRestore_Click(object sender, RoutedEventArgs e) => ProcessRunner.Open("rstrui.exe");

        // ================= Tiện ích trình duyệt =================
        private async Task LoadExtensions()
        {
            ExtSummary.Text = L.T("Đang tải…");
            var list = await Task.Run(() => BrowserExtensions.GetAll());
            ExtList.ItemsSource = list;
            int risky = list.Count(x => x.Risky);
            ExtSummary.Text = list.Count == 0
                ? L.T("Không tìm thấy tiện ích trình duyệt nào.")
                : L.F("{0} tiện ích trình duyệt, {1} tiện ích có quyền đọc dữ liệu trên mọi trang web. Hãy gỡ những tiện ích bạn không nhận ra – chúng có thể chèn quảng cáo hoặc đánh cắp thông tin.", list.Count, risky);
        }

        private async void ExtRefresh_Click(object sender, RoutedEventArgs e) => await LoadExtensions();

        private void ExtFolder_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is BrowserExtension x && Directory.Exists(x.Folder)) ProcessRunner.Open(x.Folder);
        }

        private void ExtManage_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is BrowserExtension x) BrowserExtensions.OpenManager(x);
        }

        // ================= Tệp hosts =================
        private void HostsReload_Click(object sender, RoutedEventArgs e) => HostsText.Text = HostsFile.Read();
        private void HostsDefault_Click(object sender, RoutedEventArgs e) => HostsText.Text = HostsFile.Default;

        private void HostsSave_Click(object sender, RoutedEventArgs e)
        {
            if (!Dialog.Confirm(L.T("Lưu tệp hosts?"), L.T("Bản cũ sẽ được sao lưu trong thư mục dữ liệu của DrAn For Computer."), L.T("Lưu"), kind: ToastKind.Info)) return;
            try
            {
                HostsFile.Save(HostsText.Text);
                MainWindow.Instance.Toast(L.T("Đã lưu tệp hosts."));
            }
            catch (Exception ex)
            {
                Dialog.Info(L.T("Không lưu được"), ex.Message + L.T("\n\nPhần mềm diệt virus có thể đang khóa tệp hosts."), ToastKind.Error);
            }
        }
    }
}
