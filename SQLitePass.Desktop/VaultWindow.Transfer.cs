using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.Desktop;

public sealed partial class VaultWindow
{
    private static readonly FilePickerFileType CsvFormat = new("CSV") { Patterns = ["*.csv"] };
    private static readonly FilePickerFileType JsonFormat = new("JSON") { Patterns = ["*.json"] };

    private async void ImportKeePass_Click(object? sender, RoutedEventArgs e) =>
        await ImportVaultFileAsync(VaultImportFormat.KeePassCsv, PickImportPathAsync);

    private async void ImportBitwarden_Click(object? sender, RoutedEventArgs e) =>
        await ImportVaultFileAsync(VaultImportFormat.BitwardenJson, PickImportPathAsync);

    private async void ImportBrowser_Click(object? sender, RoutedEventArgs e) =>
        await ImportVaultFileAsync(VaultImportFormat.BrowserCsv, PickImportPathAsync);

    internal Task ImportVaultFileAsync(VaultImportFormat format, Func<VaultImportFormat, Task<string?>> chooseSource) => RunOperationAsync(async token =>
    {
        if (_session is not { } session || session.Storage.IsReadOnly) return;
        var folder = SelectedFolder;
        Guid target = (folder is { IsRecycleBin: false } ? folder.Id : RootFolderId)
            ?? throw new VaultConflictException("Choose an existing vault folder for import.");
        string? source = await chooseSource(format);
        token.ThrowIfCancellationRequested();
        if (source == null) return;
        StatusText.Text = Text("ImportReading");
        var imported = await ImportExportService.ImportFromFileAsync(source, format, target, token);
        try
        {
            if (imported.Count == 0)
            {
                StatusText.Text = Text("ImportNoEntries");
                return;
            }
            string note = Text(format switch
            {
                VaultImportFormat.BitwardenJson => "ImportBitwardenNote",
                VaultImportFormat.KeePassCsv => "ImportKeePassCsvNote",
                _ => "ImportBrowserCsvNote"
            });
            if (format == VaultImportFormat.BitwardenJson)
                note += "\n\n" + Text("ImportTypesSummary", imported.Count(entry => entry.SourceItemType == 1),
                    imported.Count(entry => entry.SourceItemType == 2), imported.Count(entry => entry.SourceItemType is not (1 or 2)));
            if (!await ConfirmAsync(Text("ImportReview"), Text("ImportConfirm", imported.Count, note))) return;
            token.ThrowIfCancellationRequested();

            var progress = new Progress<VaultImportProgress>(value =>
                ReportTransferProgress(token, Text("ImportProgress", value.Completed, value.Total)));
            var result = await session.Storage.ImportEntriesAsync(imported, target, session.Crypto, token, progress);
            if (!ReferenceEquals(_session, session)) return;
            // The write may already be committed when the UI receives a cancellation.
            if (!token.IsCancellationRequested) await ReloadFolderAsync(session, target, token);
            StatusText.Text = Text("ImportSuccess", result.EntriesImported, result.GroupsCreated);
        }
        finally
        {
            foreach (var entry in imported) entry.ClearSecrets();
        }
    }, canCancel: true);

    private async Task<string?> PickImportPathAsync(VaultImportFormat format)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Text(format switch
            {
                VaultImportFormat.KeePassCsv => "ImportKeePassTitle",
                VaultImportFormat.BitwardenJson => "ImportBitwardenTitle",
                _ => "ImportBrowserTitle"
            }),
            FileTypeFilter = [format == VaultImportFormat.BitwardenJson ? JsonFormat : CsvFormat]
        });
        return files.Count == 0 ? null : RequireLocalFile(files[0]);
    }

    private async void ExportKeePass_Click(object? sender, RoutedEventArgs e) => await ExportVaultFileAsync(PickExportPathAsync);

    internal Task ExportVaultFileAsync(Func<Task<string?>> chooseDestination) => RunOperationAsync(async token =>
    {
        if (_machinePolicy.DisableExport)
        {
            StatusText.Text = Text(_machinePolicy.IsValid ? "MachinePolicyExportDisabled" : "MachinePolicyInvalid");
            return;
        }
        if (_session is not { } session) return;
        if (!await ConfirmAsync(Text("ExportTitle"), Text("ExportWarning"))) return;
        token.ThrowIfCancellationRequested();
        string? destination = await chooseDestination();
        token.ThrowIfCancellationRequested();
        if (destination == null) return;
        if (!string.Equals(Path.GetExtension(destination), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = Text("ExportChooseCsv");
            return;
        }

        StatusText.Text = Text("ExportWriting");
        var progress = new Progress<int>(count => ReportTransferProgress(token, Text("ExportProgress", count)));
        await session.Storage.ExportCsvAsync(destination, session.Crypto, token, progress);
        if (ReferenceEquals(_session, session)) StatusText.Text = Text("ExportCompleted");
    }, canCancel: true);

    private async Task<string?> PickExportPathAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Text("ExportTitle"), SuggestedFileName = "SQLitePass_Export.csv", DefaultExtension = "csv", FileTypeChoices = [CsvFormat]
        });
        return file == null ? null : RequireLocalFile(file);
    }

    private void ReportTransferProgress(CancellationToken token, string status)
    {
        if (_operation?.Token == token && !token.IsCancellationRequested) StatusText.Text = status;
    }
}
