using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SQLitePass.Core.Models
{
    public class PwCustomField : INotifyPropertyChanged
    {
        private string _key = string.Empty;
        private string _value = string.Empty;
        private bool _isProtected;

        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid EntryId { get; set; }

        public string Key
        {
            get => _key;
            set { if (_key != value) { _key = value; OnPropertyChanged(); } }
        }

        public string Value
        {
            get => _value;
            set { if (_value != value) { _value = value; OnPropertyChanged(); } }
        }

        public bool IsProtected
        {
            get => _isProtected;
            set { if (_isProtected != value) { _isProtected = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
