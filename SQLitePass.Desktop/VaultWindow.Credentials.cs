using System.Security.Cryptography;
using Avalonia.Interactivity;

namespace SQLitePass.Desktop;

public sealed partial class VaultWindow
{
    private async void ChangeCredential_Click(object? sender, RoutedEventArgs e) => await ChangeCredentialAsync();

    internal Task ChangeCredentialAsync() => RunOperationAsync(async token =>
    {
        if (_session is not { } session || session.Storage.IsReadOnly) return;
        var dialog = new CredentialChangeDialog(Text, session.Metadata.UsesKeyFile);
        if (!await ShowOwnedAsync(dialog)) return;
        var (current, replacement) = await dialog.TakeCredentialsAsync(token);
        using (current)
        using (replacement)
        {
            try { await session.ChangeCredentialAsync(current, replacement, token); }
            catch (CryptographicException)
            {
                StatusText.Text = Text("CurrentCredentialIncorrect");
                return;
            }
        }
        token.ThrowIfCancellationRequested();
        await LoadPageAsync(session, _query, null, token);
        StatusText.Text = Text("CredentialChanged");
    });
}
