using System.Windows;
using System.Windows.Input;
using BacSiMayTinh.Core;

namespace BacSiMayTinh.UI
{
    /// <summary>Điều khoản sử dụng – bắt buộc đồng ý ở lần chạy đầu tiên.</summary>
    public partial class TermsWindow : Window
    {
        public const string TermsVi =
            "1. DrAn For Computer được cung cấp MIỄN PHÍ cho mục đích cá nhân và phi thương mại.\n\n" +
            "2. Phần mềm được cung cấp \"NGUYÊN TRẠNG\" (as is), không kèm bất kỳ bảo đảm nào. Tác giả không chịu trách nhiệm về mất mát dữ liệu, hư hỏng hoặc thiệt hại phát sinh từ việc sử dụng phần mềm.\n\n" +
            "3. Phần mềm thay đổi cài đặt hệ thống (registry, dịch vụ, tệp tạm…). Hãy sao lưu dữ liệu quan trọng trước khi tối ưu. Mọi tinh chỉnh đều có thể hoàn tác và phần mềm tự tạo điểm khôi phục hệ thống trước khi thay đổi.\n\n" +
            "4. Phần mềm không thu thập và không gửi dữ liệu cá nhân của bạn ra ngoài.\n\n" +
            "5. Việc ủng hộ (donate) là hoàn toàn tự nguyện, không phải điều kiện để sử dụng phần mềm và không phát sinh nghĩa vụ hỗ trợ hay bảo hành.\n\n" +
            "6. Bản quyền phần mềm thuộc về tác giả BS. Trịnh Kế An. Bạn được tự do chia sẻ bản gốc; không được sửa đổi, bán lại hoặc phát hành lại dưới tên khác khi chưa có sự đồng ý của tác giả.\n\n" +
            "7. Không sử dụng phần mềm vào mục đích vi phạm pháp luật.\n\n" +
            "8. Báo lỗi, góp ý hoặc xin phép sử dụng: bstrinhkean@gmail.com\n\n" +
            "Bằng việc bấm « Tôi đồng ý », bạn xác nhận đã đọc và chấp nhận các điều khoản trên.";

        private readonly bool _readOnly;

        public TermsWindow(bool readOnly = false)
        {
            _readOnly = readOnly;
            InitializeComponent();
            (L.IsEnglish ? LangEn : LangVi).IsChecked = true;
            TermsText.Text = L.T(TermsVi);
            MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } };
            if (readOnly)
            {
                AgreeBox.Visibility = Visibility.Collapsed;
                ExitBtn.Visibility = Visibility.Collapsed;
                AcceptBtn.IsEnabled = true;
                AcceptBtn.Content = L.T("Đóng");
            }
        }

        private void Lang_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            L.Set(sender == LangEn ? "en" : "vi");
            TermsText.Text = L.T(TermsVi);
        }

        private void Agree_Click(object sender, RoutedEventArgs e) => AcceptBtn.IsEnabled = AgreeBox.IsChecked == true;
        private void Exit_Click(object sender, RoutedEventArgs e) => DialogResult = false;
        private void Accept_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        /// <summary>Hiện điều khoản ở lần chạy đầu. Trả về false nếu người dùng không đồng ý.</summary>
        public static bool EnsureAccepted()
        {
            if (AppSettings.Current.AcceptedTerms) return true;
            var ok = new TermsWindow().ShowDialog() == true;
            if (ok)
            {
                AppSettings.Current.AcceptedTerms = true;
                AppSettings.SaveCurrent();
            }
            return ok;
        }
    }
}
