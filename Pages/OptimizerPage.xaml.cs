using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using BacSiMayTinh.Core;
using BacSiMayTinh.Modules;
using BacSiMayTinh.UI;

namespace BacSiMayTinh.Pages
{
    public partial class OptimizerPage : UserControl, IPage, ISnapSub
    {
        private List<StartupItem> _startup;
        private List<Tweak> _tweaks;
        private List<ServiceTweak> _services;
        private List<AppxItem> _appx;
        private List<ContextMenuItem> _context;
        private string _tab;

        public OptimizerPage()
        {
            InitializeComponent();
            Loaded += (s, e) => { if (_tab == null) TabStartup.IsChecked = true; };
        }

        public void OnShown() { }
        public void OnHidden() { }
        public void SnapSub(string key) => ShowTab(key);

        public void ShowTab(string key)
        {
            var rb = key == "tweaks" ? TabTweaks : key == "services" ? TabServices : key == "appx" ? TabAppx : key == "context" ? TabContext : TabStartup;
            if (rb.IsChecked == true) Tab_Checked(rb, null);
            else rb.IsChecked = true;
        }

        private async void Tab_Checked(object sender, RoutedEventArgs e)
        {
            _tab = (string)((RadioButton)sender).Tag;
            StartupPanel.Visibility = _tab == "startup" ? Visibility.Visible : Visibility.Collapsed;
            TweaksPanel.Visibility = _tab == "tweaks" ? Visibility.Visible : Visibility.Collapsed;
            ServicesPanel.Visibility = _tab == "services" ? Visibility.Visible : Visibility.Collapsed;
            AppxPanel.Visibility = _tab == "appx" ? Visibility.Visible : Visibility.Collapsed;
            ContextPanel.Visibility = _tab == "context" ? Visibility.Visible : Visibility.Collapsed;
            switch (_tab)
            {
                case "startup": if (_startup == null) await LoadStartup(); break;
                case "tweaks": if (_tweaks == null) await LoadTweaks(); break;
                case "services": if (_services == null) await LoadServices(); break;
                case "appx": if (_appx == null) await LoadAppx(); break;
                case "context": if (_context == null) await LoadContext(); break;
            }
        }

        // ================= Menu chuột phải =================
        private async Task LoadContext()
        {
            ContextSummary.Text = L.T("Đang tải…");
            _context = await Task.Run(() => ContextMenuManager.GetItems());
            ContextList.ItemsSource = _context;
            ContextSummary.Text = _context.Count == 0
                ? L.T("Không có mục nào do phần mềm khác thêm vào menu chuột phải.")
                : L.F("{0} mục do phần mềm khác thêm vào menu chuột phải. Tắt bớt giúp menu mở nhanh và gọn hơn – có thể bật lại bất cứ lúc nào.", _context.Count);
            await Task.Run(() => { foreach (var i in _context.ToList()) i.Icon = IconHelper.FromFile(i.FilePath); });
        }

        private async void ContextRefresh_Click(object sender, RoutedEventArgs e) => await LoadContext();

