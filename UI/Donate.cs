using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BacSiMayTinh.Core;

namespace BacSiMayTinh.UI
{
    /// <summary>Hộp thoại ủng hộ tác giả: mã VietQR + số tài khoản (có nút sao chép).</summary>
    public static class Donate
    {
        public static void Show()
        {
            var panel = new StackPanel();

            var qrBorder = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(6),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new Image
                {
                    Source = new BitmapImage(new Uri("pack://application:,,,/Assets/donate_qr.png")),
                    Width = 220, Height = 220,
                    SnapsToDevicePixels = true
                }
            };
            RenderOptions.SetBitmapScalingMode(qrBorder.Child, BitmapScalingMode.NearestNeighbor);
            panel.Children.Add(qrBorder);

            var hint = new TextBlock
            {
                Text = L.T("Mở ứng dụng ngân hàng → Quét mã QR"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 12),
                FontSize = 12
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "B.Text2");
            panel.Children.Add(hint);

            var card = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 10, 10) };
            card.SetResourceReference(Border.BackgroundProperty, "B.Card2");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel();
            var bank = new TextBlock { Text = AppInfo.BankName, FontSize = 12.5 };
            bank.SetResourceReference(TextBlock.ForegroundProperty, "B.Text2");
            info.Children.Add(bank);
            var acc = new TextBox
            {
                Text = AppInfo.BankAccount, IsReadOnly = true, BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, Padding = new Thickness(0), FontSize = 22, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 2, 0, 0)
            };
            acc.SetResourceReference(Control.ForegroundProperty, "B.Accent");
            info.Children.Add(acc);
            grid.Children.Add(info);

            var copy = new Button { Content = L.T("Sao chép STK"), VerticalAlignment = VerticalAlignment.Center };
            copy.SetResourceReference(FrameworkElement.StyleProperty, "Btn.Primary");
            copy.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(AppInfo.BankAccount);
                    copy.Content = L.T("Đã sao chép ✓");
                }
                catch { }
            };
            Grid.SetColumn(copy, 1);
            grid.Children.Add(copy);
            card.Child = grid;
            panel.Children.Add(card);

            Dialog.Info(L.T("Ủng hộ tác giả"),
                L.F("{0} là phần mềm miễn phí. Nếu thấy hữu ích, bạn có thể ủng hộ tác giả để có thêm động lực phát triển. Mọi ủng hộ đều tự nguyện – không ủng hộ bạn vẫn dùng đầy đủ mọi tính năng.", AppInfo.Name),
                Core.ToastKind.Success, panel);
        }
    }
}
