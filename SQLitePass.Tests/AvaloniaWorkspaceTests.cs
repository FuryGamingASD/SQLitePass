using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;
using SQLitePass.Desktop;
using SQLitePass.Desktop.Services;

namespace SQLitePass.Tests;

public sealed partial class AvaloniaVaultTests
{
    [Fact]
    public async Task SharedWorkspaceRestoresFolderNavigationAndEntryGestures()
    {
        var directory = Directory.CreateTempSubdirectory("SQLitePass-Avalonia-workspace-");
        string path = Path.Combine(directory.FullName, "fixture.kpd");
        const string password = "Synthetic-workspace-passphrase!";
        const string entrySecret = "Synthetic-entry-password!";
        var store = new AppSettingsStore(Path.Combine(directory.FullName, "profile"));
        store.Save(new AppSettings { Language = "en", LastOpenedVaultPath = path, EnableDualFileBackup = false });
        try
        {
            using var master = new MasterKey(password);
            await using (var seed = await VaultSession.CreateAsync(path, master, store.Load(), "General", "Recycle bin"))
            {
                var parent = (await seed.Storage.LoadAllGroupsAsync(seed.Crypto)).Single(group => group.Name == "General");
                var nested = new PwGroup { ParentId = parent.Id, Name = "Nested group" };
                await seed.Storage.SaveGroupAsync(nested, seed.Crypto);
                var item = new PwEntry { GroupId = nested.Id, Title = "Synthetic entry", UserName = "demo-user", Password = entrySecret };
                try { await seed.Storage.SaveEntryAsync(item, seed.Crypto); }
                finally { item.ClearSecrets(); }
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var desktop = HeadlessUnitTestSession.StartNew(typeof(HeadlessStartup));
            await desktop.Dispatch(async () =>
            {
                var backend = new TestWorkspaceClipboard();
                var window = new VaultWindow(store.Load(), useNativeSession: false, new SessionClipboard(backend));
                var passwordBox = window.FindControl<TextBox>("MasterPassword")!;
                window.Show();
                try
                {
                    Assert.True(window.FindControl<Button>("LoginSettings")!.IsVisible);
                    Assert.True(window.FindControl<Button>("LoginTheme")!.IsVisible);
                    VerifyCompactUnlock();
                    var useKey = window.FindControl<CheckBox>("UseKeyFile")!;
                    double compactWidth = window.Bounds.Width;
                    double compactHeight = window.Bounds.Height;
                    useKey.IsChecked = true;
                    await WaitAsync(() => window.Bounds.Height > compactHeight, timeout.Token);
                    useKey.IsChecked = false;
                    await WaitAsync(() => window.Bounds.Height == compactHeight, timeout.Token);
                    await window.ToggleThemeAsync(dark => store.Update(settings => settings.DarkMode = dark));
                    Assert.False(store.Load().DarkMode);
                    Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);

                    passwordBox.Text = password;
                    window.FindControl<Button>("LoginReveal")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(passwordBox.RevealPassword);
                    await window.OpenVaultAsync();
                    Assert.False(passwordBox.RevealPassword);
                    Assert.Equal(WindowDecorations.BorderOnly, window.WindowDecorations);
                    Assert.True(window.CanResize);
                    Assert.Equal("SQLitePass", window.FindControl<TextBlock>("WorkspaceTitle")!.Text);
                    Assert.True(window.FindControl<Button>("VaultSettings")!.IsVisible);
                    Assert.True(window.FindControl<Button>("VaultTheme")!.IsVisible);
                    Assert.True(window.FindControl<WrapPanel>("VaultToolbar")!.IsVisible);
                    foreach (string name in new[] { "WindowMinimize", "WindowMaximize", "WindowClose" })
                        Assert.True(window.FindControl<Button>(name)!.IsEffectivelyVisible);

                    var tree = window.FindControl<TreeView>("Groups")!;
                    var emptyCreate = window.FindControl<Button>("EmptyNewEntry")!;
                    var addCommand = window.FindControl<MenuItem>("NewEntryCommand")!;
                    var emptyTrashCommand = window.FindControl<MenuItem>("EmptyRecycleBin")!;
                    var context = tree.ContextMenu!.Items.OfType<MenuItem>().ToArray();
                    Assert.Contains(context, item => Equals(item.Header, addCommand.Header));
                    Assert.Contains(context, item => Equals(item.Header, emptyTrashCommand.Header));
                    Assert.False(emptyCreate.IsEffectivelyVisible);
                    await window.SelectGroupAsync(window.FolderChoices.Single(group => group.IsRecycleBin));
                    Assert.False(emptyCreate.IsEffectivelyVisible);
                    Assert.True(emptyTrashCommand.IsEnabled);
                    await window.SelectGroupAsync(window.FolderChoices.Single(group => group.Caption == "General"));
                    Assert.True(emptyCreate.IsEffectivelyVisible);
                    Assert.True(emptyCreate.IsEffectivelyEnabled);

                    var parent = window.FolderChoices.Single(group => group.Caption == "General");
                    var nested = Assert.Single(parent.Children);
                    Assert.Equal("Nested group", nested.Caption);
                    Assert.True(tree.IsVisible);
                    parent.Expanded = true;
                    window.UpdateLayout();
                    var folderName = tree.GetVisualDescendants().OfType<TextBlock>().Single(label => label.Text == nested.Caption);
                    var folderPoint = CenterOf(folderName);
                    var folderPath = window.FindControl<TextBlock>("FolderPath")!;
                    string? previousPath = folderPath.Text;
                    window.MouseMove(folderPoint, RawInputModifiers.None);
                    window.MouseDown(folderPoint, MouseButton.Left, RawInputModifiers.None);
                    Assert.True(tree.IsEffectivelyEnabled);
                    Assert.Equal(previousPath, folderPath.Text);
                    window.MouseUp(folderPoint, MouseButton.Left, RawInputModifiers.None);
                    await WaitAsync(() => (tree.SelectedItem as GroupChoice)?.Id == nested.Id &&
                        folderPath.Text == nested.Name &&
                        window.FindControl<Grid>("VaultPanel")!.IsEnabled &&
                        window.FindControl<WrapPanel>("VaultToolbar")!.IsEnabled, timeout.Token);
                    var entries = window.FindControl<ListBox>("Entries")!;
                    var row = Assert.IsType<EntryRow>(Assert.Single(entries.Items));
                    Assert.Equal("••••••••", row.DisplayPassword);

                    DoubleClick("EntryUserCell");
                    await WaitAsync(() => backend.Text == "demo-user", timeout.Token);
                    await WaitAsync(() => entries.IsEnabled, timeout.Token);
                    DoubleClick("EntryPasswordCell");
                    await WaitAsync(() => backend.Text == entrySecret, timeout.Token);
                    await WaitAsync(() => entries.IsEnabled, timeout.Token);
                    Assert.Equal("••••••••", row.DisplayPassword);

                    DoubleClick("EntryTitleCell");
                    await WaitAsync(() => window.OwnedWindows.OfType<EntryEditor>().Any(), timeout.Token);
                    var editor = window.OwnedWindows.OfType<EntryEditor>().Single();
                    Assert.Equal("Synthetic entry", editor.FindControl<TextBox>("EntryTitle")!.Text);
                    var sections = editor.FindControl<TabControl>("Sections")!;
                    var credentials = editor.FindControl<ScrollViewer>("CredentialsScroll")!;
                    await WaitAsync(() => credentials.Viewport.Height > 0, timeout.Token);
                    Assert.Single(sections.Items.OfType<TabItem>().Select(tab => tab.Bounds.Y).Distinct());
                    Assert.True(credentials.Extent.Height <= credentials.Viewport.Height);

                    editor.Width = editor.MinWidth;
                    editor.Height = editor.MinHeight;
                    await WaitAsync(() => credentials.Extent.Height > credentials.Viewport.Height, timeout.Token);
                    Assert.Single(sections.Items.OfType<TabItem>().Select(tab => tab.Bounds.Y).Distinct());
                    var bar = credentials.GetVisualDescendants().OfType<ScrollBar>().Single(control =>
                        control.Name == "PART_VerticalScrollBar" && ReferenceEquals(control.TemplatedParent, credentials));
                    var titleField = editor.FindControl<TextBox>("EntryTitle")!;
                    double fieldRight = titleField.TranslatePoint(new Point(titleField.Bounds.Width, 0), editor)!.Value.X;
                    Assert.True(bar.IsEffectivelyVisible);
                    Assert.True(bar.TranslatePoint(default, editor)!.Value.X > fieldRight);
                    editor.FindControl<TextBox>("EntryTitle")!.Text = "Discard this synthetic change";
                    await WaitAsync(() => editor.FindControl<TextBlock>("EditedIndicator")!.IsVisible, timeout.Token);
                    editor.Close();
                    await WaitAsync(() => editor.OwnedWindows.Any(), timeout.Token);
                    await window.LockVaultAsync();
                    Assert.False(editor.IsVisible);
                    Assert.Null(backend.Text);
                    Assert.Empty(entries.Items);
                    Assert.Empty(tree.Items);
                    Assert.False(window.FindControl<Border>("EntryPreview")!.IsVisible);
                    await WaitAsync(() => window.Bounds.Width == compactWidth, timeout.Token);
                    VerifyCompactUnlock();

                    void DoubleClick(string name)
                    {
                        window.UpdateLayout();
                        var cell = window.GetVisualDescendants().OfType<Border>().Single(border => border.Name == name);
                        var point = CenterOf(cell);
                        window.MouseMove(point, RawInputModifiers.None);
                        for (int click = 0; click < 2; click++)
                        {
                            window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
                            window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
                        }
                    }

                    Point CenterOf(Control control) =>
                        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

                    void VerifyCompactUnlock()
                    {
                        window.UpdateLayout();
                        Assert.Equal(WindowDecorations.None, window.WindowDecorations);
                        Assert.False(window.CanResize);
                        var card = window.FindControl<Border>("UnlockCard")!;
                        Assert.Equal(window.Bounds.Width, card.Bounds.Width, 0);
                        Assert.InRange(window.Bounds.Height - card.Bounds.Height, 0, 40);
                    }
                }
                finally
                {
                    await window.LockVaultAsync();
                    var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.Closed += (_, _) => closed.TrySetResult();
                    window.Close();
                    await closed.Task.WaitAsync(timeout.Token);
                }
                return true;
            }, timeout.Token);
        }
        finally { directory.Delete(recursive: true); }
    }

    private static async Task WaitAsync(Func<bool> condition, CancellationToken token)
    {
        while (!condition()) await Task.Delay(20, token);
    }

    private sealed class TestWorkspaceClipboard : ISensitiveClipboard, IClipboardLease
    {
        public string? Text { get; private set; }

        public ValueTask<IClipboardLease?> TryCopyAsync(string text)
        {
            Text = text;
            return ValueTask.FromResult<IClipboardLease?>(this);
        }

        public ValueTask<ClipboardClearResult> TryClearAsync()
        {
            Text = null;
            return ValueTask.FromResult(ClipboardClearResult.Cleared);
        }

        public void Dispose() { }
    }
}
