using System.Windows;
using System.Windows.Controls;
using BacSiMayTinh.Controls;
using BacSiMayTinh.Core;
using BacSiMayTinh.Modules;
using BacSiMayTinh.UI;

namespace BacSiMayTinh.Pages
{
    public class CompOption : ObservableObject
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Hint { get; set; }
        private bool _checked;
        public bool IsChecked { get => _checked; set => Set(ref _checked, value); }
    }

    public class ChoiceItem
    {
        public string Value { get; set; }
        public string Text { get; set; }
        public override string ToString() => Text;
    }

    public partial class BackupPage : UserControl, IPage, ISnapSub
    {
        private List<CompOption> _comps;
        private List<DataSource> _sources;
        private CancellationTokenSource _dataCts;
        private bool _loading;
        private bool _listLoaded;

        public BackupPage()
        {
            InitializeComponent();
            _comps = new List<CompOption>
            {
                new CompOption { Id = "tweaks", Name = L.T("Tinh chỉnh Windows"), Hint = L.T("Mọi giá trị registry mà các tinh chỉnh, bảo mật, Windows Update có thể thay đổi"), IsChecked = true },
                new CompOption { Id = "services", Name = L.T("Dịch vụ"), Hint = L.T("Chế độ khởi động của các dịch vụ Windows"), IsChecked = true },
                new CompOption { Id = "startup", Name = L.T("Ứng dụng khởi động"), Hint = L.T("Danh sách và trạng thái bật/tắt"), IsChecked = true },
                new CompOption { Id = "context", Name = L.T("Menu chuột phải"), IsChecked = true },
                new CompOption { Id = "power", Name = L.T("Gói điện năng"), IsChecked = true },
                new CompOption { Id = "hosts", Name = L.T("Tệp hosts"), IsChecked = true },
                new CompOption { Id = "network", Name = L.T("DNS & Wi-Fi"), Hint = L.T("Kèm mật khẩu Wi-Fi để kết nối lại ngay sau khi cài Win"), IsChecked = true },
                new CompOption { Id = "drivers", Name = L.T("Driver"), Hint = L.T("Toàn bộ driver bên thứ ba – có thể vài trăm MB"), IsChecked = false },
                new CompOption { Id = "restorepoint", Name = L.T("Điểm khôi phục"), Hint = L.T("Tạo thêm điểm khôi phục hệ thống của Windows"), IsChecked = true },
            };
            CompList.ItemsSource = _comps;

            _loading = true;
            AutoModeBox.ItemsSource = new List<ChoiceItem>
            {
                new ChoiceItem { Value = "off", Text = L.T("Tắt") },
                new ChoiceItem { Value = "weekly", Text = L.T("Hằng tuần (thứ Bảy, 12:30)") },
                new ChoiceItem { Value = "monthly", Text = L.T("Hằng tháng (ngày 1, 12:30)") },
            };
            KeepBox.ItemsSource = new[] { 3, 5, 10, 20 }.Select(n => new ChoiceItem { Value = n.ToString(), Text = L.F("{0} bản gần nhất", n) }).ToList();
            LoadAutoSettings();
            _loading = false;
            UpdateRootTexts();
        }

        public void OnShown()
        {
            if (!_listLoaded) _ = LoadSets();
        }

        public void OnHidden() { }

        public void SnapSub(string key)
        {
            foreach (var rb in Tabs.Children.OfType<RadioButton>()) if ((string)rb.Tag == key) rb.IsChecked = true;
        }

        public void ShowTab(string key) => SnapSub(key);

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (ConfigPanel == null) return;
            var tag = (string)((RadioButton)sender).Tag;
            ConfigPanel.Visibility = tag == "config" ? Visibility.Visible : Visibility.Collapsed;
            DataPanel.Visibility = tag == "data" ? Visibility.Visible : Visibility.Collapsed;
            AutoPanel.Visibility = tag == "auto" ? Visibility.Visible : Visibility.Collapsed;
            if (tag == "data" && _sources == null) LoadSources();
            if (tag == "auto") LoadAutoSettings();
        }

        // ===================== CẤU HÌNH =====================
        private void UpdateRootTexts()
        {
            RootText.Text = L.T("Lưu tại: ") + ConfigBackup.Root;
            RootWarn.Visibility = ConfigBackup.RootOnSystemDrive ? Visibility.Visible : Visibility.Collapsed;
            AutoRootText.Text = ConfigBackup.Root + (ConfigBackup.RootOnSystemDrive ? L.T("\nĐang ở ổ hệ thống – nên chọn ổ D hoặc USB để bản sao lưu còn nguyên sau khi cài lại Windows.") : "");
        }

        private async Task LoadSets()
        {
            ListSummary.Text = L.T("Đang tải…");
            var list = await Task.Run(() => ConfigBackup.List());
            SetList.ItemsSource = list;
            EmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ListSummary.Text = list.Count == 0 ? L.T("Các bản sao lưu") : L.F("Các bản sao lưu ({0})  ·  {1}", list.Count, FileUtil.FormatSize(list.Sum(s => s.Manifest.Size)));
            _listLoaded = true;
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadSets();

        private string PickFolder(string description, string initial = null)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = description, ShowNewFolderButton = true })
            {
                if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial)) dlg.SelectedPath = initial;
                return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.SelectedPath : null;
            }
        }

        private async void ChangeRoot_Click(object sender, RoutedEventArgs e)
        {
            var p = PickFolder(L.T("Chọn nơi lưu bản sao lưu cấu hình (nên chọn ổ D hoặc USB)"), ConfigBackup.Root);
            if (p == null) return;
            var root = Path.Combine(p, "DrAnForComputer_SaoLuu");
            if (string.Equals(Path.GetFileName(p), "DrAnForComputer_SaoLuu", StringComparison.OrdinalIgnoreCase)) root = p;
            try { Directory.CreateDirectory(root); }
            catch (Exception ex) { Dialog.Info(L.T("Không dùng được thư mục này"), ex.Message, ToastKind.Error); return; }
            var old = ConfigBackup.Root;
            AppSettings.Current.BackupRoot = root;
            AppSettings.SaveCurrent();
            ConfigBackup.RememberRoot(old);
            UpdateRootTexts();
            MainWindow.Instance.Toast(L.T("Đã đổi nơi lưu bản sao lưu."));
            await LoadSets();
        }

        private async void DefaultRoot_Click(object sender, RoutedEventArgs e)
        {
            var old = ConfigBackup.Root;
            AppSettings.Current.BackupRoot = null;
            AppSettings.SaveCurrent();
            ConfigBackup.RememberRoot(old);
            UpdateRootTexts();
            await LoadSets();
        }

        private void OpenRoot_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(ConfigBackup.Root);
            ProcessRunner.Open(ConfigBackup.Root);
        }

        private async void Backup_Click(object sender, RoutedEventArgs e)
        {
            var sel = new HashSet<string>(_comps.Where(c => c.IsChecked).Select(c => c.Id));
            if (sel.Count == 0) { MainWindow.Instance.Toast(L.T("Hãy chọn ít nhất một mục để sao lưu."), ToastKind.Warning); return; }
            var main = MainWindow.Instance;
            main.ShowBusy(L.T("Đang sao lưu…"));
            try
            {
                var set = await ConfigBackup.CreateAsync(new BackupOptions { Components = sel, Kind = "manual", Note = NoteBox.Text.Trim() },
                    s => main.SetBusyText(s));
                NoteBox.Text = "";
                main.Toast(L.F("Đã sao lưu xong ({0}).", FileUtil.FormatSize(set.Manifest.Size)));
                if (sel.Contains("restorepoint") && !set.Manifest.RestorePoint)
                    main.Toast(L.T("Đã sao lưu, nhưng không tạo được điểm khôi phục (System Restore có thể đang tắt)."), ToastKind.Warning);
            }
            catch (Exception ex)
            {
                Logger.Error("Sao lưu cấu hình", ex);
                Dialog.Info(L.T("Sao lưu chưa hoàn tất"), ex.Message, ToastKind.Error);
            }
            finally { main.HideBusy(); }
            await LoadSets();
        }

        private async void OpenOther_Click(object sender, RoutedEventArgs e)
        {
            var p = PickFolder(L.T("Chọn thư mục bản sao lưu (chứa tệp manifest.json) hoặc thư mục chứa nhiều bản sao lưu"));
            if (p == null) return;
            var single = ConfigBackup.Load(p);
            if (single != null)
            {
                ConfigBackup.RememberRoot(Path.GetDirectoryName(p));
                await LoadSets();
                await DoRestore(single);
                return;
            }
            bool any = false;
            try { any = Directory.GetDirectories(p).Any(d => File.Exists(Path.Combine(d, ConfigBackup.ManifestName))); } catch { }
            if (!any) { Dialog.Info(L.T("Không tìm thấy bản sao lưu"), L.T("Thư mục này không chứa bản sao lưu của DrAn For Computer (tệp manifest.json)."), ToastKind.Warning); return; }
            ConfigBackup.RememberRoot(p);
            await LoadSets();
            MainWindow.Instance.Toast(L.T("Đã thêm các bản sao lưu trong thư mục này vào danh sách."), ToastKind.Info);
        }

        private void OpenSet_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is BackupSet s) ProcessRunner.Open(s.Folder);
        }

        private async void DeleteSet_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is BackupSet s)) return;
            if (!Dialog.Confirm(L.T("Xóa bản sao lưu này?"), s.Title + "\n" + s.CreatedText + L.T("\n\nThư mục sẽ được đưa vào Thùng rác."), L.T("Xóa"), danger: true)) return;
            try { await Task.Run(() => ConfigBackup.Delete(s)); }
            catch (Exception ex) { Dialog.Info(L.T("Không xóa được"), ex.Message, ToastKind.Error); }
            await LoadSets();
        }

        private async void Restore_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is BackupSet s) await DoRestore(s);
        }

        private async Task DoRestore(BackupSet s)
        {
            var m = s.Manifest;
            var panel = new StackPanel();
            var boxes = new List<CheckBox>();
            foreach (var c in m.Components.Where(c => c != "restorepoint"))
            {
                var label = BackupSetText.ComponentName(c);
                if (c == "drivers") label += L.F(" ({0} tệp .inf)", m.DriverCount);
                if (c == "network" && m.WifiCount > 0) label += L.F(" ({0} mạng Wi-Fi)", m.WifiCount);
                var cb = new CheckBox { Content = label, Tag = c, IsChecked = c != "drivers" || s.IsOtherMachine == false, Margin = new Thickness(0, 0, 0, 6) };
                if (c == "drivers") cb.IsChecked = false;
                boxes.Add(cb);
                panel.Children.Add(cb);
            }
            if (boxes.Count == 0)
            {
                Dialog.Info(L.T("Không có gì để phục hồi"), m.RestorePoint
                    ? L.T("Bản sao lưu này chỉ có điểm khôi phục hệ thống. Hãy dùng Tiện ích › Điểm khôi phục để quay lại.")
                    : L.T("Bản sao lưu này trống."), ToastKind.Info);
                return;
            }
            var msg = L.F("Bản sao lưu lúc {0} trên máy {1}.", s.CreatedText, m.Machine) + "\n" +
                      (s.IsOtherMachine ? L.T("Đây là bản sao lưu của máy khác – chỉ nên phục hồi tinh chỉnh, DNS, Wi-Fi; driver chỉ dùng được nếu cùng mẫu máy.\n") : "") +
                      L.T("Chọn những phần muốn đưa về như lúc sao lưu. Trạng thái hiện tại sẽ được tự sao lưu trước để có thể quay lại.") +
                      (m.RestorePoint ? L.T("\n\nBản này có kèm điểm khôi phục hệ thống – nếu cần quay lại toàn bộ Windows, dùng Tiện ích › Điểm khôi phục.") : "");
            if (!Dialog.Confirm(L.T("Phục hồi cấu hình?"), msg, L.T("Phục hồi"), kind: ToastKind.Warning, extra: panel)) return;
            var comps = new HashSet<string>(boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag));
            if (comps.Count == 0) return;

            var main = MainWindow.Instance;
            main.ShowBusy(L.T("Đang phục hồi…"));
            RestoreReport r;
            try { r = await ConfigBackup.RestoreAsync(s, comps, t => main.SetBusyText(t)); }
            catch (Exception ex)
            {
                Logger.Error("Phục hồi cấu hình", ex);
                Dialog.Info(L.T("Phục hồi chưa hoàn tất"), ex.Message, ToastKind.Error);
                return;
            }
            finally { main.HideBusy(); }
            Native.BroadcastSettingChange();
            await LoadSets();
            var text = string.Join("\n", r.Lines);
            if (r.NeedsRestart)
            {
                if (Dialog.Confirm(r.Errors == 0 ? L.T("Đã phục hồi xong") : L.T("Phục hồi xong, có vài lỗi"),
                        text + L.T("\n\nKhởi động lại máy để mọi thay đổi có hiệu lực đầy đủ."), L.T("Khởi động lại ngay"),
                        kind: r.Errors == 0 ? ToastKind.Success : ToastKind.Warning))
                    await ProcessRunner.CmdAsync("shutdown /r /t 5");
            }
            else Dialog.Info(r.Errors == 0 ? L.T("Đã phục hồi xong") : L.T("Phục hồi xong, có vài lỗi"), text, r.Errors == 0 ? ToastKind.Success : ToastKind.Warning);
        }

        // ===================== DỮ LIỆU CÁ NHÂN =====================
        private void LoadSources()
        {
            _sources = DataBackup.Sources();
            SourceList.ItemsSource = _sources;
            UpdateDataTexts();
            foreach (var s in _sources)
            {
                var src = s;
                Task.Run(() => DataBackup.MeasureSize(src)).ContinueWith(t => { src.Size = t.Result; Dispatcher.BeginInvoke(new Action(UpdateDataTexts)); },
                    TaskScheduler.Default);
            }
        }

        private void UpdateDataTexts()
        {
            if (_sources == null) return;
            var sel = _sources.Where(s => s.IsChecked).ToList();
            var known = sel.Where(s => s.Size >= 0).Sum(s => s.Size);
            DataSummary.Text = L.F("Đã chọn {0}/{1} mục  ·  khoảng {2}", sel.Count, _sources.Count, FileUtil.FormatSize(known));
            var dest = AppSettings.Current.DataBackupDest;
            DestText.Text = string.IsNullOrEmpty(dest) ? L.T("(chưa chọn)") : DataBackup.TargetFolder(dest);
            if (_dataCts == null)
            {
                var last = AppSettings.Current.LastDataBackup;
                DataStatus.Text = DateTime.TryParse(last, out var d) ? L.F("Lần sao lưu gần nhất: {0}", d.ToString("dd/MM/yyyy HH:mm")) : L.T("Chưa sao lưu lần nào.");
                if (!string.IsNullOrEmpty(dest) && string.Equals(Path.GetPathRoot(dest), FileUtil.SystemDrive, StringComparison.OrdinalIgnoreCase))
                    DataStatus.Text += L.T("  ·  Nơi lưu đang ở ổ C – sẽ mất khi cài lại Windows!");
            }
        }

        private void Source_Click(object sender, RoutedEventArgs e)
        {
            DataBackup.SaveSelection(_sources);
            UpdateDataTexts();
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var p = PickFolder(L.T("Chọn thư mục muốn sao lưu thêm"));
            if (p == null) return;
            var s = AppSettings.Current;
            if (s.DataBackupCustom == null) s.DataBackupCustom = new List<string>();
            if (!s.DataBackupCustom.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase))) s.DataBackupCustom.Add(p);
            if (s.DataBackupSources != null && !s.DataBackupSources.Contains("custom")) s.DataBackupSources.Add("custom");
            AppSettings.SaveCurrent();
            LoadSources();
        }

        private void RemoveFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is DataSource d) || !d.IsCustom) return;
            var path = d.Parts.FirstOrDefault()?.Source;
            AppSettings.Current.DataBackupCustom?.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
            AppSettings.SaveCurrent();
            LoadSources();
        }

        private void ChooseDest_Click(object sender, RoutedEventArgs e)
        {
            var p = PickFolder(L.T("Chọn ổ đĩa hoặc USB để lưu dữ liệu (nên khác ổ C)"), AppSettings.Current.DataBackupDest);
            if (p == null) return;
            if (string.Equals(Path.GetPathRoot(p), FileUtil.SystemDrive, StringComparison.OrdinalIgnoreCase) &&
                !Dialog.Confirm(L.T("Lưu vào ổ C?"), L.T("Nếu cài lại Windows, ổ C thường bị xóa sạch và bản sao lưu sẽ mất. Bạn vẫn muốn lưu vào ổ C?"), L.T("Vẫn lưu vào ổ C"))) return;
            AppSettings.Current.DataBackupDest = p;
            AppSettings.SaveCurrent();
            UpdateDataTexts();
        }

        private void SetDataRunning(bool running)
        {
            DataBackupBtn.IsEnabled = !running;
            DataRestoreBtn.IsEnabled = !running;
            SourceList.IsEnabled = !running;
            DataCancelBtn.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            DataProgressBar.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ReportProgress(DataProgress p)
        {
            Dispatcher.BeginInvoke(new Action(() =>
                DataStatus.Text = L.F("{0} tệp  ·  {1}  ·  {2}", FileUtil.FormatNumber(p.Files), FileUtil.FormatSize(p.Bytes), p.Current)));
        }

        private async void DataBackup_Click(object sender, RoutedEventArgs e)
        {
            var dest = AppSettings.Current.DataBackupDest;
            if (string.IsNullOrEmpty(dest)) { ChooseDest_Click(null, null); dest = AppSettings.Current.DataBackupDest; if (string.IsNullOrEmpty(dest)) return; }
            var sel = _sources.Where(s => s.IsChecked).ToList();
            if (sel.Count == 0) { MainWindow.Instance.Toast(L.T("Hãy chọn ít nhất một mục để sao lưu."), ToastKind.Warning); return; }
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(dest));
                var need = sel.Where(s => s.Size > 0).Sum(s => s.Size);
                if (drive.IsReady && need > drive.AvailableFreeSpace &&
                    !Dialog.Confirm(L.T("Nơi lưu có thể không đủ chỗ"), L.F("Cần khoảng {0}, nơi lưu còn trống {1}. Lần sao lưu trước (nếu có) giúp giảm dung lượng cần chép. Vẫn tiếp tục?", FileUtil.FormatSize(need), FileUtil.FormatSize(drive.AvailableFreeSpace)), L.T("Tiếp tục"))) return;
            }
            catch { }
            var running = DataBackup.RunningApps(sel);
            if (running.Count > 0 && !Dialog.Confirm(L.T("Nên đóng ứng dụng trước"), L.F("Các ứng dụng đang mở: {0}. Tệp đang được dùng có thể không chép được. Hãy đóng chúng rồi bấm Tiếp tục.", string.Join(", ", running)), L.T("Tiếp tục"), kind: ToastKind.Info)) return;

            _dataCts = new CancellationTokenSource();
            SetDataRunning(true);
            DataStatus.Text = L.T("Đang chuẩn bị…");
            try
            {
                var r = await DataBackup.RunAsync(sel, dest, ReportProgress, _dataCts.Token);
                var msg = L.F("Đã chép {0} tệp mới/thay đổi ({1}) vào {2}.", FileUtil.FormatNumber(r.Files), FileUtil.FormatSize(r.Bytes), r.Target) +
                          (r.Errors > 0 ? L.F("\n{0} tệp không chép được (đang mở hoặc không có quyền) – xem nhật ký.", r.Errors) : "");
                Dialog.Info(L.T("Sao lưu dữ liệu xong"), msg, r.Errors > 0 ? ToastKind.Warning : ToastKind.Success);
            }
            catch (OperationCanceledException) { MainWindow.Instance.Toast(L.T("Đã dừng sao lưu."), ToastKind.Info); }
            catch (Exception ex) { Logger.Error("Sao lưu dữ liệu", ex); Dialog.Info(L.T("Sao lưu chưa hoàn tất"), ex.Message, ToastKind.Error); }
            finally
            {
                _dataCts = null;
                SetDataRunning(false);
                UpdateDataTexts();
            }
        }

        private void DataCancel_Click(object sender, RoutedEventArgs e) => _dataCts?.Cancel();

        private async void DataRestore_Click(object sender, RoutedEventArgs e)
        {
            var initial = AppSettings.Current.DataBackupDest;
            if (!string.IsNullOrEmpty(initial) && Directory.Exists(DataBackup.TargetFolder(initial))) initial = DataBackup.TargetFolder(initial);
            var p = PickFolder(L.T("Chọn thư mục bản sao lưu dữ liệu (thư mục DrAn_DuLieu_…)"), initial);
            if (p == null) return;
            var man = DataBackup.LoadBackup(p);
            if (man == null)
            {
                var sub = Directory.Exists(p) ? Directory.GetDirectories(p, "DrAn_DuLieu_*").FirstOrDefault() : null;
                if (sub != null) { p = sub; man = DataBackup.LoadBackup(p); }
            }
            if (man == null) { Dialog.Info(L.T("Không tìm thấy bản sao lưu"), L.T("Thư mục này không chứa bản sao lưu dữ liệu của DrAn For Computer."), ToastKind.Warning); return; }

            var panel = new StackPanel();
            var onlyMissing = new CheckBox { Content = L.T("Chỉ chép tệp chưa có trên máy (không ghi đè tệp hiện có)"), IsChecked = true };
            panel.Children.Add(onlyMissing);
            var names = string.Join("\n", man.Entries.Select(x => "• " + x.Name).Distinct());
            if (!Dialog.Confirm(L.T("Phục hồi dữ liệu cá nhân?"),
                    L.F("Bản sao lưu ngày {0} của máy {1}:\n{2}\n\nDữ liệu sẽ được chép về đúng thư mục tương ứng trên máy này (Desktop, Tài liệu…). Hãy đóng Zalo và trình duyệt trước.",
                        DateTime.TryParse(man.Created, out var d) ? d.ToString("dd/MM/yyyy HH:mm") : man.Created, man.Machine, names),
                    L.T("Phục hồi"), kind: ToastKind.Info, extra: panel)) return;

            _dataCts = new CancellationTokenSource();
            SetDataRunning(true);
            try
            {
                var r = await DataBackup.RestoreAsync(p, man, onlyMissing.IsChecked == true, ReportProgress, _dataCts.Token);
                Dialog.Info(L.T("Phục hồi dữ liệu xong"), L.F("Đã chép {0} tệp ({1}) về máy.", FileUtil.FormatNumber(r.Files), FileUtil.FormatSize(r.Bytes)) +
                    (r.Errors > 0 ? L.F("\n{0} tệp không chép được – xem nhật ký.", r.Errors) : ""), r.Errors > 0 ? ToastKind.Warning : ToastKind.Success);
            }
            catch (OperationCanceledException) { MainWindow.Instance.Toast(L.T("Đã dừng phục hồi."), ToastKind.Info); }
            catch (Exception ex) { Logger.Error("Phục hồi dữ liệu", ex); Dialog.Info(L.T("Phục hồi chưa hoàn tất"), ex.Message, ToastKind.Error); }
            finally
            {
                _dataCts = null;
                SetDataRunning(false);
                UpdateDataTexts();
            }
        }

        // ===================== TỰ ĐỘNG =====================
        private void LoadAutoSettings()
        {
            var s = AppSettings.Current;
            _loading = true;
            AutoModeBox.SelectedItem = ((List<ChoiceItem>)AutoModeBox.ItemsSource).FirstOrDefault(c => c.Value == (s.AutoBackup ?? "off"));
            var keep = ((List<ChoiceItem>)KeepBox.ItemsSource);
            KeepBox.SelectedItem = keep.FirstOrDefault(c => c.Value == s.AutoBackupKeep.ToString()) ?? keep[2];
            AutoDrivers.IsChecked = s.AutoBackupDrivers;
            AutoData.IsChecked = s.AutoBackupData;
            BeforeChange.IsChecked = s.AutoBackupBeforeChange;
            AutoRestore.IsChecked = s.AutoRestorePoint;
            _loading = false;
            var last = DateTime.TryParse(s.LastAutoBackup, out var d) ? d.ToString("dd/MM/yyyy HH:mm") : L.T("chưa có");
            AutoStatus.Text = L.F("Lần sao lưu tự động gần nhất: {0}", last) +
                              (s.AutoBackupData && string.IsNullOrEmpty(s.DataBackupDest) ? L.T("\nChưa chọn nơi lưu dữ liệu cá nhân – hãy chọn ở thẻ « Dữ liệu cá nhân ».") : "");
            UpdateRootTexts();
        }

        private async void AutoSetting_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || AutoModeBox.SelectedItem == null) return;
            var s = AppSettings.Current;
            var mode = ((ChoiceItem)AutoModeBox.SelectedItem).Value;
            bool modeChanged = mode != (s.AutoBackup ?? "off");
            s.AutoBackup = mode;
            if (KeepBox.SelectedItem is ChoiceItem k && int.TryParse(k.Value, out var n)) s.AutoBackupKeep = n;
            s.AutoBackupDrivers = AutoDrivers.IsChecked == true;
            s.AutoBackupData = AutoData.IsChecked == true;
            s.AutoBackupBeforeChange = BeforeChange.IsChecked == true;
            s.AutoRestorePoint = AutoRestore.IsChecked == true;
            AppSettings.SaveCurrent();
            if (modeChanged)
            {
                await Scheduler.SetAutoBackupAsync(mode, Installer.IsInstalled ? Installer.InstallExe : null);
                MainWindow.Instance.Toast(mode == "off" ? L.T("Đã tắt sao lưu tự động.") : L.T("Đã bật sao lưu tự động."));
            }
            LoadAutoSettings();
        }
    }
}
