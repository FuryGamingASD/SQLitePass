namespace SQLitePass.Core.Database;

internal enum VaultIoStage { BeforeCommit, BackupCopied, BeforeBackupReplacement, RecoveryValidated, BeforeRecoveryReplacement }
