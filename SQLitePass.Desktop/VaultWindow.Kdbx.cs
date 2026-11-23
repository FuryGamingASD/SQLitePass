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

        string sourcePassword = sourceCredential.Password;
        string? sourceKeyFilePath = sourceCredential.KeyFilePath;
        sourceCredential = null;
        KdbxImportPlan? plan = null;
        try
        {
            StatusText.Text = Text("KdbxImportReading");
            plan = await KdbxImportService.ReadAsync(sourcePath, sourcePassword, sourceKeyFilePath, token);
            token.ThrowIfCancellationRequested();

            var report = plan.Report;
            string summary = Text("KdbxImportCounts", report.EntriesImported, report.GroupsImported, report.RecycleBinEntriesSkipped);
            string preserved = string.Join("\n", report.PreservedData.Select(item => "• " + item));
            string unsupported = string.Join("\n", report.UnsupportedData.Select(item =>
                "• " + item.Feature + " (" + (item.Count?.ToString() ?? Text("KdbxUnknownCount")) + "): " + item.Description));
            string review = summary + "\n\n" + Text("KdbxPreservedHeading") + "\n" + preserved +
                "\n\n" + Text("KdbxUnsupportedHeading") + "\n" + unsupported;
            if (!await ConfirmAsync(Text("KdbxImportReviewTitle"), review)) return;
            token.ThrowIfCancellationRequested();

            string password = MasterPassword.Text ?? string.Empty;
            if (password.Length != 0 && !await ConfirmAsync(Text("ConfirmNewVaultTitle"), Text("ConfirmMasterPassword"), password)) return;
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

            using var destinationMaster = await ReadCredentialAsync(token);
            StatusText.Text = Text("KdbxImportWriting");
            var result = await KdbxImportService.CreateNewVaultAsync(plan, RequireLocalFile(file), destinationMaster,
                _settings, Text("DefaultGeneralGroup"), Text("RecycleBin"), token,
                new Progress<VaultImportProgress>(value => ReportTransferProgress(token, Text("ImportProgress", value.Completed, value.Total))));
            VaultPath.Text = result.Session.Storage.FilePath;
            await PresentSessionAsync(result.Session, CancellationToken.None);
            if (!_closing) StatusText.Text = Text("KdbxImportCompleted", report.EntriesImported, report.GroupsImported);
        }
        finally
        {
            sourcePassword = string.Empty;
            sourceKeyFilePath = null;
            plan?.Dispose();
        }
    }, canCancel: true);
}
