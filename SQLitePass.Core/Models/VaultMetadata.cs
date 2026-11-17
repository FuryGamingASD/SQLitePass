using System;

namespace SQLitePass.Core.Models
{
    public class VaultMetadata
    {
        public Guid VaultId { get; set; } = Guid.NewGuid();
        public int FormatVersion { get; set; } = 2;
        public Guid KeyEpoch { get; set; } = Guid.NewGuid();
        public Guid? RootGroupId { get; set; }
        public Guid? RecycleBinGroupId { get; set; }
        public bool UsesKeyFile { get; set; }
        public string VaultName { get; set; } = "SQLitePass Vault";
        public string SaltBase64 { get; set; } = string.Empty;
        public int KdfIterations { get; set; } = 3;
        public int KdfMemoryKb { get; set; } = 64 * 1024; // 64 MB
        public int KdfParallelism { get; set; } = 2;
        public string VerificationCanary { get; set; } = string.Empty;
        public bool EncryptUsernameUrl { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
