using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BacSiMayTinh.Core;
using BacSiMayTinh.Modules;

namespace BacSiMayTinh.UI
{
    /// <summary>Hộp thoại báo lỗi & góp ý: email tác giả, sao chép địa chỉ, mở thư mục nhật ký.</summary>
    public static class Feedback
    {
        public static void Show()
        {
            var panel = new StackPanel();

            var card = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 10, 10) };
            card.SetResourceReference(Border.BackgroundProperty, "B.Card2");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var email = new TextBox
            {
                Text = AppInfo.ContactEmail, IsReadOnly = true, BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, Padding = new Thickness(0), FontSize = 17, FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            email.SetResourceReference(Control.ForegroundProperty, "B.Accent");
            grid.Children.Add(email);
            var copy = new Button { Content = L.T("Sao chép"), VerticalAlignment = VerticalAlignment.Center };
            copy.Click += (s, e) =>
            {
                try { Clipboard.SetText(AppInfo.ContactEmail); copy.Content = L.T("Đã sao chép ✓"); } catch { }
            };
            Grid.SetColumn(copy, 1);
            grid.Children.Add(copy);
            card.Child = grid;
            panel.Children.Add(card);

            var logs = new Button
            {
                Content = L.T("Mở thư mục nhật ký (để đính kèm khi báo lỗi)"),
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0)
            };
            logs.SetResourceReference(FrameworkElement.StyleProperty, "Btn.Link");
            logs.Click += (s, e) => ProcessRunner.Open(AppInfo.LogDir);
            panel.Children.Add(logs);

            if (!Dialog.Confirm(L.T("Báo lỗi & góp ý tính năng"),
                L.T("Gặp lỗi hoặc muốn có thêm tính năng? Hãy gửi email cho tác giả. Khi báo lỗi, bạn nên mô tả các bước gây lỗi và đính kèm ảnh chụp màn hình hoặc tệp nhật ký."),
                L.T("Gửi email"), kind: ToastKind.Info, extra: panel)) return;

            var subject = Uri.EscapeDataString($"[{AppInfo.Name} {AppInfo.Version}] " + L.T("Báo lỗi / Góp ý"));
            var body = Uri.EscapeDataString(
                L.T("Nội dung (mô tả lỗi hoặc tính năng mong muốn):") + "\n\n\n\n" +
                "-----\n" +
                $"{AppInfo.Name} {AppInfo.Version}\n{SystemQuick.OsName()} (build {AppInfo.WindowsBuild})\n{SystemQuick.CpuName()}\nRAM: {SystemQuick.TotalRamGb():0.#} GB");
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"mailto:{AppInfo.ContactEmail}?subject={subject}&body={body}") { UseShellExecute = true });
            }
            catch
            {
                // Máy chưa cài ứng dụng email – sao chép địa chỉ để người dùng tự gửi qua Gmail/Outlook trên web.
                try { Clipboard.SetText(AppInfo.ContactEmail); } catch { }
                MainWindow.Instance?.Toast(L.F("Máy chưa có ứng dụng email. Đã sao chép địa chỉ {0} – hãy gửi qua Gmail hoặc Outlook trên trình duyệt.", AppInfo.ContactEmail), ToastKind.Info);
            }
        }
    }
}
