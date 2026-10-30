using System.Windows;
using System.Windows.Controls;
using BacSiMayTinh.Controls;
using BacSiMayTinh.Core;
using BacSiMayTinh.Modules;
using BacSiMayTinh.UI;

namespace BacSiMayTinh.Pages
{
    public partial class SecurityPage : UserControl, IPage, ISnapSub
    {
        private List<SecItem> _items;
        private bool _checked, _checking, _threatsLoaded, _wuLoaded;
        private CancellationTokenSource _scanCts;
        private List<WuUpdate> _pending;

        public SecurityPage()
        {
            InitializeComponent();
            PauseBox.ItemsSource = new[] { 7, 14, 21, 28, 35 }.Select(d => new ChoiceItem { Value = d.ToString(), Text = L.F("{0} tuần", d / 7) }).ToList();
            PauseBox.SelectedIndex = 0;
            var hours = Enumerable.Range(0, 24).Select(h => new ChoiceItem { Value = h.ToString(), Text = h.ToString("00") + ":00" }).ToList();
            StartHour.ItemsSource = hours;
            EndHour.ItemsSource = hours;
        }

        public void OnShown()
        {
            if (!_checked) _ = RunCheck();
        }

        public void OnHidden() { }

        public void SnapSub(string key)
        {
            foreach (var rb in Tabs.Children.OfType<RadioButton>()) if ((string)rb.Tag == key) rb.IsChecked = true;
        }

        public void ShowTab(string key) => SnapSub(key);

        private async void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (CheckPanel == null) return;
            var tag = (string)((RadioButton)sender).Tag;
            CheckPanel.Visibility = tag == "check" ? Visibility.Visible : Visibility.Collapsed;
            ScanPanel.Visibility = tag == "scan" ? Visibility.Visible : Visibility.Collapsed;
            WuPanel.Visibility = tag == "wu" ? Visibility.Visible : Visibility.Collapsed;
            if (tag == "scan" && !_threatsLoaded) await LoadThreats();
            if (tag == "wu" && !_wuLoaded) LoadWu();
        }

        // ===================== Kiểm tra =====================
        private async Task RunCheck()
        {
            if (_checking) return;
            _checking = true;
            _checked = true;
            RecheckBtn.IsEnabled = false;
            FixAllBtn.IsEnabled = false;
            CheckTitle.Text = L.T("Đang kiểm tra…");
            CheckSub.Text = L.T("Đọc trạng thái diệt virus, tường lửa, UAC, Windows Update…");
            try
            {
                _items = await SecurityCheck.RunAsync();
                CheckList.ItemsSource = _items;
                ShowScore();
            }
            catch (Exception ex)
            {
                Logger.Error("Kiểm tra bảo mật", ex);
                CheckTitle.Text = L.T("Không kiểm tra được");
                CheckSub.Text = ex.Message;
            }
            finally
            {
                _checking = false;
                RecheckBtn.IsEnabled = true;
            }
        }

        private void ShowScore()
        {
            int score = SecurityCheck.Score(_items);
            ScoreText.Text = score.ToString();
            Gauge.Value = score;
            Gauge.ValueBrush = score >= 85 ? Pal.Green : score >= 60 ? Pal.Amber : Pal.Red;
            int bad = _items.Count(i => i.Level == SecLevel.Bad), warn = _items.Count(i => i.Level == SecLevel.Warn);
            CheckTitle.Text = bad > 0 ? L.F("{0} vấn đề nguy hiểm cần xử lý ngay", bad)
                            : warn > 0 ? L.F("Khá an toàn – còn {0} mục nên sửa", warn)
                            : L.T("Máy của bạn được bảo vệ tốt");
            CheckSub.Text = L.F("{0}/{1} mục đạt. Các thay đổi đều được tự sao lưu trước và có thể phục hồi trong « Sao lưu & Phục hồi ».",
                _items.Count(i => i.Level == SecLevel.Good), _items.Count);
            FixAllBtn.IsEnabled = _items.Any(i => i.CanFix && i.Id != "rdp" && i.Id != "smb1" && (i.Level == SecLevel.Bad || i.Level == SecLevel.Warn));
        }

