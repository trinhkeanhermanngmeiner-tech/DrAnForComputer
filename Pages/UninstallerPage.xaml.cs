using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using BacSiMayTinh.Core;
using BacSiMayTinh.Modules;
using BacSiMayTinh.UI;

namespace BacSiMayTinh.Pages
{
    public partial class UninstallerPage : UserControl, IPage, ISnapSub
    {
        private List<InstalledApp> _all;
        private bool _userDone, _cancel;
        private List<Leftover> _leftovers;

        public UninstallerPage()
        {
            InitializeComponent();
        }

        public void OnShown()
        {
            if (_all == null) _ = LoadAsync();
        }

        public void OnHidden() { }

        public void SnapSub(string key)
        {
            if (key == "update") TabUpdate.IsChecked = true;
            if (key == "install") TabInstall.IsChecked = true;
        }

        // ================= Tab =================
        private bool? _winget;
        private List<WingetPackage> _updates;
        private List<WingetCategory> _catalog;
        private bool _wingetBusy;

        private async void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (RemovePanel == null) return;
            var tag = (string)((RadioButton)sender).Tag;
            RemovePanel.Visibility = tag == "remove" ? Visibility.Visible : Visibility.Collapsed;
            UpdatePanel.Visibility = Visibility.Collapsed;
            InstallPanel.Visibility = Visibility.Collapsed;
            NoWingetPanel.Visibility = Visibility.Collapsed;
            if (tag == "remove") return;

            if (_winget == null) _winget = await Winget.IsAvailableAsync();
            if (_winget != true) { NoWingetPanel.Visibility = Visibility.Visible; return; }
            if (tag == "update")
            {
                UpdatePanel.Visibility = Visibility.Visible;
                if (_updates == null) await CheckUpdates();
            }
            else
            {
                InstallPanel.Visibility = Visibility.Visible;
                if (_catalog == null) await LoadCatalog();
            }
        }

        private void OpenStore_Click(object sender, RoutedEventArgs e) => ProcessRunner.Open("ms-windows-store://pdp/?productid=9NBLGGH4NNS1");

        private static void AppendLog(TextBox box, string line)
        {
            box.AppendText(line + Environment.NewLine);
            box.ScrollToEnd();
        }

        // ================= Cập nhật phần mềm =================
        private async Task CheckUpdates()
        {
            UpdCheckBtn.IsEnabled = UpdRunBtn.IsEnabled = false;
            UpdSummary.Text = L.T("Đang kiểm tra phiên bản mới… (có thể mất 10–30 giây)");
            UpdList.ItemsSource = null;
            try
            {
                _updates = await Winget.ListUpgradesAsync();
                UpdList.ItemsSource = _updates;
                UpdSummary.Text = _updates.Count == 0
                    ? L.T("Tuyệt vời! Mọi phần mềm đều đang ở phiên bản mới nhất.")
                    : L.F("Có {0} phần mềm có phiên bản mới. Bỏ chọn những phần mềm bạn không muốn cập nhật.", _updates.Count);
            }
            finally
            {
                UpdCheckBtn.IsEnabled = true;
                UpdRunBtn.IsEnabled = _updates != null && _updates.Count > 0;
            }
        }

        private async void UpdCheck_Click(object sender, RoutedEventArgs e) { if (!_wingetBusy) await CheckUpdates(); }

        private async void UpdRun_Click(object sender, RoutedEventArgs e)
        {
            var sel = _updates?.Where(u => u.IsChecked).ToList() ?? new List<WingetPackage>();
            if (sel.Count == 0 || _wingetBusy) return;
            if (!Dialog.Confirm(L.F("Cập nhật {0} phần mềm?", sel.Count),
                L.T("Hãy đóng các phần mềm sẽ được cập nhật và lưu công việc đang làm. Quá trình chạy ngầm, không cần bấm Next."),
                L.T("Cập nhật"), kind: ToastKind.Info)) return;
            await RunWinget(sel, true, UpdLog, UpdLogPanel);
            await CheckUpdates();
        }

