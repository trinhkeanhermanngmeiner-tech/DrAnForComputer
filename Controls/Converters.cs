using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BacSiMayTinh.Controls
{
    /// <summary>bool → Visibility. Tham số "!" để đảo ngược.</summary>
    public class BoolVis : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool v = value is bool b && b;
            if (value is string s) v = !string.IsNullOrEmpty(s);
            if (parameter as string == "!") v = !v;
            return v ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>bool → bool đảo ngược.</summary>
    public class Not : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool b && b);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool b && b);
    }
}
