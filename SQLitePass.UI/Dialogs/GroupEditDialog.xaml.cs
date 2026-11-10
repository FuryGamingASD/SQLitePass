using System.Windows;
using SQLitePass.Core.Models;

namespace SQLitePass.UI.Dialogs
{
    public partial class GroupEditDialog : Window
    {
        public string GroupName => TxtGroupName.Text.Trim();

        public GroupEditDialog(string initialName = "New Group")
        {
            InitializeComponent();
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
