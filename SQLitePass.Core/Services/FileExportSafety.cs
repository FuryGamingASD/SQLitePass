namespace SQLitePass.Core.Services;

internal static class FileExportSafety
{
    internal static FileStreamOptions PrivateFileOptions(FileOptions options = FileOptions.None)
    {
        var settings = new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 65536, Options = options
        };
        if (OperatingSystem.IsLinux()) settings.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return settings;
    }

    internal static void RejectDatabaseDestination(string destination)
    {
        if (!File.Exists(destination)) return;
        using var existing = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> header = stackalloc byte[16];
        if (existing.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length &&
            header.SequenceEqual("SQLite format 3\0"u8))
            throw new ArgumentException("A SQLite database cannot be replaced by a plaintext export.", nameof(destination));
    }
}
