using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Database;

namespace SQLitePass.Tests;

public class LegacyMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MigrationPreservesCredentialsHistoryAndOriginalBackup(bool withKeyFile)
    {
        string folder = Path.Combine(Path.GetTempPath(), "SQLitePass-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); string path = Path.Combine(folder, "legacy.kpd");
        byte[]? file = withKeyFile ? RandomNumberGenerator.GetBytes(128) : null;
        using var master = new MasterKey("synthetic", file); byte[] salt = RandomNumberGenerator.GetBytes(16);
        using var crypto = CryptoEngine.FromMasterKey(master, salt, 1, 8192, 1);
        Guid root = Guid.NewGuid(), entry = Guid.NewGuid();
        try
        {
            using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                c.Open(); using var cmd = c.CreateCommand();
                using var schema = new StreamReader(typeof(LegacyMigrationTests).Assembly.GetManifestResourceStream("SQLitePass.Tests.Fixtures.legacy-schema.sql")!);
                cmd.CommandText = schema.ReadToEnd(); cmd.ExecuteNonQuery();
                cmd.CommandText = """
                    INSERT INTO kp_vault_metadata(vault_id,vault_name,salt_base64,kdf_iterations,kdf_memory_kb,kdf_parallelism,verification_canary,created_at,updated_at)
                    VALUES($vault,'Legacy',$salt,1,8192,1,$canary,$now,$now);
                    INSERT INTO kp_groups(id,name,created_at,updated_at) VALUES($root,'Legacy root',$now,$now);
                    INSERT INTO kp_entries(id,group_id,title,username,password_enc,url,notes_enc,created_at,updated_at,last_access_at)
                    VALUES($entry,$root,'Private title','ENC:legitimate-plaintext',$password,'https://example.invalid',$notes,$now,$now,$now);
                    INSERT INTO kp_history(id,entry_id,title,username,password_enc,url,notes_enc,saved_at)
                    VALUES($history,$entry,'Previous','old-user',$oldPassword,'https://history.invalid',$notes,$now);
                    """;
                cmd.Parameters.AddWithValue("$vault", Guid.NewGuid().ToString()); cmd.Parameters.AddWithValue("$root", root.ToString());
                cmd.Parameters.AddWithValue("$entry", entry.ToString()); cmd.Parameters.AddWithValue("$history", Guid.NewGuid().ToString());
                cmd.Parameters.AddWithValue("$salt", Convert.ToBase64String(salt)); cmd.Parameters.AddWithValue("$canary", crypto.GenerateCanaryToken());
                cmd.Parameters.AddWithValue("$password", crypto.EncryptString("current-secret")); cmd.Parameters.AddWithValue("$oldPassword", crypto.EncryptString("previous-secret"));
                cmd.Parameters.AddWithValue("$notes", crypto.EncryptString("private notes")); cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o")); cmd.ExecuteNonQuery();
            }
            byte[] original = File.ReadAllBytes(path);
            using (var wrong = new SqliteStorageProvider(path, false))
            using (var wrongCrypto = new CryptoEngine(RandomNumberGenerator.GetBytes(32)))
                await Assert.ThrowsAsync<CryptographicException>(() => wrong.AuthenticateAsync(wrongCrypto));
            Assert.Equal(original, File.ReadAllBytes(path));
            using (var provider = new SqliteStorageProvider(path, false))
            {
                await provider.AuthenticateAsync(crypto);
                var loaded = Assert.Single(await provider.LoadEntriesAsync(null, crypto));
                Assert.Equal("current-secret", loaded.Password); Assert.Equal("ENC:legitimate-plaintext", loaded.UserName);
                Assert.Equal("previous-secret", Assert.Single(loaded.History).Password);
                Assert.Equal("https://history.invalid", loaded.History[0].Url);
                Assert.Equal(2, (await provider.LoadVaultMetadataAsync())!.FormatVersion);
                Assert.Equal(withKeyFile, (await provider.LoadVaultMetadataAsync())!.UsesKeyFile);
                string backup = Assert.Single(Directory.GetFiles(folder, "*.pre-v2-*.backup"));
                using var legacyBackup = new SqliteStorageProvider(backup, false);
                Assert.Equal(1, (await legacyBackup.LoadVaultMetadataAsync())!.FormatVersion);
            }
        }
        finally
        {
            if (file != null) CryptographicOperations.ZeroMemory(file);
            string resolved = Path.GetFullPath(folder);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("SQLitePass-migration-")) throw new InvalidOperationException("Invalid test cleanup path.");
            Directory.Delete(resolved, recursive: true);
        }
    }

    [Fact]
    public void StreamingKeyFileIsCompatibleWithTheOriginalCompositeKey()
    {
        string path = Path.Combine(Path.GetTempPath(), "SQLitePass-keyfile-" + Guid.NewGuid().ToString("N"));
        byte[] bytes = RandomNumberGenerator.GetBytes(2 * 1024 * 1024);
        try
        {
            File.WriteAllBytes(path, bytes);
            using var old = new MasterKey("synthetic", bytes); using var streamed = MasterKey.FromFile("synthetic", path);
            Assert.True(old.UsesKeyFile); Assert.True(streamed.UsesKeyFile);
            Assert.Equal(old.GetKeySeed(), streamed.GetKeySeed());
            using var oldFileOnly = new MasterKey("", bytes); using var streamedFileOnly = MasterKey.FromFile("", path);
            Assert.Equal(oldFileOnly.GetKeySeed(), streamedFileOnly.GetKeySeed());
        }
        finally { CryptographicOperations.ZeroMemory(bytes); File.Delete(path); }
    }
}
