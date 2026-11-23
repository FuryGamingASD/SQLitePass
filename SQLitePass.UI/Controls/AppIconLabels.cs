namespace SQLitePass.UI.Controls;

internal static class AppIconLabels
{
    private static string BoundedLabel(string? value) =>
        value == null ? "" : value.Length <= 256 ? value : value[..256];

    internal static AppIconKind Group(string? value)
    {
        string name = BoundedLabel(value).ToLowerInvariant().Trim();
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

    internal static AppIconKind Entry(string? value)
    {
        string text = BoundedLabel(value).ToLowerInvariant().Trim();
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
}
