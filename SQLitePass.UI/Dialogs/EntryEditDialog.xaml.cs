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
        private readonly EntryEditDraft _draft;
        private readonly PwEntryHistory _original;
        public PwEntry ResultEntry => _entry;
        private bool _isPasswordRevealed = false;
        private bool _closed;
        private bool _fileOperationBusy;
        private bool _lifetimeDisposed;
        private readonly CancellationTokenSource _lifetime = new();
        private void SetFileOperationBusy(bool busy)
        {
            _fileOperationBusy = busy;
            if (!_closed) EntryTabs.IsEnabled = !busy;
            else if (!busy) DisposeLifetime();
            UpdateEditingStatus();
        }
        private void DisposeLifetime()
        { if (!_lifetimeDisposed) { _lifetimeDisposed = true; _lifetime.Dispose(); } }

        public EntryEditDialog(PwEntry entry) : this(EntryEditDraft.Create(entry)) { }
        public EntryEditDialog(EntryEditDraft draft)
        {
            InitializeComponent();
            ArgumentNullException.ThrowIfNull(draft);
            _original = draft.Original;
            _draft = draft;
            _entry = draft.Current;
            InitializeEditingState();

            var settings = AppSettingsManager.Load();
            SQLitePass.UI.Services.WindowSecurityService.ApplyProtection(this, settings.ProtectScreenCapture);

            // Load values
            TxtTitle.Text = _entry.Title;
            TxtUserName.Text = _entry.UserName;
            SetPassword(_entry.Password);
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
            StartTrackingEdits();
            TxtTitle.Focus();
            Closed += (_, _) =>
            {
                _closed = true; _lifetime.Cancel();
                _passwordReveal.Dispose();
                if (!_fileOperationBusy) DisposeLifetime();
                TxtPasswordMasked.Clear(); TxtPasswordPlain.Clear(); TxtNotes.Clear(); TxtUserName.Clear();
                _original.Title = _original.Password = _original.Notes = _original.UserName = _original.Url = _original.SnapshotJson = string.Empty;
                if (DialogResult != true) _entry.ClearSecrets();
            };
        }

        private string CurrentPassword => _isPasswordRevealed ? TxtPasswordPlain.Text : TxtPasswordMasked.Password;

        private void SetPassword(string pwd, bool markEdited = true)
        {
            _syncingPassword = true;
            try
            {
                if (_isPasswordRevealed) { TxtPasswordPlain.Text = pwd; TxtPasswordMasked.Clear(); }
                else { TxtPasswordMasked.Password = pwd; TxtPasswordPlain.Clear(); }
            }
            finally { _syncingPassword = false; }
            UpdateEntropyDisplay(pwd);
            if (markEdited) MarkEdited();
        }

        private void UpdateEntropyDisplay(string pwd)
        {
            if (pwd.Length > SQLitePass.UI.Services.UiText.MaximumScoredPasswordLength)
            {
                PbQuality.Visibility = Visibility.Collapsed;
                TxtQualityScore.Text = SQLitePass.UI.Services.LocalizationManager.GetString("QualityNotEstimated");
                return;
            }
            PbQuality.Visibility = Visibility.Visible;
            double entropy = PasswordGenerator.CalculateEntropy(pwd);
            PbQuality.Value = Math.Min(entropy, 100);
            TxtQualityScore.Text = SQLitePass.UI.Services.LocalizationManager.GetString("QualityScore", entropy);
        }

        private void TxtPassword_Changed(object sender, TextChangedEventArgs e)
        {
            if (_isPasswordRevealed && !_syncingPassword)
            {
                UpdateEntropyDisplay(TxtPasswordPlain.Text);
                MarkEdited();
            }
        }

        private void TxtPasswordMasked_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_isPasswordRevealed && !_syncingPassword)
            {
                if (TxtQualityScore == null) return;
                UpdateEntropyDisplay(TxtPasswordMasked.Password);
                MarkEdited();
            }
        }

        private void BtnToggleVisibility_Click(object sender, RoutedEventArgs e)
        {
            _passwordReveal.Toggle();
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
            var dialog = new CustomFieldDialog { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Result != null) _entry.CustomFields.Add(dialog.Result);
        }
        private void BtnEditField_Click(object sender, RoutedEventArgs e)
        {
            if (DgCustomFields.SelectedItem is not PwCustomField field) return;
            var dialog = new CustomFieldDialog(field) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Result != null) _entry.CustomFields[_entry.CustomFields.IndexOf(field)] = dialog.Result;
        }
        private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(this, SQLitePass.UI.Services.LocalizationManager.GetString("ClearHistoryConfirm"), SQLitePass.UI.Services.LocalizationManager.GetString("ClearHistory"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) _entry.History.Clear();
        }

        private void BtnDeleteField_Click(object sender, RoutedEventArgs e)
        {
            if (DgCustomFields.SelectedItem is PwCustomField selected)
            {
                selected.Value = string.Empty;
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

        private async void BtnAttachFile_Click(object sender, RoutedEventArgs e)
        {
            if (_closed || _fileOperationBusy) return;
            var ofd = new OpenFileDialog { Title = SQLitePass.UI.Services.LocalizationManager.GetString("AttachmentSelect") };
            if (ofd.ShowDialog(this) == true && !_closed)
            {
                SetFileOperationBusy(true);
                try
                {
                    var attachment = await Task.Run(() => AttachmentFileService.ReadAsync(ofd.FileName, _lifetime.Token));
                    if (_closed) { System.Security.Cryptography.CryptographicOperations.ZeroMemory(attachment.Data); return; }
                    attachment.EntryId = _entry.Id; _entry.Attachments.Add(attachment);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { if (!_closed) MessageBox.Show(this, SQLitePass.UI.Services.UiErrorMessages.GetText(ex), SQLitePass.UI.Services.LocalizationManager.GetString("AttachmentFailed"), MessageBoxButton.OK, MessageBoxImage.Error); }
                finally { SetFileOperationBusy(false); }
            }
        }

        private async void BtnSaveAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (_closed || _fileOperationBusy) return;
            if (LstAttachments.SelectedItem is PwAttachment attach)
            {
                var sfd = new SaveFileDialog
                {
                    FileName = attach.FileName,
                    Title = SQLitePass.UI.Services.LocalizationManager.GetString("AttachmentSave")
                };
                if (sfd.ShowDialog(this) == true && !_closed)
                {
                    SetFileOperationBusy(true);
                    byte[] bytes = attach.Data.ToArray();
                    try { await Task.Run(() => AttachmentFileService.WriteAsync(sfd.FileName, bytes, _lifetime.Token)); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { if (!_closed) MessageBox.Show(this, SQLitePass.UI.Services.UiErrorMessages.GetText(ex), SQLitePass.UI.Services.LocalizationManager.GetString("AttachmentFailed"), MessageBoxButton.OK, MessageBoxImage.Error); }
                    finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); SetFileOperationBusy(false); }
                }
            }
            else
            {
                MessageBox.Show(this, SQLitePass.UI.Services.LocalizationManager.GetString("AttachmentSelectFirst"));
            }
        }

        private void BtnRemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (LstAttachments.SelectedItem is PwAttachment attach)
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(attach.Data);
                _entry.Attachments.Remove(attach);
            }
        }

        private async void BtnRestoreHistory_Click(object sender, RoutedEventArgs e)
        {
            if (_closed || _fileOperationBusy) return;
            if (DgHistory.SelectedItem is PwEntryHistory hist)
            {
                if (MessageBox.Show(this, SQLitePass.UI.Services.LocalizationManager.GetString("HistoryRestoreConfirm", hist.SavedAt.ToLocalTime()), SQLitePass.UI.Services.LocalizationManager.GetString("HistoryRestoreTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes && !_closed)
                {
                    PwEntry? snapshot = null; SetFileOperationBusy(true);
                    try
                    {
                        if (!string.IsNullOrEmpty(hist.SnapshotJson)) snapshot = await Task.Run(() => EntrySerialization.Deserialize(hist.SnapshotJson), _lifetime.Token);
                        if (_closed) return;
                        TxtTitle.Text = hist.Title;
                        TxtUserName.Text = hist.UserName;
                        SetPassword(hist.Password);
                        TxtUrl.Text = hist.Url;
                        TxtNotes.Text = hist.Notes;
                        if (snapshot != null)
                        {
                            TxtTags.Text = snapshot.Tags; ChkExpires.IsChecked = snapshot.ExpiryDate.HasValue; DpExpiry.SelectedDate = snapshot.ExpiryDate;
                            ChkAutoTypeEnabled.IsChecked = snapshot.AutoTypeEnabled; TxtAutoTypeSeq.Text = snapshot.AutoTypeSequence;
                            _entry.CustomFields.Clear(); foreach (var field in snapshot.CustomFields) _entry.CustomFields.Add(field);
                            foreach (var file in _entry.Attachments) System.Security.Cryptography.CryptographicOperations.ZeroMemory(file.Data);
                            _entry.Attachments.Clear(); foreach (var file in snapshot.Attachments) _entry.Attachments.Add(file);
                            // Transfer ownership of child objects to the active draft before cleanup.
                            snapshot.CustomFields.Clear(); snapshot.Attachments.Clear();
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { if (!_closed) MessageBox.Show(this, SQLitePass.UI.Services.UiErrorMessages.GetText(ex), SQLitePass.UI.Services.LocalizationManager.GetString("HistoryRestoreFailed"), MessageBoxButton.OK, MessageBoxImage.Error); }
                    finally { snapshot?.ClearSecrets(); SetFileOperationBusy(false); }
                }
            }
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!await PrepareFormForSaveAsync()) return;
            DialogResult = true; Close();
        }

        internal async Task<bool> PrepareFormForSaveAsync()
        {
            if (_closed) return false;
            if (_fileOperationBusy || !UpdateEditingStatus()) return false;
            DgCustomFields.CommitEdit(DataGridEditingUnit.Cell, true);
            DgCustomFields.CommitEdit(DataGridEditingUnit.Row, true);
            _entry.Title = TxtTitle.Text.Trim();
            _entry.UserName = TxtUserName.Text.Trim();
            _entry.Password = CurrentPassword;
            _entry.Url = TxtUrl.Text.Trim();
            _entry.Notes = TxtNotes.Text;
            _entry.Tags = TxtTags.Text.Trim();
            _entry.ExpiryDate = ChkExpires.IsChecked == true ? DpExpiry.SelectedDate : null;
            _entry.AutoTypeEnabled = ChkAutoTypeEnabled.IsChecked == true;
            _entry.AutoTypeSequence = TxtAutoTypeSeq.Text;
            return await ValidateAutoTypeAsync();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
