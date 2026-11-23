using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DgNet.Keepass;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;
using KdbxDatabase = DgNet.Keepass.Database;
using KdbxEntry = DgNet.Keepass.Entry;
using KdbxGroup = DgNet.Keepass.Group;

namespace SQLitePass.Tests;

public sealed class KdbxImportTests
{
    private sealed class Scratch : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SQLitePass-kdbx4-" + Guid.NewGuid().ToString("N"));
        public Scratch() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            string root = System.IO.Path.GetFullPath(Path);
            if (!root.StartsWith(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
                !System.IO.Path.GetFileName(root).StartsWith("SQLitePass-kdbx4-", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing to remove an unexpected test directory.");
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class InlineProgress(Action<VaultImportProgress> report) : IProgress<VaultImportProgress>
    { public void Report(VaultImportProgress value) => report(value); }

    private const string SourcePassword = "synthetic KDBX fixture only";

    [Fact]
    public async Task ImportsEncryptedKdbx4IntoNewVaultAndReportsPreservedAndUnsupportedData()
    {
        using var scratch = new Scratch();
        string source = CreateKdbx4(scratch.Path, entryCount: 1);
        byte[] sourceHash = SHA256.HashData(File.ReadAllBytes(source));
        byte[] encrypted = File.ReadAllBytes(source);
        Assert.DoesNotContain("synthetic-password", Encoding.Latin1.GetString(encrypted));

        using var plan = await KdbxImportService.ReadAsync(source, SourcePassword);
        Assert.Equal("4.1", plan.Report.FormatVersion);
        Assert.Equal(1, plan.Report.EntriesImported);
        Assert.Equal(2, plan.Report.GroupsImported);
        Assert.Equal(1, plan.Report.RecycleBinEntriesSkipped);
        Assert.Contains(plan.Report.PreservedData, item => item.Contains("allegati", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Report.UnsupportedData, item => item.Feature == "Note dei gruppi" && item.Count == 1);
        Assert.Contains(plan.Report.UnsupportedData, item => item.Feature == "Cestino KeePass" && item.Count == 1);
        Assert.Contains(plan.Report.UnsupportedData, item => item.Feature == "Icone personalizzate" && item.Count > 0);

        using var destinationMaster = new MasterKey("new synthetic SQLitePass password");
        var created = await KdbxImportService.CreateNewVaultAsync(plan, System.IO.Path.Combine(scratch.Path, "imported.kpd"),
            destinationMaster, new AppSettings { EnableDualFileBackup = false }, "General", "Recycle Bin");
        await using var session = created.Session;
        Assert.Equal("4.1", created.Report.FormatVersion);
        Assert.Equal(System.IO.Path.Combine(scratch.Path, "imported.kpd"), session.Storage.FilePath);
        Assert.True(await session.Storage.CheckDatabaseIntegrityAsync());
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(source)));

        var snapshot = await session.Storage.LoadSnapshotAsync(session.Crypto);
        var imported = Assert.Single(snapshot.Entries);
        Assert.Equal("KDBX Ω entry", imported.Title);
        Assert.Equal("synthetic-password", imported.Password);
        Assert.Equal("https://example.invalid/kdbx", imported.Url);
        Assert.Equal("αβγ notes", imported.Notes);
        Assert.Equal("one;two", imported.Tags);
        Assert.Equal("{USERNAME}{TAB}{PASSWORD}{ENTER}", imported.AutoTypeSequence);
        Assert.Equal(new[] { "Accounts/Personal", "Ω" },
            GroupPath(snapshot.Groups, imported.GroupId, session.Metadata.RootGroupId!.Value));
        Assert.Equal("recovery-key", Assert.Single(imported.CustomFields).Key);
        Assert.Equal("protected synthetic field", imported.CustomFields[0].Value);
        Assert.True(imported.CustomFields[0].IsProtected);
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33 }, Assert.Single(imported.Attachments).Data);
        Assert.Single(imported.History);
        Assert.Equal("previous synthetic-password", imported.History[0].Password);
        Assert.DoesNotContain(snapshot.Entries, entry => entry.Title == "Deleted fixture entry");
    }

