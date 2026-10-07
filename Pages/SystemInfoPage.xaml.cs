using System.Windows;
using System.Windows.Controls;
using BacSiMayTinh.Core;
using BacSiMayTinh.Modules;
using BacSiMayTinh.UI;

namespace BacSiMayTinh.Pages
{
    public partial class SystemInfoPage : UserControl, IPage
    {
        private List<InfoSection> _sections;
        private bool _loading;

        public SystemInfoPage()
        {
            InitializeComponent();
        }

        public void OnShown()
        {
            if (_sections == null) _ = LoadAsync();
        }

        public void OnHidden() { }

        private async Task LoadAsync()
        {
            if (_loading) return;
            _loading = true;
            Loading.Visibility = Visibility.Visible;
            ContentArea.Visibility = Visibility.Collapsed;
            RefreshBtn.IsEnabled = ExportBtn.IsEnabled = CopyBtn.IsEnabled = false;
            try
            {
                _sections = await Task.Run(() => SystemInfoCollector.Collect());
                // Chia hai cột cân bằng chiều cao.
                var left = new List<InfoSection>();
                var right = new List<InfoSection>();
                int hl = 0, hr = 0;
                foreach (var s in _sections)
                {
                    int h = 3 + s.Rows.Count + s.Bars.Count * 2;
                    if (hl <= hr) { left.Add(s); hl += h; } else { right.Add(s); hr += h; }
                }
                LeftCol.ItemsSource = left;
                RightCol.ItemsSource = right;
                Subtitle.Text = $"{SystemQuick.CpuName()}  ·  {SystemQuick.TotalRamGb():0.#} GB RAM  ·  {SystemQuick.OsName()}";
                Loading.Visibility = Visibility.Collapsed;
                ContentArea.Visibility = Visibility.Visible;
            }
            finally
            {
                _loading = false;
                RefreshBtn.IsEnabled = ExportBtn.IsEnabled = CopyBtn.IsEnabled = true;
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (_sections == null) return;
            try
            {
                var file = SystemInfoCollector.ExportHtml(_sections);
                MainWindow.Instance.Toast(L.T("Đã lưu báo cáo ra Desktop: ") + Path.GetFileName(file));
                ProcessRunner.Open(file);
            }
            catch (Exception ex)
            {
                MainWindow.Instance.Toast(L.T("Không xuất được báo cáo: ") + ex.Message, ToastKind.Error);
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (_sections == null) return;
            var sb = new StringBuilder();
            sb.AppendLine(L.F("CẤU HÌNH MÁY TÍNH – {0} ({1:dd/MM/yyyy HH:mm})", Environment.MachineName, DateTime.Now));
            foreach (var s in _sections)
            {
                sb.AppendLine();
                sb.AppendLine("== " + s.Title.ToUpper() + " ==");
                foreach (var r in s.Rows) sb.AppendLine(r.IsHeader ? "- " + r.Label : $"  {r.Label}: {r.Value}");
                foreach (var b in s.Bars) sb.AppendLine($"  {b.Label}: {b.Detail}");
            }
            sb.AppendLine();
            sb.AppendLine(L.F("(Tạo bởi {0} – {1})", AppInfo.Name, AppInfo.Author));
            try { Clipboard.SetText(sb.ToString()); MainWindow.Instance.Toast(L.T("Đã sao chép thông tin cấu hình – có thể dán vào Zalo/Facebook để gửi cho thợ."), ToastKind.Info); }
            catch { }
        }
    }
}
