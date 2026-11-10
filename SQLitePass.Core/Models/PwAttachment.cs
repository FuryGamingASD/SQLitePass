using System;

namespace SQLitePass.Core.Models
{
    public class PwAttachment
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid EntryId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>(); // Decrypted in memory
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
