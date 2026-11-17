using SQLitePass.Core.Services;

namespace SQLitePass.Tests;

public sealed partial class AttachmentFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "SQLitePass-attachments-" + Guid.NewGuid().ToString("N"));
    public AttachmentFileTests() => Directory.CreateDirectory(_folder);

    [Fact]
    public async Task AttachmentReadAndAtomicReplacementPreserveAllBytes()
    {
        string source = Path.Combine(_folder, "source.bin"), destination = Path.Combine(_folder, "destination.bin");
        byte[] bytes = Enumerable.Range(0, 65537).Select(i => (byte)i).ToArray();
        await File.WriteAllBytesAsync(source, bytes); await File.WriteAllBytesAsync(destination, [9, 9]);
        var attachment = await AttachmentFileService.ReadAsync(source);
        Assert.Equal(bytes, attachment.Data); Assert.Equal(bytes.Length, attachment.FileSize); Assert.Equal("source.bin", attachment.FileName);
        await AttachmentFileService.WriteAsync(destination, attachment.Data);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination)); Assert.Equal(bytes, attachment.Data);
        Assert.Empty(Directory.GetFiles(_folder, ".SQLitePass-export-*.tmp"));
    }

    [Fact]
    public async Task CancelledReadDoesNotOpenTheSource()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AttachmentFileService.ReadAsync(Path.Combine(_folder, "missing.bin"), cancellation.Token));
    }

    [Fact]
    public async Task CancelledExportLeavesExistingDestinationUntouched()
    {
        string path = Path.Combine(_folder, "destination.bin"); await File.WriteAllBytesAsync(path, [1, 2]);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AttachmentFileService.WriteAsync(path, new byte[] { 3, 4 }, cancellation.Token));
        Assert.Equal(new byte[] { 1, 2 }, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(_folder, ".SQLitePass-export-*.tmp"));
    }

    [Fact]
    public async Task OversizedAttachmentIsRejectedBeforeAllocation()
    {
        string path = Path.Combine(_folder, "large.bin");
        using (var stream = File.Create(path)) stream.SetLength(AttachmentFileService.MaximumAttachmentBytes + 1L);
        await Assert.ThrowsAsync<InvalidDataException>(() => AttachmentFileService.ReadAsync(path));
    }

    [Fact]
    public async Task FailedReplacementKeepsOriginalAndRemovesTemporaryExport()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = Path.Combine(_folder, "destination.bin"); await File.WriteAllBytesAsync(path, [1, 2]);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAnyAsync<IOException>(() => AttachmentFileService.WriteAsync(path, new byte[] { 3, 4 }));
        Assert.Equal(new byte[] { 1, 2 }, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(_folder, ".SQLitePass-export-*.tmp"));
    }

    public void Dispose()
    {
        string resolved = Path.GetFullPath(_folder), temporary = Path.GetFullPath(Path.GetTempPath());
        if (!resolved.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("SQLitePass-attachments-", StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid test cleanup path.");
        Directory.Delete(resolved, recursive: true);
    }
}
