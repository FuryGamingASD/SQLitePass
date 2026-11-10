using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SQLitePass.Core.Services;

namespace SQLitePass.UI.Dialogs
{
    public partial class SettingsDialog : Window
    {
        private readonly AppSettings _settings;

        public SettingsDialog()
        {
            InitializeComponent();
            _settings = AppSettingsManager.Load();

            TxtClipboardSeconds.Text = _settings.ClipboardClearSeconds.ToString();

            // Select auto-lock combo item
            foreach (ComboBoxItem item in CmbAutoLockMinutes.Items)
            {
                if (int.TryParse(item.Tag?.ToString(), out int val) && val == _settings.AutoLockMinutes)
                {
                    CmbAutoLockMinutes.SelectedItem = item;
                    break;
                }
            }
            if (CmbAutoLockMinutes.SelectedItem == null)
            {
                CmbAutoLockMinutes.SelectedIndex = 3; // 5 min default
            }

            ChkLockOnMinimize.IsChecked = _settings.AutoLockOnMinimize;
            ChkLockOnWindowsLock.IsChecked = _settings.AutoLockOnWindowsLock;

            ChkMinimizeToTray.IsChecked = _settings.MinimizeToTray;
            ChkCloseToTray.IsChecked = _settings.CloseToTray;
            ChkAlwaysOnTop.IsChecked = _settings.AlwaysOnTop;
            ChkStartWithWindows.IsChecked = _settings.StartWithWindows;

            ChkDualFileBackup.IsChecked = _settings.EnableDualFileBackup;
            TxtSecondaryBackupDir.Text = _settings.SecondaryBackupDir ?? "";

            ChkShowPasswordDefault.IsChecked = _settings.ShowPasswordColumnDefault;
            ChkDarkMode.IsChecked = _settings.DarkMode;
        }

        private void BtnBrowseSecondaryDir_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Seleziona cartella per backup secondario (es. chiavetta USB)"
            };
            if (dialog.ShowDialog() == true)
            {
                TxtSecondaryBackupDir.Text = dialog.FolderName;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtClipboardSeconds.Text, out int secs) && secs >= 3 && secs <= 300)
            {
                _settings.ClipboardClearSeconds = secs;
            }
            else
            {
                _settings.ClipboardClearSeconds = 12;
            }

            if (CmbAutoLockMinutes.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int lockMins))
            {
                _settings.AutoLockMinutes = lockMins;
            }

            _settings.AutoLockOnMinimize = ChkLockOnMinimize.IsChecked == true;
            _settings.AutoLockOnWindowsLock = ChkLockOnWindowsLock.IsChecked == true;

            _settings.MinimizeToTray = ChkMinimizeToTray.IsChecked == true;
            _settings.CloseToTray = ChkCloseToTray.IsChecked == true;
            _settings.AlwaysOnTop = ChkAlwaysOnTop.IsChecked == true;
            _settings.StartWithWindows = ChkStartWithWindows.IsChecked == true;

            _settings.EnableDualFileBackup = ChkDualFileBackup.IsChecked == true;
            _settings.SecondaryBackupDir = string.IsNullOrWhiteSpace(TxtSecondaryBackupDir.Text) ? null : TxtSecondaryBackupDir.Text.Trim();

            _settings.ShowPasswordColumnDefault = ChkShowPasswordDefault.IsChecked == true;
            _settings.DarkMode = ChkDarkMode.IsChecked == true;

            AppSettingsManager.Save(_settings);
            SQLitePass.UI.Services.ThemeManager.ApplyTheme(_settings.DarkMode);

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
