using System.Windows;
using System.Windows.Input;
using BacSiMayTinh.Controls;
using BacSiMayTinh.Core;

namespace BacSiMayTinh.UI
{
    public partial class DialogWindow : Window
    {
        public DialogWindow()
        {
            InitializeComponent();
            MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } };
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }

    /// <summary>Hộp thoại xác nhận/thông báo theo phong cách của ứng dụng.</summary>
    public static class Dialog
    {
        /// <summary>Chỉ dùng khi chụp màn hình thử (--snap): hiện hộp thoại không chặn.</summary>
        public static bool SnapMode;
        public static Window LastShown;

        private static bool ShowCore(string title, string message, string ok, string cancel, ToastKind kind, bool danger, object extra = null)
        {
            var d = new DialogWindow();
            var owner = MainWindow.Instance;
            if (owner != null && owner.IsVisible) d.Owner = owner;
            else d.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            d.TitleText.Text = title;
            d.MessageText.Text = message ?? "";
            d.MessageText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
            d.OkBtn.Content = ok;
            if (cancel == null) d.CancelBtn.Visibility = Visibility.Collapsed;
            else d.CancelBtn.Content = cancel;
            if (danger) d.OkBtn.Style = (Style)Application.Current.FindResource("Btn.Danger");
            if (extra != null) d.Extra.Content = extra;
            switch (kind)
            {
                case ToastKind.Warning: d.IconText.Text = ""; d.IconText.Foreground = Pal.Amber; d.IconBg.Background = Pal.AmberBg; break;
                case ToastKind.Error: d.IconText.Text = ""; d.IconText.Foreground = Pal.Red; d.IconBg.Background = Pal.RedBg; break;
                case ToastKind.Success: d.IconText.Text = ""; d.IconText.Foreground = Pal.Green; d.IconBg.Background = Pal.GreenBg; break;
                default: d.IconText.Text = ""; d.IconText.Foreground = Pal.Blue; d.IconBg.Background = Pal.BlueBg; break;
            }
            if (SnapMode) { LastShown = d; d.Show(); return false; }
            owner?.SetDim(true);
            try { return d.ShowDialog() == true; }
            finally { owner?.SetDim(false); }
        }

        public static bool Confirm(string title, string message, string ok = null, bool danger = false, ToastKind kind = ToastKind.Warning, object extra = null)
            => ShowCore(title, message, ok ?? L.T("Đồng ý"), L.T("Hủy"), kind, danger, extra);

        public static void Info(string title, string message, ToastKind kind = ToastKind.Info, object extra = null)
            => ShowCore(title, message, L.T("Đóng"), null, kind, false, extra);
    }
}
