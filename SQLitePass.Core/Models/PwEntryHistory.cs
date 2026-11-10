using System;

namespace SQLitePass.Core.Models
{
    public class PwEntryHistory
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid EntryId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public DateTime SavedAt { get; set; } = DateTime.UtcNow;
    }
}
