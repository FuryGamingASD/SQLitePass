using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;
using SQLitePass.UI.Services;

namespace SQLitePass.UI.Dialogs;

public partial class EntryEditDialog
{
    private TimedSecretReveal _passwordReveal = null!;
    private bool _trackingEdits, _dirty, _syncingPassword, _autoTypeInvalid, _expiryInvalid, _cancelForLock;

    private void InitializeEditingState()
    {
        _passwordReveal = new TimedSecretReveal(DisplayPassword);
        Deactivated += (_, _) => _passwordReveal.Hide();
        Closing += ConfirmDiscard;
    }

    private void DisplayPassword(bool revealed)
    {
        string value = CurrentPassword;
        _isPasswordRevealed = revealed;
        SetPassword(value, markEdited: false);
        TxtPasswordPlain.Visibility = revealed ? Visibility.Visible : Visibility.Collapsed;
        TxtPasswordMasked.Visibility = revealed ? Visibility.Collapsed : Visibility.Visible;
        BtnToggleVisibility.Content = new Controls.AppIcon { Kind = revealed ? Controls.AppIconKind.EyeOff : Controls.AppIconKind.Eye };
        BtnToggleVisibility.SetResourceReference(ToolTipProperty, revealed ? "HidePassword" : "ShowPassword");
        BtnToggleVisibility.SetResourceReference(AutomationProperties.NameProperty, revealed ? "HidePassword" : "ShowPassword");
    }

    private void StartTrackingEdits()
    {
        foreach (var box in new[] { TxtTitle, TxtUserName, TxtUrl, TxtNotes, TxtTags, TxtAutoTypeSeq })
            box.TextChanged += (_, _) => MarkEdited();
        foreach (var box in new[] { ChkExpires, ChkAutoTypeEnabled })
        { box.Checked += (_, _) => MarkEdited(); box.Unchecked += (_, _) => MarkEdited(); }
        DpExpiry.SelectedDateChanged += (_, _) => { _expiryInvalid = false; MarkEdited(); };
        DpExpiry.DateValidationError += (_, args) => { args.ThrowException = false; _expiryInvalid = true; MarkEdited(); };
        foreach (var collection in new System.Collections.Specialized.INotifyCollectionChanged[] { _entry.CustomFields, _entry.Attachments, _entry.History })
            collection.CollectionChanged += (_, _) => MarkEdited();
        _trackingEdits = true; UpdateEditingStatus();
    }

    private void MarkEdited()
    {
        if (!_trackingEdits || _closed || _syncingPassword) return;
        _dirty = true; _autoTypeInvalid = false; UpdateEditingStatus();
    }

    private bool UpdateEditingStatus()
    {
        if (!_trackingEdits || _closed) return false;
        bool titleValid = !string.IsNullOrWhiteSpace(TxtTitle.Text);
        bool expiryValid = ChkExpires.IsChecked != true || !_expiryInvalid && DpExpiry.SelectedDate.HasValue;
        string? error = !titleValid ? "TitleRequired" : !expiryValid ? "EntryExpiryInvalid" : _autoTypeInvalid ? "EntryAutoTypeInvalid" : null;
        if (error != null) TxtEntryValidation.SetResourceReference(TextBlock.TextProperty, error);
        else TxtEntryValidation.Text = string.Empty;
        TxtEntryValidation.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
        TxtTitle.SetResourceReference(BorderBrushProperty, titleValid ? "BorderColor" : "DangerText");
        TxtEditState.SetResourceReference(TextBlock.TextProperty, _fileOperationBusy ? "EntryChecking" : _dirty ? "EntryUnsavedChanges" : _entry.Revision == 0 ? "EntryNewDraft" : "EntryUnchanged");
        BtnSaveEntry.IsEnabled = !_fileOperationBusy && error == null && (_dirty || _entry.Revision == 0);
        return error == null;
    }

    private async Task<bool> ValidateAutoTypeAsync()
    {
        var validation = new PwEntry
        {
            Title = _entry.Title, UserName = _entry.UserName, Password = _entry.Password, Url = _entry.Url,
            Notes = _entry.Notes, AutoTypeSequence = _entry.AutoTypeSequence
        };
        foreach (var field in _entry.CustomFields) validation.CustomFields.Add(new PwCustomField { Key = field.Key, Value = field.Value });
        SetFileOperationBusy(true); UpdateEditingStatus();
        try
        {
            if (_entry.AutoTypeEnabled)
                await Task.Run(() => { _lifetime.Token.ThrowIfCancellationRequested(); AutoTypeService.Parse(validation); }, _lifetime.Token);
            if (!_closed) await _draft.PrepareForSaveAsync(_lifetime.Token);
            return !_closed;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        { _autoTypeInvalid = true; return false; }
        finally { validation.ClearSecrets(); SetFileOperationBusy(false); UpdateEditingStatus(); }
    }

    private void ConfirmDiscard(object? sender, CancelEventArgs args)
    {
        if (_cancelForLock || !_dirty || DialogResult.HasValue) return;
        if (MessageBox.Show(this, LocalizationManager.GetString("EntryDiscardChanges"), LocalizationManager.GetString("EntryEditTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) args.Cancel = true;
    }

    internal void CancelForLock()
    {
        _cancelForLock = true;
        if (_closed)
        {
            _entry.ClearSecrets();
            return;
        }
        if (_closing)
        {
            // OnClosing will force the already-running close to complete after
            // any discard prompt returns. Avoid mutating WPF controls mid-close.
            _cancelCloseRequested = true;
            _entry.ClearSecrets();
            return;
        }

        _passwordReveal.Hide(); _entry.ClearSecrets();
        SensitiveWindowService.ClearControls(this); Close();
    }
}
