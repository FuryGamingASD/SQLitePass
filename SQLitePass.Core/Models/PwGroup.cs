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

        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? ParentId { get; set; }

        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); } }
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

        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Tree navigation
        public ObservableCollection<PwGroup> Children { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString() => Name;
    }
}
