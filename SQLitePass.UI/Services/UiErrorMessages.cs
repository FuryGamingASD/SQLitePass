using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.UI.Services;

internal sealed class UserInputException(string resourceKey) : Exception
{
    public string ResourceKey { get; } = resourceKey;
}

internal static class UiErrorMessages
{
    // Display a stable, translated reason without echoing arbitrary exception content or secrets.
    public static string GetText(Exception exception)
    {
        AppLogger.LogError("Operation did not complete", exception);
        string key = exception switch
        {
            UserInputException input => input.ResourceKey,
            OperationCanceledException => "ErrorCancelled",
            UnauthorizedAccessException => "ErrorAccess",
            FileNotFoundException or DirectoryNotFoundException => "ErrorMissingFile",
            CryptographicException => "ErrorCredential",
            InvalidDataException or FormatException or JsonException => "ErrorIntegrity",
            SettingsConflictException => "ErrorConflict",
            VaultConflictException => "ErrorConflict",
            InvalidOperationException => "ErrorConflict",
            SqliteException or IOException => "ErrorStorage",
            ArgumentException => "ErrorInvalidInput",
            _ => "ErrorUnexpected"
        };
        return LocalizationManager.GetString(key);
    }
}
