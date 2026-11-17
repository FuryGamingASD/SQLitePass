using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Database;

namespace SQLitePass.Tests;

public sealed class LegacyStreamingTests
{
    private sealed class Fixture : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "SQLitePass-legacy-stream-" + Guid.NewGuid().ToString("N"));
        public string PathName => Path.Combine(DirectoryPath, "legacy.kpd");
        public CryptoEngine Crypto { get; }
        public Guid Root { get; } = Guid.NewGuid();
        public Guid[] Ids { get; } = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        public Fixture()
        {
            Directory.CreateDirectory(DirectoryPath);
            using var master = new MasterKey("synthetic legacy stream"); byte[] salt = RandomNumberGenerator.GetBytes(16);
            Crypto = CryptoEngine.FromMasterKey(master, salt, 1, 8192, 1);
            using var connection = Open();
            using var schema = new StreamReader(typeof(LegacyMigrationTests).Assembly.GetManifestResourceStream("SQLitePass.Tests.Fixtures.legacy-schema.sql")!);
            Execute(connection, schema.ReadToEnd());
            Execute(connection, """
                INSERT INTO kp_vault_metadata(vault_id,vault_name,salt_base64,kdf_iterations,kdf_memory_kb,kdf_parallelism,verification_canary,created_at,updated_at)
                VALUES($vault,'Synthetic legacy',$salt,1,8192,1,$canary,$now,$now);
                INSERT INTO kp_groups(id,name,created_at,updated_at) VALUES($root,'Root',$now,$now);
                """, ("$vault", Guid.NewGuid().ToString()), ("$salt", Convert.ToBase64String(salt)), ("$canary", Crypto.GenerateCanaryToken()),
                ("$root", Root.ToString()), ("$now", DateTime.UtcNow.ToString("O")));
            foreach (var (id, index) in Ids.Select((id, index) => (id, index)))
            {
                Execute(connection, """
                    INSERT INTO kp_entries(id,group_id,title,username,password_enc,url,notes_enc,created_at,updated_at,last_access_at)
                    VALUES($id,$root,$title,'synthetic user',$secret,'https://synthetic.invalid',$notes,$now,$now,$now);
                    INSERT INTO kp_custom_fields(id,entry_id,field_key,field_val_enc,is_protected) VALUES($field,$id,'token',$secret,1);
                    INSERT INTO kp_attachments(id,entry_id,filename,filesize,data_enc,created_at) VALUES($attachment,$id,'synthetic.bin',4,$bytes,$now);
                    """, ("$id", id.ToString()), ("$root", Root.ToString()), ("$title", "Synthetic " + index), ("$secret", Crypto.EncryptString("secret " + index)),
                    ("$notes", Crypto.EncryptString("synthetic notes")), ("$now", DateTime.UtcNow.ToString("O")), ("$field", Guid.NewGuid().ToString()),
                    ("$attachment", Guid.NewGuid().ToString()), ("$bytes", Crypto.EncryptBytes([1, 2, 3, 4])));
            }
            for (int index = 0; index < 60; index++)
                Execute(connection, """
                    INSERT INTO kp_history(id,entry_id,title,username,password_enc,url,notes_enc,saved_at)
                    VALUES($id,$entry,'Synthetic history','synthetic',$secret,'https://history.invalid',$notes,$now)
                    """, ("$id", Guid.NewGuid().ToString()), ("$entry", Ids[0].ToString()), ("$secret", Crypto.EncryptString("history " + index)),
                    ("$notes", Crypto.EncryptString("")), ("$now", DateTime.UtcNow.ToString("O")));
        }
        public SqliteConnection Open()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = PathName, Pooling = false }.ToString()); connection.Open(); return connection;
        }
        public static void Execute(SqliteConnection connection, string sql, params (string Name, object Value)[] arguments)
        {
            using var command = connection.CreateCommand(); command.CommandText = sql;
            foreach (var (name, value) in arguments) command.Parameters.AddWithValue(name, value);
            command.ExecuteNonQuery();
        }
        public void Dispose()
        {
            Crypto.Dispose(); string resolved = Path.GetFullPath(DirectoryPath);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(resolved).StartsWith("SQLitePass-legacy-stream-", StringComparison.Ordinal)) throw new InvalidOperationException("Invalid fixture cleanup path.");
            Directory.Delete(resolved, recursive: true);
        }
    }

    [Fact]
    public void StreamingReaderClearsThePreviousRecordAndRetainsOnlyTheLastFiftyRevisions()
    {
        using var fixture = new Fixture(); using var connection = fixture.Open(); using var transaction = connection.BeginTransaction(deferred: true);
        var plan = LegacyVaultReader.ValidateReferences(connection, transaction);
        using var entries = LegacyVaultReader.Entries(connection, fixture.Crypto, false, transaction, plan).GetEnumerator();
        Assert.Equal(3, plan.EntryCount); Assert.True(entries.MoveNext()); var previous = entries.Current;
        Assert.Equal("secret 0", previous.Password); Assert.Equal(50, previous.History.Count);
        Assert.Equal("history 10", previous.History[0].Password); Assert.Equal("history 59", previous.History[^1].Password);
        byte[] attachment = Assert.Single(previous.Attachments).Data;
        Assert.True(entries.MoveNext()); Assert.Empty(previous.Password); Assert.Empty(previous.CustomFields); Assert.All(attachment, value => Assert.Equal(0, value));
        var current = entries.Current; entries.Dispose(); Assert.Empty(current.Password);
    }

    [Fact]
    public async Task StreamingMigrationPreservesChildrenAndHandlesNonCanonicalOwnerIds()
    {
        using var fixture = new Fixture();
        using (var connection = fixture.Open())
            Fixture.Execute(connection, "PRAGMA foreign_keys=OFF; UPDATE kp_custom_fields SET entry_id=$id WHERE entry_id=$original", ("$id", "{" + fixture.Ids[0].ToString().ToUpperInvariant() + "}"), ("$original", fixture.Ids[0].ToString()));
        using var storage = new SqliteStorageProvider(fixture.PathName, false); await storage.AuthenticateAsync(fixture.Crypto);
        var entries = await storage.LoadEntriesAsync(null, fixture.Crypto);
        Assert.Equal(3, entries.Count);
        foreach (var entry in entries)
        {
            Assert.Equal(entry.Password, Assert.Single(entry.CustomFields).Value);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, Assert.Single(entry.Attachments).Data);
        }
        Assert.Equal(50, entries.Single(entry => entry.Id == fixture.Ids[0]).History.Count);
    }

    [Fact]
    public async Task MigrationCommitFailureRollsBackAllStreamedRowsAndPreservesTheSource()
    {
        using var fixture = new Fixture(); byte[] original = File.ReadAllBytes(fixture.PathName);
        using var storage = new SqliteStorageProvider(fixture.PathName, false);
        storage.SimulateIoFailure = stage => { if (stage == VaultIoStage.BeforeCommit) throw new IOException("Synthetic migration commit failure"); };
        await Assert.ThrowsAsync<IOException>(() => storage.AuthenticateAsync(fixture.Crypto));
        Assert.Equal(original, File.ReadAllBytes(fixture.PathName));
        using var connection = fixture.Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='sp_metadata'"; Assert.Equal(0L, command.ExecuteScalar());
        command.CommandText = "SELECT COUNT(*) FROM kp_entries"; Assert.Equal(3L, command.ExecuteScalar());
        Assert.Single(Directory.GetFiles(fixture.DirectoryPath, "*.pre-v2-*.backup"));
    }

    [Fact]
    public async Task CorruptLastLegacyRecordIsRejectedBeforeSchemaMigration()
    {
        using var fixture = new Fixture(); byte[] damaged = fixture.Crypto.EncryptBytes("synthetic"u8.ToArray()); damaged[^1] ^= 1;
        using (var connection = fixture.Open()) Fixture.Execute(connection, "UPDATE kp_entries SET password_enc=$secret WHERE id=$id",
            ("$secret", Convert.ToBase64String(damaged)), ("$id", fixture.Ids[^1].ToString()));
        byte[] original = File.ReadAllBytes(fixture.PathName);
        using var storage = new SqliteStorageProvider(fixture.PathName, false);
        await Assert.ThrowsAnyAsync<CryptographicException>(() => storage.AuthenticateAsync(fixture.Crypto));
        Assert.Equal(original, File.ReadAllBytes(fixture.PathName));
        Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.pre-v2-*.backup"));
    }
}
