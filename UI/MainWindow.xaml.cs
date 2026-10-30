using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BacSiMayTinh.Controls;
using BacSiMayTinh.Core;
using BacSiMayTinh.Pages;

namespace BacSiMayTinh.UI
{
    public interface IPage
    {
        void OnShown();
        void OnHidden();
    }

    public partial class MainWindow : Window
    {
        public static MainWindow Instance { get; private set; }

        private readonly Dictionary<string, Func<UserControl>> _factories = new Dictionary<string, Func<UserControl>>
        {
            ["dashboard"] = () => new DashboardPage(),
            ["cleaner"] = () => new CleanerPage(),
            ["optimizer"] = () => new OptimizerPage(),
            ["repair"] = () => new RepairPage(),
            ["backup"] = () => new BackupPage(),
            ["drivers"] = () => new DriversPage(),
            ["security"] = () => new SecurityPage(),
            ["sysinfo"] = () => new SystemInfoPage(),
            ["uninstall"] = () => new UninstallerPage(),
            ["disk"] = () => new DiskPage(),
            ["tools"] = () => new ToolsPage(),
            ["settings"] = () => new SettingsPage(),
        };
        private readonly Dictionary<string, UserControl> _pages = new Dictionary<string, UserControl>();
        private UserControl _current;
        private readonly DispatcherTimer _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        private int _busyCount;
        public bool ForceExit { get; set; }

        /// <summary>Máy yếu (≤ 4GB RAM hoặc ≤ 2 luồng CPU) → giảm hiệu ứng.</summary>
        public static bool ReduceEffects
        {
            get
            {
                var s = AppSettings.Current.ReduceEffects;
                if (s.HasValue) return s.Value;
                return Native.GetMemory().ullTotalPhys <= 4.5 * 1024 * 1024 * 1024 || Environment.ProcessorCount <= 2;
            }
        }

        public MainWindow()
        {
            Instance = this;
            InitializeComponent();
            UpdateStaticTexts();
            (L.IsEnglish ? LangEn : LangVi).IsChecked = true;
            L.Changed += OnLanguageChanged;
            _toastTimer.Tick += (s, e) => HideToast();
            StateChanged += (s, e) =>
            {
                Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
                MaxBtn.Content = WindowState == WindowState.Maximized ? "" : "";
                if (WindowState == WindowState.Minimized) { (_current as IPage)?.OnHidden(); TrimMemory(); }
                else (_current as IPage)?.OnShown();
            };
            SourceInitialized += (s, e) =>
            {
                try
                {
                    var hwnd = new WindowInteropHelper(this).Handle;
                    int round = 2; // Windows 11: bo góc cửa sổ
                    Native.DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
                }
                catch { }
            };
            Closing += OnClosing;
            Loaded += (s, e) => { if (_current == null) Navigate("dashboard"); _ = UpdateFlow.AutoCheckAsync(); };
        }

        private void UpdateStaticTexts()
        {
            if (UpdateFlow.Pending != null) UpdateBannerText.Text = L.F("Có bản mới {0}", UpdateFlow.Pending.Version);
            VersionText.Text = L.F("Phiên bản {0}", AppInfo.Version);
            AdminText.Text = AppInfo.IsAdmin ? L.T("● Đang chạy quyền quản trị") : L.T("● Chưa có quyền quản trị");
        }

        // ================= Ngôn ngữ =================
        private void Lang_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            L.Set(sender == LangEn ? "en" : "vi");
        }

        /// <summary>Đổi ngôn ngữ: chữ cố định đổi ngay; các trang được dựng lại để dữ liệu hiển thị theo ngôn ngữ mới.</summary>
        private void OnLanguageChanged()
        {
            UpdateStaticTexts();
            (L.IsEnglish ? LangEn : LangVi).IsChecked = true;
            var key = _pages.FirstOrDefault(p => p.Value == _current).Key ?? "dashboard";
            (_current as IPage)?.OnHidden();
            _pages.Clear();
            _current = null;
            ShowPage(key);
            TrayService.Rebuild();
        }

        // ================= Ủng hộ tác giả =================
        private void Donate_Click(object sender, RoutedEventArgs e) => Donate.Show();
        private void Feedback_Click(object sender, RoutedEventArgs e) => Feedback.Show();

