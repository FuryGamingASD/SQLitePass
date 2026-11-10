using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SQLitePass.UI.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                if (Invert) b = !b;
                return b ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v)
            {
                bool b = v == Visibility.Visible;
                return Invert ? !b : b;
            }
            return false;
        }
    }

    public class EntropyToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double entropy = 0;
            if (value is double d) entropy = d;
            else if (value is string s && double.TryParse(s, out double parsed)) entropy = parsed;

            if (entropy < 40) return new SolidColorBrush(Color.FromRgb(220, 53, 69));   // Red
            if (entropy < 70) return new SolidColorBrush(Color.FromRgb(255, 193, 7));  // Orange/Yellow
            if (entropy < 100) return new SolidColorBrush(Color.FromRgb(40, 167, 69)); // Green
            return new SolidColorBrush(Color.FromRgb(23, 162, 184));                   // Cyan (very strong)
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class PasswordMaskConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str && !string.IsNullOrEmpty(str))
            {
                return new string('•', Math.Min(str.Length, 16));
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