        // ================= Cài nhanh =================
        private async Task LoadCatalog()
        {
            _catalog = Winget.Catalog();
            CatalogList.ItemsSource = _catalog;
            InsSummary.Text = L.T("Đang kiểm tra phần mềm đã cài…");
            var installed = await Winget.InstalledIdsAsync();
            foreach (var p in _catalog.SelectMany(c => c.Items)) p.IsInstalled = installed.Contains(p.Id);
            InsSummary.Text = L.T("Chọn phần mềm cần cài – tải trực tiếp từ nguồn chính thức của nhà phát hành, cài tự động không cần bấm Next.");
        }

        private async void InsRun_Click(object sender, RoutedEventArgs e)
        {
            var sel = _catalog?.SelectMany(c => c.Items).Where(p => p.IsChecked).ToList() ?? new List<WingetPackage>();
            if (sel.Count == 0) { MainWindow.Instance.Toast(L.T("Hãy chọn ít nhất một phần mềm."), ToastKind.Info); return; }
            if (_wingetBusy) return;
            if (!Dialog.Confirm(L.F("Cài đặt {0} phần mềm?", sel.Count),
                "• " + string.Join("\n• ", sel.Select(p => p.Name)) + "\n\n" + L.T("Phần mềm được tải từ nguồn chính thức qua winget. Cần kết nối Internet."),
                L.T("Cài đặt"), kind: ToastKind.Info)) return;
            await RunWinget(sel, false, InsLog, InsLogPanel);
            var installed = await Winget.InstalledIdsAsync();
            foreach (var p in _catalog.SelectMany(c => c.Items)) { p.IsInstalled = installed.Contains(p.Id); if (p.IsInstalled) p.IsChecked = false; }
        }

        private async Task RunWinget(List<WingetPackage> items, bool upgrade, TextBox log, Border logPanel)
        {
            _wingetBusy = true;
            UpdRunBtn.IsEnabled = UpdCheckBtn.IsEnabled = InsRunBtn.IsEnabled = false;
            logPanel.Visibility = Visibility.Visible;
            int ok = 0, n = 0;
            try
            {
                foreach (var p in items)
                {
                    n++;
                    p.Status = L.F("Đang xử lý ({0}/{1})…", n, items.Count);
                    AppendLog(log, "══ " + p.Name + " (" + p.Id + ")");
                    Action<string, bool> onLine = (line, progress) =>
                    {
                        if (progress) return;
                        var t = line.Trim();
                        if (t.Length > 1 && !t.All(ch => "-\\|/█▒ ".IndexOf(ch) >= 0))
                            Dispatcher.BeginInvoke(new Action(() => AppendLog(log, "   " + t)));
                    };
                    var r = upgrade ? await Winget.UpgradeAsync(p.Id, onLine) : await Winget.InstallAsync(p.Id, onLine);
                    if (r.Ok) { ok++; p.Status = upgrade ? L.T("✓ Đã cập nhật") : L.T("✓ Đã cài đặt"); }
                    else p.Status = L.F("✗ Lỗi (mã {0})", r.ExitCode);
                    Logger.Info($"winget {(upgrade ? "upgrade" : "install")} {p.Id}: {r.ExitCode}");
                }
            }
            finally
            {
                _wingetBusy = false;
                UpdCheckBtn.IsEnabled = InsRunBtn.IsEnabled = true;
                UpdRunBtn.IsEnabled = _updates != null && _updates.Count > 0;
            }
            MainWindow.Instance.Toast(L.F("Hoàn tất {0}/{1} phần mềm.", ok, items.Count), ok == items.Count ? ToastKind.Success : ToastKind.Warning);
        }

        private async Task LoadAsync()
        {
            Summary.Text = L.T("Đang tải danh sách phần mềm…");
            _all = await Task.Run(() => UninstallerService.List());
            ApplyFilter();
            var list = _all.ToList();
            await Task.Run(() =>
            {
                foreach (var a in list) a.Icon = IconHelper.FromFile(UninstallerService.IconFile(a));
            });
        }

