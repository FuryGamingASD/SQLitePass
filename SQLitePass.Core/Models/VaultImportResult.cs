namespace SQLitePass.Core.Models;

public sealed record VaultImportResult(int EntriesImported, int GroupsCreated);
public sealed record VaultImportProgress(int Completed, int Total);
