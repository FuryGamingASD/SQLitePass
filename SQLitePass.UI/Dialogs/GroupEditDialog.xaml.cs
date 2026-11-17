using System.Windows;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;
using SQLitePass.UI.Services;

namespace SQLitePass.UI.Dialogs
{
    public partial class GroupEditDialog : Window
    {
        public string GroupName => TxtGroupName.Text.Trim();

        public GroupEditDialog(string? initialName = null)
        {
            InitializeComponent();
            var settings = AppSettingsManager.Load();
            SQLitePass.UI.Services.WindowSecurityService.ApplyProtection(this, settings.ProtectScreenCapture);
            TxtGroupName.Text = initialName ?? LocalizationManager.GetString("NewGroup");
            TxtGroupName.SelectAll();
            TxtGroupName.Focus();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtGroupName.Text))
            {
                MessageBox.Show(this, LocalizationManager.GetString("GroupNameRequired"), LocalizationManager.GetString("WarningTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
