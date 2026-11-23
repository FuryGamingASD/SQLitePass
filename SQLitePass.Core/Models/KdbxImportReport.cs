namespace SQLitePass.Core.Models;

public sealed record KdbxUnsupportedData(string Feature, int? Count, string Description);

public sealed record KdbxImportReport(
    string FormatVersion,
    int GroupsImported,
    int EntriesImported,
    int RecycleBinEntriesSkipped,
    IReadOnlyList<string> PreservedData,
    IReadOnlyList<KdbxUnsupportedData> UnsupportedData);