        private void ApplyFilter()
        {
            if (_all == null) return;
            var q = SearchBox.Text.Trim();
            IEnumerable<InstalledApp> items = _all;
            if (q.Length > 0)
                items = items.Where(a => a.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0
                                      || (a.Publisher ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);
            switch (SortBox.SelectedIndex)
            {
                case 1: items = items.OrderByDescending(a => a.SizeBytes); break;
                case 2: items = items.OrderByDescending(a => a.InstallDate ?? DateTime.MinValue); break;
                default: items = items.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase); break;
            }
            var list = items.ToList();
            AppList.ItemsSource = list;
            Summary.Text = L.F("{0} phần mềm  ·  tổng dung lượng ước tính {1}", list.Count, FileUtil.FormatSize(list.Sum(a => a.SizeBytes)));
        }

        private void Search_Changed(object sender, TextChangedEventArgs e)
        {
            SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilter();
        }

        private void Sort_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) ApplyFilter(); }
        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private async void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is InstalledApp app)) return;
            if (!Dialog.Confirm(L.T("Gỡ cài đặt ") + app.Name + "?",
                L.T("Trình gỡ cài đặt của phần mềm sẽ được mở. Sau khi gỡ xong, DrAn For Computer sẽ quét thư mục và registry còn sót lại."),
                L.T("Gỡ cài đặt"), danger: true)) return;

            Process proc;
            try { proc = UninstallerService.StartUninstall(app); }
            catch (Exception ex)
            {
                Logger.Error("Mở trình gỡ cài đặt " + app.Name, ex);
                Dialog.Info(L.T("Không mở được trình gỡ cài đặt"), ex.Message, ToastKind.Error);
                return;
            }

            _userDone = _cancel = false;
            WaitTitle.Text = L.T("Đang gỡ ") + app.Name + "…";
            WaitPanel.Visibility = Visibility.Visible;
            LeftoverPanel.Visibility = Visibility.Collapsed;
            Overlay.Visibility = Visibility.Visible;

            // Chờ tới khi phần mềm biến mất khỏi danh sách cài đặt hoặc người dùng xác nhận.
            while (!_cancel && !_userDone)
            {
                await Task.Delay(1000);
                if (!UninstallerService.StillInstalled(app)) break;
            }
            proc?.Dispose();
            if (_cancel) { Overlay.Visibility = Visibility.Collapsed; return; }

            if (UninstallerService.StillInstalled(app))
            {
                Overlay.Visibility = Visibility.Collapsed;
                Dialog.Info(L.T("Phần mềm vẫn còn"), app.Name + L.T(" vẫn còn trong máy (có thể bạn đã hủy quá trình gỡ). Không quét phần còn sót để tránh xóa nhầm."), ToastKind.Warning);
                return;
            }

            WaitTitle.Text = L.T("Đang quét phần còn sót lại…");
            _leftovers = await Task.Run(() => UninstallerService.ScanLeftovers(app));
            _all.Remove(app);
            ApplyFilter();
            if (_leftovers.Count == 0)
            {
                Overlay.Visibility = Visibility.Collapsed;
                MainWindow.Instance.Toast(L.F("Đã gỡ sạch {0} – không còn gì sót lại.", app.Name));
                return;
            }
            LeftoverTitle.Text = L.F("Tìm thấy {0} mục còn sót lại của {1}", _leftovers.Count, app.Name);
            LeftoverList.ItemsSource = _leftovers;
            WaitPanel.Visibility = Visibility.Collapsed;
            LeftoverPanel.Visibility = Visibility.Visible;
        }

        private void WaitCancel_Click(object sender, RoutedEventArgs e) => _cancel = true;
        private void WaitDone_Click(object sender, RoutedEventArgs e) => _userDone = true;
        private void LeftoverSkip_Click(object sender, RoutedEventArgs e) => Overlay.Visibility = Visibility.Collapsed;

        private async void LeftoverDelete_Click(object sender, RoutedEventArgs e)
        {
            var sel = _leftovers?.Where(l => l.IsChecked).ToList() ?? new List<Leftover>();
            if (sel.Count == 0) { Overlay.Visibility = Visibility.Collapsed; return; }
            int n = await Task.Run(() => UninstallerService.DeleteLeftovers(sel));
            Overlay.Visibility = Visibility.Collapsed;
            MainWindow.Instance.Toast(L.F("Đã dọn {0} mục còn sót lại.", n));
        }
    }
}
