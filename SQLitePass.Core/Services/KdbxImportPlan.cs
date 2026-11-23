using SQLitePass.Core.Models;

namespace SQLitePass.Core.Services;

public sealed class KdbxImportPlan : IDisposable
{
    private List<PwEntry>? _entries;

    internal KdbxImportPlan(List<PwEntry> entries, KdbxImportReport report)
    {
        _entries = entries;
        Report = report;
    }

    public KdbxImportReport Report { get; }

    internal IReadOnlyList<PwEntry> Entries => _entries ?? throw new ObjectDisposedException(nameof(KdbxImportPlan));

    public void Dispose()
    {
        var entries = Interlocked.Exchange(ref _entries, null);
        if (entries == null) return;
        foreach (var entry in entries) entry.ClearSecrets();
        entries.Clear();
    }
}
