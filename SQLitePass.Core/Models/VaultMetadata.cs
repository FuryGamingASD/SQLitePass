using System;

namespace SQLitePass.Core.Models
{
    public class VaultMetadata
    {
        public Guid VaultId { get; set; } = Guid.NewGuid();
        public string VaultName { get; set; } = "SQLitePass Vault";
        public string SaltBase64 { get; set; } = string.Empty;
        public int KdfIterations { get; set; } = 3;
        public int KdfMemoryKb { get; set; } = 64 * 1024; // 64 MB
        public int KdfParallelism { get; set; } = 2;
        public string VerificationCanary { get; set; } = string.Empty;
        public bool EncryptUsernameUrl { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
