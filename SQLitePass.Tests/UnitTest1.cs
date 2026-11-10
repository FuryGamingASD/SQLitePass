using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Database;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;
using Xunit;

namespace SQLitePass.Tests
{
    public class CoreCryptoTests
    {
        [Fact]
        public void TestMasterKeyAndArgon2KeyDerivation()
        {
            using var masterKey = new MasterKey("SuperSecretPassword123!");
            byte[] salt = new byte[16] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

            byte[] key1 = CryptoEngine.DeriveKeyArgon2(masterKey, salt, iterations: 2, memoryKb: 1024, parallelism: 1);
            byte[] key2 = CryptoEngine.DeriveKeyArgon2(masterKey, salt, iterations: 2, memoryKb: 1024, parallelism: 1);

            Assert.NotNull(key1);
            Assert.Equal(32, key1.Length);
            Assert.Equal(key1, key2);
        }

        [Fact]
        public void TestAesGcmStringEncryptionDecryption()
        {
            byte[] dummyKey = new byte[32];
            for (int i = 0; i < 32; i++) dummyKey[i] = (byte)i;

            using var engine = new CryptoEngine(dummyKey);

            string originalText = "MySecretBankPassword#2026";
            string encrypted = engine.EncryptString(originalText);

            Assert.False(string.IsNullOrEmpty(encrypted));
            Assert.NotEqual(originalText, encrypted);

            string decrypted = engine.DecryptString(encrypted);
            Assert.Equal(originalText, decrypted);
        }

        [Fact]
        public void TestCanaryVerificationToken()
        {
            byte[] keyA = new byte[32];
            byte[] keyB = new byte[32];
            keyA[0] = 1;
            keyB[0] = 2;

            using var engineA = new CryptoEngine(keyA);
            using var engineB = new CryptoEngine(keyB);

            string canaryA = engineA.GenerateCanaryToken();

            Assert.True(engineA.VerifyCanaryToken(canaryA));
            Assert.False(engineB.VerifyCanaryToken(canaryA));
        }

        [Fact]
        public void TestPasswordGenerator()
        {
            var options = new PasswordGeneratorOptions
            {
                Length = 24,
                IncludeUpperCase = true,
                IncludeLowerCase = true,
                IncludeDigits = true,
                IncludeSpecial = true,
                ExcludeLookAlike = true
            };

            string password = PasswordGenerator.Generate(options);

            Assert.Equal(24, password.Length);
            double entropy = PasswordGenerator.CalculateEntropy(password);
            Assert.True(entropy > 80.0);
        }

        [Fact]
        public void TestKeePassCsvExportImport()
        {
            var gid = Guid.NewGuid();
            var entries = new List<PwEntry>
            {
                new PwEntry { Id = Guid.NewGuid(), GroupId = gid, Title = "Google", UserName = "john@gmail.com", Password = "Pass123!\"test", Url = "https://google.com", Notes = "Some\nNotes" }
            };

            var groupMap = new Dictionary<Guid, string> { { gid, "Websites" } };

            string csv = ImportExportService.ExportToKeePassCsv(entries, groupMap);
            Assert.Contains("Google", csv);
            Assert.Contains("john@gmail.com", csv);

            var imported = ImportExportService.ImportFromKeePassCsv(csv, gid);
            Assert.Single(imported);
            Assert.Equal("Google", imported[0].Title);
            Assert.Equal("john@gmail.com", imported[0].UserName);
            Assert.Equal("Pass123!\"test", imported[0].Password);
        }

        [Fact]
        public async Task TestSqliteStorageAndDualFileBackup()
        {
            string testDbPath = Path.Combine(Path.GetTempPath(), $"test_vault_{Guid.NewGuid():N}.kpd");
            string backupPath = testDbPath + ".backup";

            try
            {
                var provider = new SqliteStorageProvider(testDbPath);
                await provider.InitializeDatabaseAndSchemaAsync();

                byte[] dummyKey = new byte[32];
                for (int i = 0; i < 32; i++) dummyKey[i] = (byte)i;
                using var crypto = new CryptoEngine(dummyKey);

                // Add a group
                var group = new PwGroup { Id = Guid.NewGuid(), Name = "Personal" };
                await provider.SaveGroupAsync(group, crypto);

                // Add an entry
                var entry = new PwEntry
                {
                    Id = Guid.NewGuid(),
                    GroupId = group.Id,
                    Title = "ProtonMail",
                    UserName = "test@pm.me",
                    Password = "MySecretEncryptedPassword!2026",
                    Url = "https://proton.me",
                    Notes = "Secret personal notes"
                };
                await provider.SaveEntryAsync(entry, crypto);

                // Verify entry was saved and decrypted correctly
                var loadedEntries = await provider.LoadEntriesAsync(group.Id, crypto);
                Assert.Single(loadedEntries);
                Assert.Equal("ProtonMail", loadedEntries[0].Title);
                Assert.Equal("test@pm.me", loadedEntries[0].UserName);
                Assert.Equal("MySecretEncryptedPassword!2026", loadedEntries[0].Password);
                Assert.Equal("Secret personal notes", loadedEntries[0].Notes);

                // Verify .backup file exists and was duplicated automatically!
                Assert.True(File.Exists(backupPath), "Backup file was not created!");
                Assert.True(new FileInfo(backupPath).Length > 0, "Backup file is empty!");
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(testDbPath)) File.Delete(testDbPath);
                if (File.Exists(backupPath)) File.Delete(backupPath);
            }
        }

        [Fact]
        public void TestEntryHistorySnapshot()
        {
            var entry = new PwEntry
            {
                Title = "Original Title",
                UserName = "user1",
                Password = "pwd1",
                Url = "https://original.com",
                Notes = "original notes"
            };

            var snapshot = entry.CreateHistorySnapshot();

            Assert.Equal(entry.Id, snapshot.EntryId);
            Assert.Equal("Original Title", snapshot.Title);
            Assert.Equal("user1", snapshot.UserName);
            Assert.Equal("pwd1", snapshot.Password);

            entry.Title = "Modified Title";
            Assert.Equal("Original Title", snapshot.Title);
        }
    }
}
