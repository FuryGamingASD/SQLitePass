namespace SQLitePass.Core.Models;

public sealed record KdbxUnsupportedData(string TextKey, int? Count);

public sealed record KdbxImportReport(
    string FormatVersion,
    int GroupsImported,
    int EntriesImported,
    int RecycleBinEntriesSkipped,
    IReadOnlyList<string> PreservedData,
    IReadOnlyList<KdbxUnsupportedData> UnsupportedData);
