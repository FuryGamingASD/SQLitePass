using System.Security.Cryptography;
using SQLitePass.Core.Models;

namespace SQLitePass.Core.Services;

public enum VaultImportFormat { KeePassCsv, BitwardenJson, BrowserCsv }

public static partial class ImportExportService
{
    public static Task<List<PwEntry>> ImportFromFileAsync(string path, VaultImportFormat format, Guid destination,
        CancellationToken token = default) => Task.Run(() =>
    {
        const int maximumBytes = 32 * 1024 * 1024;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        long length = stream.Length;
        if (length > maximumBytes) throw new InvalidDataException("The import file exceeds 32 MB.");
        byte[] bytes = new byte[(int)length];
        List<PwEntry>? items = null;
        try
        {
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) throw new InvalidDataException("The import file changed while reading it.");
            token.ThrowIfCancellationRequested();
            using var data = new MemoryStream(bytes, writable: false);
            using var reader = new StreamReader(data);
            string content = reader.ReadToEnd();
            items = format switch
            {
                VaultImportFormat.KeePassCsv => ImportFromKeePassCsv(content, destination),
                VaultImportFormat.BitwardenJson => ImportFromBitwardenJson(content, destination),
                VaultImportFormat.BrowserCsv => ImportFromBrowserCsv(content, destination),
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
            token.ThrowIfCancellationRequested();
            return items;
        }
        catch
        {
            if (items != null) foreach (var entry in items) entry.ClearSecrets();
            throw;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }, token);
}
