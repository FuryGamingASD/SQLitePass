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
        public async Task TestAutoTypeServiceExecution()
        {
            var entry = new PwEntry
            {
                Title = "Test",
                UserName = "user",
                Password = "pwd"
            };
            await AutoTypeService.ExecuteAutoTypeAsync(entry, IntPtr.Zero, 0);
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

        [Fact]
        public void TestBitwardenJsonImport()
        {
            string bitwardenJson = @"
            {
                ""encrypted"": false,
                ""items"": [
                    {
                        ""id"": ""e0a7f14b-6d12-4299-8d19-91345f09cb8d"",
                        ""type"": 1,
                        ""name"": ""GitHub Account"",
                        ""notes"": ""Development credentials"",
                        ""login"": {
                            ""username"": ""octocat"",
                            ""password"": ""SuperPass#2026!"",
                            ""totp"": ""JBSWY3DPEHPK3PXP"",
                            ""uris"": [
                                { ""match"": null, ""uri"": ""https://github.com/login"" }
                            ]
                        },
                        ""fields"": [
                            { ""name"": ""RecoveryCode"", ""value"": ""1234-5678"", ""type"": 1 }
                        ]
                    }
                ]
            }";

            var defaultGid = Guid.NewGuid();
            var imported = ImportExportService.ImportFromBitwardenJson(bitwardenJson, defaultGid);

            Assert.Single(imported);
            var item = imported[0];
            Assert.Equal("GitHub Account", item.Title);
            Assert.Equal("octocat", item.UserName);
            Assert.Equal("SuperPass#2026!", item.Password);
            Assert.Equal("https://github.com/login", item.Url);
            Assert.Contains("Development credentials", item.Notes);
            Assert.Contains("TOTP: JBSWY3DPEHPK3PXP", item.Notes);
            Assert.Single(item.CustomFields);
            Assert.Equal("RecoveryCode", item.CustomFields[0].Key);
            Assert.Equal("1234-5678", item.CustomFields[0].Value);
            Assert.True(item.CustomFields[0].IsProtected);
        }

        [Fact]
        public void TestBrowserCsvImport()
        {
            // Test Chrome/Edge format
            string chromeCsv = "name,url,username,password,note\nNetflix,https://netflix.com,user@mail.com,NetPass99!,Family account";
            var gid = Guid.NewGuid();
            var importedChrome = ImportExportService.ImportFromBrowserCsv(chromeCsv, gid);

            Assert.Single(importedChrome);
            Assert.Equal("Netflix", importedChrome[0].Title);
            Assert.Equal("https://netflix.com", importedChrome[0].Url);
            Assert.Equal("user@mail.com", importedChrome[0].UserName);
            Assert.Equal("NetPass99!", importedChrome[0].Password);
            Assert.Equal("Family account", importedChrome[0].Notes);

            // Test Firefox format
            string ffCsv = "\"url\",\"username\",\"password\",\"httpRealm\",\"formActionOrigin\",\"guid\",\"timeCreated\",\"timeLastUsed\",\"timePasswordChanged\"\n\"https://amazon.com\",\"buyer@mail.com\",\"AmzSecret!\",\"\",\"\",\"123\",\"1600000000\",\"1600000000\",\"1600000000\"";
            var importedFf = ImportExportService.ImportFromBrowserCsv(ffCsv, gid);

            Assert.Single(importedFf);
            Assert.Equal("amazon.com", importedFf[0].Title);
            Assert.Equal("https://amazon.com", importedFf[0].Url);
            Assert.Equal("buyer@mail.com", importedFf[0].UserName);
            Assert.Equal("AmzSecret!", importedFf[0].Password);
        }

        [Fact]
        public async Task TestBatchSaveEntriesAndIntegrityCheck()
        {
            string testDbPath = Path.Combine(Path.GetTempPath(), $"batch_test_{Guid.NewGuid():N}.kpd");
            string backupPath = testDbPath + ".backup";

            try
            {
                var provider = new SqliteStorageProvider(testDbPath);
                await provider.InitializeDatabaseAndSchemaAsync();

                // Check integrity on new DB
                bool ok = await provider.CheckDatabaseIntegrityAsync();
                Assert.True(ok, "Database integrity check should be OK");

                byte[] key = new byte[32];
                for (int i = 0; i < 32; i++) key[i] = (byte)i;
                using var crypto = new CryptoEngine(key);

                var group = new PwGroup { Id = Guid.NewGuid(), Name = "Batch Test Group" };
                await provider.SaveGroupAsync(group, crypto);

                var entries = new List<PwEntry>();
                for (int i = 1; i <= 50; i++)
                {
                    entries.Add(new PwEntry
                    {
                        Id = Guid.NewGuid(),
                        GroupId = group.Id,
                        Title = $"Item {i}",
                        UserName = $"user_{i}",
                        Password = $"pass_{i}_{Guid.NewGuid()}",
                        Url = $"https://site{i}.org"
                    });
                }

                // Batch save all 50 entries in one transaction
                await provider.SaveEntriesBatchAsync(entries, crypto);

                var loaded = await provider.LoadEntriesAsync(null, crypto);
                Assert.Equal(50, loaded.Count);

                // Verify backup was updated atomically
                Assert.True(File.Exists(backupPath));
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(testDbPath)) File.Delete(testDbPath);
                if (File.Exists(backupPath)) File.Delete(backupPath);
            }
        }

        [Fact]
        public void TestAppLoggerOutput()
        {
            AppLogger.LogInfo("Unit test log info entry");
            AppLogger.LogWarning("Unit test warning");
            AppLogger.LogError("Unit test error", new InvalidOperationException("Test exception"));

            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SQLitePass",
                "logs"
            );
            string logFile = Path.Combine(logDir, "sqlitepass.log");

            Assert.True(File.Exists(logFile));
            string content = File.ReadAllText(logFile);
            Assert.Contains("Unit test log info entry", content);
            Assert.Contains("Unit test warning", content);
            Assert.Contains("Test exception", content);
        }

        [Fact]
        public async Task TestGroupHierarchyAndReordering()
        {
            string testDbPath = Path.Combine(Path.GetTempPath(), $"group_test_{Guid.NewGuid():N}.kpd");
            try
            {
                var provider = new SqliteStorageProvider(testDbPath);
                await provider.InitializeDatabaseAndSchemaAsync();

                byte[] key = new byte[32];
                for (int i = 0; i < 32; i++) key[i] = (byte)i;
                using var crypto = new CryptoEngine(key);

                var root = new PwGroup { Id = Guid.NewGuid(), Name = "Root Vault", SortOrder = 0 };
                var groupA = new PwGroup { Id = Guid.NewGuid(), ParentId = root.Id, Name = "Folder A", SortOrder = 0 };
                var groupB = new PwGroup { Id = Guid.NewGuid(), ParentId = root.Id, Name = "Folder B", SortOrder = 1 };

                await provider.SaveGroupAsync(root, crypto);
                await provider.SaveGroupAsync(groupA, crypto);
                await provider.SaveGroupAsync(groupB, crypto);

                var loaded = await provider.LoadAllGroupsAsync(crypto);
                Assert.Equal(3, loaded.Count);

                // Move Group B inside Group A
                groupB.ParentId = groupA.Id;
                groupB.SortOrder = 0;
                await provider.SaveGroupAsync(groupB, crypto);

                var reloaded = await provider.LoadAllGroupsAsync(crypto);
                var reloadedB = reloaded.Find(g => g.Id == groupB.Id);
                Assert.NotNull(reloadedB);
                Assert.Equal(groupA.Id, reloadedB.ParentId);

                // Reorder groups using SaveGroupsBatchAsync
                groupA.SortOrder = 10;
                await provider.SaveGroupsBatchAsync(new[] { groupA, groupB }, crypto);

                var batchLoaded = await provider.LoadAllGroupsAsync(crypto);
                Assert.Equal(10, batchLoaded.Find(g => g.Id == groupA.Id)?.SortOrder);
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(testDbPath)) File.Delete(testDbPath);
                if (File.Exists(testDbPath + ".backup")) File.Delete(testDbPath + ".backup");
            }
        }
    }
}
