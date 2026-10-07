using System.Windows;
using System.Windows.Controls;
using BacSiMayTinh.Core;
using BacSiMayTinh.Modules;
using BacSiMayTinh.UI;

namespace BacSiMayTinh.Pages
{
    public partial class SettingsPage : UserControl, IPage
    {
        private bool _loading;

        public SettingsPage()
        {
            InitializeComponent();
            Load();
        }

        public void OnShown() { }
        public void OnHidden() { }

        private static AppSettings S => AppSettings.Current;

        private void Load()
        {
            _loading = true;
            (S.Theme == "light" ? ThemeLight : ThemeDark).IsChecked = true;
            ReduceEffects.IsChecked = MainWindow.ReduceEffects;
            EffectsHint.Text = S.ReduceEffects.HasValue
                ? L.T("Tắt hoạt ảnh chuyển trang để tiết kiệm CPU/GPU.")
                : L.T("Tự động: ") + (MainWindow.ReduceEffects ? L.T("đang BẬT vì máy cấu hình yếu.") : L.T("đang tắt vì máy đủ mạnh."));
            AutoRestore.IsChecked = S.AutoRestorePoint;
            StartWithWindows.IsChecked = S.StartWithWindows;
            MinimizeToTray.IsChecked = S.MinimizeToTray;
            AutoFreeRam.IsChecked = S.AutoFreeRam;
            foreach (ComboBoxItem i in RamThreshold.Items) if ((string)i.Tag == S.AutoFreeRamThreshold.ToString()) i.IsSelected = true;
            if (RamThreshold.SelectedIndex < 0) RamThreshold.SelectedIndex = 2;
            foreach (ComboBoxItem i in AutoClean.Items) if ((string)i.Tag == S.AutoClean) i.IsSelected = true;
            if (AutoClean.SelectedIndex < 0) AutoClean.SelectedIndex = 0;
            DataPath.Text = AppInfo.DataDir;
            AboutVersion.Text = AppInfo.Version + " – " + AppInfo.Edition;
            AboutOs.Text = SystemQuick.OsName();
            SloganText.Text = AppInfo.Slogan;
            AboutBank.Text = AppInfo.BankName;
            (L.IsEnglish ? LangEn : LangVi).IsChecked = true;
            UpdateRow.Visibility = Updater.Enabled ? Visibility.Visible : Visibility.Collapsed;
            AutoUpdate.IsChecked = S.AutoCheckUpdate;
            UpdateHintText();
            UpdateInstallState();
            _loading = false;
        }

        private void UpdateInstallState()
        {
            bool installed = Installer.IsInstalled;
            InstallTitle.Text = installed ? L.T("Đã cài đặt vào máy") : L.T("Đang chạy bản di động (portable)");
            InstallHint.Text = installed
                ? Installer.InstallDir
                : L.T("Cài vào Program Files để có lối tắt ở Start menu, Desktop và gỡ được trong Cài đặt › Ứng dụng của Windows.");
            InstallBtn.Content = installed ? L.T("Cập nhật bản cài") : L.T("Cài đặt vào máy");
            InstallBtn.Visibility = installed && Installer.RunningFromInstall ? Visibility.Collapsed : Visibility.Visible;
            UninstallBtn.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Lang_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            L.Set(sender == LangEn ? "en" : "vi");
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            var desktop = new CheckBox { Content = L.T("Tạo lối tắt trên Desktop"), IsChecked = true };
            if (!Dialog.Confirm(L.F("Cài đặt {0} vào máy?", AppInfo.Name),
                L.F("Phần mềm sẽ được chép vào {0}, tạo lối tắt ở Start menu và đăng ký trong danh sách ứng dụng của Windows.", Installer.InstallDir),
                L.T("Cài đặt"), kind: ToastKind.Info, extra: desktop)) return;
            var main = MainWindow.Instance;
            main.ShowBusy(L.T("Đang cài đặt…"));
            bool d = desktop.IsChecked == true;
            try { await Task.Run(() => Installer.Install(d)); }
            catch (Exception ex)
            {
                Logger.Error("Cài đặt", ex);
                main.HideBusy();
                Dialog.Info(L.T("Cài đặt chưa hoàn tất"), ex.Message, ToastKind.Error);
                return;
            }
            main.HideBusy();
            UpdateInstallState();
            if (!Installer.RunningFromInstall && Dialog.Confirm(L.T("Đã cài đặt xong!"),
                L.T("Mở bản đã cài ngay bây giờ? (Bản đang chạy sẽ đóng lại – bạn có thể xóa tệp tải về.)"), L.T("Mở bản đã cài"), kind: ToastKind.Success))
            {
                Installer.LaunchInstalledAfterExit();
                main.ExitApp();
            }
        }

