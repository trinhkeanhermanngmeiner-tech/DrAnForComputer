using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace BacSiMayTinh.Core
{
    /// <summary>
    /// Đa ngôn ngữ: chuỗi gốc tiếng Việt được dùng làm khóa, bản tiếng Anh nằm trong <see cref="Strings.En"/>.
    /// L.T("…") dịch chuỗi, L.F("… {0}", x) dịch rồi định dạng.
    /// </summary>
    public static class L
    {
        private static string _lang = "vi";
        public static string Lang => _lang;
        public static bool IsEnglish => _lang == "en";
        public static CultureInfo Culture { get; private set; } = new CultureInfo("vi-VN");

        public static event Action Changed;

        public static void Init(string lang)
        {
            _lang = lang == "en" ? "en" : "vi";
            Culture = new CultureInfo(_lang == "en" ? "en-US" : "vi-VN");
        }

        public static string T(string vi)
        {
            if (_lang != "en" || string.IsNullOrEmpty(vi)) return vi;
            return Strings.En.TryGetValue(vi, out var en) ? en : vi;
        }

        public static string F(string vi, params object[] args)
        {
            try { return string.Format(Culture, T(vi), args); }
            catch { return string.Format(Culture, vi, args); }
        }

        public static void Set(string lang)
        {
            if ((lang == "en" ? "en" : "vi") == _lang) return;
            Init(lang);
            AppSettings.Current.Language = _lang;
            AppSettings.SaveCurrent();
            Controls.Loc.Instance.Refresh();
            Changed?.Invoke();
        }
    }
}

namespace BacSiMayTinh.Controls
{
    /// <summary>Nguồn ràng buộc để giao diện tự đổi ngôn ngữ ngay lập tức.</summary>
    public class Loc : INotifyPropertyChanged
    {
        public static readonly Loc Instance = new Loc();
        public int Version { get; private set; }
        public event PropertyChangedEventHandler PropertyChanged;

        public void Refresh()
        {
            Version++;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Version)));
        }
    }

    public class TConverter : IValueConverter
    {
        public static readonly TConverter Instance = new TConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Core.L.T(parameter as string);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Dùng trong XAML: Text="{c:T 'Chuỗi tiếng Việt'}".</summary>
    [MarkupExtensionReturnType(typeof(object))]
    public class TExtension : MarkupExtension
    {
        public TExtension() { }
        public TExtension(string key) { Key = key; }

        [ConstructorArgument("key")]
        public string Key { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var b = new Binding(nameof(Loc.Version))
            {
                Source = Loc.Instance,
                Mode = BindingMode.OneWay,
                Converter = TConverter.Instance,
                ConverterParameter = Key
            };
            return b.ProvideValue(serviceProvider);
        }
    }
}
