using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Database;
using SQLitePass.Core.Models;

namespace SQLitePass.TestHost;

internal static class VaultBenchmark
{
    private const int EntryCount = 96;
    private const int AttachmentBytes = 128 * 1024;
    private sealed record Measurement(double Milliseconds, long AllocatedManagedBytes, long RetainedManagedBytes,
        int Entries, int AttachmentBuffers, int HistorySnapshots);

    public static async Task<int> RunAsync(bool manyEntries = false)
    {
        // The benchmark never accepts a vault path or credentials.
        string folder = Path.Combine(Path.GetTempPath(), "SQLitePass-benchmark-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); AppContext.SetData("SQLitePass.LogDirectory", Path.Combine(folder, "logs"));
        try
        {
            string path = Path.Combine(folder, "synthetic.kpd");
            using var master = new MasterKey("synthetic-benchmark-only");
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            using var crypto = CryptoEngine.FromMasterKey(master, salt, 1, 8192, 1);
            int entryCount = manyEntries ? 10000 : EntryCount, attachmentBytes = manyEntries ? 0 : AttachmentBytes;
            using var storage = await CreateDataset(path, crypto, salt, entryCount, attachmentBytes);
            // Warm up both serializers and crypto paths before measuring.
            await Measure(storage, crypto, true); await Measure(storage, crypto, false); await MeasurePage(storage, crypto);
            var full = await Measure(storage, crypto, true);
            var overview = await Measure(storage, crypto, false);
            var page = await MeasurePage(storage, crypto);
            var output = new
            {
                SyntheticOnly = true,
                Runtime = RuntimeInformation.FrameworkDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                EntryCount = entryCount,
                AttachmentBytesPerEntry = attachmentBytes,
                HistorySnapshotsPerEntry = attachmentBytes > 0 ? 1 : 0,
                DatabaseBytesIncludingSidecars = new[] { "", "-wal", "-shm" }.Select(suffix => new FileInfo(path + suffix)).Where(file => file.Exists).Sum(file => file.Length),
                Full = full,
                Overview = overview,
                BoundedPage = page,
                PageEntryLimit = 200,
                PageEstimatedByteBudget = 8 * 1024 * 1024,
                RetainedMemoryReductionPercent = full.RetainedManagedBytes > 0 ? Math.Round(100.0 * (full.RetainedManagedBytes - overview.RetainedManagedBytes) / full.RetainedManagedBytes, 2) : (double?)null,
                RetainedMemoryMethod = "Managed heap difference while holding the result and after clearing/releasing it, with full collection at each point; approximate, not process memory.",
                Limits = "Local synthetic measurement; not total process memory or a worst-case latency test. Paging still authenticates all ciphertext and scans/decrypts records transiently. The estimated page byte budget permits one oversized record so navigation never skips it."
            };
            Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        finally
        {
            string resolved = Path.GetFullPath(folder), temporary = Path.GetFullPath(Path.GetTempPath());
            if (!resolved.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("SQLitePass-benchmark-", StringComparison.Ordinal))
                throw new InvalidOperationException("Invalid benchmark cleanup path.");
            Directory.Delete(resolved, recursive: true);
        }
    }

    private static async Task<SqliteStorageProvider> CreateDataset(string path, CryptoEngine crypto, byte[] salt, int entryCount, int attachmentBytes)
    {
        var root = new PwGroup { Name = "Synthetic benchmark", IsRoot = true };
        var metadata = new VaultMetadata
        {
            SaltBase64 = Convert.ToBase64String(salt), VerificationCanary = crypto.GenerateCanaryToken(),
            KdfIterations = 1, KdfMemoryKb = 8192, KdfParallelism = 1
        };
        var storage = await SqliteStorageProvider.CreateVaultAsync(path, metadata, crypto, [root], [], enableBackup: false);
        var entries = new List<PwEntry>();
        try
        {
            for (int i = 0; i < entryCount; i++)
            {
                var entry = new PwEntry { GroupId = root.Id, Title = "Synthetic " + i, UserName = "synthetic", Password = "synthetic", Notes = "Synthetic benchmark data" };
                entry.CustomFields.Add(new PwCustomField { Key = "fixture", Value = "synthetic" });
                if (attachmentBytes > 0)
                {
                    entry.Attachments.Add(new PwAttachment { FileName = "fixture.bin", Data = RandomNumberGenerator.GetBytes(attachmentBytes), FileSize = attachmentBytes });
                    entry.History.Add(entry.CreateHistorySnapshot());
                }
                entries.Add(entry);
            }
            await storage.SaveEntriesBatchAsync(entries, crypto); return storage;
        }
        catch { storage.Dispose(); throw; }
        finally { foreach (var entry in entries) entry.ClearSecrets(); entries.Clear(); }
    }

    private static async Task<Measurement> Measure(SqliteStorageProvider storage, CryptoEngine crypto, bool includeDetails)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long allocated = GC.GetTotalAllocatedBytes(precise: true);
        var timer = Stopwatch.StartNew();
        var snapshot = await storage.LoadSnapshotAsync(crypto, includeDetails);
        timer.Stop();
        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocated;
        long heldBytes = GC.GetTotalMemory(forceFullCollection: true);
        var result = new Measurement(Math.Round(timer.Elapsed.TotalMilliseconds, 2), allocatedBytes, 0,
            snapshot.Entries.Count, snapshot.Entries.Sum(entry => entry.Attachments.Count), snapshot.Entries.Sum(entry => entry.History.Count));
        GC.KeepAlive(snapshot);
        foreach (var entry in snapshot.Entries) entry.ClearSecrets(); snapshot.Entries.Clear(); snapshot.Groups.Clear();
        snapshot = null;
        return result with { RetainedManagedBytes = Math.Max(0, heldBytes - GC.GetTotalMemory(forceFullCollection: true)) };
    }

    private static async Task<Measurement> MeasurePage(SqliteStorageProvider storage, CryptoEngine crypto)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long allocated = GC.GetTotalAllocatedBytes(precise: true);
        var timer = Stopwatch.StartNew();
        var page = await storage.LoadEntryPageAsync(crypto, new EntryPageQuery(PageSize: 200));
        timer.Stop();
        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocated;
        long heldBytes = GC.GetTotalMemory(forceFullCollection: true);
        var result = new Measurement(Math.Round(timer.Elapsed.TotalMilliseconds, 2), allocatedBytes, 0,
            page.Entries.Count, page.Entries.Sum(entry => entry.Attachments.Count), page.Entries.Sum(entry => entry.History.Count));
        GC.KeepAlive(page);
        foreach (var entry in page.Entries) entry.ClearSecrets(); page.Entries.Clear(); page.Groups.Clear();
        page = null;
        return result with { RetainedManagedBytes = Math.Max(0, heldBytes - GC.GetTotalMemory(forceFullCollection: true)) };
    }
}