    [Fact]
    public async Task SupportsKeyFileOnlyAndDoesNotModifyTheSource()
    {
        using var scratch = new Scratch();
        string keyFile = System.IO.Path.Combine(scratch.Path, "source.keyx");
        string source = CreateKdbx4(scratch.Path, entryCount: 1, password: string.Empty, keyFilePath: keyFile);
        using var plan = await KdbxImportService.ReadAsync(source, string.Empty, keyFile);
        Assert.Equal(1, plan.Report.EntriesImported);
        using var destinationMaster = new MasterKey("new synthetic SQLitePass password");
        var created = await KdbxImportService.CreateNewVaultAsync(plan, System.IO.Path.Combine(scratch.Path, "key-only.kpd"),
            destinationMaster, new AppSettings { EnableDualFileBackup = false }, "General", "Recycle Bin");
        await using var session = created.Session;
        Assert.Equal("synthetic-password", Assert.Single((await session.Storage.LoadSnapshotAsync(session.Crypto)).Entries).Password);
    }

    [Theory]
    [InlineData(CipherAlgorithm.Aes128Cbc, false)]
    [InlineData(CipherAlgorithm.Aes256Cbc, true)]
    [InlineData(CipherAlgorithm.Twofish256Cbc, false)]
    public async Task ReadsSupportedKdbx4CiphersAndAesKdf(CipherAlgorithm cipher, bool aesKdf)
    {
        using var scratch = new Scratch();
        string source = CreateKdbx4(scratch.Path, entryCount: 1, cipher: cipher,
            kdf: aesKdf ? new AesKdf(RandomNumberGenerator.GetBytes(32), 10) : null);
        using var plan = await KdbxImportService.ReadAsync(source, SourcePassword);
        Assert.Equal(1, plan.Report.EntriesImported);
    }

    [Fact]
    public async Task RejectsWrongCredentialsKdbx3AndUnsafeKdfBeforeImport()
    {
        using var scratch = new Scratch();
        string kdbx4 = CreateKdbx4(scratch.Path, entryCount: 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => KdbxImportService.ReadAsync(kdbx4, "wrong password"));

        string kdbx3 = CreateKdbx3(scratch.Path);
        await Assert.ThrowsAsync<NotSupportedException>(() => KdbxImportService.ReadAsync(kdbx3, SourcePassword));

        string unsafeKdf = System.IO.Path.Combine(scratch.Path, "unsafe-kdf.kdbx");
        File.Copy(kdbx4, unsafeKdf);
        SetArgon2MemoryBytes(unsafeKdf, (512UL * 1024 + 1) * 1024);
        await Assert.ThrowsAsync<InvalidDataException>(() => KdbxImportService.ReadAsync(unsafeKdf, SourcePassword));

        string unsafeHeader = System.IO.Path.Combine(scratch.Path, "unsafe-header.kdbx");
        File.Copy(kdbx4, unsafeHeader);
        SetFirstHeaderFieldLength(unsafeHeader, uint.MaxValue);
        await Assert.ThrowsAsync<InvalidDataException>(() => KdbxImportService.ReadAsync(unsafeHeader, SourcePassword));

        string missingArgonVersion = System.IO.Path.Combine(scratch.Path, "missing-argon-version.kdbx");
        File.Copy(kdbx4, missingArgonVersion);
        MutateArgon2Version(missingArgonVersion, remove: true);
        await Assert.ThrowsAsync<NotSupportedException>(() => KdbxImportService.ReadAsync(missingArgonVersion, SourcePassword));

        string wrongTypeArgonVersion = System.IO.Path.Combine(scratch.Path, "wrong-type-argon-version.kdbx");
        File.Copy(kdbx4, wrongTypeArgonVersion);
        MutateArgon2Version(wrongTypeArgonVersion, remove: false);
        await Assert.ThrowsAsync<NotSupportedException>(() => KdbxImportService.ReadAsync(wrongTypeArgonVersion, SourcePassword));

        string oversizedKeyFile = System.IO.Path.Combine(scratch.Path, "oversized.key");
        File.WriteAllBytes(oversizedKeyFile, new byte[4 * 1024 * 1024 + 1]);
        await Assert.ThrowsAsync<InvalidDataException>(() => KdbxImportService.ReadAsync(kdbx4, SourcePassword, oversizedKeyFile));
    }

