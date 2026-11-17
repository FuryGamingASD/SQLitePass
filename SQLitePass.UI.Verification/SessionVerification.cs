using System.IO;
using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Database;
using SQLitePass.Core.Models;
using SQLitePass.UI;
using SQLitePass.UI.Dialogs;

namespace SQLitePass.UI.Verification;

// Exercise the actual WPF session and editor on an STA dispatcher, without showing a window.
internal static class SessionVerification
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private)!.GetValue(instance)!;
    private static Task InvokeTask(object instance, string name, params object[] arguments) => (Task)instance.GetType().GetMethod(name, Private)!.Invoke(instance, arguments)!;
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidDataException(reason); }

    public static async Task<object> VerifyAsync(string temporary)
    {
        Console.WriteLine("Session verification: creating synthetic vault.");
        using var master = new MasterKey("synthetic-session-verification");
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        using var crypto = CryptoEngine.FromMasterKey(master, salt, 1, 8192, 1);
        var metadata = new VaultMetadata { SaltBase64 = Convert.ToBase64String(salt), VerificationCanary = crypto.GenerateCanaryToken(), KdfIterations = 1, KdfMemoryKb = 8192, KdfParallelism = 1 };
        var root = new PwGroup { Name = "Synthetic session", IsRoot = true };
        var child = new PwGroup { Name = "Synthetic child", ParentId = root.Id };
        var source = new PwEntry { GroupId = child.Id, Title = "Synthetic entry", UserName = "synthetic user", Password = "synthetic password", Tags = "work", ExpiryDate = DateTime.Today.AddDays(-1) };
        using var storage = await SqliteStorageProvider.CreateVaultAsync(Path.Combine(temporary, "session.kpd"), metadata, crypto, [root, child], [source], false);
        Console.WriteLine("Session verification: constructing window.");
        var main = new MainWindow(storage, crypto, metadata, enableDesktopIntegration: false);
        try
        {
            Console.WriteLine("Session verification: loading rows.");
            await InvokeTask(main, "RefreshDataAsync");

            Console.WriteLine("Session verification: preparing editor.");
            var entries = Field<IList>(main, "_visibleEntries");
            Require(entries.Count == 1, "The provider did not load the synthetic row into the UI.");
            var loadedRoot = (PwGroup)Field<IList>(main, "_rootGroups")[0]!;
            main.GetType().GetField("_selectedGroup", Private)!.SetValue(main, loadedRoot.Children.Single(group => group.Id == child.Id));
            await InvokeTask(main, "RefreshDataAsync");
            Require(Field<TextBlock>(main, "TxtEntryScope").Text == "Synthetic session › Synthetic child", "Selected folder breadcrumb is incorrect.");
            await main.ApplyVerificationFiltersAsync("other", EntryExpiryFilter.All);
            Require(entries.Count == 0, "The UI tag query did not filter the provider result.");
            await main.ApplyVerificationFiltersAsync("work", EntryExpiryFilter.Expired);
            Require(entries.Count == 1, "Combined UI filters did not return the expired synthetic entry.");
            var loaded = (PwEntry)entries[0]!;
            foreach (string cursor in new[] { "_pageCursor", "_nextPageCursor", "_previousPageCursor" })
                main.GetType().GetField(cursor, Private)!.SetValue(main, new EntryPageCursor("synthetic sort secret", source.Id, "synthetic", "synthetic"));
            Field<DataGrid>(main, "DgEntries").SelectedItem = loaded;
            new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
            var editor = new EntryEditDialog(source) { Owner = main };
            var draft = editor.ResultEntry;
            var user = Field<TextBox>(editor, "TxtUserName");
            var masked = Field<PasswordBox>(editor, "TxtPasswordMasked");
            var plain = Field<TextBox>(editor, "TxtPasswordPlain");
            Require(masked.Password.Length > 0 && user.Text.Length > 0, "Editor setup did not contain synthetic credentials.");
            Field<SQLitePass.UI.Services.TimedSecretReveal>(editor, "_passwordReveal").Reveal();
            user.Text = "synthetic unsaved edit";
            Field<SQLitePass.UI.Services.TimedSecretReveal>(main, "_previewReveal").Reveal();
            Field<SQLitePass.UI.Services.TimedSecretReveal>(main, "_columnReveal").Reveal();
            Console.WriteLine("Session verification: locking.");
            await InvokeTask(main, "LockVaultAsync", false);
            Console.WriteLine("Session verification: checking cleared state.");
            Require(Field<bool>(main, "_isLocked") && !Field<bool>(main, "_isLocking"), "Session did not finish locking.");
            Require(entries.Count == 0 && Field<IList>(main, "_rootGroups").Count == 0, "Vault controls retain session rows.");
            Require(loaded.Password.Length == 0 && loaded.UserName.Length == 0 && draft.Password.Length == 0, "Session/editor models retain credentials after lock.");
            Require(masked.Password.Length == 0 && plain.Text.Length == 0 && user.Text.Length == 0, "Editor controls retain credentials after lock.");
            Require(crypto.IsDisposed && Field<FrameworkElement>(main, "LockedOverlay").IsEnabled, "Key disposal or locked overlay state is incorrect.");
            Require(main.OwnedWindows.Count == 0, "Owned dialogs remain open after lock.");
            Require(new[] { "_pageCursor", "_nextPageCursor", "_previousPageCursor" }.All(name => main.GetType().GetField(name, Private)!.GetValue(main) == null), "Paging cursors retain session sort values after lock.");
            source.ClearSecrets();
            Require(!Field<SQLitePass.UI.Services.TimedSecretReveal>(main, "_columnReveal").IsRevealed &&
                Field<TextBox>(main, "TxtTagFilter").Text.Length == 0 && Field<TextBlock>(main, "TxtEntryScope").Text.Length == 0, "Lock retained reveal/filter/breadcrumb state.");
            return new { SyntheticOnly = true, WindowsShown = false, DesktopInputUsed = false, RowsCleared = true, PagingCursorsCleared = true, ProviderLoadingTested = true, UiFiltersTested = true, RevealsAndFiltersCleared = true, EditorCleared = true, KeyDisposed = true, OwnedDialogsClosed = true };
        }
        finally { main.Close(); source.ClearSecrets(); }
    }

}
