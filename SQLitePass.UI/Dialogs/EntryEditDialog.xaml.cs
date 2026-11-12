using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.UI.Dialogs
{
    public partial class EntryEditDialog : Window
    {
        private readonly PwEntry _entry;
        private bool _isPasswordRevealed = false;

        public EntryEditDialog(PwEntry entry)
        {
            InitializeComponent();
            _entry = entry ?? throw new ArgumentNullException(nameof(entry));

            var settings = AppSettingsManager.Load();
            SQLitePass.UI.Services.WindowSecurityService.ApplyProtection(this, settings.ProtectScreenCapture);

            // Load values
            TxtTitle.Text = _entry.Title;
            TxtUserName.Text = _entry.UserName;
            TxtPasswordMasked.Password = _entry.Password;
            TxtPasswordPlain.Text = _entry.Password;
            TxtUrl.Text = _entry.Url;
            TxtNotes.Text = _entry.Notes;
            TxtTags.Text = _entry.Tags;

            if (_entry.ExpiryDate.HasValue)
            {
                ChkExpires.IsChecked = true;
                DpExpiry.IsEnabled = true;
                DpExpiry.SelectedDate = _entry.ExpiryDate.Value;
            }

            ChkAutoTypeEnabled.IsChecked = _entry.AutoTypeEnabled;
            TxtAutoTypeSeq.Text = _entry.AutoTypeSequence;

            DgCustomFields.ItemsSource = _entry.CustomFields;
            LstAttachments.ItemsSource = _entry.Attachments;
            DgHistory.ItemsSource = _entry.History;

            UpdateEntropyDisplay(_entry.Password);
            TxtTitle.Focus();
        }

        private string CurrentPassword => _isPasswordRevealed ? TxtPasswordPlain.Text : TxtPasswordMasked.Password;

        private void SetPassword(string pwd)
        {
            TxtPasswordPlain.Text = pwd;
            TxtPasswordMasked.Password = pwd;
            UpdateEntropyDisplay(pwd);
        }

        private void UpdateEntropyDisplay(string pwd)
        {
            double entropy = PasswordGenerator.CalculateEntropy(pwd);
            PbQuality.Value = Math.Min(entropy, 128);
            TxtQualityScore.Text = $"{entropy:F1} bits";
        }

        private void TxtPassword_Changed(object sender, TextChangedEventArgs e)
        {
            if (_isPasswordRevealed)
            {
                TxtPasswordMasked.Password = TxtPasswordPlain.Text;
                UpdateEntropyDisplay(TxtPasswordPlain.Text);
            }
        }

        private void TxtPasswordMasked_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_isPasswordRevealed)
            {
                TxtPasswordPlain.Text = TxtPasswordMasked.Password;
                UpdateEntropyDisplay(TxtPasswordMasked.Password);
            }
        }

        private void BtnToggleVisibility_Click(object sender, RoutedEventArgs e)
        {
            _isPasswordRevealed = !_isPasswordRevealed;
            if (_isPasswordRevealed)
            {
                TxtPasswordPlain.Visibility = Visibility.Visible;
                TxtPasswordMasked.Visibility = Visibility.Collapsed;
                BtnToggleVisibility.Content = "🔒";
            }
            else
            {
                TxtPasswordPlain.Visibility = Visibility.Collapsed;
                TxtPasswordMasked.Visibility = Visibility.Visible;
                BtnToggleVisibility.Content = "👁";
            }
        }

        private void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            var genDlg = new PasswordGeneratorDialog { Owner = this };
            if (genDlg.ShowDialog() == true)
            {
                SetPassword(genDlg.ResultPassword);
            }
        }

        private void ChkExpires_Changed(object sender, RoutedEventArgs e)
        {
            DpExpiry.IsEnabled = ChkExpires.IsChecked == true;
            if (ChkExpires.IsChecked == true && !DpExpiry.SelectedDate.HasValue)
            {
                DpExpiry.SelectedDate = DateTime.Today.AddMonths(3);
            }
        }

        private void BtnAddField_Click(object sender, RoutedEventArgs e)
        {
            _entry.CustomFields.Add(new PwCustomField
            {
                Key = "New Field",
                Value = "",
                IsProtected = false
            });
        }

        private void BtnDeleteField_Click(object sender, RoutedEventArgs e)
        {
            if (DgCustomFields.SelectedItem is PwCustomField selected)
            {
                _entry.CustomFields.Remove(selected);
            }
        }

        private void BtnInsertPlaceholder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Content is string tag)
            {
                TxtAutoTypeSeq.Text += tag;
            }
        }

        private void BtnAttachFile_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog { Title = "Select file to attach" };
            if (ofd.ShowDialog() == true)
            {
                byte[] data = File.ReadAllBytes(ofd.FileName);
                _entry.Attachments.Add(new PwAttachment
                {
                    FileName = Path.GetFileName(ofd.FileName),
                    FileSize = data.Length,
                    Data = data,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        private void BtnSaveAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (LstAttachments.SelectedItem is PwAttachment attach)
            {
                var sfd = new SaveFileDialog
                {
                    FileName = attach.FileName,
                    Title = "Save Attachment"
                };
                if (sfd.ShowDialog() == true)
                {
                    File.WriteAllBytes(sfd.FileName, attach.Data);
                    MessageBox.Show("Attachment saved successfully.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                MessageBox.Show("Please select an attachment to save.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnRemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (LstAttachments.SelectedItem is PwAttachment attach)
            {
                _entry.Attachments.Remove(attach);
            }
        }

        private void BtnRestoreHistory_Click(object sender, RoutedEventArgs e)
        {
            if (DgHistory.SelectedItem is PwEntryHistory hist)
            {
                if (MessageBox.Show($"Restore revision from {hist.SavedAt:yyyy-MM-dd HH:mm:ss}?", "Restore Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    TxtTitle.Text = hist.Title;
                    TxtUserName.Text = hist.UserName;
                    SetPassword(hist.Password);
                    TxtUrl.Text = hist.Url;
                    TxtNotes.Text = hist.Notes;
                }
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // Snapshot current state to history before saving if modified
            if (!string.IsNullOrEmpty(_entry.Title) &&
               (_entry.Title != TxtTitle.Text || _entry.Password != CurrentPassword || _entry.UserName != TxtUserName.Text))
            {
                _entry.History.Add(_entry.CreateHistorySnapshot());
            }

            _entry.Title = TxtTitle.Text.Trim();
            _entry.UserName = TxtUserName.Text.Trim();
            _entry.Password = CurrentPassword;
            _entry.Url = TxtUrl.Text.Trim();
            _entry.Notes = TxtNotes.Text;
            _entry.Tags = TxtTags.Text.Trim();
            _entry.ExpiryDate = ChkExpires.IsChecked == true ? DpExpiry.SelectedDate : null;
            _entry.AutoTypeEnabled = ChkAutoTypeEnabled.IsChecked == true;
            _entry.AutoTypeSequence = TxtAutoTypeSeq.Text;
            _entry.UpdatedAt = DateTime.UtcNow;

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
