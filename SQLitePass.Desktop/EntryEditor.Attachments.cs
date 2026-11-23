using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.Desktop;

public sealed partial class EntryEditor
{
    private void InitializeAttachments()
    {
        AddAttachment.Content = _text("AddAttachment", []);
        SaveAttachment.Content = _text("SaveAttachment", []);
        RemoveAttachment.Content = _text("Delete", []);
        Attachments.ItemsSource = _draft.Attachments;
    }

    private void Attachments_SelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        SaveAttachment.IsEnabled = RemoveAttachment.IsEnabled = Attachments.SelectedItem is PwAttachment;

    private async void AddAttachment_Click(object? sender, RoutedEventArgs e) => await AddAttachmentAsync(PickAttachmentAsync);

    internal Task AddAttachmentAsync(Func<CancellationToken, Task<string?>> chooseFile) => RunEditorOperationAsync(async token =>
    {
        await _editDraft.LoadAttachmentsAsync(token);
        token.ThrowIfCancellationRequested();
        var path = await chooseFile(token);
        token.ThrowIfCancellationRequested();
        if (path == null) return;
        var attachment = await AttachmentFileService.ReadAsync(path, token);
        if (token.IsCancellationRequested)
        {
            CryptographicOperations.ZeroMemory(attachment.Data);
            token.ThrowIfCancellationRequested();
        }
        attachment.EntryId = _draft.Id;
        _draft.Attachments.Add(attachment);
        MarkEdited();
        Attachments.SelectedItem = attachment;
    }, "AttachmentFailed");

    private async Task<string?> PickAttachmentAsync(CancellationToken token)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = _text("AttachmentSelect", []) }).WaitAsync(token);
        if (files.Count == 0) return null;
        return files[0].TryGetLocalPath() ?? throw new ArgumentException("A local attachment is required.");
    }

    private async void SaveAttachment_Click(object? sender, RoutedEventArgs e)
    {
        if (Attachments.SelectedItem is PwAttachment selected) await ExportAttachmentAsync(selected, PickAttachmentDestinationAsync);
    }

    internal Task ExportAttachmentAsync(PwAttachment attachment, Func<PwAttachment, CancellationToken, Task<string?>> chooseDestination) =>
        RunEditorOperationAsync(async token =>
        {
            string? path = await chooseDestination(attachment, token);
            token.ThrowIfCancellationRequested();
            if (path == null) return;
            string destination = Path.GetFullPath(path);
            if (_vaultPath is { } vault && (destination.Equals(vault, StringComparison.OrdinalIgnoreCase) ||
                destination.StartsWith(vault + ".", StringComparison.OrdinalIgnoreCase) ||
                destination.Equals(vault + "-wal", StringComparison.OrdinalIgnoreCase) ||
                destination.Equals(vault + "-shm", StringComparison.OrdinalIgnoreCase) ||
                destination.Equals(vault + "-journal", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("An attachment cannot replace the vault or its recovery files.", nameof(path));

            byte[] bytes = attachment.Data.ToArray();
            try { await AttachmentFileService.WriteAsync(destination, bytes, token); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }, "AttachmentFailed");

    private async Task<string?> PickAttachmentDestinationAsync(PwAttachment attachment, CancellationToken token)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = _text("AttachmentSave", []), SuggestedFileName = Path.GetFileName(attachment.FileName.Replace('\\', '/'))
        }).WaitAsync(token);
        return file == null ? null : file.TryGetLocalPath() ?? throw new ArgumentException("A local destination is required.");
    }

    private void RemoveAttachment_Click(object? sender, RoutedEventArgs e) => RemoveSelectedAttachment();

    internal void RemoveSelectedAttachment()
    {
        if (Attachments.SelectedItem is not PwAttachment attachment) return;
        CryptographicOperations.ZeroMemory(attachment.Data);
        _draft.Attachments.Remove(attachment);
        MarkEdited();
    }
}
