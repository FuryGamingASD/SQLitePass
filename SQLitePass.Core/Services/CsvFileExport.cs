using System.Text;
using SQLitePass.Core.Models;

namespace SQLitePass.Core.Services;

public static partial class ImportExportService
{
    private static void RejectDatabaseExportDestination(string destination)
    {
        if (!File.Exists(destination)) return;
        using var existing = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> header = stackalloc byte[16];
        if (existing.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length &&
            header.SequenceEqual("SQLite format 3\0"u8))
            throw new ArgumentException("A SQLite database cannot be replaced by a plaintext CSV export.", nameof(destination));
    }
    // Own a stable snapshot of the supplied entries until this task completes.
    // Stream rows off the UI thread and replace the destination only after flush/cancellation checks.
    public static Task ExportToKeePassCsvFileAsync(string path, IEnumerable<PwEntry> entries,
        IDictionary<Guid, string> groupNames, CancellationToken cancellationToken = default, IProgress<int>? progress = null) =>
        Task.Run(() => WriteCsvFile(path, entries, groupNames, cancellationToken, progress), cancellationToken);

    // Called only on a worker, including when the records are streamed from a SQLite read transaction.
    internal static void WriteCsvFile(string path, IEnumerable<PwEntry> entries, IDictionary<Guid, string> groupNames,
        CancellationToken cancellationToken, IProgress<int>? progress)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string destination = Path.GetFullPath(path);
            // Checking content also catches an existing vault reached through a drive/path alias.
            RejectDatabaseExportDestination(destination);
            string temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".SQLitePass-export-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536))
                {
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 65536, leaveOpen: true))
                    {
                        writer.WriteLine("\"Group\",\"Title\",\"Username\",\"Password\",\"URL\",\"Notes\"");
                        int count = 0;
                        foreach (var entry in entries)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            string group = groupNames.TryGetValue(entry.GroupId, out var name) ? name : "General";
                            string row = $"\"{EscapeCsv(group)}\",\"{EscapeCsv(entry.Title)}\",\"{EscapeCsv(entry.UserName)}\",\"{EscapeCsv(entry.Password)}\",\"{EscapeCsv(entry.Url)}\",\"{EscapeCsv(entry.Notes)}\"";
                            writer.WriteLine(row);
                            count++; if (count == 1 || count % 100 == 0) progress?.Report(count);
                        }
                        progress?.Report(count);
                        cancellationToken.ThrowIfCancellationRequested();
                        writer.Flush();
                    }
                    stream.Flush(flushToDisk: true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                RejectDatabaseExportDestination(destination);
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
}
