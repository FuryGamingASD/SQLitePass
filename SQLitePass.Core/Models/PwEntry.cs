using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SQLitePass.Core.Models
{
    public class PwEntry : INotifyPropertyChanged
    {
        private string _title = string.Empty;
        private string _userName = string.Empty;
        private string _password = string.Empty;
        private string _url = string.Empty;
        private string _notes = string.Empty;
        private string _tags = string.Empty;
        private int _iconIndex = 0;
        private DateTime? _expiryDate;
        private bool _autoTypeEnabled = true;
        private string _autoTypeSequence = "{USERNAME}{TAB}{PASSWORD}{ENTER}";
        private DateTime _createdAt = DateTime.UtcNow;
        private DateTime _updatedAt = DateTime.UtcNow;
        private DateTime _lastAccessAt = DateTime.UtcNow;

        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid GroupId { get; set; }
        public long Revision { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public int? SourceItemType { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasLoadedDetails { get; private set; } = true;
        internal void MarkAsOverview() => HasLoadedDetails = false;
        internal int? GridPasswordLength { get; set; }
        internal int? GridAutoTypeSequenceLength { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasPassword => (GridPasswordLength ?? Password?.Length ?? 0) > 0;
        [System.Text.Json.Serialization.JsonIgnore]
        public string? SourceGroupPath { get; set; }

        public string Title
        {
            get => _title;
            set { if (_title != value) { _title = value; OnPropertyChanged(); } }
        }

        public string UserName
        {
            get => _userName;
            set { if (_userName != value) { _userName = value; OnPropertyChanged(); } }
        }

        public string Password
        {
            get => _password;
            set { if (_password != value) { _password = value; OnPropertyChanged(); } }
        }

        public string Url
        {
            get => _url;
            set { if (_url != value) { _url = value; OnPropertyChanged(); } }
        }

        public string Notes
        {
            get => _notes;
            set { if (_notes != value) { _notes = value; OnPropertyChanged(); } }
        }

        public string Tags
        {
            get => _tags;
            set { if (_tags != value) { _tags = value; OnPropertyChanged(); } }
        }

        public int IconIndex
        {
            get => _iconIndex;
            set { if (_iconIndex != value) { _iconIndex = value; OnPropertyChanged(); } }
        }

        public DateTime? ExpiryDate
        {
            get => _expiryDate;
            set { if (_expiryDate != value) { _expiryDate = value; OnPropertyChanged(); } }
        }

        public bool AutoTypeEnabled
        {
            get => _autoTypeEnabled;
            set { if (_autoTypeEnabled != value) { _autoTypeEnabled = value; OnPropertyChanged(); } }
        }

        public string AutoTypeSequence
        {
            get => _autoTypeSequence;
            set { if (_autoTypeSequence != value) { _autoTypeSequence = value; OnPropertyChanged(); } }
        }

        public DateTime CreatedAt
        {
            get => _createdAt;
            set { if (_createdAt != value) { _createdAt = value; OnPropertyChanged(); } }
        }

        public DateTime UpdatedAt
        {
            get => _updatedAt;
            set { if (_updatedAt != value) { _updatedAt = value; OnPropertyChanged(); } }
        }

        /// <summary>Legacy/imported timestamp retained for compatibility; not an activity log or a UI usage counter.</summary>
        public DateTime LastAccessAt
        {
            get => _lastAccessAt;
            set { if (_lastAccessAt != value) { _lastAccessAt = value; OnPropertyChanged(); } }
        }

        public ObservableCollection<PwCustomField> CustomFields { get; } = new();
        public ObservableCollection<PwAttachment> Attachments { get; } = new();
        public ObservableCollection<PwEntryHistory> History { get; } = new();

        /// <summary>
        /// Creates a deep copy of this entry to keep in history before modification.
        /// </summary>
        public PwEntryHistory CreateHistorySnapshot()
        {
            return new PwEntryHistory
            {
                Id = Guid.NewGuid(),
                EntryId = this.Id,
                Title = this.Title,
                UserName = this.UserName,
                Password = this.Password,
                Url = this.Url,
                Notes = this.Notes,
                SnapshotJson = EntrySerialization.Serialize(this, includeHistory: false),
                SavedAt = DateTime.UtcNow
            };
        }

        public PwEntry DeepClone(bool newIdentity = false)
        {
            var copy = EntrySerialization.Deserialize(EntrySerialization.Serialize(this, includeHistory: !newIdentity));
            if (newIdentity)
            {
                copy.Id = Guid.NewGuid(); copy.Revision = 0; copy.History.Clear();
                copy.CreatedAt = copy.UpdatedAt = DateTime.UtcNow;
                foreach (var field in copy.CustomFields) { field.Id = Guid.NewGuid(); field.EntryId = copy.Id; }
                foreach (var file in copy.Attachments) { file.Id = Guid.NewGuid(); file.EntryId = copy.Id; }
            }
            return copy;
        }

        public PwEntry CreateOverview()
        {
            var copy = new PwEntry
            {
                Id = Id, GroupId = GroupId, Revision = Revision, Title = Title, UserName = UserName, Password = Password,
                Url = Url, Notes = Notes, Tags = Tags, IconIndex = IconIndex, ExpiryDate = ExpiryDate,
                AutoTypeEnabled = AutoTypeEnabled, AutoTypeSequence = AutoTypeSequence,
                CreatedAt = CreatedAt, UpdatedAt = UpdatedAt, LastAccessAt = LastAccessAt
            };
            copy.MarkAsOverview(); return copy;
        }

        // Storage serializes a separate header without cloning attachment buffers or history strings.
        // The child objects are borrowed for this operation and must never be cleared through this copy.
        internal PwEntry CreateStorageCopy()
        {
            if (!HasLoadedDetails) throw new InvalidOperationException("Caricare i dettagli completi prima del salvataggio.");
            var copy = CreateOverview(); copy.HasLoadedDetails = true;
            foreach (var field in CustomFields) copy.CustomFields.Add(field);
            foreach (var file in Attachments) copy.Attachments.Add(file);
            foreach (var history in History.TakeLast(50)) copy.History.Add(history);
            return copy;
        }

        public void ClearSecrets()
        {
            GridPasswordLength = GridAutoTypeSequenceLength = null;
            UserName = Password = Url = Notes = Tags = Title = AutoTypeSequence = string.Empty;
            foreach (var field in CustomFields) field.Value = string.Empty;
            foreach (var file in Attachments) System.Security.Cryptography.CryptographicOperations.ZeroMemory(file.Data);
            foreach (var history in History) history.Title = history.Password = history.Notes = history.UserName = history.Url = history.SnapshotJson = string.Empty;
            CustomFields.Clear(); Attachments.Clear(); History.Clear();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
