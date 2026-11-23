using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.Desktop.Services;

[SupportedOSPlatform("windows")]
internal static class NativeWorkspaceVerification
{
    internal static async Task VerifyAsync(VaultWindow window)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(22));
        var token = deadline.Token;
        string directory = (string)AppContext.GetData("SQLitePass.SettingsDirectory")!;
        string path = Path.Combine(directory, "workspace-fixture.kpd");
        const string credential = "Synthetic-Windows-workspace-fixture!";
        using var master = new MasterKey(credential);
        var settings = new AppSettings { EnableDualFileBackup = false };
        var source = new PwGroup { Name = "Synthetic source folder" };
        var destination = new PwGroup { Name = "Synthetic destination folder" };
        await using (var seed = await VaultSession.CreateAsync(path, master, settings, "General", "Recycle bin", token))
        {
            source.ParentId = destination.ParentId = seed.Metadata.RootGroupId;
            await seed.Storage.SaveGroupsBatchAsync([source, destination], seed.Crypto);
            await NativeAutoTypeVerification.SeedShortcutEntryAsync(seed, destination.Id);
        }

        window.FindControl<TextBox>("VaultPath")!.Text = path;
        window.FindControl<TextBox>("MasterPassword")!.Text = credential;
        await window.OpenVaultAsync().WaitAsync(token);
        try
        {
            var groups = window.FindControl<TreeView>("Groups")!;
            var toolbar = window.FindControl<WrapPanel>("VaultToolbar")!;
            if (!groups.IsEffectivelyVisible || string.IsNullOrWhiteSpace(AutomationProperties.GetName(groups)) ||
                !toolbar.IsEffectivelyVisible || string.IsNullOrWhiteSpace(AutomationProperties.GetName(window.FindControl<Button>("VaultSettings")!)))
                throw new InvalidOperationException("The workspace did not expose the group tree and settings control.");

            window.ShowFromTray();
            var windowHandle = window.TryGetPlatformHandle()!.Handle;
            var input = new WindowsAutoTypeInput();
            while (input.ForegroundWindow != windowHandle)
            {
                input.Activate(windowHandle);
                await Task.Delay(50, token);
            }
            window.UpdateLayout();
            PixelPoint from = FolderCenter(source.Name);
            PixelPoint to = FolderCenter(destination.Name);
            await Task.Run(async () =>
            {
                MovePointer(from);
                await Task.Delay(120, token);
                mouse_event(2, 0, 0, 0, IntPtr.Zero);
                try
                {
                    MovePointer(new PixelPoint(from.X + 14, from.Y));
                    await Task.Delay(240, token);
                    MovePointer(to);
                    await Task.Delay(330, token);
                }
                finally { mouse_event(4, 0, 0, 0, IntPtr.Zero); }
            }, token);
            while (!toolbar.IsEffectivelyEnabled || window.FolderChoices.Single(choice => choice.Id == source.Id).Folder?.ParentId != destination.Id)
                await Task.Delay(45, token);
            Console.WriteLine("Verified Windows native drag from an unselected folder to another folder.");

            await NativeAutoTypeVerification.VerifyGlobalShortcutAsync(window, directory, token);

            PixelPoint FolderCenter(string name)
            {
                var label = groups.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Text == name);
                return label.PointToScreen(new Point(Math.Min(34, label.Bounds.Width / 2), label.Bounds.Height / 2));
            }
        }
        finally { await window.LockVaultAsync(); }

        await using var reopened = await VaultSession.OpenAsync(path, master, settings, () => Task.FromResult(false))
            ?? throw new InvalidOperationException("The synthetic workspace vault could not be reopened.");
        var persisted = await reopened.Storage.LoadAllGroupsAsync(reopened.Crypto);
        if (persisted.Single(group => group.Id == source.Id).ParentId != destination.Id)
            throw new InvalidOperationException("The folder placement did not persist.");
        Console.WriteLine("Verified Windows folder placement after reopening the synthetic vault.");
    }

    private static void MovePointer(PixelPoint point)
    {
        if (!SetCursorPos(point.X, point.Y)) throw new InvalidOperationException("The isolated desktop rejected cursor movement.");
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint x, uint y, uint data, IntPtr extraInfo);
}
