namespace SQLitePass.UI.Services;

internal static class UiText
{
    public const int MaximumScoredPasswordLength = 4096;

    public static string Preview(string? value, int limit = 256)
    {
        if (string.IsNullOrEmpty(value)) return "";
        limit = Math.Clamp(limit, 1, 16384);
        if (value.Length <= limit) return value;
        if (char.IsHighSurrogate(value[limit - 1])) limit--;
        return value[..limit] + "…";
    }
}
