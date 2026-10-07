using System.Windows;
using System.Windows.Controls;
using BacSiMayTinh.Core;
using BacSiMayTinh.Modules;
using BacSiMayTinh.UI;

namespace BacSiMayTinh.Pages
{
    public partial class CleanerPage : UserControl, IPage, ISnapSub
    {
        private List<CleanGroup> _groups;
        private bool _busy;

        public CleanerPage()
        {
            InitializeComponent();
        }

        public void OnShown()
        {
            if (_groups == null) Load();
        }

        public void OnHidden() { }
        public void SnapSub(string key) => StartScan();

        private void Load()
        {
            _groups = CleanerCatalog.Build();
            GroupList.ItemsSource = _groups;
        }

        private IEnumerable<CleanItem> AllItems => _groups.SelectMany(g => g.Items);

        public async void StartScan()
        {
            if (_groups == null) Load();
            await ScanAsync();
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            ScanBtn.IsEnabled = !busy;
            CleanBtn.IsEnabled = !busy && AllItems.Any(i => i.Scanned);
            DeepBtn.IsEnabled = !busy;
            Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task ScanAsync()
        {
            if (_busy) return;
            SetBusy(true);
            var items = AllItems.ToList();
            SummaryTitle.Text = L.T("Đang quét…");
            int done = 0;
            foreach (var it in items)
            {
                SummarySub.Text = L.T("Đang kiểm tra: ") + it.Name;
                it.Status = L.T("Đang quét…");
                await Task.Run(() => CleanerEngine.Scan(it));
                it.Status = it.Size > 0 ? FileUtil.FormatNumber(it.FileCount) + L.T(" tệp") : "";
                Progress.Value = ++done * 100.0 / items.Count;
            }
            UpdateSummary();
            SetBusy(false);
        }

        private void UpdateSummary()
        {
            long total = AllItems.Where(i => i.IsChecked).Sum(i => i.Size);
            long all = AllItems.Sum(i => i.Size);
            SummaryTitle.Text = all > 0 ? L.F("Tìm thấy {0} có thể dọn", FileUtil.FormatSize(all)) : L.T("Máy của bạn rất sạch!");
            SummarySub.Text = L.F("Đã chọn {0} để dọn. Bỏ chọn những mục bạn muốn giữ lại rồi bấm « Dọn dẹp ».", FileUtil.FormatSize(total));
        }

        private async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAsync();

        private async void Clean_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            var items = AllItems.Where(i => i.IsChecked && (i.Size > 0 || !i.Scanned)).ToList();
            if (items.Count == 0) { MainWindow.Instance.Toast(L.T("Không có mục nào được chọn để dọn."), ToastKind.Info); return; }

            var running = items.Where(i => i.IsAppRunning()).Select(i => i.Name.Split('–')[0].Trim()).Distinct().ToList();
            var warnings = items.Where(i => i.HasWarning).ToList();
            var msg = new StringBuilder();
            msg.AppendLine(L.F("Sẽ dọn {0} mục, khoảng {1}.", items.Count, FileUtil.FormatSize(items.Sum(i => i.Size))));
            if (running.Count > 0)
            {
                msg.AppendLine();
                msg.AppendLine(L.T("Các ứng dụng sau đang mở: ") + string.Join(", ", running) + ".");
                msg.AppendLine(L.T("Phần dữ liệu đang được dùng sẽ được bỏ qua – hãy đóng chúng trước để dọn sạch hơn."));
            }
            foreach (var w in warnings)
            {
                msg.AppendLine();
                msg.AppendLine("• " + w.Name + ": " + w.Warning);
            }
            if (!Dialog.Confirm(L.T("Bắt đầu dọn dẹp?"), msg.ToString().Trim(), L.T("Dọn dẹp"), kind: ToastKind.Info)) return;

            SetBusy(true);
            long freed = 0;
            int done = 0;
            SummaryTitle.Text = L.T("Đang dọn dẹp…");
            foreach (var it in items)
            {
                SummarySub.Text = L.T("Đang dọn: ") + it.Name;
                it.Status = L.T("Đang dọn…");
                long f = await Task.Run(() => CleanerEngine.Clean(it));
                freed += f;
                it.Status = f > 0 ? L.T("Đã dọn ") + FileUtil.FormatSize(f) : L.T("Đã dọn");
                Progress.Value = ++done * 100.0 / items.Count;
            }
            SetBusy(false);
            SummaryTitle.Text = L.F("Đã giải phóng {0}!", FileUtil.FormatSize(freed));
            SummarySub.Text = L.T("Một số tệp đang được sử dụng sẽ được bỏ qua – điều này là bình thường.");
            MainWindow.Instance.Toast(L.F("Dọn dẹp xong – giải phóng {0}.", FileUtil.FormatSize(freed)));
        }

        private async void Deep_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            if (!Dialog.Confirm(L.T("Dọn sâu hệ thống?"),
                L.T("DrAn For Computer sẽ chạy công cụ DISM của Microsoft để xóa các bản cập nhật Windows cũ đã bị thay thế.\n\n") +
                L.T("Quá trình mất khoảng 5–15 phút, máy có thể hơi chậm trong lúc chạy. Sau khi dọn, bạn sẽ không gỡ được các bản cập nhật cũ."),
                L.T("Bắt đầu"), kind: ToastKind.Info)) return;

            var main = MainWindow.Instance;
            SetBusy(true);
            long before = 0;
            try { before = new DriveInfo(FileUtil.SystemDrive).TotalFreeSpace; } catch { }
            main.ShowBusy(L.T("Đang dọn sâu hệ thống (DISM)…\nVui lòng không tắt máy."));
            try
            {
                await ProcessRunner.CmdAsync("DISM /Online /Cleanup-Image /StartComponentCleanup",
                    (line, progress) => { if (line.Contains("%")) main.SetBusyText(L.T("Đang dọn sâu hệ thống (DISM)…\n") + line.Trim()); });
            }
            finally
            {
                main.HideBusy();
                SetBusy(false);
            }
            long after = before;
            try { after = new DriveInfo(FileUtil.SystemDrive).TotalFreeSpace; } catch { }
            main.Toast(L.F("Dọn sâu hoàn tất – giải phóng khoảng {0}.", FileUtil.FormatSize(Math.Max(0, after - before))));
        }
    }
}
