using BacSiMayTinh.Core;
using BacSiMayTinh.Modules;
using WinForms = System.Windows.Forms;

namespace BacSiMayTinh.UI
{
    /// <summary>Biểu tượng dưới khay hệ thống + tự giải phóng RAM khi vượt ngưỡng.</summary>
    public static class TrayService
    {
        private static WinForms.NotifyIcon _icon;
        private static System.Windows.Threading.DispatcherTimer _ramTimer;
        private static DateTime _lastAutoFree = DateTime.MinValue;
        private static bool _balloonShown;

        public static void Init()
        {
            if (_icon != null) { UpdateRamTimer(); return; }
            _icon = new WinForms.NotifyIcon
            {
                Text = AppInfo.Name + " – " + AppInfo.Author,
                Visible = true,
            };
            try { _icon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(AppInfo.ExePath); } catch { _icon.Icon = System.Drawing.SystemIcons.Application; }

            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add(L.T("Mở DrAn For Computer"), null, (s, e) => MainWindow.Instance?.ShowFromTray());
            menu.Items.Add(L.T("Giải phóng RAM"), null, async (s, e) =>
            {
                var freed = await Task.Run(() => MemoryCleaner.Free());
                Balloon(L.T("Đã giải phóng ") + FileUtil.FormatSize(freed) + " RAM.");
            });
            menu.Items.Add(L.T("Dọn rác nhanh"), null, async (s, e) =>
            {
                var freed = await Task.Run(() => CleanerEngine.QuickClean());
                Balloon(L.T("Đã dọn ") + FileUtil.FormatSize(freed) + L.T(" tệp rác."));
            });
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(L.T("Thoát"), null, (s, e) => MainWindow.Instance?.ExitApp());
            _icon.ContextMenuStrip = menu;
            _icon.MouseClick += (s, e) => { if (e.Button == WinForms.MouseButtons.Left) MainWindow.Instance?.ShowFromTray(); };
            UpdateRamTimer();
        }

        public static void UpdateRamTimer()
        {
            if (_ramTimer == null)
            {
                _ramTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                _ramTimer.Tick += async (s, e) =>
                {
                    var load = Native.GetMemory().dwMemoryLoad;
                    if (load < AppSettings.Current.AutoFreeRamThreshold || (DateTime.Now - _lastAutoFree).TotalMinutes < 5) return;
                    _lastAutoFree = DateTime.Now;
                    var freed = await Task.Run(() => MemoryCleaner.Free());
                    if (freed > 50L * 1024 * 1024) Balloon(L.F("RAM đạt {0}% – đã tự giải phóng {1}.", load, FileUtil.FormatSize(freed)));
                };
            }
            if (AppSettings.Current.AutoFreeRam) _ramTimer.Start(); else _ramTimer.Stop();
        }

        public static void Balloon(string text, bool once = false)
        {
            if (_icon == null) return;
            if (once && _balloonShown) return;
            _balloonShown = true;
            try { _icon.ShowBalloonTip(4000, AppInfo.Name, text, WinForms.ToolTipIcon.Info); } catch { }
        }

        /// <summary>Dựng lại menu khay (khi đổi ngôn ngữ).</summary>
        public static void Rebuild()
        {
            if (_icon == null) return;
            Dispose();
            Init();
        }

        public static void Dispose()
        {
            if (_icon == null) return;
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }
    }
}
