using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using BacSiMayTinh.Controls;
using BacSiMayTinh.Core;
using BacSiMayTinh.Modules;
using BacSiMayTinh.UI;

namespace BacSiMayTinh.Pages
{
    public partial class RepairPage : UserControl, IPage
    {
        private readonly List<RepairFix> _fixes;
        private bool _running;
        private UniformGrid _grid;

        public RepairPage()
        {
            InitializeComponent();
            _fixes = RepairCatalog.Build();
            FixList.ItemsSource = _fixes;
            SizeChanged += (s, e) => UpdateColumns();
        }

        public void OnShown() => RefreshDrives();
        public void OnHidden() { }

        private void FixGrid_Loaded(object sender, RoutedEventArgs e)
        {
            _grid = sender as UniformGrid;
            UpdateColumns();
        }

        private void UpdateColumns()
        {
            if (_grid != null) _grid.Columns = ActualWidth > 1250 ? 3 : 2;
        }

        private void RefreshDrives()
        {
            var drives = new List<string>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType == DriveType.Removable && d.IsReady)
                        drives.Add(d.Name + "  " + (string.IsNullOrEmpty(d.VolumeLabel) ? "USB" : d.VolumeLabel) + " (" + FileUtil.FormatSize(d.TotalSize) + ")");
                }
                catch { }
            }
            if (drives.Count == 0) drives.Add(L.T("Chưa cắm USB"));
            foreach (var f in _fixes.Where(x => x.NeedsDrive))
            {
                var keep = f.SelectedDrive;
                f.Drives = drives;
                f.SelectedDrive = drives.Contains(keep) ? keep : drives[0];
            }
        }

        private void Drive_DropDownOpened(object sender, EventArgs e) => RefreshDrives();

        private void Append(string line)
        {
            ConsoleText.AppendText(line + Environment.NewLine);
            ConsoleText.ScrollToEnd();
        }

        private async void Fix_Click(object sender, RoutedEventArgs e)
        {
            if (_running || !((sender as FrameworkElement)?.DataContext is RepairFix fix)) return;

            string param = null;
            if (fix.NeedsDrive)
            {
                var sel = fix.SelectedDrive ?? "";
                if (sel.Length < 3 || sel[1] != ':')
                {
                    Dialog.Info(L.T("Chưa có USB"), L.T("Hãy cắm USB bị nhiễm virus vào máy rồi chọn ổ USB trong danh sách."), ToastKind.Warning);
                    return;
                }
                param = sel.Substring(0, 3);
            }

            var msg = fix.Description + L.T("\n\nThời gian dự kiến: ") + fix.Duration + "." +
                      (fix.NeedsRestart ? L.T("\nCần khởi động lại máy sau khi sửa.") : "") +
                      (fix.Risk != Risk.Safe ? L.T("\nMột điểm khôi phục hệ thống sẽ được tạo trước.") : "");
            if (!Dialog.Confirm(fix.Title, msg, L.T("Bắt đầu sửa"), danger: fix.Risk == Risk.Advanced,
                kind: fix.Risk == Risk.Safe ? ToastKind.Info : ToastKind.Warning)) return;

            if (fix.Risk != Risk.Safe) await RestorePointService.EnsureForSessionAsync();

            _running = true;
            FixList.IsEnabled = false;
            ConsolePanel.Visibility = Visibility.Visible;
            RunSpinner.Visibility = Visibility.Visible;
            ConsoleTitle.Text = L.T("Đang sửa: ") + fix.Title;
            ProgressLine.Text = "";
            if (ConsoleText.Text.Length > 0) Append("");
            Append("══════ " + fix.Title + " – " + DateTime.Now.ToString("HH:mm:ss") + " ══════");
            fix.State = L.T("Đang chạy…");
            fix.StateBrush = Pal.Blue;
            Logger.Info("Sửa lỗi: " + fix.Title);

            var ctx = new RepairContext
            {
                Parameter = param,
                Log = s => Dispatcher.BeginInvoke(new Action(() => Append(s))),
                Progress = s => Dispatcher.BeginInvoke(new Action(() => ProgressLine.Text = s.Length > 70 ? s.Substring(0, 70) : s)),
            };
            bool ok = true;
            try
            {
                await fix.Action(ctx);
                await Task.Delay(100);
                fix.State = L.T("✓ Đã sửa lúc ") + DateTime.Now.ToString("HH:mm");
                fix.StateBrush = Pal.Green;
            }
            catch (Exception ex)
            {
                ok = false;
                Logger.Error("Sửa lỗi " + fix.Id, ex);
                Append(L.T("Lỗi: ") + ex.Message);
                fix.State = L.T("Không hoàn tất: ") + ex.Message;
                fix.StateBrush = Pal.Red;
            }
            finally
            {
                _running = false;
                FixList.IsEnabled = true;
                RunSpinner.Visibility = Visibility.Collapsed;
                ConsoleTitle.Text = L.T("Nhật ký thực hiện");
                ProgressLine.Text = "";
                Append(ok ? L.T("✓ Hoàn tất.") : L.T("✗ Chưa hoàn tất."));
            }

            if (ok && fix.NeedsRestart)
            {
                if (Dialog.Confirm(L.T("Khởi động lại máy?"), L.T("Cần khởi động lại để hoàn tất việc sửa lỗi. Hãy lưu lại công việc đang làm trước khi tiếp tục."),
                    L.T("Khởi động lại ngay"), kind: ToastKind.Info))
                    await ProcessRunner.CmdAsync("shutdown /r /t 5");
            }
            else if (ok) MainWindow.Instance.Toast(L.T("Đã xử lý xong: ") + fix.Title);
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(ConsoleText.Text); MainWindow.Instance.Toast(L.T("Đã sao chép nhật ký."), ToastKind.Info); } catch { }
        }

        private void HideConsole_Click(object sender, RoutedEventArgs e)
        {
            if (!_running) ConsolePanel.Visibility = Visibility.Collapsed;
        }
    }
}
