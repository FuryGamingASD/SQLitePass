using System.Security.Cryptography;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Database;

namespace SQLitePass.Tests;

public partial class VaultSecurityTests
{
    [Fact]
    public async Task SuccessfulBackupRemovesTemporaryDatabaseAndJournals()
    {
        using var vault = await TestVault.Create(backup: true);
        await vault.Add();

        Assert.Null(vault.Storage.LastBackupError);
        Assert.True(File.Exists(vault.Storage.BackupFilePath));
        Assert.Empty(Directory.GetFiles(vault.DirectoryPath, "*.tmp*"));
    }

    [Fact]
    public async Task SimulatedFullDiskBeforeCommitRollsBackWithoutUpdatingBackup()
    {
        using var vault = await TestVault.Create(backup: true); var entry = await vault.Add();
        byte[] backup = File.ReadAllBytes(vault.Storage.BackupFilePath);
        vault.Storage.SimulateIoFailure = stage => { if (stage == VaultIoStage.BeforeCommit) throw new IOException("Synthetic full disk"); };
        entry.Password = "must roll back";
        await Assert.ThrowsAsync<IOException>(() => vault.Storage.SaveEntryAsync(entry, vault.Crypto));
        vault.Storage.SimulateIoFailure = null;
        Assert.Equal("secret", (await vault.Storage.LoadEntryAsync(entry.Id, vault.Crypto)).Password);
        Assert.Equal(backup, File.ReadAllBytes(vault.Storage.BackupFilePath));
    }

    [Theory]
    [InlineData((int)VaultIoStage.BackupCopied)]
    [InlineData((int)VaultIoStage.BeforeBackupReplacement)]
    public async Task BackupIoFailurePreservesPreviousBackupAndReportsCommittedSave(int failure)
    {
        using var vault = await TestVault.Create(backup: true); var entry = await vault.Add();
        byte[] backup = File.ReadAllBytes(vault.Storage.BackupFilePath);
        vault.Storage.SimulateIoFailure = stage => { if ((int)stage == failure) throw new IOException("Synthetic removal of device"); };
        entry.Password = "committed"; await vault.Storage.SaveEntryAsync(entry, vault.Crypto);
        Assert.NotNull(vault.Storage.LastBackupError);
        Assert.Equal("committed", (await vault.Storage.LoadEntryAsync(entry.Id, vault.Crypto)).Password);
        Assert.Equal(backup, File.ReadAllBytes(vault.Storage.BackupFilePath));
        Assert.Empty(Directory.GetFiles(vault.DirectoryPath, "*.tmp*"));
    }

    [Fact]
    public async Task FilesystemReplacementFailureKeepsPreviousBackupAndCommittedSave()
    {
        using var vault = await TestVault.Create(backup: true); var entry = await vault.Add();
        string backupPath = vault.Storage.BackupFilePath;
        byte[] previousBackup = File.ReadAllBytes(backupPath);
        string blockedRotationPath = backupPath + ".1";
        if (File.Exists(blockedRotationPath)) File.Delete(blockedRotationPath);
        Directory.CreateDirectory(blockedRotationPath);

        entry.Password = "database commit survives backup rotation failure";
        await vault.Storage.SaveEntryAsync(entry, vault.Crypto);

        Assert.NotNull(vault.Storage.LastBackupError);
        Assert.Equal(entry.Password, (await vault.Storage.LoadEntryAsync(entry.Id, vault.Crypto)).Password);
        Assert.Equal(previousBackup, File.ReadAllBytes(backupPath));
        Assert.Empty(Directory.GetFiles(vault.DirectoryPath, "*.tmp*"));
    }

    [Fact]
    public async Task DeletionPreservesAReadableSnapshotEvenWhenAutomaticBackupIsDisabled()
    {
        using var vault = await TestVault.Create(); var entry = await vault.Add();
        await vault.Storage.DeleteEntryAsync(entry.Id);
        Assert.Empty(await vault.Storage.LoadEntriesAsync(null, vault.Crypto));
        string path = Assert.Single(Directory.GetFiles(vault.DirectoryPath, "*.pre-delete-*.backup"));
        using var snapshot = new SqliteStorageProvider(path, false);
        using var master = new MasterKey("test-only-password"); var metadata = (await snapshot.LoadVaultMetadataAsync())!;
        using var crypto = CryptoEngine.FromMasterKey(master, Convert.FromBase64String(metadata.SaltBase64), 1, 8192, 1);
        await snapshot.AuthenticateAsync(crypto);
        Assert.Equal("secret", Assert.Single(await snapshot.LoadEntriesAsync(null, crypto)).Password);
    }

    [Theory]
    [InlineData((int)VaultIoStage.RecoveryValidated)]
    [InlineData((int)VaultIoStage.BeforeRecoveryReplacement)]
    public async Task FailedRecoveryKeepsOriginalAndRestoresItsJournals(int failure)
    {
        using var vault = await TestVault.Create(backup: true); await vault.Add();
        await vault.Storage.CloseAsync(); string path = vault.Storage.FilePath;
        byte[] main = File.ReadAllBytes(path), wal = [1, 2, 3, 4], shm = [5, 6, 7];
        File.WriteAllBytes(path + "-wal", wal); File.WriteAllBytes(path + "-shm", shm);
        using var recovery = new SqliteStorageProvider(path, false);
        recovery.SimulateIoFailure = stage => { if ((int)stage == failure) throw new IOException("Synthetic replacement failure"); };
        using var master = new MasterKey("test-only-password");
        await Assert.ThrowsAsync<IOException>(() => recovery.RestoreBackupAsync(master));
        Assert.Equal(main, File.ReadAllBytes(path)); Assert.Equal(wal, File.ReadAllBytes(path + "-wal")); Assert.Equal(shm, File.ReadAllBytes(path + "-shm"));
        Assert.Empty(Directory.GetFiles(vault.DirectoryPath, "*.restore-*.tmp"));
    }
}
