using System.Windows;
using System.Windows.Controls;
using BacSiMayTinh.Controls;
using BacSiMayTinh.Core;
using BacSiMayTinh.Modules;
using BacSiMayTinh.UI;

namespace BacSiMayTinh.Pages
{
    public partial class DriversPage : UserControl, IPage, ISnapSub
    {
        private List<DriverInfo> _all;
        private List<WuUpdate> _updates;
        private List<StorePackage> _store;
        private bool _loaded, _busy, _vendorLoaded;

        public DriversPage()
        {
            InitializeComponent();
        }

        public void OnShown()
        {
            if (!_loaded) _ = LoadInstalled();
        }

        public void OnHidden() { }

        public void SnapSub(string key)
        {
            foreach (var rb in Tabs.Children.OfType<RadioButton>()) if ((string)rb.Tag == key) rb.IsChecked = true;
        }

        public void ShowTab(string key) => SnapSub(key);

        private async void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (InstalledPanel == null) return;
            var tag = (string)((RadioButton)sender).Tag;
            InstalledPanel.Visibility = tag == "installed" ? Visibility.Visible : Visibility.Collapsed;
            UpdatePanel.Visibility = tag == "update" ? Visibility.Visible : Visibility.Collapsed;
            StorePanel.Visibility = tag == "store" ? Visibility.Visible : Visibility.Collapsed;
            HistoryPanel.Visibility = tag == "history" ? Visibility.Visible : Visibility.Collapsed;
            if (tag == "history") LoadHistory();
            if (tag == "update" && !_vendorLoaded) await LoadVendors();
        }

        // ===================== Driver đang dùng =====================
        private async Task LoadInstalled()
        {
            _loaded = true;
            InstalledSummary.Text = L.T("Đang đọc danh sách driver…");
            var data = await Task.Run(() => (drivers: DriverManager.List(true), problems: DriverManager.Problems()));
            _all = data.drivers;
            ShowProblems(data.problems);
            ApplyFilter();
            MainWindow.TrimMemory();
        }

        private void ShowProblems(List<ProblemDevice> problems)
        {
            ProblemCard.Visibility = problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ProblemTitle.Text = L.F("{0} thiết bị đang gặp sự cố hoặc thiếu driver", problems.Count);
            ProblemList.ItemsSource = problems;
        }

        private void ApplyFilter()
        {
            if (_all == null) return;
            var q = (SearchBox.Text ?? "").Trim();
            IEnumerable<DriverInfo> list = _all;
            if (InboxBox.IsChecked != true) list = list.Where(d => d.IsThirdParty);
            if (q.Length > 0)
                list = list.Where(d => (d.DeviceName + " " + d.Provider + " " + d.Manufacturer + " " + d.ClassText + " " + d.InfName)
                    .IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);
            var shown = list.ToList();
            DriverList.ItemsSource = shown;
            int third = _all.Count(d => d.IsThirdParty), old = _all.Count(d => d.IsOld);
            InstalledSummary.Text = L.F("{0} thiết bị dùng driver của hãng thứ ba, {1} dùng driver có sẵn của Windows.", third, _all.Count - third) +
                                    (old > 0 ? L.F(" {0} driver đã hơn 5 năm tuổi – nên kiểm tra bản mới.", old) : "");
        }

        private void Search_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();
        private void Inbox_Click(object sender, RoutedEventArgs e) => ApplyFilter();

