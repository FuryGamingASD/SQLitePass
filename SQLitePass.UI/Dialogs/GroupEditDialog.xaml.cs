using System.Windows;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.UI.Dialogs
{
    public partial class GroupEditDialog : Window
    {
        public string GroupName => TxtGroupName.Text.Trim();

        public GroupEditDialog(string initialName = "New Group")
        {
            InitializeComponent();
            var settings = AppSettingsManager.Load();
            SQLitePass.UI.Services.WindowSecurityService.ApplyProtection(this, settings.ProtectScreenCapture);
            TxtGroupName.Text = initialName;
            TxtGroupName.SelectAll();
            TxtGroupName.Focus();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtGroupName.Text))
            {
                MessageBox.Show("Group name cannot be empty.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
