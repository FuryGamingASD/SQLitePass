using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;
using Velopack.Locators;
using Velopack.Sources;

namespace SQLitePass.Desktop.Services;

internal static class NativeAppImageUpdateVerification
{
    private const string Credential = "Synthetic-AppImage-update-credential!";
    private const string EntryTitle = "Synthetic entry preserved across AppImage upgrade";

    internal static async Task VerifyAsync(VaultWindow window, string[] args)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        var token = timeout.Token;
        string directory = args[^1];
        string vaultPath = Path.Combine(directory, "upgrade-fixture.kpd");
        var locator = VelopackLocator.Current;
        string? imagePath = Environment.GetEnvironmentVariable("APPIMAGE");
        if (locator.AppId != "SQLitePass" || locator.Channel != "linux" || locator.CurrentlyInstalledVersion == null || !File.Exists(imagePath))
            throw new InvalidOperationException("The upgrade probe needs a running official Linux AppImage.");

        var receipt = new Receipt(locator.CurrentlyInstalledVersion.ToString(),
            typeof(Program).Assembly.GetName().Version!.ToString(), imagePath);
        if (args[0] == "--verify-appimage-restarted")
        {
            if (!window.FindControl<ScrollViewer>("UnlockPanel")!.IsVisible ||
                !string.IsNullOrEmpty(window.FindControl<TextBox>("MasterPassword")!.Text))
                throw new InvalidOperationException("The updated app did not start with a locked vault.");
            await OpenFixtureAsync(window, vaultPath, token);
            await window.LockVaultAsync().WaitAsync(token);
            WriteReceipt(directory, "after", receipt);
            window.RequestExit();
            return;
        }

        await CreateFixtureAsync(vaultPath, token);
        await OpenFixtureAsync(window, vaultPath, token);
        if (window.NativeClipboard is not { } clipboard ||
            !await clipboard.CopyAsync("Synthetic AppImage update clipboard test", TimeSpan.FromMinutes(2), token))
            throw new InvalidOperationException("The old AppImage could not seed the verification clipboard.");
        WriteReceipt(directory, "before", receipt);
        var update = window.CheckUpdatesAsync(new SimpleFileSource(new DirectoryInfo(args[1])), ["--verify-appimage-restarted", directory]);
        while (window.OwnedWindows.Count == 0 && !update.IsCompleted) await Task.Delay(40, token);
        var confirmation = window.OwnedWindows.SingleOrDefault()
            ?? throw new InvalidOperationException($"No update confirmation was shown. Status: {window.FindControl<TextBlock>("StatusText")!.Text}");
        confirmation.GetLogicalDescendants().OfType<Button>().Single(button => !button.IsCancel)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!await update.WaitAsync(token) || window.FindControl<Grid>("VaultPanel")!.IsVisible ||
            !string.IsNullOrEmpty(await window.Clipboard!.TryGetTextAsync()))
            throw new InvalidOperationException("The old AppImage did not lock, clear its clipboard and schedule the update.");
        WriteReceipt(directory, "scheduled", receipt);
    }

    private static async Task CreateFixtureAsync(string path, CancellationToken token)
    {
        using var master = new MasterKey(Credential);
        await using var session = await VaultSession.CreateAsync(path, master, new AppSettings { EnableDualFileBackup = false },
            "General", "Recycle Bin", token);
        await session.Storage.SaveEntriesBatchAsync([new PwEntry
        {
            Title = EntryTitle,
            UserName = "Synthetic",
            GroupId = session.Metadata.RootGroupId!.Value
        }], session.Crypto);
    }

    private static async Task OpenFixtureAsync(VaultWindow window, string path, CancellationToken token)
    {
        window.FindControl<TextBox>("VaultPath")!.Text = path;
        window.FindControl<TextBox>("MasterPassword")!.Text = Credential;
        await window.OpenVaultAsync().WaitAsync(token);
        if (!window.FindControl<ListBox>("Entries")!.Items.OfType<EntryRow>().Any(row => row.Title == EntryTitle))
            throw new InvalidOperationException("The AppImage could not open the synthetic vault entry.");
    }

    private static void WriteReceipt(string directory, string stage, Receipt receipt)
    {
        string path = Path.Combine(directory, stage + ".json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(receipt));
        File.Move(path + ".tmp", path);
    }

    private sealed record Receipt(string Version, string AssemblyVersion, string AppImagePath);
}
