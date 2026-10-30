using System.Windows;
using System.Windows.Threading;
using BacSiMayTinh.Core;
using BacSiMayTinh.Modules;
using BacSiMayTinh.UI;

namespace BacSiMayTinh
{
    public partial class App : Application
    {
        private Mutex _mutex;
        private EventWaitHandle _showEvent;
        private const string MutexName = "DrAnForComputer_SingleInstance";
        private const string ShowEventName = "DrAnForComputer_ShowWindow";

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var args = e.Args;

            DispatcherUnhandledException += OnUnhandled;
            AppDomain.CurrentDomain.UnhandledException += (s, ex) => Logger.Error("Lỗi nghiêm trọng", ex.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, ex) => { Logger.Error("Lỗi tác vụ nền", ex.Exception); ex.SetObserved(); };

            // Ngôn ngữ: tham số --lang en/vi (dùng khi chụp màn hình thử) hoặc theo cài đặt.
            int li = Array.IndexOf(args, "--lang");
            L.Init(li >= 0 && li + 1 < args.Length ? args[li + 1] : AppSettings.Current.Language);

            // Chế độ dọn rác tự động theo lịch – không hiện giao diện.
            if (args.Contains("--autoclean"))
            {
                Logger.Info("Dọn rác tự động theo lịch bắt đầu");
                var freed = CleanerEngine.QuickClean();
                Logger.Info("Dọn rác tự động xong: " + FileUtil.FormatSize(freed));
                Shutdown();
                return;
            }

            // Sao lưu tự động theo lịch – không hiện giao diện.
            if (args.Contains("--autobackup"))
            {
                Logger.Info("Sao lưu tự động theo lịch bắt đầu");
                ConfigBackup.RunScheduled();
                Shutdown();
                return;
            }

            if (args.Length >= 2 && args[0] == "--selftest")
            {
                int fails = SelfTest.Run(args[1]);
                Shutdown(fails);
                return;
            }

            // Gỡ cài đặt (gọi từ Cài đặt › Ứng dụng của Windows hoặc từ trang Cài đặt).
            if (args.Contains("--uninstall"))
            {
                ApplyTheme(AppSettings.Current.Theme);
                RunUninstall();
                Shutdown();
                return;
            }

            bool snap = args.Length >= 3 && args[0] == "--snap";
            if (!snap)
            {
                // Đóng bản 1.0 (tên cũ) nếu đang chạy dưới khay để không chạy song song hai phiên bản.
                foreach (var old in System.Diagnostics.Process.GetProcessesByName(AppInfo.OldId))
                {
                    try { old.Kill(); old.WaitForExit(3000); Logger.Info("Đã đóng bản cũ (PID " + old.Id + ")"); }
                    catch { }
                    finally { old.Dispose(); }
                }

                _mutex = new Mutex(true, MutexName, out bool created);
                if (!created)
                {
                    try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
                    Shutdown();
                    return;
                }
                _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                var t = new Thread(() =>
                {
                    while (_showEvent.WaitOne())
                        Dispatcher.BeginInvoke(new Action(() => (MainWindow as MainWindow)?.ShowFromTray()));
                }) { IsBackground = true };
                t.Start();
            }

            ApplyTheme(AppSettings.Current.Theme);
            Logger.Info($"Khởi động {AppInfo.Name} v{AppInfo.Version} (Admin: {AppInfo.IsAdmin}, Build: {AppInfo.WindowsBuild})");

            // Điều khoản sử dụng – bắt buộc đồng ý ở lần chạy đầu tiên.
            if (!snap && !args.Contains("--tray") && !TermsWindow.EnsureAccepted())
            {
                Shutdown();
                return;
            }
            _ = Scheduler.MigrateAsync();

            var w = new MainWindow();
            MainWindow = w;
            bool tray = args.Contains("--tray");
            if (tray || AppSettings.Current.MinimizeToTray || AppSettings.Current.AutoFreeRam) TrayService.Init();

            if (snap)
            {
                w.Show();
                w.Snap(args[1], args[2], args.Length >= 4 && !args[3].StartsWith("--") ? args[3] : null);
            }
            else if (!tray) w.Show();
        }

        private static void RunUninstall()
        {
            var opts = new System.Windows.Controls.StackPanel();
            var revert = new System.Windows.Controls.CheckBox { Content = L.T("Hoàn tác mọi tối ưu đã áp dụng (khuyên dùng)"), IsChecked = true, Margin = new Thickness(0, 4, 0, 8) };
            var data = new System.Windows.Controls.CheckBox { Content = L.T("Xóa cả nhật ký và bản sao lưu"), IsChecked = false };
            opts.Children.Add(revert);
            opts.Children.Add(data);
            if (!Dialog.Confirm(L.F("Gỡ cài đặt {0}?", AppInfo.Name),
                L.T("Phần mềm sẽ được gỡ khỏi máy, kèm lối tắt và các tác vụ tự động."), L.T("Gỡ cài đặt"), danger: true, extra: opts)) return;
            try
            {
                Installer.Uninstall(revert.IsChecked == true, data.IsChecked == true);
                Dialog.Info(L.T("Đã gỡ cài đặt"), L.F("Cảm ơn bạn đã sử dụng {0}!", AppInfo.Name), ToastKind.Success);
            }
            catch (Exception ex)
            {
                Logger.Error("Gỡ cài đặt", ex);
                Dialog.Info(L.T("Gỡ cài đặt chưa hoàn tất"), ex.Message, ToastKind.Error);
            }
        }

        private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Error("Lỗi giao diện", e.Exception);
            e.Handled = true;
            try { UI.MainWindow.Instance?.Toast(L.T("Đã xảy ra lỗi: ") + e.Exception.Message, ToastKind.Error); } catch { }
        }

        public static void ApplyTheme(string theme)
        {
            var dicts = Current.Resources.MergedDictionaries;
            var src = new Uri(theme == "light" ? "Themes/Light.xaml" : "Themes/Dark.xaml", UriKind.Relative);
            dicts[0] = new ResourceDictionary { Source = src };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            TrayService.Dispose();
            try { _mutex?.ReleaseMutex(); } catch { }
            base.OnExit(e);
        }
    }
}
