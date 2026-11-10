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

    public class GroupIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string name = (value?.ToString() ?? string.Empty).ToLowerInvariant().Trim();
            if (string.IsNullOrEmpty(name)) return "📁";

            if (name.Contains("generale") || name.Contains("general") || name.Contains("root")) return "📁";
            if (name.Contains("web") || name.Contains("internet") || name.Contains("sito") || name.Contains("online")) return "🌐";
            if (name.Contains("mail") || name.Contains("posta") || name.Contains("email")) return "✉️";
            if (name.Contains("banc") || name.Contains("bank") || name.Contains("conto") || name.Contains("finanz") || name.Contains("cart") || name.Contains("card")) return "💳";
            if (name.Contains("lavoro") || name.Contains("work") || name.Contains("ufficio") || name.Contains("job")) return "💼";
            if (name.Contains("social") || name.Contains("chat") || name.Contains("messag")) return "💬";
            if (name.Contains("shop") || name.Contains("amazon") || name.Contains("ecommerce") || name.Contains("spesa")) return "🛒";
            if (name.Contains("server") || name.Contains("db") || name.Contains("database") || name.Contains("host") || name.Contains("cloud")) return "🖥️";
            if (name.Contains("sviluppo") || name.Contains("dev") || name.Contains("code") || name.Contains("git")) return "💻";
            if (name.Contains("gioch") || name.Contains("game") || name.Contains("steam") || name.Contains("epic")) return "🎮";
            if (name.Contains("personale") || name.Contains("personal") || name.Contains("privat")) return "👤";
            if (name.Contains("cestino") || name.Contains("trash") || name.Contains("eliminat")) return "🗑️";

            return "📁";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EntryIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = (value?.ToString() ?? string.Empty).ToLowerInvariant().Trim();
            if (string.IsNullOrEmpty(text)) return "🔑";

            if (text.Contains("google") || text.Contains("chrome") || text.Contains("web") || text.Contains("site")) return "🌐";
            if (text.Contains("mail") || text.Contains("outlook") || text.Contains("gmail") || text.Contains("yahoo") || text.Contains("proton")) return "✉️";
            if (text.Contains("bank") || text.Contains("banc") || text.Contains("paypal") || text.Contains("visa") || text.Contains("mastercard") || text.Contains("poste") || text.Contains("revolut")) return "💳";
            if (text.Contains("amazon") || text.Contains("ebay") || text.Contains("shop") || text.Contains("store") || text.Contains("aliexpress")) return "🛒";
            if (text.Contains("apple") || text.Contains("icloud")) return "🍎";
            if (text.Contains("microsoft") || text.Contains("windows") || text.Contains("azure") || text.Contains("office")) return "🪟";
            if (text.Contains("github") || text.Contains("gitlab") || text.Contains("bitbucket") || text.Contains("code")) return "💻";
            if (text.Contains("server") || text.Contains("ssh") || text.Contains("vps") || text.Contains("ftp") || text.Contains("hosting")) return "🖥️";
            if (text.Contains("wifi") || text.Contains("router") || text.Contains("modem") || text.Contains("network")) return "📶";
            if (text.Contains("social") || text.Contains("facebook") || text.Contains("instagram") || text.Contains("twitter") || text.Contains("x.com") || text.Contains("linkedin") || text.Contains("discord") || text.Contains("telegram") || text.Contains("whatsapp")) return "💬";
            if (text.Contains("steam") || text.Contains("epic") || text.Contains("playstation") || text.Contains("xbox") || text.Contains("game")) return "🎮";
            if (text.Contains("pin") || text.Contains("codice") || text.Contains("spid") || text.Contains("cie")) return "🔢";

            return "🔑";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