    [Fact]
    public async Task CancellationDuringImportRemovesTheStagingVaultAndLeavesNoDestination()
    {
        using var scratch = new Scratch();
        string source = CreateKdbx4(scratch.Path, entryCount: 55);
        using var plan = await KdbxImportService.ReadAsync(source, SourcePassword);
        string destination = System.IO.Path.Combine(scratch.Path, "cancelled.kpd");
        using var master = new MasterKey("destination password");
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(value => { if (value.Completed >= 50) cancellation.Cancel(); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => KdbxImportService.CreateNewVaultAsync(plan, destination,
            master, new AppSettings { EnableDualFileBackup = false }, "General", "Recycle Bin", cancellation.Token, progress));

        Assert.False(File.Exists(destination));
        Assert.Single(Directory.EnumerateFiles(scratch.Path), file => file == source);
        Assert.Empty(Directory.EnumerateDirectories(scratch.Path));
    }

    [Fact]
    public async Task FailedMidBatchImportRollsBackAndDoesNotPublishAFile()
    {
        using var scratch = new Scratch();
        string source = CreateKdbx4(scratch.Path, entryCount: 55);
        using var plan = await KdbxImportService.ReadAsync(source, SourcePassword);
        string destination = System.IO.Path.Combine(scratch.Path, "failed.kpd");
        using var master = new MasterKey("destination password");
        var progress = new InlineProgress(value =>
        {
            if (value.Completed >= 50) throw new IOException("synthetic storage failure");
        });

        await Assert.ThrowsAsync<IOException>(() => KdbxImportService.CreateNewVaultAsync(plan, destination,
            master, new AppSettings { EnableDualFileBackup = false }, "General", "Recycle Bin", default, progress));

        Assert.False(File.Exists(destination));
        Assert.Single(Directory.EnumerateFiles(scratch.Path), file => file == source);
        Assert.Empty(Directory.EnumerateDirectories(scratch.Path));
    }

    [Fact]
    public async Task ExistingDestinationIsNeverOverwritten()
    {
        using var scratch = new Scratch();
        string source = CreateKdbx4(scratch.Path, entryCount: 1);
        using var plan = await KdbxImportService.ReadAsync(source, SourcePassword);
        string destination = System.IO.Path.Combine(scratch.Path, "existing.kpd");
        byte[] sentinel = Encoding.UTF8.GetBytes("existing destination fixture");
        File.WriteAllBytes(destination, sentinel);
        using var master = new MasterKey("destination password");

        await Assert.ThrowsAsync<IOException>(() => KdbxImportService.CreateNewVaultAsync(plan, destination,
            master, new AppSettings { EnableDualFileBackup = false }, "General", "Recycle Bin"));

        Assert.Equal(sentinel, File.ReadAllBytes(destination));
        Assert.Empty(Directory.EnumerateDirectories(scratch.Path));
    }

    private static IReadOnlyList<string> GroupPath(IReadOnlyList<PwGroup> groups, Guid leafId, Guid rootId)
    {
        var byId = groups.ToDictionary(group => group.Id);
        var result = new List<string>();
        Guid? current = leafId;
        while (current != null && current != rootId)
        {
            var group = byId[current.Value];
            result.Insert(0, group.Name);
            current = group.ParentId;
        }
        return result.Skip(1).ToArray(); // skip the SQLitePass General target group
    }

    private static string CreateKdbx4(string directory, int entryCount, string password = SourcePassword,
        string? keyFilePath = null, CipherAlgorithm cipher = CipherAlgorithm.ChaCha20, IKdf? kdf = null)
    {
        string path = System.IO.Path.Combine(directory, $"fixture-{Guid.NewGuid():N}.kdbx");
        string? sourceKeyFile = keyFilePath;
        if (!string.IsNullOrWhiteSpace(sourceKeyFile)) File.WriteAllBytes(sourceKeyFile, RandomNumberGenerator.GetBytes(32));
        var settings = new Settings
        {
            Cipher = cipher,
            Kdf = kdf ?? new Argon2Kdf(RandomNumberGenerator.GetBytes(16), 1, 8192, 1)
        };
        using var database = sourceKeyFile == null
            ? KdbxDatabase.Create(password, settings)
            : KdbxDatabase.Create(password, sourceKeyFile, settings);
        database.Metadata.Name = "Synthetic encrypted import fixture";
        database.Metadata.RecycleBinEnabled = true;
        Guid customIcon = Guid.NewGuid();
        database.Metadata.CustomIcons.Add(new CustomIcon { Uuid = customIcon, Data = [0x89, 0x50, 0x4E, 0x47] });

        var accounts = new KdbxGroup
        {
            Name = "Accounts/Personal",
            Notes = "synthetic group note",
            CustomIconUuid = customIcon
        };
        var unicode = new KdbxGroup { Name = "Ω" };
        accounts.AddGroup(unicode);
        database.RootGroup.AddGroup(accounts);

        for (int i = 0; i < entryCount; i++)
        {
            var entry = new KdbxEntry
            {
                Title = i == 0 ? "KDBX Ω entry" : $"KDBX fixture entry {i}",
                UserName = "synthetic-user",
                Password = "synthetic-password",
                Url = "https://example.invalid/kdbx",
                Notes = "αβγ notes",
                Tags = "one;two",
                IconId = 10,
                CustomIconUuid = customIcon,
                AutoType = new AutoType { Enabled = true, DefaultSequence = "{USERNAME}{TAB}{PASSWORD}{ENTER}",
                    Associations = [new AutoTypeAssociation { Window = "example.invalid", Sequence = "{USERNAME}{ENTER}" }] }
            };
            entry.Strings["recovery-key"] = new EntryString { Value = "protected synthetic field", Protected = true };
            entry.Binaries.Add(new EntryBinary { Name = "fixture.bin", Data = [0x11, 0x22, 0x33], IsProtected = true });
            if (i == 0)
            {
                entry.Times.Expires = true;
                entry.Times.ExpiryTime = DateTime.UtcNow.AddDays(30);
                entry.History.Add(new KdbxEntry
                {
                    Title = "KDBX Ω entry",
                    UserName = "synthetic-user",
                    Password = "previous synthetic-password",
                    Url = "https://example.invalid/kdbx",
                    Notes = "previous notes",
                    Times = Times.Create()
                });
            }
            unicode.AddEntry(entry);
        }

        var recycleBin = new KdbxGroup { Name = "Recycle Bin", Uuid = Guid.NewGuid(), IsExpanded = false };
        database.Metadata.RecycleBinUuid = recycleBin.Uuid;
        database.RootGroup.AddGroup(recycleBin);
        recycleBin.AddEntry(new KdbxEntry { Title = "Deleted fixture entry", Password = "deleted fixture secret" });
        database.SaveAs(path);
        return path;
    }

    private static string CreateKdbx3(string directory)
    {
        string path = System.IO.Path.Combine(directory, $"fixture-v3-{Guid.NewGuid():N}.kdbx");
        var settings = new Settings
        {
            Format = KdbxFormat.Kdbx3,
            Kdf = new AesKdf(RandomNumberGenerator.GetBytes(32), 10)
        };
        using var database = KdbxDatabase.Create(SourcePassword, settings);
        database.RootGroup.AddEntry(new KdbxEntry { Title = "Synthetic KDBX3 fixture" });
        database.SaveAs(path);
        return path;
    }

    private static void SetArgon2MemoryBytes(string path, ulong memoryBytes)
    {
        byte[] file = File.ReadAllBytes(path);
        int offset = 12;
        while (offset < file.Length)
        {
            byte fieldId = file[offset++];
            uint fieldLength = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(offset, 4));
            offset += 4;
            if (fieldLength > file.Length - offset) throw new InvalidDataException("Bad test fixture header.");
            if (fieldId == 0x0B)
            {
                int mapOffset = offset + 2;
                while (mapOffset < offset + fieldLength)
                {
                    byte type = file[mapOffset++];
                    if (type == 0) break;
                    uint keyLength = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(mapOffset, 4));
                    mapOffset += 4;
                    string key = Encoding.UTF8.GetString(file, mapOffset, checked((int)keyLength));
                    mapOffset += checked((int)keyLength);
                    uint valueLength = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(mapOffset, 4));
                    mapOffset += 4;
                    if (key == "M")
                    {
                        Assert.Equal(8u, valueLength);
                        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(mapOffset, 8), memoryBytes);
                        File.WriteAllBytes(path, file);
                        return;
                    }
                    mapOffset += checked((int)valueLength);
                }
                throw new InvalidDataException("Argon2 memory field is missing from test fixture.");
            }
            offset += (int)fieldLength;
            if (fieldId == 0) break;
        }
        throw new InvalidDataException("KDBX KDF header is missing from test fixture.");
    }

    private static void SetFirstHeaderFieldLength(string path, uint length)
    {
        byte[] file = File.ReadAllBytes(path);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(13, 4), length);
        File.WriteAllBytes(path, file);
    }

    private static void MutateArgon2Version(string path, bool remove)
    {
        byte[] file = File.ReadAllBytes(path);
        int fieldOffset = 12;
        while (fieldOffset < file.Length)
        {
            byte fieldId = file[fieldOffset++];
            int fieldLengthOffset = fieldOffset;
            uint fieldLength = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(fieldOffset, 4));
            fieldOffset += 4;
            if (fieldLength > file.Length - fieldOffset) throw new InvalidDataException("Bad test fixture header.");
            int fieldEnd = fieldOffset + checked((int)fieldLength);
            if (fieldId == 0x0B)
            {
                int cursor = fieldOffset + 2;
                while (cursor < fieldEnd)
                {
                    int itemStart = cursor;
                    byte type = file[cursor++];
                    if (type == 0) break;
                    uint keyLength = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(cursor, 4));
                    cursor += 4;
                    string key = Encoding.UTF8.GetString(file, cursor, checked((int)keyLength));
                    cursor += checked((int)keyLength);
                    uint valueLength = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(cursor, 4));
                    cursor += 4;
                    cursor += checked((int)valueLength);
                    if (key != "V") continue;
                    if (!remove)
                    {
                        file[itemStart] = 0x18; // String instead of the required UInt32.
                        File.WriteAllBytes(path, file);
                        return;
                    }

                    int removedBytes = cursor - itemStart;
                    byte[] updated = new byte[file.Length - removedBytes];
                    Buffer.BlockCopy(file, 0, updated, 0, itemStart);
                    Buffer.BlockCopy(file, cursor, updated, itemStart, file.Length - cursor);
                    BinaryPrimitives.WriteUInt32LittleEndian(updated.AsSpan(fieldLengthOffset, 4), fieldLength - (uint)removedBytes);
                    File.WriteAllBytes(path, updated);
                    return;
                }
                throw new InvalidDataException("Argon2 version field is missing from the fixture.");
            }
            fieldOffset = fieldEnd;
            if (fieldId == 0) break;
        }
        throw new InvalidDataException("KDBX KDF header is missing from the fixture.");
    }
}
