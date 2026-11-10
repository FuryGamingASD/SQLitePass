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
                SavedAt = DateTime.UtcNow
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