        private void ContextToggle_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is ContextMenuItem item)) return;
            try
            {
                ContextMenuManager.SetEnabled(item, item.IsEnabled);
                MainWindow.Instance.Toast((item.IsEnabled ? L.T("Đã bật: ") : L.T("Đã tắt: ")) + item.Name + " – " + L.T("có hiệu lực sau khi khởi động lại Explorer."), ToastKind.Info);
            }
            catch (Exception ex)
            {
                item.IsEnabled = !item.IsEnabled;
                MainWindow.Instance.Toast(L.T("Không thay đổi được: ") + ex.Message, ToastKind.Error);
            }
        }

        // ================= Khởi động =================
        private async Task LoadStartup()
        {
            StartupSummary.Text = L.T("Đang tải danh sách…");
            _startup = await Task.Run(() => StartupManager.GetItems());
            StartupList.ItemsSource = _startup;
            UpdateStartupSummary();
            await Task.Run(() => { foreach (var i in _startup.ToList()) i.Icon = IconHelper.FromFile(i.ExePath); });
        }

        private void UpdateStartupSummary()
        {
            int on = _startup.Count(i => i.IsEnabled);
            StartupSummary.Text = _startup.Count == 0
                ? L.T("Không có ứng dụng nào khởi động cùng Windows.")
                : L.F("{0}/{1} ứng dụng đang tự chạy khi bật máy. Tắt bớt những ứng dụng không cần thiết để máy khởi động nhanh hơn (không ảnh hưởng tới việc dùng ứng dụng).", on, _startup.Count);
        }

        private async void StartupRefresh_Click(object sender, RoutedEventArgs e) => await LoadStartup();

        private void StartupToggle_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is StartupItem item)) return;
            try
            {
                StartupManager.SetEnabled(item, item.IsEnabled);
                MainWindow.Instance.Toast((item.IsEnabled ? L.T("Đã bật: ") : L.T("Đã tắt khởi động cùng Windows: ")) + item.Name);
            }
            catch (Exception ex)
            {
                item.IsEnabled = !item.IsEnabled;
                MainWindow.Instance.Toast(L.T("Không thay đổi được: ") + ex.Message, ToastKind.Error);
            }
            UpdateStartupSummary();
        }

        private void StartupDelete_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is StartupItem item)) return;
            if (!Dialog.Confirm(L.T("Xóa khỏi danh sách khởi động?"),
                L.F("\"{0}\" sẽ bị xóa hẳn khỏi danh sách khởi động cùng Windows.\n\nNên dùng công tắc để TẮT thay vì xóa, để có thể bật lại khi cần.", item.Name),
                L.T("Xóa"), danger: true)) return;
            StartupManager.Delete(item);
            _startup.Remove(item);
            StartupList.ItemsSource = null;
            StartupList.ItemsSource = _startup;
            UpdateStartupSummary();
            MainWindow.Instance.Toast(L.T("Đã xóa ") + item.Name);
        }

        // ================= Tinh chỉnh =================
        private async Task LoadTweaks()
        {
            _tweaks = await Task.Run(() =>
            {
                var l = TweakCatalog.Build();
                foreach (var t in l) t.Refresh();
                return l;
            });
            TweakGroups.ItemsSource = TweakCatalog.CategoryGlyphs.Keys
                .Select(k => new TweakCategory { Name = L.T(k), Glyph = TweakCatalog.CategoryGlyphs[k], Items = _tweaks.Where(t => t.Category == k).ToList() })
                .Where(g => g.Items.Count > 0).ToList();
        }

        private async void TweakToggle_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is Tweak t)) return;
            bool want = t.IsApplied;
            t.IsBusy = true;
            try
            {
                if (want) await RestorePointService.EnsureForSessionAsync();
                await Task.Run(() => { if (want) t.Apply(); else t.Revert(); });
                Native.BroadcastSettingChange();
                MainWindow.Instance.Toast((want ? L.T("Đã áp dụng: ") : L.T("Đã hoàn tác: ")) + t.Name + (t.HasNote && want ? " – " + t.Note : ""));
            }
            catch (Exception ex)
            {
                Logger.Error("Tinh chỉnh " + t.Id, ex);
                MainWindow.Instance.Toast(L.T("Không thực hiện được: ") + ex.Message, ToastKind.Error);
            }
            finally
            {
                t.Refresh();
                t.IsBusy = false;
            }
        }

        private async void ApplyRecommended_Click(object sender, RoutedEventArgs e)
        {
            var todo = _tweaks?.Where(t => t.Recommended && !t.IsApplied).ToList() ?? new List<Tweak>();
            if (todo.Count == 0) { MainWindow.Instance.Toast(L.T("Tất cả mục khuyên dùng đã được áp dụng."), ToastKind.Info); return; }
            if (!Dialog.Confirm(L.T("Áp dụng mục khuyên dùng?"),
                L.T("Sẽ áp dụng ") + todo.Count + L.T(" tinh chỉnh:\n\n• ") + string.Join("\n• ", todo.Select(t => t.Name)) + L.T("\n\nBạn có thể hoàn tác từng mục hoặc tất cả bất cứ lúc nào."),
                L.T("Áp dụng"), kind: ToastKind.Info)) return;
            await RestorePointService.EnsureForSessionAsync();
            await ApplyTweaks(todo);
            MainWindow.Instance.Toast(L.F("Đã áp dụng {0} tinh chỉnh khuyên dùng.", todo.Count));
        }

        private static async Task ApplyTweaks(IEnumerable<Tweak> list)
        {
            var main = MainWindow.Instance;
            main.ShowBusy(L.T("Đang áp dụng tinh chỉnh…"));
            try
            {
                await Task.Run(() =>
                {
                    foreach (var t in list)
                    {
                        main.SetBusyText(L.T("Đang áp dụng: ") + t.Name);
                        try { t.Apply(); } catch (Exception ex) { Logger.Error("Áp dụng " + t.Id, ex); }
                        t.Refresh();
                    }
                });
                Native.BroadcastSettingChange();
            }
            finally { main.HideBusy(); }
        }

        private async void RestartExplorer_Click(object sender, RoutedEventArgs e)
        {
            await ProcessRunner.CmdAsync("taskkill /f /im explorer.exe");
            await Task.Delay(2500);
            if (Process.GetProcessesByName("explorer").Length == 0)
                ProcessRunner.Open(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
            MainWindow.Instance.Toast(L.T("Đã khởi động lại Windows Explorer."));
        }

        // ================= Dịch vụ =================
        private async Task LoadServices()
        {
            _services = await Task.Run(() => ServiceCatalog.Build());
            ServiceList.ItemsSource = _services;
        }

        private async void ServiceToggle_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is ServiceTweak s)) return;
            bool disable = s.IsDisabled;
            s.IsBusy = true;
            try
            {
                if (disable) await RestorePointService.EnsureForSessionAsync();
                await Task.Run(() => { if (disable) s.Disable(); else s.Restore(); });
                MainWindow.Instance.Toast((disable ? L.T("Đã tắt dịch vụ: ") : L.T("Đã khôi phục dịch vụ: ")) + s.Title);
            }
            catch (Exception ex)
            {
                MainWindow.Instance.Toast(L.T("Không thay đổi được dịch vụ: ") + ex.Message, ToastKind.Error);
            }
            finally
            {
                await Task.Run(() => s.Refresh());
                s.IsBusy = false;
            }
        }

        // ================= Ứng dụng cài sẵn =================
        private async Task LoadAppx()
        {
            AppxSummary.Text = L.T("Đang tải danh sách ứng dụng…");
            AppxList.ItemsSource = null;
            _appx = await AppxDebloat.ListAsync();
            ShowAppx();
        }

        private void ShowAppx()
        {
            var show = AppxShowAll.IsChecked == true ? _appx : _appx.Where(a => a.Known).ToList();
            AppxList.ItemsSource = show;
            AppxSummary.Text = show.Count == 0
                ? L.T("Không tìm thấy ứng dụng cài sẵn không cần thiết. Tuyệt vời!")
                : L.F("Tìm thấy {0} ứng dụng cài sẵn. Chọn những ứng dụng bạn không dùng để gỡ (có thể cài lại từ Microsoft Store).", show.Count);
        }

        private void AppxShowAll_Click(object sender, RoutedEventArgs e) { if (_appx != null) ShowAppx(); }
        private async void AppxRefresh_Click(object sender, RoutedEventArgs e) => await LoadAppx();

        private async void AppxRemove_Click(object sender, RoutedEventArgs e)
        {
            var sel = _appx?.Where(a => a.IsChecked).ToList() ?? new List<AppxItem>();
            if (sel.Count == 0) { MainWindow.Instance.Toast(L.T("Hãy chọn ít nhất một ứng dụng để gỡ."), ToastKind.Info); return; }
            if (!Dialog.Confirm(L.T("Gỡ ") + sel.Count + L.T(" ứng dụng?"),
                "• " + string.Join("\n• ", sel.Select(a => a.Title)) + L.T("\n\nBạn có thể cài lại bất cứ lúc nào từ Microsoft Store."),
                L.T("Gỡ cài đặt"), danger: true)) return;
            var main = MainWindow.Instance;
            main.ShowBusy(L.T("Đang gỡ ứng dụng…"));
            int ok = 0;
            try
            {
                foreach (var a in sel)
                {
                    main.SetBusyText(L.T("Đang gỡ: ") + a.Title);
                    if (await AppxDebloat.RemoveAsync(a)) ok++;
                }
            }
            finally { main.HideBusy(); }
            main.Toast(L.F("Đã gỡ {0}/{1} ứng dụng.", ok, sel.Count), ok == sel.Count ? ToastKind.Success : ToastKind.Warning);
            await LoadAppx();
        }

        // ================= Chế độ máy yếu & hoàn tác =================
        public async void RunLowEndPreset()
        {
            bool hdd = SystemQuick.SystemDiskType() == "HDD";
            var msg = L.T("Chế độ máy yếu sẽ áp dụng một lần các tối ưu sau:\n\n") +
                      L.T("• Tối ưu hiệu ứng hình ảnh, tắt hiệu ứng trong suốt\n") +
                      L.T("• Tắt Tin tức/Widgets, quảng cáo, gợi ý và Bing trong Start\n") +
                      L.T("• Tắt Game DVR, giảm thu thập dữ liệu (Telemetry)\n") +
                      L.T("• Chặn ứng dụng Store chạy ngầm, mở menu nhanh hơn\n") +
                      L.T("• Bật chế độ nguồn Hiệu suất cao\n") +
                      L.T("• Tắt các dịch vụ không cần thiết (Telemetry, Fax, Bản đồ, Báo cáo lỗi…)") +
                      (hdd ? L.T("\n• Tắt SysMain (máy bạn dùng ổ HDD)") : "") +
                      L.T("\n\nMột điểm khôi phục sẽ được tạo trước. Bạn có thể bấm « Hoàn tác tất cả » bất cứ lúc nào.");
            if (!Dialog.Confirm(L.T("Bật Chế độ máy yếu?"), msg, L.T("Áp dụng ngay"), kind: ToastKind.Info)) return;

            await RestorePointService.EnsureForSessionAsync();
            var main = MainWindow.Instance;
            main.ShowBusy(L.T("Đang áp dụng Chế độ máy yếu…"));
            int count = 0;
            try
            {
                await Task.Run(() =>
                {
                    foreach (var t in TweakCatalog.Build().Where(t => t.LowEnd && !t.Check()))
                    {
                        main.SetBusyText(L.T("Đang áp dụng: ") + t.Name);
                        try { t.Apply(); count++; } catch (Exception ex) { Logger.Error("Chế độ máy yếu " + t.Id, ex); }
                    }
                    foreach (var s in ServiceCatalog.Build().Where(s => (s.LowEnd || (s.LowEndHddOnly && hdd)) && !s.IsDisabled))
                    {
                        main.SetBusyText(L.T("Đang tắt dịch vụ: ") + s.Title);
                        try { s.Disable(); count++; } catch (Exception ex) { Logger.Error("Chế độ máy yếu dịch vụ " + s.Name, ex); }
                    }
                });
                Native.BroadcastSettingChange();
            }
            finally { main.HideBusy(); }
            _tweaks = null; _services = null;
            if (_tab == "tweaks") await LoadTweaks();
            if (_tab == "services") await LoadServices();
            Dialog.Info(L.T("Đã bật Chế độ máy yếu"),
                L.F("Đã áp dụng {0} tối ưu.\n\nHãy khởi động lại máy để mọi thay đổi có hiệu lực đầy đủ.", count), ToastKind.Success);
        }

        private void LowEnd_Click(object sender, RoutedEventArgs e) => RunLowEndPreset();

        private async void RevertAll_Click(object sender, RoutedEventArgs e)
        {
            var tweaks = await Task.Run(() => TweakCatalog.Build().Where(t => t.AppliedByUs).ToList());
            var services = await Task.Run(() => ServiceCatalog.Build().Where(s => UndoStore.Current.Services.ContainsKey(s.Name)).ToList());
            if (tweaks.Count == 0 && services.Count == 0)
            {
                MainWindow.Instance.Toast(L.T("Chưa có thay đổi nào cần hoàn tác."), ToastKind.Info);
                return;
            }
            if (!Dialog.Confirm(L.T("Hoàn tác tất cả?"),
                L.F("Sẽ đưa {0} tinh chỉnh và {1} dịch vụ về trạng thái trước khi dùng DrAn For Computer.", tweaks.Count, services.Count),
                L.T("Hoàn tác"), kind: ToastKind.Warning)) return;
            var main = MainWindow.Instance;
            main.ShowBusy(L.T("Đang hoàn tác…"));
            try
            {
                await Task.Run(() =>
                {
                    foreach (var t in tweaks) { try { t.Revert(); } catch (Exception ex) { Logger.Error("Hoàn tác " + t.Id, ex); } }
                    foreach (var s in services) { try { s.Restore(); } catch (Exception ex) { Logger.Error("Khôi phục " + s.Name, ex); } }
                });
                Native.BroadcastSettingChange();
            }
            finally { main.HideBusy(); }
            _tweaks = null; _services = null;
            if (_tab == "tweaks") await LoadTweaks();
            if (_tab == "services") await LoadServices();
            main.Toast(L.T("Đã hoàn tác tất cả thay đổi. Hãy khởi động lại máy để có hiệu lực đầy đủ."));
        }
    }
}
