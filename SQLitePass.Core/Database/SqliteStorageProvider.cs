using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Models;

namespace SQLitePass.Core.Database
{
    public class SqliteStorageProvider
    {
        public string FilePath { get; }
        public string BackupFilePath => FilePath + ".backup";
        private readonly string _connectionString;

        public SqliteStorageProvider(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Database file path cannot be empty.", nameof(filePath));

            FilePath = Path.GetFullPath(filePath);
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = FilePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();
        }

        private SqliteConnection CreateOpenConnection()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();

            // Set crash-resilience pragmas: Write-Ahead Logging (WAL)
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                PRAGMA foreign_keys = ON;
            ";
            cmd.ExecuteNonQuery();

            return conn;
        }

        public async Task InitializeDatabaseAndSchemaAsync()
        {
            string dir = Path.GetDirectoryName(FilePath)!;
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var conn = CreateOpenConnection();

            string ddl = @"
            CREATE TABLE IF NOT EXISTS kp_vault_metadata (
                vault_id TEXT PRIMARY KEY,
                vault_name TEXT NOT NULL,
                salt_base64 TEXT NOT NULL,
                kdf_iterations INTEGER NOT NULL DEFAULT 3,
                kdf_memory_kb INTEGER NOT NULL DEFAULT 65536,
                kdf_parallelism INTEGER NOT NULL DEFAULT 2,
                verification_canary TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS kp_groups (
                id TEXT PRIMARY KEY,
                parent_id TEXT NULL,
                name TEXT NOT NULL,
                icon_index INTEGER NOT NULL DEFAULT 48,
                is_expanded INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS kp_entries (
                id TEXT PRIMARY KEY,
                group_id TEXT NOT NULL,
                title TEXT NOT NULL,
                username TEXT NOT NULL,
                password_enc TEXT NOT NULL,
                url TEXT NOT NULL,
                notes_enc TEXT NOT NULL,
                tags TEXT NULL,
                icon_index INTEGER NOT NULL DEFAULT 0,
                expiry_date TEXT NULL,
                autotype_enabled INTEGER NOT NULL DEFAULT 1,
                autotype_seq TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                last_access_at TEXT NOT NULL,
                FOREIGN KEY (group_id) REFERENCES kp_groups(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS kp_custom_fields (
                id TEXT PRIMARY KEY,
                entry_id TEXT NOT NULL,
                field_key TEXT NOT NULL,
                field_val_enc TEXT NOT NULL,
                is_protected INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (entry_id) REFERENCES kp_entries(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS kp_attachments (
                id TEXT PRIMARY KEY,
                entry_id TEXT NOT NULL,
                filename TEXT NOT NULL,
                filesize INTEGER NOT NULL,
                data_enc BLOB NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY (entry_id) REFERENCES kp_entries(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS kp_history (
                id TEXT PRIMARY KEY,
                entry_id TEXT NOT NULL,
                title TEXT NOT NULL,
                username TEXT NOT NULL,
                password_enc TEXT NOT NULL,
                url TEXT NOT NULL,
                notes_enc TEXT NOT NULL,
                saved_at TEXT NOT NULL,
                FOREIGN KEY (entry_id) REFERENCES kp_entries(id) ON DELETE CASCADE
            );
            ";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = ddl;
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public async Task<bool> VaultExistsAsync()
        {
            if (!File.Exists(FilePath)) return false;

            try
            {
                using var conn = CreateOpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM kp_vault_metadata;";
                var count = Convert.ToInt64(await cmd.ExecuteScalarAsync().ConfigureAwait(false));
                return count > 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<VaultMetadata?> LoadVaultMetadataAsync()
        {
            using var conn = CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT vault_id, vault_name, salt_base64, kdf_iterations, kdf_memory_kb, kdf_parallelism, verification_canary, created_at, updated_at FROM kp_vault_metadata LIMIT 1;";

            using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            if (await reader.ReadAsync().ConfigureAwait(false))
            {
                return new VaultMetadata
                {
                    VaultId = Guid.Parse(reader.GetString(0)),
                    VaultName = reader.GetString(1),
                    SaltBase64 = reader.GetString(2),
                    KdfIterations = reader.GetInt32(3),
                    KdfMemoryKb = reader.GetInt32(4),
                    KdfParallelism = reader.GetInt32(5),
                    VerificationCanary = reader.GetString(6),
                    CreatedAt = DateTime.Parse(reader.GetString(7)),
                    UpdatedAt = DateTime.Parse(reader.GetString(8))
                };
            }

            return null;
        }

        public async Task SaveVaultMetadataAsync(VaultMetadata metadata)
        {
            using var conn = CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO kp_vault_metadata (vault_id, vault_name, salt_base64, kdf_iterations, kdf_memory_kb, kdf_parallelism, verification_canary, created_at, updated_at)
                VALUES (@id, @name, @salt, @iters, @mem, @par, @canary, @created, @updated)
                ON CONFLICT(vault_id) DO UPDATE SET
                    vault_name = excluded.vault_name,
                    salt_base64 = excluded.salt_base64,
                    kdf_iterations = excluded.kdf_iterations,
                    kdf_memory_kb = excluded.kdf_memory_kb,
                    kdf_parallelism = excluded.kdf_parallelism,
                    verification_canary = excluded.verification_canary,
                    updated_at = excluded.updated_at;
            ";

            cmd.Parameters.AddWithValue("@id", metadata.VaultId.ToString());
            cmd.Parameters.AddWithValue("@name", metadata.VaultName);
            cmd.Parameters.AddWithValue("@salt", metadata.SaltBase64);
            cmd.Parameters.AddWithValue("@iters", metadata.KdfIterations);
            cmd.Parameters.AddWithValue("@mem", metadata.KdfMemoryKb);
            cmd.Parameters.AddWithValue("@par", metadata.KdfParallelism);
            cmd.Parameters.AddWithValue("@canary", metadata.VerificationCanary);
            cmd.Parameters.AddWithValue("@created", metadata.CreatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("@updated", metadata.UpdatedAt.ToString("o"));

            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            TriggerAtomicBackup();
        }

        public async Task<List<PwGroup>> LoadAllGroupsAsync(CryptoEngine crypto)
        {
            var groups = new List<PwGroup>();

            using var conn = CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, parent_id, name, icon_index, is_expanded, created_at, updated_at FROM kp_groups ORDER BY created_at ASC;";

            using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                string idStr = reader.GetString(0);
                string? parentIdStr = reader.IsDBNull(1) ? null : reader.GetString(1);
                string name = reader.GetString(2);
                int iconIndex = reader.GetInt32(3);
                bool isExpanded = reader.GetInt32(4) == 1;
                DateTime createdAt = DateTime.Parse(reader.GetString(5));
                DateTime updatedAt = DateTime.Parse(reader.GetString(6));

                groups.Add(new PwGroup
                {
                    Id = Guid.Parse(idStr),
                    ParentId = parentIdStr != null ? Guid.Parse(parentIdStr) : null,
                    Name = name,
                    IconIndex = iconIndex,
                    IsExpanded = isExpanded,
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt
                });
            }

            return groups;
        }

        public async Task SaveGroupAsync(PwGroup group, CryptoEngine crypto)
        {
            using var conn = CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO kp_groups (id, parent_id, name, icon_index, is_expanded, created_at, updated_at)
                VALUES (@id, @parent, @name, @icon, @expanded, @created, @updated)
                ON CONFLICT(id) DO UPDATE SET
                    parent_id = excluded.parent_id,
                    name = excluded.name,
                    icon_index = excluded.icon_index,
                    is_expanded = excluded.is_expanded,
                    updated_at = excluded.updated_at;
            ";

            cmd.Parameters.AddWithValue("@id", group.Id.ToString());
            cmd.Parameters.AddWithValue("@parent", group.ParentId.HasValue ? group.ParentId.Value.ToString() : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@name", group.Name);
            cmd.Parameters.AddWithValue("@icon", group.IconIndex);
            cmd.Parameters.AddWithValue("@expanded", group.IsExpanded ? 1 : 0);
            cmd.Parameters.AddWithValue("@created", group.CreatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("@updated", DateTime.UtcNow.ToString("o"));

            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            TriggerAtomicBackup();
        }

        public async Task DeleteGroupAsync(Guid groupId)
        {
            using var conn = CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM kp_groups WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", groupId.ToString());

            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            TriggerAtomicBackup();
        }

        public async Task<List<PwEntry>> LoadEntriesAsync(Guid? groupId, CryptoEngine crypto)
        {
            var entries = new List<PwEntry>();

            using var conn = CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            if (groupId.HasValue)
            {
                cmd.CommandText = "SELECT id, group_id, title, username, password_enc, url, notes_enc, tags, icon_index, expiry_date, autotype_enabled, autotype_seq, created_at, updated_at, last_access_at FROM kp_entries WHERE group_id = @gid;";
                cmd.Parameters.AddWithValue("@gid", groupId.Value.ToString());
            }
            else
            {
                cmd.CommandText = "SELECT id, group_id, title, username, password_enc, url, notes_enc, tags, icon_index, expiry_date, autotype_enabled, autotype_seq, created_at, updated_at, last_access_at FROM kp_entries;";
            }

            using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
            {
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    string passEnc = reader.GetString(4);
                    string notesEnc = reader.GetString(6);

                    string decryptedPass = "";
                    string decryptedNotes = "";

                    try { decryptedPass = crypto.DecryptString(passEnc); } catch { decryptedPass = "[Decryption Error]"; }
                    try { decryptedNotes = crypto.DecryptString(notesEnc); } catch { decryptedNotes = "[Decryption Error]"; }

                    var entry = new PwEntry
                    {
                        Id = Guid.Parse(reader.GetString(0)),
                        GroupId = Guid.Parse(reader.GetString(1)),
                        Title = reader.GetString(2),
                        UserName = reader.GetString(3),
                        Password = decryptedPass,
                        Url = reader.GetString(5),
                        Notes = decryptedNotes,
                        Tags = reader.IsDBNull(7) ? "" : reader.GetString(7),
                        IconIndex = reader.GetInt32(8),
                        ExpiryDate = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9)),
                        AutoTypeEnabled = reader.GetInt32(10) == 1,
                        AutoTypeSequence = reader.IsDBNull(11) ? "{USERNAME}{TAB}{PASSWORD}{ENTER}" : reader.GetString(11),
                        CreatedAt = DateTime.Parse(reader.GetString(12)),
                        UpdatedAt = DateTime.Parse(reader.GetString(13)),
                        LastAccessAt = DateTime.Parse(reader.GetString(14))
                    };
                    entries.Add(entry);
                }
            }

            // Load details (custom fields, history, attachments)
            foreach (var entry in entries)
            {
                await LoadEntryDetailsAsync(conn, entry, crypto).ConfigureAwait(false);
            }

            return entries;
        }

        private static async Task LoadEntryDetailsAsync(SqliteConnection conn, PwEntry entry, CryptoEngine crypto)
        {
            // Custom fields
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, field_key, field_val_enc, is_protected FROM kp_custom_fields WHERE entry_id = @eid;";
                cmd.Parameters.AddWithValue("@eid", entry.Id.ToString());
                using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    string encVal = reader.GetString(2);
                    string decVal = "";
                    try { decVal = crypto.DecryptString(encVal); } catch { decVal = "[Decryption Error]"; }

                    entry.CustomFields.Add(new PwCustomField
                    {
                        Id = Guid.Parse(reader.GetString(0)),
                        EntryId = entry.Id,
                        Key = reader.GetString(1),
                        Value = decVal,
                        IsProtected = reader.GetInt32(3) == 1
                    });
                }
            }

            // History
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, title, username, password_enc, url, notes_enc, saved_at FROM kp_history WHERE entry_id = @eid ORDER BY saved_at DESC;";
                cmd.Parameters.AddWithValue("@eid", entry.Id.ToString());
                using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    string pass = "";
                    string notes = "";
                    try { pass = crypto.DecryptString(reader.GetString(3)); } catch { pass = "[Error]"; }
                    try { notes = crypto.DecryptString(reader.GetString(5)); } catch { notes = "[Error]"; }

                    entry.History.Add(new PwEntryHistory
                    {
                        Id = Guid.Parse(reader.GetString(0)),
                        EntryId = entry.Id,
                        Title = reader.GetString(1),
                        UserName = reader.GetString(2),
                        Password = pass,
                        Url = reader.GetString(4),
                        Notes = notes,
                        SavedAt = DateTime.Parse(reader.GetString(6))
                    });
                }
            }

            // Attachments
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, filename, filesize, data_enc, created_at FROM kp_attachments WHERE entry_id = @eid;";
                cmd.Parameters.AddWithValue("@eid", entry.Id.ToString());
                using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    byte[] encBytes = (byte[])reader[3];
                    byte[] decBytes = Array.Empty<byte>();
                    try { decBytes = crypto.DecryptBytes(encBytes); } catch { }

                    entry.Attachments.Add(new PwAttachment
                    {
                        Id = Guid.Parse(reader.GetString(0)),
                        EntryId = entry.Id,
                        FileName = reader.GetString(1),
                        FileSize = reader.GetInt64(2),
                        Data = decBytes,
                        CreatedAt = DateTime.Parse(reader.GetString(4))
                    });
                }
            }
        }

        public async Task SaveEntryAsync(PwEntry entry, CryptoEngine crypto)
        {
            using var conn = CreateOpenConnection();
            using var transaction = conn.BeginTransaction();

            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO kp_entries (id, group_id, title, username, password_enc, url, notes_enc, tags, icon_index, expiry_date, autotype_enabled, autotype_seq, created_at, updated_at, last_access_at)
                        VALUES (@id, @gid, @title, @user, @pass, @url, @notes, @tags, @icon, @expiry, @at_enabled, @at_seq, @created, @updated, @access)
                        ON CONFLICT(id) DO UPDATE SET
                            group_id = excluded.group_id,
                            title = excluded.title,
                            username = excluded.username,
                            password_enc = excluded.password_enc,
                            url = excluded.url,
                            notes_enc = excluded.notes_enc,
                            tags = excluded.tags,
                            icon_index = excluded.icon_index,
                            expiry_date = excluded.expiry_date,
                            autotype_enabled = excluded.autotype_enabled,
                            autotype_seq = excluded.autotype_seq,
                            updated_at = excluded.updated_at,
                            last_access_at = excluded.last_access_at;
                    ";

                    cmd.Parameters.AddWithValue("@id", entry.Id.ToString());
                    cmd.Parameters.AddWithValue("@gid", entry.GroupId.ToString());
                    cmd.Parameters.AddWithValue("@title", entry.Title);
                    cmd.Parameters.AddWithValue("@user", entry.UserName);
                    cmd.Parameters.AddWithValue("@pass", crypto.EncryptString(entry.Password));
                    cmd.Parameters.AddWithValue("@url", entry.Url);
                    cmd.Parameters.AddWithValue("@notes", crypto.EncryptString(entry.Notes));
                    cmd.Parameters.AddWithValue("@tags", entry.Tags ?? "");
                    cmd.Parameters.AddWithValue("@icon", entry.IconIndex);
                    cmd.Parameters.AddWithValue("@expiry", entry.ExpiryDate.HasValue ? entry.ExpiryDate.Value.ToString("o") : (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@at_enabled", entry.AutoTypeEnabled ? 1 : 0);
                    cmd.Parameters.AddWithValue("@at_seq", entry.AutoTypeSequence ?? "{USERNAME}{TAB}{PASSWORD}{ENTER}");
                    cmd.Parameters.AddWithValue("@created", entry.CreatedAt.ToString("o"));
                    cmd.Parameters.AddWithValue("@updated", DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("@access", entry.LastAccessAt.ToString("o"));

                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                // Custom fields
                using (var delCmd = conn.CreateCommand())
                {
                    delCmd.Transaction = transaction;
                    delCmd.CommandText = "DELETE FROM kp_custom_fields WHERE entry_id = @eid;";
                    delCmd.Parameters.AddWithValue("@eid", entry.Id.ToString());
                    await delCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                foreach (var field in entry.CustomFields)
                {
                    using var fCmd = conn.CreateCommand();
                    fCmd.Transaction = transaction;
                    fCmd.CommandText = @"INSERT INTO kp_custom_fields (id, entry_id, field_key, field_val_enc, is_protected)
                                         VALUES (@id, @eid, @key, @val, @prot);";
                    fCmd.Parameters.AddWithValue("@id", field.Id.ToString());
                    fCmd.Parameters.AddWithValue("@eid", entry.Id.ToString());
                    fCmd.Parameters.AddWithValue("@key", field.Key);
                    fCmd.Parameters.AddWithValue("@val", crypto.EncryptString(field.Value));
                    fCmd.Parameters.AddWithValue("@prot", field.IsProtected ? 1 : 0);
                    await fCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                // Attachments
                using (var delAttach = conn.CreateCommand())
                {
                    delAttach.Transaction = transaction;
                    delAttach.CommandText = "DELETE FROM kp_attachments WHERE entry_id = @eid;";
                    delAttach.Parameters.AddWithValue("@eid", entry.Id.ToString());
                    await delAttach.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                foreach (var attach in entry.Attachments)
                {
                    using var aCmd = conn.CreateCommand();
                    aCmd.Transaction = transaction;
                    aCmd.CommandText = @"INSERT INTO kp_attachments (id, entry_id, filename, filesize, data_enc, created_at)
                                         VALUES (@id, @eid, @name, @size, @data, @created);";
                    aCmd.Parameters.AddWithValue("@id", attach.Id.ToString());
                    aCmd.Parameters.AddWithValue("@eid", entry.Id.ToString());
                    aCmd.Parameters.AddWithValue("@name", attach.FileName);
                    aCmd.Parameters.AddWithValue("@size", attach.Data?.Length ?? 0);
                    aCmd.Parameters.AddWithValue("@data", crypto.EncryptBytes(attach.Data));
                    aCmd.Parameters.AddWithValue("@created", attach.CreatedAt.ToString("o"));
                    await aCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                // History snapshots
                foreach (var hist in entry.History)
                {
                    using var hCmd = conn.CreateCommand();
                    hCmd.Transaction = transaction;
                    hCmd.CommandText = @"INSERT OR IGNORE INTO kp_history (id, entry_id, title, username, password_enc, url, notes_enc, saved_at)
                                         VALUES (@id, @eid, @title, @user, @pass, @url, @notes, @saved);";
                    hCmd.Parameters.AddWithValue("@id", hist.Id.ToString());
                    hCmd.Parameters.AddWithValue("@eid", entry.Id.ToString());
                    hCmd.Parameters.AddWithValue("@title", hist.Title);
                    hCmd.Parameters.AddWithValue("@user", hist.UserName);
                    hCmd.Parameters.AddWithValue("@pass", crypto.EncryptString(hist.Password));
                    hCmd.Parameters.AddWithValue("@url", hist.Url);
                    hCmd.Parameters.AddWithValue("@notes", crypto.EncryptString(hist.Notes));
                    hCmd.Parameters.AddWithValue("@saved", hist.SavedAt.ToString("o"));
                    await hCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            TriggerAtomicBackup();
        }

        public async Task DeleteEntryAsync(Guid entryId)
        {
            using var conn = CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM kp_entries WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", entryId.ToString());

            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            TriggerAtomicBackup();
        }

        /// <summary>
        /// Executes an isolated, atomic snapshot to the .backup file using SQLite's native Online Backup API.
        /// Leaves the backup file completely closed on disk, immune to concurrent corruption.
        /// </summary>
        public void TriggerAtomicBackup()
        {
            try
            {
                string tempBackupPath = BackupFilePath + ".tmp";
                string destConnStr = new SqliteConnectionStringBuilder
                {
                    DataSource = tempBackupPath,
                    Pooling = false
                }.ToString();

                // 1. Perform SQLite online backup into temporary file
                using (var source = new SqliteConnection(_connectionString))
                using (var dest = new SqliteConnection(destConnStr))
                {
                    source.Open();
                    dest.Open();
                    source.BackupDatabase(dest);
                }

                // 2. Atomically replace the target backup file
                if (File.Exists(tempBackupPath))
                {
                    if (File.Exists(BackupFilePath))
                    {
                        File.Replace(tempBackupPath, BackupFilePath, null);
                    }
                    else
                    {
                        File.Move(tempBackupPath, BackupFilePath);
                    }

                    // 3. Optional copy to secondary backup directory (USB drive, secondary disk)
                    try
                    {
                        var settings = Services.AppSettingsManager.Load();
                        if (!string.IsNullOrWhiteSpace(settings.SecondaryBackupDir) && Directory.Exists(settings.SecondaryBackupDir))
                        {
                            string secDest = Path.Combine(settings.SecondaryBackupDir, Path.GetFileName(BackupFilePath));
                            File.Copy(BackupFilePath, secDest, overwrite: true);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Atomic backup failed: {ex.Message}");
            }
        }
    }
}