        private string PickFolder(string description)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = description })
                return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.SelectedPath : null;
        }

        private async Task DoExport(List<string> infs)
        {
            var p = PickFolder(L.T("Chọn nơi lưu bản sao lưu driver (nên chọn ổ D hoặc USB)"));
            if (p == null) return;
            var target = Path.Combine(p, "SaoLuuDriver_" + ConfigBackup.SafeName(Environment.MachineName) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm"));
            var main = MainWindow.Instance;
            main.ShowBusy(L.T("Đang sao lưu driver…\nCó thể mất vài phút."));
            int n;
            try { n = await DriverManager.ExportAsync(infs, target, s => main.SetBusyText(L.T("Đang sao lưu driver…\n") + s)); }
            finally { main.HideBusy(); }
            main.Toast(L.F("Đã sao lưu {0} gói driver vào {1}", n, target));
            ProcessRunner.Open(target);
        }

        private async void BackupAll_Click(object sender, RoutedEventArgs e) => await DoExport(null);

        private async void BackupSelected_Click(object sender, RoutedEventArgs e)
        {
            var sel = (_all ?? new List<DriverInfo>()).Where(d => d.IsChecked && d.IsThirdParty).Select(d => d.InfName).ToList();
            if (sel.Count == 0) { MainWindow.Instance.Toast(L.T("Hãy tích chọn driver muốn sao lưu (driver có sẵn của Windows không cần sao lưu)."), ToastKind.Info); return; }
            await DoExport(sel);
        }

        private async void InstallFolder_Click(object sender, RoutedEventArgs e)
        {
            var p = PickFolder(L.T("Chọn thư mục chứa driver (bản sao lưu hoặc driver tải từ trang hãng, có tệp .inf)"));
            if (p == null) return;
            int infs = 0;
            try { infs = Directory.GetFiles(p, "*.inf", SearchOption.AllDirectories).Length; } catch { }
            if (infs == 0) { Dialog.Info(L.T("Không có driver"), L.T("Thư mục này không chứa tệp .inf nào. Nếu là tệp .exe của hãng, hãy chạy tệp đó để cài."), ToastKind.Warning); return; }
            if (!Dialog.Confirm(L.T("Cài driver từ thư mục?"), L.F("Tìm thấy {0} tệp .inf. Windows sẽ chỉ dùng driver nào phù hợp và tốt hơn driver hiện tại.", infs), L.T("Cài đặt"), kind: ToastKind.Info)) return;
            await RestorePointService.EnsureForSessionAsync();
            var main = MainWindow.Instance;
            main.ShowBusy(L.T("Đang cài driver…"));
            (bool ok, int count) r;
            try { r = await Task.Run(() => DriverManager.InstallFromFolder(p, s => main.SetBusyText(L.T("Đang cài driver…\n") + s))); }
            finally { main.HideBusy(); }
            Dialog.Info(r.ok ? L.T("Đã cài driver") : L.T("Cài driver chưa hoàn tất"),
                L.F("Đã nạp {0} gói driver. Nên khởi động lại máy để driver có hiệu lực.", r.count), r.ok ? ToastKind.Success : ToastKind.Warning);
            await LoadInstalled();
        }

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            var main = MainWindow.Instance;
            main.ShowBusy(L.T("Đang quét lại phần cứng…"));
            try { await DriverManager.ScanHardwareAsync(); await Task.Delay(1500); }
            finally { main.HideBusy(); }
            await LoadInstalled();
        }

        private void FindForProblem_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("update");
            SearchUpdates_Click(null, null);
        }

        // ===================== Cập nhật =====================
        private async Task LoadVendors()
        {
            _vendorLoaded = true;
            var v = await Task.Run(() => DriverManager.VendorTools());
            VendorList.ItemsSource = v;
            VendorCard.Visibility = Visibility.Visible;
        }

        private void Vendor_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is VendorTool t) ProcessRunner.Open(t.Url);
        }

        private void SetUpdateBusy(bool busy, string text = null)
        {
            _busy = busy;
            SearchBtn.IsEnabled = !busy;
            InstallBtn.IsEnabled = !busy && _updates != null && _updates.Any(u => u.IsChecked);
            UpdateSpinner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (text != null) UpdateStatus.Text = text;
        }

        private async void SearchUpdates_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            if (Wua.ServiceDisabled)
            {
                if (!Dialog.Confirm(L.T("Windows Update đang bị tắt"), L.T("Cần bật dịch vụ Windows Update (chế độ Thủ công) để tìm driver. Bật lại ngay?"), L.T("Bật lại"))) return;
                Wua.EnableService();
            }
            UpdateList.ItemsSource = null;
            UpdateEmpty.Visibility = Visibility.Visible;
            UpdateEmptyText.Text = L.T("Đang hỏi máy chủ Windows Update… (thường mất 1–3 phút)");
            SetUpdateBusy(true, L.T("Đang tìm driver mới…"));
            try
            {
                _updates = await Task.Run(() => DriverUpdater.Search(CancellationToken.None));
                UpdateList.ItemsSource = _updates;
                UpdateEmpty.Visibility = _updates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                UpdateEmptyText.Text = L.T("Không có driver mới trên Windows Update – driver của máy đã là bản phù hợp nhất. Muốn bản mới hơn nữa, hãy dùng công cụ của hãng bên dưới.");
                UpdateStatus.Text = _updates.Count == 0 ? L.T("Driver đã cập nhật đầy đủ.") : L.F("Có {0} driver mới. Chọn các mục muốn cài rồi bấm « Cài mục đã chọn ».", _updates.Count);
                UpdateTitle.Text = _updates.Count == 0 ? L.T("Driver đã là bản mới nhất") : L.F("Tìm thấy {0} driver mới", _updates.Count);
            }
            catch (WuServiceDisabledException ex) { UpdateEmptyText.Text = ex.Message; }
            catch (Exception ex)
            {
                Logger.Error("Tìm driver", ex);
                UpdateEmptyText.Text = L.T("Không tìm được: ") + ex.Message;
            }
            finally { SetUpdateBusy(false); }
        }

        private void UpdateCheck_Click(object sender, RoutedEventArgs e) => SetUpdateBusy(false);

        private async void HideUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_busy || !((sender as FrameworkElement)?.DataContext is WuUpdate u)) return;
            if (!Dialog.Confirm(L.T("Ẩn bản cập nhật này?"), u.Title + L.T("\n\nWindows sẽ không tự cài bản này nữa. Có thể bỏ ẩn sau."), L.T("Ẩn"), kind: ToastKind.Info)) return;
            if (await Task.Run(() => Wua.SetHidden(u, true)))
            {
                _updates.Remove(u);
                UpdateList.ItemsSource = null;
                UpdateList.ItemsSource = _updates;
                MainWindow.Instance.Toast(L.T("Đã ẩn bản cập nhật."), ToastKind.Info);
            }
        }

        private async void Unhide_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            var main = MainWindow.Instance;
            main.ShowBusy(L.T("Đang tìm các bản cập nhật đã ẩn…"));
            int n = 0;
            try
            {
                n = await Task.Run(() =>
                {
                    int c = 0;
                    using (DriverPolicy.AllowDuringScan())
                        foreach (var u in Wua.Search("IsInstalled=0 and Type='Driver' and IsHidden=1", true, CancellationToken.None))
                            if (Wua.SetHidden(u, false)) c++;
                    return c;
                });
            }
            catch (Exception ex) { main.Toast(L.T("Lỗi: ") + ex.Message, ToastKind.Error); return; }
            finally { main.HideBusy(); }
            main.Toast(n == 0 ? L.T("Không có driver nào đang bị ẩn.") : L.F("Đã bỏ ẩn {0} bản cập nhật driver. Hãy tìm lại.", n), ToastKind.Info);
        }

        private async void InstallUpdates_Click(object sender, RoutedEventArgs e)
        {
            if (_busy || _updates == null) return;
            var sel = _updates.Where(u => u.IsChecked).ToList();
            if (sel.Count == 0) return;
            if (!Dialog.Confirm(L.F("Cài {0} driver?", sel.Count),
                    "• " + string.Join("\n• ", sel.Select(u => u.Title)) +
                    L.T("\n\nTrước khi cài: driver hiện tại được sao lưu, cấu hình được tự sao lưu và một điểm khôi phục được tạo. Màn hình có thể chớp tắt khi cài driver card đồ họa."),
                    L.T("Cài đặt"), kind: ToastKind.Info)) return;

            await RestorePointService.EnsureForSessionAsync();
            SetUpdateBusy(true, L.T("Đang cài driver…"));
            bool reboot = false;
            int ok = 0;
            try
            {
                foreach (var u in sel)
                {
                    u.Status = L.T("Đang chuẩn bị…");
                    u.StatusBrush = Pal.Blue;
                    var r = await Task.Run(() => DriverUpdater.Install(u, s => Dispatcher.BeginInvoke(new Action(() => u.Status = s))));
                    u.Status = r.Message;
                    u.StatusBrush = r.Success ? Pal.Green : Pal.Red;
                    if (r.Success) { ok++; u.IsChecked = false; }
                    reboot |= r.RebootRequired;
                }
            }
            finally { SetUpdateBusy(false, L.F("Đã cài {0}/{1} driver.", ok, sel.Count)); }
            _loaded = false;
            if (reboot && Dialog.Confirm(L.T("Khởi động lại máy?"), L.T("Một số driver cần khởi động lại máy để hoàn tất. Hãy lưu công việc trước."), L.T("Khởi động lại ngay"), kind: ToastKind.Info))
                await ProcessRunner.CmdAsync("shutdown /r /t 5");
            else if (ok > 0) MainWindow.Instance.Toast(L.F("Đã cài {0} driver. Nếu có trục trặc, vào « Lịch sử & quay lại » để dùng lại bản cũ.", ok));
        }

        // ===================== Dọn driver cũ =====================
        private async void StoreScan_Click(object sender, RoutedEventArgs e)
        {
            var main = MainWindow.Instance;
            main.ShowBusy(L.T("Đang quét kho driver…"));
            try { _store = await Task.Run(() => DriverStore.FindOldDuplicates()); }
            catch (Exception ex) { Logger.Error("Quét kho driver", ex); _store = new List<StorePackage>(); }
            finally { main.HideBusy(); }
            StoreList.ItemsSource = _store;
            StoreEmpty.Visibility = _store.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            StoreEmpty.Text = L.T("Không có driver cũ trùng lặp – kho driver đã gọn gàng.");
            UpdateStoreTotal();
        }

        private void StoreCheck_Click(object sender, RoutedEventArgs e) => UpdateStoreTotal();

        private void UpdateStoreTotal()
        {
            var sel = (_store ?? new List<StorePackage>()).Where(p => p.IsChecked).ToList();
            StoreTotal.Text = sel.Count == 0 ? "" : L.F("{0} gói  ·  {1}", sel.Count, FileUtil.FormatSize(sel.Sum(p => p.Size)));
            StoreCleanBtn.IsEnabled = sel.Count > 0;
        }

        private async void StoreClean_Click(object sender, RoutedEventArgs e)
        {
            var sel = (_store ?? new List<StorePackage>()).Where(p => p.IsChecked).ToList();
            if (sel.Count == 0) return;
            if (!Dialog.Confirm(L.F("Xóa {0} gói driver cũ?", sel.Count),
                    L.F("Giải phóng khoảng {0}. Windows sẽ từ chối xóa nếu gói đang được dùng, nên thiết bị của bạn không bị ảnh hưởng.", FileUtil.FormatSize(sel.Sum(p => p.Size))),
                    L.T("Xóa"), danger: true)) return;
            var main = MainWindow.Instance;
            main.ShowBusy(L.T("Đang dọn driver cũ…"));
            int ok = 0;
            long freed = 0;
            bool backup = StoreBackup.IsChecked == true;
            try
            {
                if (backup)
                {
                    var dir = Path.Combine(ConfigBackup.Root, "DriverCuDaXoa", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
                    await DriverManager.ExportAsync(sel.Select(p => p.PublishedName), dir, s => main.SetBusyText(L.T("Đang sao lưu: ") + s));
                }
                foreach (var p in sel)
                {
                    main.SetBusyText(L.T("Đang xóa: ") + p.Title);
                    if (await Task.Run(() => DriverStore.Delete(p))) { ok++; freed += p.Size; }
                }
            }
            finally { main.HideBusy(); }
            main.Toast(L.F("Đã xóa {0}/{1} gói, giải phóng {2}.", ok, sel.Count, FileUtil.FormatSize(freed)), ok == sel.Count ? ToastKind.Success : ToastKind.Warning);
            StoreScan_Click(null, null);
        }

        // ===================== Lịch sử =====================
        private void LoadHistory()
        {
            var h = DriverHistory.Load();
            HistoryList.ItemsSource = h.Items;
            HistoryEmpty.Visibility = h.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void HistoryFolder_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is DriverChange c && Directory.Exists(c.BackupDir)) ProcessRunner.Open(c.BackupDir);
        }

        private async void Rollback_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is DriverChange c)) return;
            if (!Dialog.Confirm(L.T("Quay lại driver cũ?"), c.Title + "\n" + c.Detail +
                    L.T("\n\nGói driver mới sẽ bị gỡ và thiết bị dùng lại bản trước đó. Màn hình/mạng có thể chớp tắt trong giây lát."), L.T("Quay lại"), kind: ToastKind.Warning)) return;
            var main = MainWindow.Instance;
            main.ShowBusy(L.T("Đang quay lại driver cũ…"));
            RestoreReport r;
            try { r = await Task.Run(() => DriverUpdater.Rollback(c, s => main.SetBusyText(s))); }
            finally { main.HideBusy(); }
            LoadHistory();
            _loaded = false;
            if (Dialog.Confirm(r.Errors == 0 ? L.T("Đã quay lại driver cũ") : L.T("Chưa quay lại hoàn toàn"), string.Join("\n", r.Lines) + L.T("\n\nNên khởi động lại máy."),
                    L.T("Khởi động lại ngay"), kind: r.Errors == 0 ? ToastKind.Success : ToastKind.Warning))
                await ProcessRunner.CmdAsync("shutdown /r /t 5");
        }
    }
}
