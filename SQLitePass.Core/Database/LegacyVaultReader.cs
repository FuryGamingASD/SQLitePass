using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Models;

namespace SQLitePass.Core.Database;

// A yielded entry is owned by this iterator and is cleared on advance/disposal.
internal static class LegacyVaultReader
{
    private const long MaximumRecordBytes = 64L * 1024 * 1024;
    internal sealed record MigrationPlan(int EntryCount, HashSet<string> NonCanonicalOwners);

    private static IEnumerable<Dictionary<string, object>> Rows(SqliteConnection connection, string table,
        SqliteTransaction? transaction = null, Guid? owner = null, bool nonCanonicalOwner = false)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT * FROM {table}" + (owner.HasValue && !nonCanonicalOwner ? " WHERE entry_id=$entry" : "") + " ORDER BY rowid";
        if (owner.HasValue && !nonCanonicalOwner) command.Parameters.AddWithValue("$entry", owner.Value.ToString());
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (owner.HasValue && nonCanonicalOwner && Guid.Parse(reader.GetString(reader.GetOrdinal("entry_id"))) != owner) continue;
            var row = new Dictionary<string, object>();
            for (int index = 0; index < reader.FieldCount; index++) row[reader.GetName(index)] = reader.GetValue(index);
            try { yield return row; }
            finally { row.Clear(); }
        }
    }

    private static string S(Dictionary<string, object> row, string field, string fallback = "") =>
        row.TryGetValue(field, out var value) && value != DBNull.Value ? Convert.ToString(value, CultureInfo.InvariantCulture)! : fallback;
    private static int N(Dictionary<string, object> row, string field, int fallback = 0) =>
        int.Parse(S(row, field, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
    private static DateTime D(Dictionary<string, object> row, string field) =>
        DateTime.Parse(S(row, field), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    internal static VaultMetadata? Metadata(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var rows = Rows(connection, "kp_vault_metadata", transaction).GetEnumerator();
        if (!rows.MoveNext()) return null;
        var row = rows.Current;
        var metadata = new VaultMetadata
        {
            VaultId = Guid.Parse(S(row, "vault_id")), VaultName = S(row, "vault_name"), SaltBase64 = S(row, "salt_base64"),
            VerificationCanary = S(row, "verification_canary"), KdfIterations = N(row, "kdf_iterations", 3),
            KdfMemoryKb = N(row, "kdf_memory_kb", 65536), KdfParallelism = N(row, "kdf_parallelism", 2),
            EncryptUsernameUrl = N(row, "encrypt_username_url") == 1, CreatedAt = D(row, "created_at"), UpdatedAt = D(row, "updated_at"), FormatVersion = 1
        };
        if (rows.MoveNext()) throw new InvalidDataException("Il file contiene più vault mescolati. Usare una copia integra.");
        return metadata;
    }

    internal static List<PwGroup> Groups(SqliteConnection connection, SqliteTransaction transaction) =>
        Rows(connection, "kp_groups", transaction).Select(row => new PwGroup
        {
            Id = Guid.Parse(S(row, "id")), ParentId = Guid.TryParse(S(row, "parent_id"), out var parent) ? parent : null,
            Name = S(row, "name"), IconIndex = N(row, "icon_index", 48), SortOrder = N(row, "sort_order"), IsExpanded = N(row, "is_expanded", 1) == 1,
            CreatedAt = D(row, "created_at"), UpdatedAt = D(row, "updated_at")
        }).ToList();

    internal static MigrationPlan ValidateReferences(SqliteConnection connection, SqliteTransaction transaction)
    {
        var identities = new HashSet<Guid>();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction; command.CommandText = "SELECT id FROM kp_entries";
            using var reader = command.ExecuteReader();
            while (reader.Read()) if (!identities.Add(Guid.Parse(reader.GetString(0)))) throw new InvalidDataException("Identità legacy duplicate.");
        }
        var nonCanonical = new HashSet<string>();
        foreach (string table in new[] { "kp_custom_fields", "kp_attachments", "kp_history" })
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = $"SELECT entry_id FROM {table}";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string raw = reader.GetString(0); Guid id = Guid.Parse(raw);
                if (!identities.Contains(id)) throw new InvalidDataException("Record orfano.");
                if (raw != id.ToString()) nonCanonical.Add(table);
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT 1 FROM kp_attachments WHERE length(data_enc)>$limit LIMIT 1";
            command.Parameters.AddWithValue("$limit", MaximumRecordBytes + 28);
            if (command.ExecuteScalar() != null) throw new InvalidDataException("Allegato legacy oltre il limite del formato supportato.");
        }
        return new(identities.Count, nonCanonical);
    }

    private static int HistoryCount(SqliteConnection connection, SqliteTransaction transaction, Guid entryId, bool nonCanonical)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        if (!nonCanonical)
        {
            command.CommandText = "SELECT COUNT(*) FROM kp_history WHERE entry_id=$id"; command.Parameters.AddWithValue("$id", entryId.ToString());
            return checked(Convert.ToInt32(command.ExecuteScalar()));
        }
        command.CommandText = "SELECT entry_id FROM kp_history";
        using var reader = command.ExecuteReader(); int count = 0;
        while (reader.Read()) if (Guid.Parse(reader.GetString(0)) == entryId) count = checked(count + 1);
        return count;
    }

    internal static IEnumerable<PwEntry> Entries(SqliteConnection connection, CryptoEngine key, bool encryptedUsernameUrl,
        SqliteTransaction transaction, MigrationPlan plan)
    {
        string Secret(Dictionary<string, object> row, string field) => key.DecryptString(S(row, field));
        string Optional(Dictionary<string, object> row, string field)
        {
            string value = S(row, field);
            return encryptedUsernameUrl && value.StartsWith("ENC:", StringComparison.Ordinal) ? key.DecryptString(value[4..]) : value;
        }
        foreach (var row in Rows(connection, "kp_entries", transaction))
        {
            var entry = new PwEntry();
            try
            {
                entry.Id = Guid.Parse(S(row, "id")); entry.GroupId = Guid.Parse(S(row, "group_id")); entry.Title = S(row, "title");
                entry.UserName = Optional(row, "username"); entry.Password = Secret(row, "password_enc"); entry.Url = Optional(row, "url"); entry.Notes = Secret(row, "notes_enc");
                entry.Tags = S(row, "tags"); entry.IconIndex = N(row, "icon_index"); entry.AutoTypeEnabled = N(row, "autotype_enabled", 1) == 1;
                entry.AutoTypeSequence = S(row, "autotype_seq", "{USERNAME}{TAB}{PASSWORD}{ENTER}");
                entry.ExpiryDate = string.IsNullOrEmpty(S(row, "expiry_date")) ? null : D(row, "expiry_date");
                entry.CreatedAt = D(row, "created_at"); entry.UpdatedAt = D(row, "updated_at"); entry.LastAccessAt = D(row, "last_access_at");
                long minimumBytes = 256 + new[] { entry.Title, entry.UserName, entry.Password, entry.Url, entry.Notes, entry.Tags, entry.AutoTypeSequence }.Sum(value => (long)Encoding.UTF8.GetByteCount(value));
                void AddBudget(long bytes)
                {
                    minimumBytes = checked(minimumBytes + bytes);
                    if (minimumBytes > MaximumRecordBytes) throw new InvalidDataException("La voce legacy supera il limite del formato supportato.");
                }
                AddBudget(0);
                foreach (var child in Rows(connection, "kp_custom_fields", transaction, entry.Id, plan.NonCanonicalOwners.Contains("kp_custom_fields")))
                {
                    var field = new PwCustomField { Id = Guid.Parse(S(child, "id")), EntryId = entry.Id, Key = S(child, "field_key"), Value = Secret(child, "field_val_enc"), IsProtected = N(child, "is_protected") == 1 };
                    try { AddBudget(96L + Encoding.UTF8.GetByteCount(field.Key) + Encoding.UTF8.GetByteCount(field.Value)); entry.CustomFields.Add(field); }
                    catch { field.Value = ""; throw; }
                }
                foreach (var child in Rows(connection, "kp_attachments", transaction, entry.Id, plan.NonCanonicalOwners.Contains("kp_attachments")))
                {
                    byte[] bytes = key.DecryptBytes((byte[])child["data_enc"]);
                    try
                    {
                        AddBudget(96L + 4L * ((bytes.Length + 2L) / 3) + Encoding.UTF8.GetByteCount(S(child, "filename")));
                        entry.Attachments.Add(new PwAttachment { Id = Guid.Parse(S(child, "id")), EntryId = entry.Id, FileName = S(child, "filename"), Data = bytes, FileSize = Convert.ToInt64(child["filesize"]), CreatedAt = D(child, "created_at") });
                    }
                    catch { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); throw; }
                }
                bool nonCanonicalHistory = plan.NonCanonicalOwners.Contains("kp_history");
                int skip = Math.Max(0, HistoryCount(connection, transaction, entry.Id, nonCanonicalHistory) - 50), index = 0;
                foreach (var child in Rows(connection, "kp_history", transaction, entry.Id, nonCanonicalHistory))
                {
                    var history = new PwEntryHistory
                    {
                        Id = Guid.Parse(S(child, "id")), EntryId = entry.Id, Title = S(child, "title"), UserName = Optional(child, "username"),
                        Password = Secret(child, "password_enc"), Url = S(child, "url"), Notes = Secret(child, "notes_enc"), SavedAt = D(child, "saved_at")
                    };
                    try
                    {
                        // Validate discarded revisions too; retain the same last 50 as the writer.
                        if (index++ < skip) continue;
                        AddBudget(128L + new[] { history.Title, history.UserName, history.Password, history.Url, history.Notes }.Sum(value => (long)Encoding.UTF8.GetByteCount(value)));
                        entry.History.Add(history); history = null!;
                    }
                    finally { if (history != null) history.Title = history.UserName = history.Password = history.Url = history.Notes = ""; }
                }
                yield return entry;
            }
            finally { entry.ClearSecrets(); }
        }
    }
}
