using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using BacSiMayTinh.Core;
using BacSiMayTinh.Modules;
using BacSiMayTinh.UI;

namespace BacSiMayTinh.Pages
{
    public class FolderRow
    {
        public DirNode Node { get; set; }
        public string Name { get; set; }
        public string Glyph { get; set; } = "";
        public long Size { get; set; }
        public long Files { get; set; }
        public double Percent { get; set; }
        public string SizeText => FileUtil.FormatSize(Size);
        public string PercentText => Percent.ToString("0.#") + "%";
        public string FilesText => FileUtil.FormatNumber(Files) + L.T(" tệp");
    }

    public partial class DiskPage : UserControl, IPage, ISnapSub
    {
        private DirNode _root, _current;
        private List<BigFile> _bigFiles = new List<BigFile>();
        private List<DuplicateGroup> _dups;
        private CancellationTokenSource _cts;
        private readonly DispatcherTimer _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        private ScanProgress _progress;
        private bool _busy;

        public DiskPage()
        {
            InitializeComponent();
            _progressTimer.Tick += (s, e) => UpdateProgress();
            LoadDrives();
        }

        public void OnShown() { }
        public void OnHidden() { }
        public void SnapSub(string key) { if (key == "speed") TabSpeed.IsChecked = true; }

        private void LoadDrives()
        {
            var items = new List<string>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady || (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable)) continue;
                    var label = string.IsNullOrEmpty(d.VolumeLabel) ? L.T("Ổ đĩa") : d.VolumeLabel;
                    items.Add(L.F("{0}  {1} – trống {2}", d.Name, label, FileUtil.FormatSize(d.TotalFreeSpace)));
                }
                catch { }
            }
            TargetBox.ItemsSource = items;
            TargetBox.SelectedIndex = 0;
        }

        private string TargetPath
        {
            get
            {
                var s = TargetBox.SelectedItem as string ?? "";
                int i = s.IndexOf("  ");
                return i > 0 ? s.Substring(0, i) : s;
            }
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = L.T("Chọn thư mục cần phân tích"), ShowNewFolderButton = false })
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                var list = (TargetBox.ItemsSource as List<string>) ?? new List<string>();
                var entry = dlg.SelectedPath + L.T("  (thư mục)");
                if (!list.Contains(entry)) list.Insert(0, entry);
                TargetBox.ItemsSource = null;
                TargetBox.ItemsSource = list;
                TargetBox.SelectedItem = entry;
            }
        }

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (FoldersPanel == null) return;
            var tag = (string)((RadioButton)sender).Tag;
            FoldersPanel.Visibility = tag == "folders" ? Visibility.Visible : Visibility.Collapsed;
            FilesPanel.Visibility = tag == "files" ? Visibility.Visible : Visibility.Collapsed;
            DupsPanel.Visibility = tag == "dups" ? Visibility.Visible : Visibility.Collapsed;
            SpeedPanel.Visibility = tag == "speed" ? Visibility.Visible : Visibility.Collapsed;
            ScanCard.Visibility = tag == "speed" ? Visibility.Collapsed : Visibility.Visible;
            ScanBtn.Visibility = tag == "dups" ? Visibility.Collapsed : Visibility.Visible;
            if (tag == "speed" && SpeedDrive.Items.Count == 0) LoadSpeedDrives();
        }

        // ================= Tốc độ & USB giả dung lượng =================
        private CancellationTokenSource _speedCts;
        private DriveTestProgress _speedProg;
        private readonly DispatcherTimer _speedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };

        private void LoadSpeedDrives()
        {
            var items = new List<string>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady || (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable)) continue;
                    var label = string.IsNullOrEmpty(d.VolumeLabel) ? (d.DriveType == DriveType.Removable ? "USB" : L.T("Ổ đĩa")) : d.VolumeLabel;
                    items.Add(L.F("{0}  {1} – trống {2}", d.Name, label, FileUtil.FormatSize(d.TotalFreeSpace)));
                }
                catch { }
            }
            SpeedDrive.ItemsSource = items;
            // Ưu tiên chọn sẵn USB/thẻ nhớ nếu có.
            int rem = items.FindIndex(s => { try { return new DriveInfo(s.Substring(0, 3)).DriveType == DriveType.Removable; } catch { return false; } });
            SpeedDrive.SelectedIndex = rem >= 0 ? rem : 0;
            _speedTimer.Tick -= SpeedTick;
            _speedTimer.Tick += SpeedTick;
        }

        private void SpeedTick(object sender, EventArgs e)
        {
            var p = _speedProg;
            if (p == null) return;
            SpeedProgress.Value = p.Total > 0 ? p.Done * 100.0 / p.Total : 0;
            if (p.WriteMBps > 0) WriteText.Text = p.WriteMBps.ToString("0", L.Culture) + " MB/s";
            if (p.ReadMBps > 0) ReadText.Text = p.ReadMBps.ToString("0", L.Culture) + " MB/s";
            if (p.Phase == "read") VerifyText.Text = FileUtil.FormatSize(p.GoodBytes) + (p.BadBytes > 0 ? " ✗" : " ✓");
            SpeedPhase.Text = (p.Phase == "read" ? L.T("Đang đọc lại và kiểm tra…") : L.T("Đang ghi dữ liệu thử…")) +
                              "  " + FileUtil.FormatSize(p.Done) + " / " + FileUtil.FormatSize(p.Total);
        }

        private string SpeedRoot => (SpeedDrive.SelectedItem as string ?? "").Length >= 3 ? ((string)SpeedDrive.SelectedItem).Substring(0, 3) : null;

        private async void Speed_Click(object sender, RoutedEventArgs e)
        {
            var root = SpeedRoot;
            if (root == null) return;
            long free = new DriveInfo(root).AvailableFreeSpace;
            long size = Math.Min(1024L * 1024 * 1024, free - 256L * 1024 * 1024);
            if (size < 64L * 1024 * 1024) { Dialog.Info(L.T("Không đủ dung lượng trống"), L.T("Ổ đĩa cần ít nhất khoảng 300 MB trống để đo tốc độ."), ToastKind.Warning); return; }
            await RunDriveTest(root, size, false);
        }

        private async void Fake_Click(object sender, RoutedEventArgs e)
        {
            var root = SpeedRoot;
            if (root == null) return;
            if (string.Equals(root, FileUtil.SystemDrive, StringComparison.OrdinalIgnoreCase))
            {
                Dialog.Info(L.T("Không kiểm tra ổ hệ thống"), L.T("Kiểm tra dung lượng thật sẽ tạm lấp đầy ổ đĩa nên không dùng cho ổ chứa Windows. Hãy chọn USB, thẻ nhớ hoặc ổ khác."), ToastKind.Warning);
                return;
            }
            var di = new DriveInfo(root);
            long size = di.AvailableFreeSpace - 64L * 1024 * 1024;
            double minutes = size / 1048576.0 / 15 / 60 * 2; // ước tính theo tốc độ USB 2.0 ~15 MB/s
            if (!Dialog.Confirm(L.T("Kiểm tra dung lượng thật?"),
                L.F("Sẽ ghi {0} dữ liệu thử vào phần trống của ổ {1} rồi đọc lại để kiểm tra.\n\nDữ liệu có sẵn không bị ảnh hưởng. Với USB chậm, việc này có thể mất khoảng {2} phút – bạn có thể bấm « Dừng » bất cứ lúc nào.",
                    FileUtil.FormatSize(size), root, Math.Max(1, (int)minutes)),
                L.T("Bắt đầu"), kind: ToastKind.Info)) return;
            await RunDriveTest(root, size, true);
        }

        private void SpeedStop_Click(object sender, RoutedEventArgs e) => _speedCts?.Cancel();

        private async Task RunDriveTest(string root, long size, bool capacity)
        {
            _speedCts = new CancellationTokenSource();
            _speedProg = new DriveTestProgress();
            SpeedBtn.IsEnabled = FakeBtn.IsEnabled = SpeedDrive.IsEnabled = false;
            SpeedStopBtn.Visibility = Visibility.Visible;
            WriteText.Text = ReadText.Text = VerifyText.Text = "--";
            SpeedResult.Text = SpeedResultSub.Text = "";
            _speedTimer.Start();
            DriveTestResult result;
            try { result = await DriveTest.RunAsync(root, size, _speedProg, _speedCts.Token); }
            finally
            {
                _speedTimer.Stop();
                SpeedTick(null, null);
                SpeedBtn.IsEnabled = FakeBtn.IsEnabled = SpeedDrive.IsEnabled = true;
                SpeedStopBtn.Visibility = Visibility.Collapsed;
            }
            SpeedProgress.Value = result.Cancelled ? SpeedProgress.Value : 100;
            SpeedPhase.Text = result.Cancelled ? L.T("Đã dừng.") : L.T("Hoàn tất.");
            if (result.WriteMBps > 0) WriteText.Text = result.WriteMBps.ToString("0", L.Culture) + " MB/s";
            if (result.ReadMBps > 0) ReadText.Text = result.ReadMBps.ToString("0", L.Culture) + " MB/s";

            bool bad = result.BadBytes > 0 || result.FirstErrorAt >= 0;
            if (result.Cancelled)
            {
                SpeedResult.Text = L.T("Đã dừng kiểm tra.");
                SpeedResult.Foreground = (System.Windows.Media.Brush)FindResource("B.Text2");
            }
            else if (bad)
            {
                long real = Math.Max(0, result.FirstErrorAt);
                SpeedResult.Text = L.F("✗ Phát hiện lỗi! Dung lượng thật chỉ khoảng {0}.", FileUtil.FormatSize(real));
                SpeedResult.Foreground = Controls.Pal.Red;
                SpeedResultSub.Text = L.T("Ổ đĩa này là hàng giả dung lượng hoặc đã bị hỏng. Không nên lưu dữ liệu quan trọng vào đây.") +
                                      (string.IsNullOrEmpty(result.Error) ? "" : "  (" + result.Error + ")");
            }
            else if (!string.IsNullOrEmpty(result.Error))
            {
                SpeedResult.Text = L.T("Không kiểm tra được: ") + result.Error;
                SpeedResult.Foreground = Controls.Pal.Amber;
            }
            else
            {
                double readSpeed = result.ReadMBps;
                string rating = readSpeed >= 1500 ? L.T("Rất nhanh – chuẩn SSD NVMe") : readSpeed >= 300 ? L.T("Nhanh – chuẩn SSD SATA") :
                                readSpeed >= 80 ? L.T("Trung bình – ổ HDD hoặc USB 3.0") : L.T("Chậm – USB 2.0 hoặc thẻ nhớ");
                SpeedResult.Text = capacity
                    ? L.F("✓ Dung lượng thật đúng như quảng cáo – đã kiểm tra {0} không lỗi.", FileUtil.FormatSize(result.GoodBytes))
                    : L.F("✓ Đọc {0} MB/s · Ghi {1} MB/s", readSpeed.ToString("0", L.Culture), result.WriteMBps.ToString("0", L.Culture));
                SpeedResult.Foreground = Controls.Pal.Green;
                SpeedResultSub.Text = L.T("Đánh giá: ") + rating;
            }
            LoadSpeedDrivesKeepSelection();
        }

        private void LoadSpeedDrivesKeepSelection()
        {
            var sel = SpeedRoot;
            LoadSpeedDrives();
            var items = SpeedDrive.ItemsSource as List<string>;
            int i = items?.FindIndex(s => s.StartsWith(sel ?? "?")) ?? -1;
            if (i >= 0) SpeedDrive.SelectedIndex = i;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            ScanBtn.IsEnabled = DupScanBtn.IsEnabled = !busy;
            CancelBtn.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (busy) _progressTimer.Start(); else _progressTimer.Stop();
        }

        private void UpdateProgress()
        {
            var p = _progress;
            if (p == null) return;
            var cur = p.Current ?? "";
            if (cur.Length > 60) cur = "…" + cur.Substring(cur.Length - 60);
            ProgressText.Text = L.F("Đã quét {0} tệp  ·  {1}  ·  {2}", FileUtil.FormatNumber(p.Files), FileUtil.FormatSize(p.Bytes), cur);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        // ================= Phân tích thư mục & tệp lớn =================
        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            var path = TargetPath;
            if (!Directory.Exists(path)) { MainWindow.Instance.Toast(L.T("Thư mục không tồn tại."), ToastKind.Warning); return; }
            _cts = new CancellationTokenSource();
            _progress = new ScanProgress();
            SetBusy(true);
            var files = new List<BigFile>();
            var start = DateTime.Now;
            try
            {
                var ct = _cts.Token;
                var prog = _progress;
                _root = await Task.Run(() => DiskScanner.Scan(path, files, 300, prog, ct));
                _bigFiles = files;
                ShowNode(_root);
                FileList.ItemsSource = _bigFiles;
                FilesSummary.Text = L.F("{0} tệp lớn nhất – tổng {1}. Hãy kiểm tra kỹ trước khi xóa.", _bigFiles.Count, FileUtil.FormatSize(_bigFiles.Sum(f => f.Size)));
                ProgressText.Text = (_cts.IsCancellationRequested ? L.T("Đã dừng. ") : L.T("Hoàn tất. ")) +
                    L.F("{0} tệp, {1} trong {2:0.#} giây.", FileUtil.FormatNumber(_progress.Files), FileUtil.FormatSize(_progress.Bytes), (DateTime.Now - start).TotalSeconds);
            }
            finally { SetBusy(false); }
        }

        private void ShowNode(DirNode node)
        {
            if (node == null) return;
            _current = node;
            PathText.Text = node.FullPath;
            PathSize.Text = FileUtil.FormatSize(node.Size) + "  ·  " + FileUtil.FormatNumber(node.Files) + L.T(" tệp");
            UpBtn.IsEnabled = node.Parent != null;
            double total = Math.Max(1, node.Size);
            var rows = node.Children.Where(c => c.Size > 0).Select(c => new FolderRow
            {
                Node = c, Name = c.Name, Size = c.Size, Files = c.Files, Percent = c.Size * 100.0 / total
            }).ToList();
            if (node.LooseFiles > 0)
                rows.Add(new FolderRow
                {
                    Name = L.T("(Các tệp trong thư mục này)"), Glyph = "", Size = node.LooseFiles,
                    Files = node.Files - node.Children.Sum(c => c.Files), Percent = node.LooseFiles * 100.0 / total
                });
            FolderList.ItemsSource = rows.OrderByDescending(r => r.Size).ToList();
        }

        private void Folder_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FolderList.SelectedItem is FolderRow r && r.Node != null && r.Node.Children.Count + (r.Node.LooseFiles > 0 ? 1 : 0) > 0)
                ShowNode(r.Node);
        }

        private void Up_Click(object sender, RoutedEventArgs e) => ShowNode(_current?.Parent);

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var p = (FolderList.SelectedItem as FolderRow)?.Node?.FullPath ?? _current?.FullPath ?? TargetPath;
            if (Directory.Exists(p)) ProcessRunner.Open(p);
        }

        private void ShowFile_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is BigFile f) ProcessRunner.ShowInExplorer(f.Path);
        }

        private async Task DeleteFiles(List<BigFile> sel)
        {
            if (sel.Count == 0) { MainWindow.Instance.Toast(L.T("Hãy chọn tệp cần xóa."), ToastKind.Info); return; }
            if (!Dialog.Confirm(L.F("Xóa {0} tệp?", sel.Count),
                L.F("Tổng {0}. Các tệp sẽ được đưa vào Thùng rác – bạn vẫn có thể khôi phục nếu xóa nhầm.\n\n• ", FileUtil.FormatSize(sel.Sum(f => f.Size))) +
                string.Join("\n• ", sel.Take(12).Select(f => f.Name)) + (sel.Count > 12 ? "\n…" : ""),
                L.T("Xóa vào Thùng rác"), danger: true)) return;
            int ok = 0; long bytes = 0;
            MainWindow.Instance.ShowBusy(L.T("Đang xóa tệp…"));
            try
            {
                await Task.Run(() =>
                {
                    foreach (var f in sel)
                        if (FileUtil.SendToRecycleBin(f.Path)) { f.IsDeleted = true; f.IsChecked = false; ok++; bytes += f.Size; }
                });
            }
            finally { MainWindow.Instance.HideBusy(); }
            MainWindow.Instance.Toast(L.F("Đã chuyển {0} tệp ({1}) vào Thùng rác. Hãy dọn Thùng rác để giải phóng dung lượng.", ok, FileUtil.FormatSize(bytes)));
        }

        private async void DeleteFiles_Click(object sender, RoutedEventArgs e)
        {
            await DeleteFiles(_bigFiles.Where(f => f.IsChecked && !f.IsDeleted).ToList());
            FileList.ItemsSource = null;
            FileList.ItemsSource = _bigFiles.Where(f => !f.IsDeleted).ToList();
        }

        // ================= Trùng lặp =================
        private async void DupScan_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            var path = TargetPath;
            if (!Directory.Exists(path)) return;
            long min = long.Parse((string)((ComboBoxItem)MinSizeBox.SelectedItem).Tag) * 1024 * 1024;
            _cts = new CancellationTokenSource();
            _progress = new ScanProgress();
            SetBusy(true);
            DupList.ItemsSource = null;
            try
            {
                var ct = _cts.Token;
                var prog = _progress;
                _dups = await Task.Run(() => DuplicateFinder.Find(path, min, prog, ct));
                DupList.ItemsSource = _dups;
                long waste = _dups.Sum(g => g.Size * (g.Files.Count - 1));
                DupSummary.Text = _dups.Count == 0
                    ? L.T("Không tìm thấy tệp trùng lặp nào.")
                    : L.F("Tìm thấy {0} nhóm tệp trùng lặp – có thể giải phóng {1}. Bản cũ nhất trong mỗi nhóm được giữ lại.", _dups.Count, FileUtil.FormatSize(waste));
                ProgressText.Text = L.T("Hoàn tất tìm tệp trùng lặp.");
            }
            finally { SetBusy(false); }
        }

        private async void DeleteDups_Click(object sender, RoutedEventArgs e)
        {
            if (_dups == null) return;
            // Không cho xóa hết mọi bản trong một nhóm.
            foreach (var g in _dups)
                if (g.Files.All(f => f.IsChecked || f.IsDeleted) && g.Files.Any(f => !f.IsDeleted))
                {
                    Dialog.Info(L.T("Cần giữ lại ít nhất một bản"), L.F("Nhóm \"{0}\" đang chọn xóa tất cả các bản. Hãy bỏ chọn ít nhất một bản để giữ lại.", g.FileName), ToastKind.Warning);
                    return;
                }
            await DeleteFiles(_dups.SelectMany(g => g.Files).Where(f => f.IsChecked && !f.IsDeleted).ToList());
        }
    }
}
