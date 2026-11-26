using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.Desktop;

public sealed partial class VaultWindow
{
    private static readonly FilePickerFileType KdbxFileType = new("KeePass KDBX") { Patterns = ["*.kdbx"] };

    private async void ImportKdbx_Click(object? sender, RoutedEventArgs e) => await RunOperationAsync(async token =>
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Text("KdbxImportTitle"),
            FileTypeFilter = [KdbxFileType]
        });
        token.ThrowIfCancellationRequested();
        if (files.Count == 0) return;
        string sourcePath = RequireLocalFile(files[0]);

        var credentialDialog = new KdbxCredentialDialog(Text("KdbxImportCredentials"), Text("KdbxSourcePassword"),
            Text("KdbxSourceKeyFile"), Text("Browse"), Text("KdbxContinue"), Text("Cancel"), Text("KdbxKeyFileOnlyNote"));
        ProtectDialog(credentialDialog);
        var sourceCredential = await credentialDialog.ShowDialog<KdbxCredentialResult?>(this);
        token.ThrowIfCancellationRequested();
        if (sourceCredential == null) return;
        if (string.IsNullOrEmpty(sourceCredential.Password) && string.IsNullOrWhiteSpace(sourceCredential.KeyFilePath))
        {
            StatusText.Text = Text("KdbxSourceCredentialRequired");
            return;
        }

        string sourcePassword = sourceCredential.Password;
        string? sourceKeyFilePath = sourceCredential.KeyFilePath;
        string destinationPassword = string.Empty;
        string? destinationKeyFilePath = null;
        sourceCredential = null;
        KdbxImportPlan? plan = null;
        try
        {
            StatusText.Text = Text("KdbxImportReading");
            plan = await KdbxImportService.ReadAsync(sourcePath, sourcePassword, sourceKeyFilePath, token);
            token.ThrowIfCancellationRequested();

            var report = plan.Report;
            string summary = Text("KdbxImportCounts", report.EntriesImported, report.GroupsImported, report.RecycleBinEntriesSkipped);
            string preserved = string.Join("\n", report.PreservedData.Select(key => "• " + Text(key)));
            string unsupported = string.Join("\n", report.UnsupportedData.Select(item =>
                "• " + (item.Count?.ToString() ?? Text("KdbxUnknownCount")) + " — " + Text(item.TextKey)));
            string review = summary + "\n\n" + Text("KdbxPreservedHeading") + "\n" + preserved +
                "\n\n" + Text("KdbxUnsupportedHeading") + "\n" + unsupported;
            if (!await ConfirmAsync(Text("KdbxImportReviewTitle"), review)) return;
            token.ThrowIfCancellationRequested();

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Text("KdbxImportDestination"),
                SuggestedFileName = "PasswordVault.kpd",
                DefaultExtension = "kpd",
                FileTypeChoices = [new FilePickerFileType("SQLitePass") { Patterns = ["*.kpd"] }]
            });
            token.ThrowIfCancellationRequested();
            if (file == null) return;
            string destinationPath = RequireLocalFile(file);
            if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
            {
                StatusText.Text = Text("KdbxDestinationExists");
                return;
            }

            var destinationDialog = new KdbxCredentialDialog(Text("KdbxDestinationCredentials"),
                Text("KdbxDestinationPassword"), Text("KdbxDestinationKeyFile"), Text("Browse"),
                Text("KdbxContinue"), Text("Cancel"), Text("KdbxKeyFileOnlyNote"));
            ProtectDialog(destinationDialog);
            var destinationCredential = await destinationDialog.ShowDialog<KdbxCredentialResult?>(this);
            token.ThrowIfCancellationRequested();
            if (destinationCredential == null) return;
            destinationPassword = destinationCredential.Password;
            destinationKeyFilePath = destinationCredential.KeyFilePath;
            destinationCredential = null;
            if (string.IsNullOrEmpty(destinationPassword) && string.IsNullOrWhiteSpace(destinationKeyFilePath))
            {
                StatusText.Text = Text("KdbxDestinationCredentialRequired");
                return;
            }
            if (destinationPassword.Length != 0 &&
                !await ConfirmAsync(Text("ConfirmNewVaultTitle"), Text("ConfirmMasterPassword"), destinationPassword)) return;
            token.ThrowIfCancellationRequested();

            using var destinationMaster = await Task.Run(() => string.IsNullOrWhiteSpace(destinationKeyFilePath)
                ? new MasterKey(destinationPassword)
                : MasterKey.FromFile(destinationPassword, destinationKeyFilePath), token);
            StatusText.Text = Text("KdbxImportWriting");
            var result = await KdbxImportService.CreateNewVaultAsync(plan, destinationPath, destinationMaster,
                _settings, Text("DefaultGeneralGroup"), Text("RecycleBin"), token,
                new Progress<VaultImportProgress>(value => ReportTransferProgress(token, Text("ImportProgress", value.Completed, value.Total))));
            VaultPath.Text = result.Session.Storage.FilePath;
            try
            {
                await PresentSessionAsync(result.Session, CancellationToken.None);
            }
            catch (Exception error)
            {
                AppLogger.LogError("The imported vault was saved but could not be opened in the current window", error);
                if (!_closing) StatusText.Text = Text("KdbxImportSavedOpenFailed", result.Session.Storage.FilePath);
                return;
            }
            if (!_closing) StatusText.Text = Text("KdbxImportCompleted", report.EntriesImported, report.GroupsImported,
                result.Session.Storage.FilePath);
        }
        finally
        {
            sourcePassword = string.Empty;
            sourceKeyFilePath = null;
            destinationPassword = string.Empty;
            destinationKeyFilePath = null;
            plan?.Dispose();
        }
    }, canCancel: true);
}
