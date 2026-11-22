using System.IO;
using System.Windows;
using Microsoft.Win32;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;
using SQLitePass.UI.Dialogs;
using SQLitePass.UI.Services;

namespace SQLitePass.UI;

public partial class MainWindow
{
    private CancellationTokenSource? _importCancellation;
    private async void MenuImportCsv_Click(object sender, RoutedEventArgs e) => await ImportFileAsync(VaultImportFormat.KeePassCsv);
    private async void MenuImportBitwardenJson_Click(object sender, RoutedEventArgs e) => await ImportFileAsync(VaultImportFormat.BitwardenJson);
    private async void MenuImportBrowserCsv_Click(object sender, RoutedEventArgs e) => await ImportFileAsync(VaultImportFormat.BrowserCsv);

    private async Task ImportFileAsync(VaultImportFormat format)
    {
        if (_isLocked || _isLocking || _importCancellation != null) return;
        var dialog = new OpenFileDialog
        {
            Title = LocalizationManager.GetString(format switch { VaultImportFormat.KeePassCsv => "ImportKeePassTitle", VaultImportFormat.BitwardenJson => "ImportBitwardenTitle", _ => "ImportBrowserTitle" }),
            Filter = format == VaultImportFormat.BitwardenJson ? "JSON (*.json)|*.json" : "CSV (*.csv)|*.csv"
        };
        if (dialog.ShowDialog(this) != true || _isLocked) return;
        int generation = _sessionGeneration;
        var storage = _storage; var crypto = _crypto;
        Guid target = _selectedGroup?.IsRecycleBin == false ? _selectedGroup.Id : _metadata.RootGroupId ?? Guid.Empty;
        using var cancellation = new CancellationTokenSource(); _importCancellation = cancellation;
        OperationProgressDialog? progressWindow = null; List<PwEntry>? imported = null;
        try
        {
            progressWindow = new OperationProgressDialog(cancellation) { Owner = this }; progressWindow.Show();
            imported = await ImportExportService.ImportFromFileAsync(dialog.FileName, format, target, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_isLocked || generation != _sessionGeneration) return;
            if (imported.Count == 0) throw new UserInputException("ImportNoEntries");
            string note = LocalizationManager.GetString(format == VaultImportFormat.BitwardenJson ? "ImportBitwardenNote" : "ImportCsvNote");
            if (format == VaultImportFormat.BitwardenJson)
                note += "\n\n" + LocalizationManager.GetString("ImportTypesSummary", imported.Count(entry => entry.SourceItemType == 1), imported.Count(entry => entry.SourceItemType == 2), imported.Count(entry => entry.SourceItemType is not (1 or 2)));
            if (MessageBox.Show(this, LocalizationManager.GetString("ImportConfirm", imported.Count, note),
                LocalizationManager.GetString("ImportReview"), MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
            cancellation.Token.ThrowIfCancellationRequested();
            if (_isLocked || generation != _sessionGeneration) return;
            var progress = new Progress<VaultImportProgress>(value => progressWindow.Report(value));
            var result = await storage.ImportEntriesAsync(imported, target, crypto, cancellation.Token, progress);
            // After commit, cancellation must not describe a completed import as rolled back.
            if (_isLocked || generation != _sessionGeneration) return;
            await RefreshDataAsync();
            if (_isLocked || generation != _sessionGeneration) return;
            MessageBox.Show(this, LocalizationManager.GetString("ImportSuccess", result.EntriesImported, result.GroupsCreated), LocalizationManager.GetString("ImportCompleted"));
            AppLogger.LogInfo("Importazione completata.");
        }
        catch (OperationCanceledException)
        {
            if (!_isLocked && generation == _sessionGeneration) TxtOperationStatus.Text = LocalizationManager.GetString("ImportCancelled");
        }
        catch (Exception ex)
        {
            AppLogger.LogError("Importazione non completata", ex);
            if (!_isLocked && generation == _sessionGeneration) MessageBox.Show(this, SQLitePass.UI.Services.UiErrorMessages.GetText(ex), LocalizationManager.GetString("ImportFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (imported != null) foreach (var entry in imported) entry.ClearSecrets();
            progressWindow?.Complete();
            if (ReferenceEquals(_importCancellation, cancellation)) _importCancellation = null;
        }
    }
}
