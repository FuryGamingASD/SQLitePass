using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SQLitePass.Core.Models
{
    public class PwGroup : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private int _iconIndex = 48; // Default folder icon
        private bool _isExpanded = true;
        private bool _isSelected;
        private int _sortOrder = 0;

        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? ParentId { get; set; }
        public long Revision { get; set; }
        public bool IsRecycleBin { get; set; }
        public bool IsRoot { get; set; }
        public PwGroup DeepClone() => new()
        {
            Id = Id, ParentId = ParentId, Revision = Revision, IsRecycleBin = IsRecycleBin, IsRoot = IsRoot,
            Name = Name, IconIndex = IconIndex, IsExpanded = IsExpanded, IsSelected = IsSelected, SortOrder = SortOrder,
            CreatedAt = CreatedAt, UpdatedAt = UpdatedAt
        };

        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); } }
        }

        public int SortOrder
        {
            get => _sortOrder;
            set { if (_sortOrder != value) { _sortOrder = value; OnPropertyChanged(); } }
        }

        public int IconIndex
        {
            get => _iconIndex;
            set { if (_iconIndex != value) { _iconIndex = value; OnPropertyChanged(); } }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); } }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Tree navigation
        [System.Text.Json.Serialization.JsonIgnore]
        public ObservableCollection<PwGroup> Children { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString() => Name;
    }
}
