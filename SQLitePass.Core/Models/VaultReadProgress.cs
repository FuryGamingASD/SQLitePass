namespace SQLitePass.Core.Models;

public enum VaultReadPhase { Verifying, LoadingEntries }
public sealed record VaultReadProgress(VaultReadPhase Phase, int Completed, int Total);
