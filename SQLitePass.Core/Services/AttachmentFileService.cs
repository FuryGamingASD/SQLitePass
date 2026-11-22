using System.Security.Cryptography;
using SQLitePass.Core.Models;

namespace SQLitePass.Core.Services;

public static class AttachmentFileService
{
    public const int MaximumAttachmentBytes = 20 * 1024 * 1024;

    public static async Task<PwAttachment> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        long length = stream.Length;
        if (length > MaximumAttachmentBytes) throw new InvalidDataException("Il file supera il limite di 20 MB per allegato.");
        byte[] bytes = new byte[checked((int)length)];
        try
        {
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (stream.ReadByte() != -1) throw new InvalidDataException("The attachment changed while reading it.");
            cancellationToken.ThrowIfCancellationRequested();
            return new PwAttachment { FileName = Path.GetFileName(path), Data = bytes, FileSize = bytes.Length, CreatedAt = DateTime.UtcNow };
        }
        catch { CryptographicOperations.ZeroMemory(bytes); throw; }
    }

    // The caller owns the input bytes. Cancellation is checked before the atomic replacement.
    public static async Task WriteAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string destination = Path.GetFullPath(path);
        FileExportSafety.RejectDatabaseDestination(destination);
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".SQLitePass-export-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileExportSafety.PrivateFileOptions(FileOptions.Asynchronous)))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false); stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            FileExportSafety.RejectDatabaseDestination(destination);
            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