        private async void Recheck_Click(object sender, RoutedEventArgs e) => await RunCheck();

        private async Task<string> ApplyFix(SecItem item)
        {
            item.IsBusy = true;
            try { return await item.Fix(); }
            catch (Exception ex) { Logger.Error("Sửa bảo mật " + item.Id, ex); return L.T("Lỗi: ") + ex.Message; }
            finally { item.IsBusy = false; }
        }

        private async void Fix_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is SecItem item) || item.Fix == null) return;
            if (item.Id == "smb1" && !Dialog.Confirm(L.T("Tắt SMBv1?"), L.T("Máy in mạng, ổ chia sẻ (NAS) đời rất cũ có thể không truy cập được nữa. Có thể bật lại trong « Turn Windows features on or off »."), L.T("Tắt SMBv1"))) return;
            if (item.Id == "rdp" && !Dialog.Confirm(L.T("Tắt Remote Desktop?"), L.T("Bạn sẽ không điều khiển máy này từ xa bằng Remote Desktop được nữa (TeamViewer, UltraViewer không bị ảnh hưởng)."), L.T("Tắt"))) return;
            await RestorePointService.EnsureForSessionAsync();
            var msg = await ApplyFix(item);
            if (!string.IsNullOrEmpty(msg)) MainWindow.Instance.Toast(msg, msg.StartsWith(L.T("Lỗi: ")) ? ToastKind.Error : ToastKind.Success);
            await RunCheck();
        }

        private async void FixAll_Click(object sender, RoutedEventArgs e)
        {
            var list = _items.Where(i => i.CanFix && i.Id != "rdp" && i.Id != "smb1" && (i.Level == SecLevel.Bad || i.Level == SecLevel.Warn)).ToList();
            if (list.Count == 0) return;
            if (!Dialog.Confirm(L.T("Sửa các mục bảo mật?"), "• " + string.Join("\n• ", list.Select(i => i.Title)) +
                    L.T("\n\nCấu hình hiện tại được tự sao lưu trước. SMBv1 và Remote Desktop cần sửa riêng vì có thể ảnh hưởng máy in mạng, điều khiển từ xa."),
                    L.T("Sửa tất cả"), kind: ToastKind.Info)) return;
            await RestorePointService.EnsureForSessionAsync();
            var main = MainWindow.Instance;
            main.ShowBusy(L.T("Đang sửa…"));
            var msgs = new List<string>();
            try
            {
                foreach (var i in list)
                {
                    main.SetBusyText(L.T("Đang sửa: ") + i.Title);
                    var m = await ApplyFix(i);
                    if (!string.IsNullOrEmpty(m)) msgs.Add("• " + m);
                }
            }
            finally { main.HideBusy(); }
            Dialog.Info(L.T("Đã sửa xong"), string.Join("\n", msgs), ToastKind.Success);
            await RunCheck();
        }

        // ===================== Quét virus =====================
        private async Task LoadThreats()
        {
            _threatsLoaded = true;
            if (!SecurityCheck.DefenderAvailable)
            {
                ScanSub.Text = L.T("Không tìm thấy Microsoft Defender trên máy này. Hãy dùng phần mềm diệt virus đang cài.");
                QuickBtn.IsEnabled = FullBtn.IsEnabled = FolderBtn.IsEnabled = false;
                return;
            }
            var list = await SecurityCheck.ThreatsAsync();
            ThreatList.ItemsSource = list;
            ThreatEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            int active = list.Count(t => t.Active);
            ThreatSummary.Text = list.Count == 0 ? L.T("Mối đe dọa đã phát hiện") : L.F("Mối đe dọa đã phát hiện ({0}, {1} đang hoạt động)", list.Count, active);
            RemoveBtn.Visibility = active > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetScanning(bool on)
        {
            QuickBtn.IsEnabled = FullBtn.IsEnabled = FolderBtn.IsEnabled = !on;
            CancelScanBtn.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            ScanProgress.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task DoScan(int type, string path, string title)
        {
            _scanCts = new CancellationTokenSource();
            SetScanning(true);
            ScanTitle.Text = title;
            var started = DateTime.Now;
            ScanSub.Text = L.T("Đang quét… Bạn vẫn dùng máy bình thường được.");
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) => ScanSub.Text = L.F("Đang quét… {0:mm\\:ss}  ·  Bạn vẫn dùng máy bình thường được.", DateTime.Now - started);
            timer.Start();
            RunResult r = null;
            try { r = await SecurityCheck.ScanAsync(type, path, null, _scanCts.Token); }
            catch (Exception ex) { Logger.Error("Quét virus", ex); }
            finally
            {
                timer.Stop();
                bool cancelled = _scanCts.IsCancellationRequested;
                _scanCts = null;
                SetScanning(false);
                ScanTitle.Text = L.T("Quét virus bằng Microsoft Defender");
                if (cancelled) ScanSub.Text = L.T("Đã dừng quét.");
                else if (r == null || r.ExitCode < 0) ScanSub.Text = L.T("Không quét được – Microsoft Defender có thể đang bị tắt hoặc do phần mềm diệt virus khác quản lý.");
                else if (r.ExitCode == 2) ScanSub.Text = L.F("Phát hiện mối đe dọa! (thời gian quét {0:mm\\:ss}) – xem danh sách bên dưới.", DateTime.Now - started);
                else if (r.ExitCode == 0) ScanSub.Text = L.F("Quét xong lúc {0}: không phát hiện virus.", DateTime.Now.ToString("HH:mm"));
                else ScanSub.Text = L.F("Quét kết thúc với mã {0}.", r.ExitCode);
            }
            await LoadThreats();
            if (r != null && r.ExitCode == 2) MainWindow.Instance.Toast(L.T("Defender phát hiện mối đe dọa – hãy xử lý ngay."), ToastKind.Error);
        }

        private async void Quick_Click(object sender, RoutedEventArgs e) => await DoScan(1, null, L.T("Đang quét nhanh…"));

        private async void Full_Click(object sender, RoutedEventArgs e)
        {
            if (!Dialog.Confirm(L.T("Quét toàn bộ máy?"), L.T("Quét mọi tệp trên mọi ổ đĩa, có thể mất từ 30 phút đến vài giờ và làm máy chậm hơn trong lúc quét."), L.T("Bắt đầu quét"), kind: ToastKind.Info)) return;
            await DoScan(2, null, L.T("Đang quét toàn bộ máy…"));
        }

        private async void Folder_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = L.T("Chọn ổ USB hoặc thư mục cần quét virus") })
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                await DoScan(3, dlg.SelectedPath, L.T("Đang quét: ") + dlg.SelectedPath);
            }
        }

        private void CancelScan_Click(object sender, RoutedEventArgs e) => _scanCts?.Cancel();

        private async void Remove_Click(object sender, RoutedEventArgs e)
        {
            var main = MainWindow.Instance;
            main.ShowBusy(L.T("Đang xử lý mối đe dọa…"));
            try { await SecurityCheck.RemoveThreatsAsync(); }
            finally { main.HideBusy(); }
            await LoadThreats();
            main.Toast(L.T("Đã yêu cầu Defender xử lý (cách ly/xóa) các mối đe dọa."));
        }

        // ===================== Windows Update =====================
        private void LoadWu()
        {
            _wuLoaded = true;
            var until = WuManager.PausedUntil;
            PauseStatus.Text = until.HasValue
                ? L.F("Đang tạm dừng đến {0}.", until.Value.ToString("dd/MM/yyyy HH:mm"))
                : Wua.ServiceDisabled ? L.T("Dịch vụ Windows Update đang bị TẮT hoàn toàn – máy không nhận bản vá bảo mật.") : L.T("Windows Update đang hoạt động bình thường.");
            ResumeBtn.Visibility = until.HasValue || Wua.ServiceDisabled ? Visibility.Visible : Visibility.Collapsed;
            var (s, en, manual) = WuManager.ActiveHours;
            StartHour.SelectedIndex = s;
            EndHour.SelectedIndex = en;
            HoursStatus.Text = (manual ? L.F("Hiện tại: {0:00}:00 – {1:00}:00.", s, en) : L.T("Hiện tại: Windows tự điều chỉnh theo thói quen dùng máy.")) +
                               L.T(" Windows không tự khởi động lại máy để cập nhật trong khoảng giờ này (tối đa 18 tiếng).");
        }

        private async void Pause_Click(object sender, RoutedEventArgs e)
        {
            if (!(PauseBox.SelectedItem is ChoiceItem c) || !int.TryParse(c.Value, out var days)) return;
            await ConfigBackup.EnsureSessionSnapshotAsync();
            try
            {
                WuManager.Pause(days);
                MainWindow.Instance.Toast(L.F("Đã tạm dừng Windows Update đến {0}.", DateTime.Now.AddDays(days).ToString("dd/MM/yyyy")));
            }
            catch (Exception ex) { Dialog.Info(L.T("Không tạm dừng được"), ex.Message, ToastKind.Error); }
            LoadWu();
        }

        private void Resume_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Wua.ServiceDisabled) Wua.EnableService();
                WuManager.Resume();
                MainWindow.Instance.Toast(L.T("Windows Update đã hoạt động trở lại."));
            }
            catch (Exception ex) { Dialog.Info(L.T("Lỗi"), ex.Message, ToastKind.Error); }
            LoadWu();
        }

        private void Hours_Click(object sender, RoutedEventArgs e)
        {
            int s = StartHour.SelectedIndex, en = EndHour.SelectedIndex;
            if (s < 0 || en < 0) return;
            try
            {
                WuManager.SetActiveHours(s, en);
                MainWindow.Instance.Toast(L.F("Đã đặt giờ hoạt động {0:00}:00 – {1:00}:00.", s, en));
            }
            catch (Exception ex) { Dialog.Info(L.T("Không áp dụng được"), ex.Message, ToastKind.Warning); }
            LoadWu();
        }

        private async void Pending_Click(object sender, RoutedEventArgs e)
        {
            PendingBtn.IsEnabled = false;
            PendingStatus.Text = L.T("Đang kiểm tra bản cập nhật… (có thể mất 1–3 phút)");
            try
            {
                _pending = await Task.Run(() => WuManager.PendingSoftware(CancellationToken.None));
                PendingList.ItemsSource = _pending;
                PendingStatus.Text = _pending.Count == 0 ? L.T("Không có bản cập nhật nào đang chờ.") : L.F("{0} bản cập nhật đang chờ cài. Bấm « Ẩn » với bản gây lỗi.", _pending.Count);
            }
            catch (Exception ex) { PendingStatus.Text = L.T("Không kiểm tra được: ") + ex.Message; }
            finally { PendingBtn.IsEnabled = true; }
        }

        private async void HidePending_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is WuUpdate u)) return;
            if (!Dialog.Confirm(L.T("Ẩn bản cập nhật này?"), u.Title + L.T("\n\nWindows sẽ không tự cài bản này nữa. Có thể bỏ ẩn sau."), L.T("Ẩn"), kind: ToastKind.Info)) return;
            if (await Task.Run(() => Wua.SetHidden(u, true)))
            {
                _pending.Remove(u);
                PendingList.ItemsSource = null;
                PendingList.ItemsSource = _pending;
                MainWindow.Instance.Toast(L.T("Đã ẩn bản cập nhật."), ToastKind.Info);
            }
        }

        private async void UnhideAll_Click(object sender, RoutedEventArgs e)
        {
            var main = MainWindow.Instance;
            main.ShowBusy(L.T("Đang tìm các bản cập nhật đã ẩn…"));
            int n = 0;
            try
            {
                n = await Task.Run(() =>
                {
                    int c = 0;
                    foreach (var u in WuManager.Hidden(CancellationToken.None)) if (Wua.SetHidden(u, false)) c++;
                    return c;
                });
            }
            catch (Exception ex) { main.Toast(L.T("Lỗi: ") + ex.Message, ToastKind.Error); return; }
            finally { main.HideBusy(); }
            main.Toast(n == 0 ? L.T("Không có bản cập nhật nào đang bị ẩn.") : L.F("Đã bỏ ẩn {0} bản cập nhật.", n), ToastKind.Info);
        }

        private void OpenWu_Click(object sender, RoutedEventArgs e) => ProcessRunner.Open("ms-settings:windowsupdate");
    }
}
