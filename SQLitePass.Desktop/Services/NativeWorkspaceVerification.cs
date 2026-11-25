using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
        VerifyNoSystemCaption(window);
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
            VerifyNoSystemCaption(window);
            if (window.FindControl<TextBlock>("WorkspaceTitle")!.Text != "SQLitePass")
                throw new InvalidOperationException("The workspace header must identify SQLitePass.");
            var groups = window.FindControl<TreeView>("Groups")!;
            var toolbar = window.FindControl<StackPanel>("VaultToolbar")!;
            if (!groups.IsEffectivelyVisible || string.IsNullOrWhiteSpace(AutomationProperties.GetName(groups)) ||
                !toolbar.IsEffectivelyVisible || !toolbar.Children.Contains(window.FindControl<Button>("LockVault")!) ||
                string.IsNullOrWhiteSpace(AutomationProperties.GetName(window.FindControl<Button>("VaultSettings")!)))
                throw new InvalidOperationException("The workspace did not expose the group tree and settings control.");

            window.ShowFromTray();
            var windowHandle = window.TryGetPlatformHandle()!.Handle;
            foreach (string name in new[] { "WindowMinimize", "WindowMaximize", "WindowClose" })
            {
                var button = window.FindControl<Button>(name)!;
                if (!button.IsEffectivelyVisible || string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)))
                    throw new InvalidOperationException("A workspace window control is missing or unlabeled.");
            }
            var maximize = window.FindControl<Button>("WindowMaximize")!;
            maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (!IsZoomed(windowHandle)) await Task.Delay(40, token);
            VerifyNoSystemCaption(window);
            maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (IsZoomed(windowHandle)) await Task.Delay(40, token);
            var minimize = window.FindControl<Button>("WindowMinimize")!;
            minimize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (!IsIconic(windowHandle)) await Task.Delay(40, token);
            window.ShowFromTray();
            while (IsIconic(windowHandle)) await Task.Delay(40, token);
            Console.WriteLine("Verified the integrated Windows controls and native maximize, restore and minimize.");

            var input = new WindowsAutoTypeInput();
            while (input.ForegroundWindow != windowHandle)
            {
                input.Activate(windowHandle);
                await Task.Delay(50, token);
            }
            window.UpdateLayout();
            await DragInsideAsync(FolderCenter(source.Name), FolderCenter(destination.Name), destination.Id);
            while (!toolbar.IsEffectivelyEnabled || window.FolderChoices.Single(choice => choice.Id == source.Id).Folder?.ParentId != destination.Id)
                await Task.Delay(45, token);
            Console.WriteLine("Verified Windows native drag from an unselected folder and the visible destination outline.");

            await window.SelectGroupAsync(window.FolderChoices.Single(choice => choice.Id == destination.Id)).WaitAsync(token);
            window.UpdateLayout();
            var entry = window.FindControl<ListBox>("Entries")!.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Name == "EntryTitleCell");
            await DragInsideAsync(entry.PointToScreen(new Point(30, entry.Bounds.Height / 2)), FolderCenter(source.Name), source.Id);
            var treeSelection = window.FindControl<TreeView>("Groups")!;
            while (!toolbar.IsEffectivelyEnabled || (treeSelection.SelectedItem as GroupChoice)?.Id != source.Id)
                await Task.Delay(45, token);
            if (treeSelection.GetVisualDescendants().OfType<Border>().Any(border => border.Classes.Contains("drop-fill") && border.IsEffectivelyVisible))
                throw new InvalidOperationException("The destination outline remained visible after the drop.");
            Console.WriteLine("Verified Windows native entry drag into a folder and its destination outline.");

            await NativeAutoTypeVerification.VerifyGlobalShortcutAsync(window, directory, token);

            PixelPoint FolderCenter(string name)
            {
                var label = groups.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Text == name);
                return label.PointToScreen(new Point(Math.Min(34, label.Bounds.Width / 2), label.Bounds.Height / 2));
            }

            async Task DragInsideAsync(PixelPoint from, PixelPoint to, Guid target)
            {
                var outline = groups.GetVisualDescendants().OfType<Border>().Single(border =>
                    border.Classes.Contains("drop-fill") && border.DataContext is GroupChoice choice && choice.Id == target);
                bool highlighted = false;
                EventHandler<DragEventArgs> observe = (_, args) =>
                {
                    if (args.DragEffects == DragDropEffects.Move && outline.IsEffectivelyVisible) highlighted = true;
                };
                groups.AddHandler(DragDrop.DragOverEvent, observe, RoutingStrategies.Bubble, handledEventsToo: true);
                try
                {
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
                    if (!highlighted) throw new InvalidOperationException("A valid folder destination did not show the inside-drop outline.");
                }
                finally { groups.RemoveHandler(DragDrop.DragOverEvent, observe); }
            }
        }
        finally { await window.LockVaultAsync(); }
        VerifyNoSystemCaption(window);

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

    private static void VerifyNoSystemCaption(VaultWindow window)
    {
        window.UpdateLayout();
        if (!GetWindowRect(window.TryGetPlatformHandle()!.Handle, out var bounds))
            throw new InvalidOperationException("The Windows workspace frame could not be measured.");
        double captionHeight = (window.PointToScreen(default).Y - bounds.Top) / window.RenderScaling;
        if (captionHeight >= 20)
            throw new InvalidOperationException($"Unexpected Windows system caption ({captionHeight:F1} DIP).");
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out WindowRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint x, uint y, uint data, IntPtr extraInfo);
}
