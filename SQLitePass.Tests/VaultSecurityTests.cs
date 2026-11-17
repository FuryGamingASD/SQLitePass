using Microsoft.Data.Sqlite;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Database;
using SQLitePass.Core.Models;
using System.Security.Cryptography;

namespace SQLitePass.Tests;

public partial class VaultSecurityTests
{
    private sealed class TestVault : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "SQLitePass-tests-" + Guid.NewGuid().ToString("N"));
        public SqliteStorageProvider Storage { get; private set; } = null!;
        public CryptoEngine Crypto { get; private set; } = null!;
        public PwGroup Root { get; } = new() { Name = "Private root", IsRoot = true };
        public PwGroup Trash { get; } = new() { Name = "Trash", IsRecycleBin = true };
        public static async Task<TestVault> Create(bool backup = false)
        {
            var v = new TestVault(); Directory.CreateDirectory(v.DirectoryPath); v.Trash.ParentId = v.Root.Id;
            using var master = new MasterKey("test-only-password"); byte[] salt = RandomNumberGenerator.GetBytes(16);
            v.Crypto = CryptoEngine.FromMasterKey(master, salt, 1, 8192, 1);
            var metadata = new VaultMetadata { SaltBase64 = Convert.ToBase64String(salt), VerificationCanary = v.Crypto.GenerateCanaryToken(), KdfIterations = 1, KdfMemoryKb = 8192, KdfParallelism = 1 };
            v.Storage = await SqliteStorageProvider.CreateVaultAsync(Path.Combine(v.DirectoryPath, "vault.kpd"), metadata, v.Crypto, [v.Root, v.Trash], [], backup);
            return v;
        }
        public async Task<PwEntry> Add(string password = "secret")
        {
            var entry = new PwEntry { GroupId = Root.Id, Title = "Private title", UserName = "ENC:valid-username", Password = password, Url = "https://private.invalid" };
            await Storage.SaveEntryAsync(entry, Crypto); return entry;
        }
        public void Sql(string sql)
        {
            using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Storage.FilePath, Pooling = false }.ToString());
            c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
        }
        public void Dispose()
        {
            Storage.Dispose(); Crypto.Dispose();
            string root = Path.GetFullPath(DirectoryPath), temporary = Path.GetFullPath(Path.GetTempPath());
            if (!root.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("SQLitePass-tests-")) throw new InvalidOperationException("Invalid test cleanup path.");
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TamperedPayloadBlocksReadWriteAndLeavesBackupUnchanged()
    {
        using var v = await TestVault.Create(backup: true); var entry = await v.Add();
        byte[] backup = File.ReadAllBytes(v.Storage.BackupFilePath);
        v.Sql($"UPDATE sp_entries SET payload=zeroblob(length(payload)) WHERE id='{entry.Id}'");
        await Assert.ThrowsAsync<InvalidDataException>(() => v.Storage.LoadEntriesAsync(null, v.Crypto));
        Assert.True(v.Storage.IsReadOnly);
        await Assert.ThrowsAsync<InvalidOperationException>(() => v.Storage.SaveEntryAsync(entry, v.Crypto));
        Assert.Equal(backup, File.ReadAllBytes(v.Storage.BackupFilePath));
    }

    [Fact]
    public async Task TransplantingCiphertextIsRejected()
    {
        using var v = await TestVault.Create(); var a = await v.Add("alpha"); var b = await v.Add("beta");
        v.Sql($"UPDATE sp_entries SET payload=(SELECT payload FROM sp_entries WHERE id='{b.Id}') WHERE id='{a.Id}'");
        await Assert.ThrowsAsync<InvalidDataException>(() => v.Storage.LoadEntriesAsync(null, v.Crypto));
    }

    [Fact]
    public async Task RemovingARecordIsDetected()
    {
        using var v = await TestVault.Create(); var entry = await v.Add();
        v.Sql($"DELETE FROM sp_entries WHERE id='{entry.Id}'");
        await Assert.ThrowsAsync<InvalidDataException>(() => v.Storage.LoadEntriesAsync(null, v.Crypto));
    }

    [Fact]
    public async Task RemovingAllRecordsAndManifestCannotResetAnExistingVault()
    {
        using var v = await TestVault.Create(); await v.Add();
        v.Sql("DELETE FROM sp_entries; DELETE FROM sp_groups; UPDATE sp_metadata SET authentication=''");
        await v.Storage.CloseAsync();
        using var reopened = new SqliteStorageProvider(v.Storage.FilePath, false);
        var metadata = (await reopened.LoadVaultMetadataAsync())!;
        using var master = new MasterKey("test-only-password");
        using var key = CryptoEngine.FromMasterKey(master, Convert.FromBase64String(metadata.SaltBase64), 1, 8192, 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => reopened.AuthenticateAsync(key));
    }

    [Fact]
    public async Task ImportCreatesFoldersAndEntriesTogetherWithoutMutatingTheSource()
    {
        using var v = await TestVault.Create();
        var first = new PwEntry { GroupId = v.Root.Id, SourceGroupPath = v.Root.Name + "/Work/Accounts", Title = "one", Password = "secret" };
        var second = new PwEntry { GroupId = v.Root.Id, SourceGroupPath = "work/accounts", Title = "two" };
        var result = await v.Storage.ImportEntriesAsync([first, second], v.Root.Id, v.Crypto);
        Assert.Equal(new VaultImportResult(2, 2), result);
        var snapshot = await v.Storage.LoadSnapshotAsync(v.Crypto);
        var work = Assert.Single(snapshot.Groups, g => g.Name == "Work");
        var accounts = Assert.Single(snapshot.Groups, g => g.Name == "Accounts");
        Assert.Equal(v.Root.Id, work.ParentId); Assert.Equal(work.Id, accounts.ParentId);
        Assert.All(snapshot.Entries, entry => Assert.Equal(accounts.Id, entry.GroupId));
        Assert.Equal(v.Root.Id, first.GroupId); Assert.Equal(0, first.Revision); Assert.Equal("secret", first.Password);
    }

    [Fact]
    public async Task ImportFailureRollsBackPreviouslyCreatedFoldersAndEntriesAndKeepsBackup()
    {
        using var v = await TestVault.Create(backup: true); var existing = await v.Add();
        byte[] backup = File.ReadAllBytes(v.Storage.BackupFilePath);
        var first = new PwEntry { GroupId = v.Root.Id, SourceGroupPath = "New/Subfolder", Title = "should roll back" };
        var collision = new PwEntry { Id = existing.Id, GroupId = v.Root.Id, Title = "ID collision" };
        await Assert.ThrowsAsync<SqliteException>(() => v.Storage.ImportEntriesAsync([first, collision], v.Root.Id, v.Crypto));
        var snapshot = await v.Storage.LoadSnapshotAsync(v.Crypto);
        Assert.Equal(2, snapshot.Groups.Count); Assert.Equal(existing.Id, Assert.Single(snapshot.Entries).Id);
        Assert.Equal(backup, File.ReadAllBytes(v.Storage.BackupFilePath));
        Assert.False(v.Storage.IsReadOnly);
    }

    private sealed class InlineProgress(Action<VaultImportProgress> report) : IProgress<VaultImportProgress>
    { public void Report(VaultImportProgress value) => report(value); }

    [Fact]
    public async Task ImportCancellationBeforeCommitRollsBackTheEntireBatch()
    {
        using var v = await TestVault.Create(); using var cancellation = new CancellationTokenSource();
        var entries = new[] { new PwEntry { SourceGroupPath = "New/Sub", Title = "one" }, new PwEntry { Title = "two" } };
        var progress = new InlineProgress(value => { if (value.Completed > 0) cancellation.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => v.Storage.ImportEntriesAsync(entries, v.Root.Id, v.Crypto, cancellation.Token, progress));
        var snapshot = await v.Storage.LoadSnapshotAsync(v.Crypto);
        Assert.Equal(2, snapshot.Groups.Count); Assert.Empty(snapshot.Entries);
    }

    [Fact]
    public async Task ImportRejectsAmbiguousFoldersWithoutGuessing()
    {
        using var v = await TestVault.Create();
        await v.Storage.SaveGroupsBatchAsync([new PwGroup { Name = "Work", ParentId = v.Root.Id }, new PwGroup { Name = "work", ParentId = v.Root.Id }], v.Crypto);
        await Assert.ThrowsAsync<InvalidDataException>(() => v.Storage.ImportEntriesAsync([new PwEntry { SourceGroupPath = "Work" }], v.Root.Id, v.Crypto));
        Assert.Empty(await v.Storage.LoadEntriesAsync(null, v.Crypto));
    }

    [Fact]
    public async Task ImportCannotRouteAnEntryIntoTheRecycleBin()
    {
        using var v = await TestVault.Create();
        await Assert.ThrowsAsync<InvalidDataException>(() => v.Storage.ImportEntriesAsync([new PwEntry { SourceGroupPath = v.Trash.Name }], v.Root.Id, v.Crypto));
        await Assert.ThrowsAsync<InvalidDataException>(() => v.Storage.ImportEntriesAsync([new PwEntry()], v.Trash.Id, v.Crypto));
        Assert.Empty(await v.Storage.LoadEntriesAsync(null, v.Crypto));
    }

    [Fact]
    public async Task BatchRoundTripIncludesChildrenAndCanRemoveThem()
    {
        using var v = await TestVault.Create(); var entry = await v.Add();
        entry.CustomFields.Add(new PwCustomField { Key = "recovery", Value = "private" });
        entry.Attachments.Add(new PwAttachment { FileName = "private.txt", Data = [1, 2, 3] });
        entry.History.Add(entry.CreateHistorySnapshot());
        await v.Storage.SaveEntriesBatchAsync([entry], v.Crypto);
        var loaded = Assert.Single(await v.Storage.LoadEntriesAsync(null, v.Crypto));
        Assert.Single(loaded.CustomFields); Assert.Equal(new byte[] { 1, 2, 3 }, Assert.Single(loaded.Attachments).Data); Assert.Single(loaded.History);
        Assert.Equal("ENC:valid-username", loaded.UserName);
        loaded.CustomFields.Clear(); loaded.Attachments.Clear(); loaded.History.Clear();
        await v.Storage.SaveEntriesBatchAsync([loaded], v.Crypto);
        loaded = Assert.Single(await v.Storage.LoadEntriesAsync(null, v.Crypto));
        Assert.Empty(loaded.CustomFields); Assert.Empty(loaded.Attachments); Assert.Empty(loaded.History);
    }

    [Fact]
    public async Task OldWriterCannotSaveAfterKeyRotation()
    {
        using var v = await TestVault.Create(); var entry = await v.Add();
        using var other = new SqliteStorageProvider(v.Storage.FilePath, false);
        using var master = new MasterKey("test-only-password"); var metadata = (await other.LoadVaultMetadataAsync())!;
        using var oldKey = CryptoEngine.FromMasterKey(master, Convert.FromBase64String(metadata.SaltBase64), 1, 8192, 1);
        await other.AuthenticateAsync(oldKey);
        using var replacement = new CryptoEngine(RandomNumberGenerator.GetBytes(32));
        await v.Storage.ChangeMasterKeyAsync(v.Crypto, replacement, Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)), 1, 8192, 1, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => other.SaveEntryAsync(entry, oldKey));
        Assert.Equal("secret", Assert.Single(await v.Storage.LoadEntriesAsync(null, replacement)).Password);
    }

    [Fact]
    public async Task StaleRevisionDoesNotOverwriteNewerData()
    {
        using var v = await TestVault.Create(); var entry = await v.Add(); var stale = entry.DeepClone();
        entry.Password = "newer"; await v.Storage.SaveEntryAsync(entry, v.Crypto);
        await Assert.ThrowsAsync<VaultConflictException>(() => v.Storage.SaveEntryAsync(stale, v.Crypto));
        Assert.Equal("newer", Assert.Single(await v.Storage.LoadEntriesAsync(null, v.Crypto)).Password);
    }

    [Fact]
    public async Task ExistingVaultCannotBeReinitialized()
    {
        using var v = await TestVault.Create(); await v.Add();
        byte[] before = File.ReadAllBytes(v.Storage.FilePath);
        await Assert.ThrowsAsync<IOException>(() => v.Storage.InitializeDatabaseAndSchemaAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => v.Storage.SaveVaultMetadataAsync(new VaultMetadata()));
        Assert.Equal(before, File.ReadAllBytes(v.Storage.FilePath));
    }

    [Fact]
    public async Task DeepCloneDoesNotShareCustomFieldsOrAttachmentBuffers()
    {
        using var v = await TestVault.Create(); var entry = await v.Add();
        entry.CustomFields.Add(new PwCustomField { Value = "original" }); entry.Attachments.Add(new PwAttachment { Data = [1, 2] });
        var draft = entry.DeepClone(); draft.CustomFields[0].Value = "changed"; draft.Attachments[0].Data[0] = 9;
        Assert.Equal("original", entry.CustomFields[0].Value); Assert.Equal(1, entry.Attachments[0].Data[0]);
        var duplicate = entry.DeepClone(newIdentity: true); Assert.NotEqual(entry.Id, duplicate.Id); Assert.Single(duplicate.CustomFields); Assert.Single(duplicate.Attachments);
    }

    [Fact]
    public async Task DisposedSessionCannotReadOrWriteEvenEmptySecrets()
    {
        using var v = await TestVault.Create(); var entry = await v.Add(""); v.Storage.Dispose();
        await Assert.ThrowsAsync<InvalidOperationException>(() => v.Storage.LoadEntriesAsync(null, v.Crypto));
        await Assert.ThrowsAsync<InvalidOperationException>(() => v.Storage.SaveEntryAsync(entry, v.Crypto));
        await Assert.ThrowsAsync<InvalidOperationException>(() => v.Storage.DeleteEntryAsync(entry.Id));
    }

    [Fact]
    public void AssociatedDataAndEmptyValuesAreAuthenticated()
    {
        using var crypto = new CryptoEngine(RandomNumberGenerator.GetBytes(32));
        string payload = crypto.EncryptString("", "record-A/password");
        Assert.NotEmpty(payload); Assert.Equal("", crypto.DecryptString(payload, "record-A/password"));
        Assert.ThrowsAny<CryptographicException>(() => crypto.DecryptString(payload, "record-B/password"));
        Assert.Throws<CryptographicException>(() => crypto.DecryptString("", "record-A/password"));
    }

    [Theory]
    [InlineData(0, 65536, 2)] [InlineData(3, 1048576, 2)] [InlineData(3, 65536, 100)]
    public void RejectsUnboundedKdfCosts(int iterations, int memory, int parallelism) =>
        Assert.Throws<InvalidDataException>(() => CryptoEngine.ValidateKdf(new byte[16], iterations, memory, parallelism));

    [Fact]
    public async Task RestorePreservesOriginalAndSidecarsWithoutReplayingThem()
    {
        using var v = await TestVault.Create(backup: true); await v.Add(); v.Storage.Dispose();
        byte[] damaged = [3, 1, 4, 1, 5], journal = [9, 2, 6, 5];
        File.WriteAllBytes(v.Storage.FilePath, damaged); File.WriteAllBytes(v.Storage.FilePath + "-wal", journal);
        File.WriteAllBytes(v.Storage.FilePath + "-shm", journal);
        using var master = new MasterKey("test-only-password");
        using var recovery = new SqliteStorageProvider(v.Storage.FilePath, false);
        string preserved = await recovery.RestoreBackupAsync(master);
        Assert.Equal(damaged, File.ReadAllBytes(preserved)); Assert.Equal(journal, File.ReadAllBytes(preserved + "-wal"));
        Assert.False(File.Exists(v.Storage.FilePath + "-wal"));
        var metadata = (await recovery.LoadVaultMetadataAsync())!;
        using var key = CryptoEngine.FromMasterKey(master, Convert.FromBase64String(metadata.SaltBase64), 1, 8192, 1);
        await recovery.AuthenticateAsync(key);
        Assert.Equal("secret", Assert.Single(await recovery.LoadEntriesAsync(null, key)).Password);
    }
    [Fact]
    public async Task RestoreDoesNotReplayARealWalLeftByAnInterruptedProcess()
    {
        using var v = await TestVault.Create(backup: true); await v.Add(); v.Storage.Dispose();
        string testAssembly = typeof(VaultSecurityTests).Assembly.Location;
        var start = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { "exec", "--runtimeconfig", Path.ChangeExtension(testAssembly, ".runtimeconfig.json"),
            "--depsfile", Path.ChangeExtension(testAssembly, ".deps.json"), typeof(SQLitePass.TestHost.Marker).Assembly.Location, v.Storage.FilePath }) start.ArgumentList.Add(argument);
        using var child = System.Diagnostics.Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await child.WaitForExitAsync(timeout.Token); Assert.Equal(0, child.ExitCode);
        Assert.True(File.Exists(v.Storage.FilePath + "-wal")); Assert.True(new FileInfo(v.Storage.FilePath + "-wal").Length > 32);
        using var master = new MasterKey("test-only-password"); using var recovery = new SqliteStorageProvider(v.Storage.FilePath, false);
        string preserved = await recovery.RestoreBackupAsync(master); Assert.True(File.Exists(preserved + "-wal"));
        var metadata = (await recovery.LoadVaultMetadataAsync())!;
        using var key = CryptoEngine.FromMasterKey(master, Convert.FromBase64String(metadata.SaltBase64), 1, 8192, 1);
        await recovery.AuthenticateAsync(key);
        Assert.Equal("secret", Assert.Single(await recovery.LoadEntriesAsync(null, key)).Password);
    }
    [Fact]
    public async Task WrongBackupCredentialNeverReplacesMainFile()
    {
        using var v = await TestVault.Create(backup: true); await v.Add(); v.Storage.Dispose();
        byte[] before = File.ReadAllBytes(v.Storage.FilePath);
        using var master = new MasterKey("incorrect"); using var recovery = new SqliteStorageProvider(v.Storage.FilePath, false);
        await Assert.ThrowsAsync<CryptographicException>(() => recovery.RestoreBackupAsync(master));
        Assert.Equal(before, File.ReadAllBytes(v.Storage.FilePath));
    }
    [Fact]
    public async Task RestoreRefusesAConcurrentUnlockedSession()
    {
        using var v = await TestVault.Create(backup: true);
        using var master = new MasterKey("test-only-password"); using var recovery = new SqliteStorageProvider(v.Storage.FilePath, false);
        await Assert.ThrowsAsync<IOException>(() => recovery.RestoreBackupAsync(master));
    }
    [Fact]
    public async Task BackupFailureIsVisibleWithoutMisreportingTheCommittedSave()
    {
        using var v = await TestVault.Create();
        Directory.CreateDirectory(v.Storage.BackupFilePath); v.Storage.ConfigureBackup(true, null);
        await v.Add();
        Assert.NotNull(v.Storage.LastBackupError);
        Assert.Single(await v.Storage.LoadEntriesAsync(null, v.Crypto));
    }
    [Fact]
    public async Task GroupTrashPreservesDescendantCredentials()
    {
        using var v = await TestVault.Create();
        var group = new PwGroup { Name = "Parent", ParentId = v.Root.Id }; await v.Storage.SaveGroupAsync(group, v.Crypto);
        var child = new PwGroup { Name = "Child", ParentId = group.Id }; await v.Storage.SaveGroupAsync(child, v.Crypto);
        var entry = new PwEntry { GroupId = child.Id, Password = "preserved" }; await v.Storage.SaveEntryAsync(entry, v.Crypto);
        await v.Storage.TrashGroupAsync(group.Id, v.Crypto);
        Assert.Equal(v.Trash.Id, Assert.Single(await v.Storage.LoadEntriesAsync(null, v.Crypto)).GroupId);
        Assert.DoesNotContain(await v.Storage.LoadAllGroupsAsync(v.Crypto), item => item.Id == child.Id || item.Id == group.Id);
    }
    [Fact]
    public async Task CyclicGroupChangeRollsBackAtomically()
    {
        using var v = await TestVault.Create();
        var group = new PwGroup { Name = "Child", ParentId = v.Root.Id }; await v.Storage.SaveGroupAsync(group, v.Crypto);
        v.Root.ParentId = group.Id;
        await Assert.ThrowsAsync<InvalidDataException>(() => v.Storage.SaveGroupAsync(v.Root, v.Crypto));
        using var another = new SqliteStorageProvider(v.Storage.FilePath, false);
        using var master = new MasterKey("test-only-password"); var m = (await another.LoadVaultMetadataAsync())!;
        using var key = CryptoEngine.FromMasterKey(master, Convert.FromBase64String(m.SaltBase64), 1, 8192, 1);
        await another.AuthenticateAsync(key);
        Assert.Null((await another.LoadAllGroupsAsync(key)).Single(g => g.Id == v.Root.Id).ParentId);
    }
    [Fact]
    public async Task AddedDatabaseTriggersAreDetectedBeforeTheyCanChangeAWrittenRecord()
    {
        using var v = await TestVault.Create(); var entry = await v.Add();
        v.Sql("CREATE TRIGGER injected AFTER UPDATE ON sp_entries BEGIN DELETE FROM sp_entries; END");
        await Assert.ThrowsAsync<InvalidDataException>(() => v.Storage.SaveEntryAsync(entry, v.Crypto));
        Assert.True(v.Storage.IsReadOnly);
    }
    [Fact]
    public async Task FailedRekeyLeavesTheOldSessionAndRecordsUsable()
    {
        using var v = await TestVault.Create(); await v.Add();
        var replacement = new CryptoEngine(RandomNumberGenerator.GetBytes(32)); replacement.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => v.Storage.ChangeMasterKeyAsync(v.Crypto, replacement,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)), 1, 8192, 1, true));
        Assert.False(v.Crypto.IsDisposed);
        Assert.Equal("secret", Assert.Single(await v.Storage.LoadEntriesAsync(null, v.Crypto)).Password);
    }
    [Fact]
    public async Task ConcurrentWritersLeaveTheLatestBackupComplete()
    {
        using var v = await TestVault.Create(backup: true);
        using var master = new MasterKey("test-only-password");
        using var other = new SqliteStorageProvider(v.Storage.FilePath);
        var metadata = (await other.LoadVaultMetadataAsync())!;
        using var key = CryptoEngine.FromMasterKey(master, Convert.FromBase64String(metadata.SaltBase64), 1, 8192, 1);
        await other.AuthenticateAsync(key);
        var a = new PwEntry { GroupId = v.Root.Id, Password = "a" }; var b = new PwEntry { GroupId = v.Root.Id, Password = "b" };
        await Task.WhenAll(v.Storage.SaveEntryAsync(a, v.Crypto), other.SaveEntryAsync(b, key));
        Assert.Null(v.Storage.LastBackupError); Assert.Null(other.LastBackupError);
        using var snapshot = new SqliteStorageProvider(v.Storage.BackupFilePath, false);
        using var snapshotKey = CryptoEngine.FromMasterKey(master, Convert.FromBase64String(metadata.SaltBase64), 1, 8192, 1);
        await snapshot.AuthenticateAsync(snapshotKey);
        Assert.Equal(2, (await snapshot.LoadEntriesAsync(null, snapshotKey)).Count);
    }
    [Fact]
    public async Task RootCannotBeMovedUnderAnotherTopLevelGroup()
    {
        using var v = await TestVault.Create();
        var another = new PwGroup { Name = "Another root" }; await v.Storage.SaveGroupAsync(another, v.Crypto);
        v.Root.ParentId = another.Id;
        await Assert.ThrowsAsync<InvalidDataException>(() => v.Storage.SaveGroupAsync(v.Root, v.Crypto));
        Assert.False(v.Storage.IsReadOnly);
        Assert.Null((await v.Storage.LoadAllGroupsAsync(v.Crypto)).Single(group => group.Id == v.Root.Id).ParentId);
    }
}
