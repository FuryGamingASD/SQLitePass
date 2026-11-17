using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SQLitePass.Core.Models;
using SQLitePass.UI.Controls;
using SQLitePass.UI.Services;

namespace SQLitePass.UI.Converters
{
    public sealed class TextPreviewConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            UiText.Preview(value as string, int.TryParse(parameter?.ToString(), out int limit) ? limit : 256);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
    public sealed class LocalDateTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is DateTime date ? date.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : string.Empty;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
    public class ProtectedFieldConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            values.Length > 1 && values[1] is true ? "••••••••" : UiText.Preview(values.FirstOrDefault()?.ToString(), 512);
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
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

    public class NotNullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StringNotEmptyToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !string.IsNullOrWhiteSpace(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class CanAutoTypeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is PwEntry entry)
            {
                return entry.AutoTypeEnabled && (!string.IsNullOrWhiteSpace(entry.UserName) || !string.IsNullOrWhiteSpace(entry.Password));
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
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
            if (value is PwGroup group)
            {
                if (group.IsRecycleBin) return AppIconKind.Trash;
                if (group.IsRoot) return AppIconKind.Shield;
                value = group.Name;
            }
            string name = UiText.Preview(value?.ToString()).ToLowerInvariant().Trim();
            if (string.IsNullOrEmpty(name)) return AppIconKind.Folder;

            if (name.Contains("generale") || name.Contains("general") || name.Contains("root")) return AppIconKind.Folder;
            if (name.Contains("web") || name.Contains("internet") || name.Contains("sito") || name.Contains("online")) return AppIconKind.Globe;
            if (name.Contains("mail") || name.Contains("posta") || name.Contains("email")) return AppIconKind.Mail;
            if (name.Contains("banc") || name.Contains("bank") || name.Contains("conto") || name.Contains("finanz") || name.Contains("cart") || name.Contains("card")) return AppIconKind.Card;
            if (name.Contains("lavoro") || name.Contains("work") || name.Contains("ufficio") || name.Contains("job")) return AppIconKind.Briefcase;
            if (name.Contains("social") || name.Contains("chat") || name.Contains("messag")) return AppIconKind.Chat;
            if (name.Contains("shop") || name.Contains("amazon") || name.Contains("ecommerce") || name.Contains("spesa")) return AppIconKind.Cart;
            if (name.Contains("server") || name.Contains("db") || name.Contains("database") || name.Contains("host") || name.Contains("cloud")) return AppIconKind.Monitor;
            if (name.Contains("sviluppo") || name.Contains("dev") || name.Contains("code") || name.Contains("git")) return AppIconKind.Code;
            if (name.Contains("gioch") || name.Contains("game") || name.Contains("steam") || name.Contains("epic")) return AppIconKind.Gamepad;
            if (name.Contains("personale") || name.Contains("personal") || name.Contains("privat")) return AppIconKind.User;

            return AppIconKind.Folder;
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
            string text = UiText.Preview(value?.ToString()).ToLowerInvariant().Trim();
            if (string.IsNullOrEmpty(text)) return AppIconKind.Key;

            if (text.Contains("google") || text.Contains("chrome") || text.Contains("web") || text.Contains("site")) return AppIconKind.Globe;
            if (text.Contains("mail") || text.Contains("outlook") || text.Contains("gmail") || text.Contains("yahoo") || text.Contains("proton")) return AppIconKind.Mail;
            if (text.Contains("bank") || text.Contains("banc") || text.Contains("paypal") || text.Contains("visa") || text.Contains("mastercard") || text.Contains("poste") || text.Contains("revolut")) return AppIconKind.Card;
            if (text.Contains("amazon") || text.Contains("ebay") || text.Contains("shop") || text.Contains("store") || text.Contains("aliexpress")) return AppIconKind.Cart;
            if (text.Contains("apple") || text.Contains("icloud")) return AppIconKind.Monitor;
            if (text.Contains("microsoft") || text.Contains("windows") || text.Contains("azure") || text.Contains("office")) return AppIconKind.Monitor;
            if (text.Contains("github") || text.Contains("gitlab") || text.Contains("bitbucket") || text.Contains("code")) return AppIconKind.Code;
            if (text.Contains("server") || text.Contains("ssh") || text.Contains("vps") || text.Contains("ftp") || text.Contains("hosting")) return AppIconKind.Monitor;
            if (text.Contains("wifi") || text.Contains("router") || text.Contains("modem") || text.Contains("network")) return AppIconKind.Wifi;
            if (text.Contains("social") || text.Contains("facebook") || text.Contains("instagram") || text.Contains("twitter") || text.Contains("x.com") || text.Contains("linkedin") || text.Contains("discord") || text.Contains("telegram") || text.Contains("whatsapp")) return AppIconKind.Chat;
            if (text.Contains("steam") || text.Contains("epic") || text.Contains("playstation") || text.Contains("xbox") || text.Contains("game")) return AppIconKind.Gamepad;
            if (text.Contains("pin") || text.Contains("codice") || text.Contains("spid") || text.Contains("cie")) return AppIconKind.Keyboard;

            return AppIconKind.Key;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