        // ================= Cập nhật =================
        public void ShowUpdateBanner(string version)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => ShowUpdateBanner(version))); return; }
            UpdateBannerText.Text = L.F("Có bản mới {0}", version);
            UpdateBanner.Visibility = Visibility.Visible;
        }

        private async void UpdateBanner_Click(object sender, RoutedEventArgs e)
        {
            if (UpdateFlow.Pending != null) await UpdateFlow.OfferAsync(UpdateFlow.Pending);
        }

        // ================= Điều hướng =================
        public void Navigate(string key)
        {
            var rb = Nav.Children.OfType<RadioButton>().FirstOrDefault(r => (string)r.CommandParameter == key);
            if (rb != null && rb.IsChecked != true) { rb.IsChecked = true; return; }
            ShowPage(key);
        }

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.CommandParameter is string key) ShowPage(key);
        }

        private void ShowPage(string key)
        {
            if (!_factories.ContainsKey(key)) return;
            if (!_pages.TryGetValue(key, out var page))
            {
                page = _factories[key]();
                _pages[key] = page;
            }
            if (_current == page) return;
            (_current as IPage)?.OnHidden();
            _current = page;
            PageHost.Content = page;
            if (!ReduceEffects)
            {
                page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            }
            (page as IPage)?.OnShown();
        }

        public T GetPage<T>(string key) where T : UserControl
        {
            Navigate(key);
            return _pages.TryGetValue(key, out var p) ? p as T : null;
        }

        // ================= Thông báo nhanh =================
        public void Toast(string text, ToastKind kind = ToastKind.Success)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => Toast(text, kind))); return; }
            ToastText.Text = text;
            switch (kind)
            {
                case ToastKind.Success: ToastIcon.Text = ""; ToastIcon.Foreground = Pal.Green; break;
                case ToastKind.Warning: ToastIcon.Text = ""; ToastIcon.Foreground = Pal.Amber; break;
                case ToastKind.Error: ToastIcon.Text = ""; ToastIcon.Foreground = Pal.Red; break;
                default: ToastIcon.Text = ""; ToastIcon.Foreground = Pal.Blue; break;
            }
            ToastBox.Visibility = Visibility.Visible;
            ToastBox.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
            _toastTimer.Stop();
            _toastTimer.Interval = TimeSpan.FromSeconds(Math.Max(3.5, text.Length / 18.0));
            _toastTimer.Start();
            if (!IsVisible) TrayService.Balloon(text);
        }

        private void HideToast()
        {
            _toastTimer.Stop();
            var a = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            a.Completed += (s, e) => ToastBox.Visibility = Visibility.Collapsed;
            ToastBox.BeginAnimation(OpacityProperty, a);
        }

        // ================= Lớp chờ =================
        public void ShowBusy(string text)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => ShowBusy(text)); return; }
            _busyCount++;
            BusyText.Text = text;
            BusyOverlay.Visibility = Visibility.Visible;
        }

        public void SetBusyText(string text)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => SetBusyText(text))); return; }
            BusyText.Text = text;
        }

        public void HideBusy()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(HideBusy); return; }
            _busyCount = Math.Max(0, _busyCount - 1);
            if (_busyCount == 0) BusyOverlay.Visibility = Visibility.Collapsed;
        }

        public void SetDim(bool on) => DimOverlay.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Thu gọn bộ nhớ của chính ứng dụng (sau khi quét xong hoặc khi thu nhỏ) – giúp nhẹ máy yếu.</summary>
        public static void TrimMemory()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                using (var p = System.Diagnostics.Process.GetCurrentProcess()) Native.EmptyWorkingSet(p.Handle);
            }
            catch { }
        }

        // ================= Cửa sổ =================
        private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Max_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!ForceExit && AppSettings.Current.MinimizeToTray)
            {
                e.Cancel = true;
                TrayService.Init();
                Hide();
                (_current as IPage)?.OnHidden();
                TrimMemory();
                TrayService.Balloon(L.T("DrAn For Computer vẫn chạy dưới khay hệ thống. Bấm vào biểu tượng để mở lại."), once: true);
                return;
            }
            (_current as IPage)?.OnHidden();
            Application.Current.Shutdown();
        }

        public void ShowFromTray()
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true; Topmost = false;
            (_current as IPage)?.OnShown();
        }

        public void ExitApp()
        {
            ForceExit = true;
            Close();
            Application.Current.Shutdown();
        }

        // ================= Chụp màn hình (dùng khi phát triển) =================
        public async void Snap(string page, string file, string sub)
        {
            Width = 1200; Height = 780;
            if (page == "terms" || page == "donate" || page == "feedback")
            {
                Window target;
                if (page == "terms") { target = new TermsWindow(); target.Show(); }
                else { Navigate("dashboard"); await Task.Delay(500); Dialog.SnapMode = true; if (page == "donate") Donate.Show(); else Feedback.Show(); target = Dialog.LastShown; }
                await Task.Delay(1500);
                RenderToPng(target, file);
                ExitApp();
                return;
            }
            Navigate(page);
            await Task.Delay(800);
            string langBefore = null;
            if (sub == "toggle")
            {
                // Thử đổi ngôn ngữ khi đang chạy (giống bấm nút VI/EN trên thanh tiêu đề).
                await Task.Delay(4000);
                langBefore = L.Lang;
                (L.IsEnglish ? LangVi : LangEn).IsChecked = true;
            }
            else if (sub != null && _pages.TryGetValue(page, out var p) && p is ISnapSub ss) ss.SnapSub(sub);
            await Task.Delay(page == "uninstall" && sub != null ? 25000
                : page == "security" || page == "drivers" ? 12000
                : page == "sysinfo" || page == "dashboard" || page == "backup" || sub != null ? 6000 : 3500);
            RenderToPng(this, file);
            if (langBefore != null) L.Set(langBefore); // trả lại cài đặt ngôn ngữ ban đầu
            ExitApp();
        }

        private static void RenderToPng(Window w, string file)
        {
            w.UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(w);
            var rtb = new RenderTargetBitmap((int)(w.ActualWidth * dpi.DpiScaleX), (int)(w.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            rtb.Render(w);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(file)) enc.Save(fs);
        }
    }

    public interface ISnapSub
    {
        void SnapSub(string key);
    }
}
