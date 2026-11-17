using Microsoft.Data.Sqlite;

if (args.Length == 1 && args[0] == "--benchmark") return await SQLitePass.TestHost.VaultBenchmark.RunAsync();
if (args.Length == 1 && args[0] == "--benchmark-many") return await SQLitePass.TestHost.VaultBenchmark.RunAsync(manyEntries: true);

if (args.Length == 1 && args[0] == "--sqlite-version")
{
    using var probe = new SqliteConnection("Data Source=:memory:"); probe.Open();
    using var command = probe.CreateCommand(); command.CommandText = "SELECT sqlite_version()";
    Console.WriteLine(command.ExecuteScalar()); return 0;
}

if (args.Length != 1) return 2;
string path = Path.GetFullPath(args[0]);
string? parent = Path.GetDirectoryName(path);
if (parent == null || !parent.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
    !Path.GetFileName(parent).StartsWith("SQLitePass-tests-", StringComparison.Ordinal) || Path.GetFileName(path) != "vault.kpd") return 3;

// Only synthetic fixtures. No UI, SendInput, network, or credential material.
var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
connection.Open();
using (var command = connection.CreateCommand())
{
    command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; PRAGMA wal_checkpoint(TRUNCATE); UPDATE sp_entries SET payload=zeroblob(length(payload));";
    command.ExecuteNonQuery();
}
GC.KeepAlive(connection);
// An OS process exit without sqlite3_close leaves a real WAL for the recovery test.
Environment.Exit(0);
return 0;

namespace SQLitePass.TestHost { public static class Marker { } }
