namespace SQLitePass.Core.Models;

public sealed record VaultSnapshot(VaultMetadata Metadata, List<PwGroup> Groups, List<PwEntry> Entries);