        private void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            ProcessRunner.Open(Installer.InstallExe, "--uninstall");
            if (Installer.RunningFromInstall) MainWindow.Instance.ExitApp();
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            await UpdateFlow.CheckManualAsync();
            UpdateHintText();
        }

        private void GitHub_Click(object sender, RoutedEventArgs e) => ProcessRunner.Open(AppInfo.GitHubUrl);

        private void UpdateHintText()
        {
            UpdateHint.Text = DateTime.TryParse(S.LastUpdateCheck, out var last)
                ? L.F("Đang dùng bản {0} · kiểm tra lần cuối {1:HH:mm dd/MM/yyyy}", AppInfo.Version, last)
                : L.F("Đang dùng bản {0}", AppInfo.Version);
        }

        private void Donate_Click(object sender, RoutedEventArgs e) => Donate.Show();
        private void Feedback_Click(object sender, RoutedEventArgs e) => Feedback.Show();

        private void Terms_Click(object sender, RoutedEventArgs e)
        {
            var w = new TermsWindow(true) { Owner = MainWindow.Instance, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            MainWindow.Instance.SetDim(true);
            try { w.ShowDialog(); } finally { MainWindow.Instance.SetDim(false); }
        }

        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            S.Theme = ThemeLight.IsChecked == true ? "light" : "dark";
            AppSettings.SaveCurrent();
            App.ApplyTheme(S.Theme);
        }

        private async void Setting_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            S.AutoRestorePoint = AutoRestore.IsChecked == true;
            S.MinimizeToTray = MinimizeToTray.IsChecked == true;
            S.AutoFreeRam = AutoFreeRam.IsChecked == true;
            S.AutoCheckUpdate = AutoUpdate.IsChecked == true;
            if (sender == ReduceEffects)
            {
                S.ReduceEffects = ReduceEffects.IsChecked == true;
                EffectsHint.Text = L.T("Tắt hoạt ảnh chuyển trang để tiết kiệm CPU/GPU.");
            }

            if (sender == StartWithWindows)
            {
                bool on = StartWithWindows.IsChecked == true;
                await Scheduler.SetStartupAsync(on);
                S.StartWithWindows = on;
                MainWindow.Instance.Toast(on ? L.T("DrAn For Computer sẽ chạy ẩn dưới khay khi bật máy.") : L.T("Đã tắt khởi động cùng Windows."), ToastKind.Info);
            }
            AppSettings.SaveCurrent();

            if (S.MinimizeToTray || S.AutoFreeRam) TrayService.Init();
            TrayService.UpdateRamTimer();
        }

        private void RamThreshold_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || !(RamThreshold.SelectedItem is ComboBoxItem i)) return;
            S.AutoFreeRamThreshold = int.Parse((string)i.Tag);
            AppSettings.SaveCurrent();
        }

        private async void AutoClean_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || !(AutoClean.SelectedItem is ComboBoxItem i)) return;
            S.AutoClean = (string)i.Tag;
            AppSettings.SaveCurrent();
            await Scheduler.SetAutoCleanAsync(S.AutoClean);
            MainWindow.Instance.Toast(S.AutoClean == "off" ? L.T("Đã tắt dọn rác tự động.") : L.T("Đã đặt lịch dọn rác tự động."), ToastKind.Info);
        }

        private void GoOptimizer_Click(object sender, RoutedEventArgs e) => MainWindow.Instance.Navigate("optimizer");
        private void OpenData_Click(object sender, RoutedEventArgs e) => ProcessRunner.Open(AppInfo.DataDir);
    }
}
