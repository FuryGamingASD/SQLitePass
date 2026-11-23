using Avalonia.Styling;

namespace SQLitePass.Desktop;

public static class DesktopAppearance
{
    public static ThemeVariant HighContrast { get; } = new("SQLitePassHighContrast", ThemeVariant.Dark);
}
