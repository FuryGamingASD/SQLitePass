using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SQLitePass.Core.Services;
using SQLitePass.Desktop.Services;

namespace SQLitePass.Desktop;

public sealed partial class VaultWindow
{
    private bool _autoTypeAvailable;
    private bool _autoTypeInProgress;
    private WindowsAutoTypeHotkey? _autoTypeHotkey;

    private void InitializeAutoType()
    {
        using var linuxInput = OperatingSystem.IsLinux() ? X11AutoTypeInput.TryCreate() : null;
        _autoTypeAvailable = OperatingSystem.IsWindows() || linuxInput != null;
        PropertyChanged += (_, change) =>
        {
            if ((change.Property == WindowStateProperty && WindowState == WindowState.Minimized) ||
                (change.Property == IsVisibleProperty && !IsVisible)) StopAutoType();
        };
        UpdateVaultCommands();
    }

    private void StopAutoType()
    {
        if (!_autoTypeInProgress) return;
        _operation?.Cancel();
        _ownedDialog?.Close(false);
    }

    private async void AutoTypeEntry_Click(object? sender, RoutedEventArgs e)
    {
        if (Entries.SelectedItem is EntryRow row) await RunOperationAsync(token => AutoTypeEntryAsync(row, token), canCancel: true);
    }

    private void RegisterGlobalAutoType()
    {
        if (_desktopSession == null || !OperatingSystem.IsWindows()) return;
        _autoTypeHotkey = WindowsAutoTypeHotkey.TryRegister(this, GlobalAutoTypePressed);
        if (_autoTypeHotkey == null) StatusText.Text = Text("HotkeyUnavailableStatus");
    }

    private void GlobalAutoTypePressed()
    {
        if (!OperatingSystem.IsWindows() || _session is not { } session || _systemLocked || _closing || _operation != null) return;
        var input = new WindowsAutoTypeInput(allowActivation: false);
        if (ReadAutoTypeTarget(input) is not { } target) return;
        Dispatcher.UIThread.Post(async () =>
        {
            if (!ReferenceEquals(_session, session)) return;
            await RunOperationAsync(token => AutoTypeEntryAsync(null, token, target), canCancel: true);
        });
    }

    private async Task AutoTypeEntryAsync(EntryRow? row, CancellationToken token, AutoTypeTarget? capturedTarget = null)
    {
        if (_session is not { } session || (row != null && _query.OnlyRecycleBin)) return;
        using var linuxInput = OperatingSystem.IsLinux() ? X11AutoTypeInput.TryCreate() : null;
        IAutoTypeInput? input = OperatingSystem.IsWindows() ? new WindowsAutoTypeInput(allowActivation: false) : linuxInput;
        if (input == null)
        {
            StatusText.Text = Text("AutoTypeStopped");
            return;
        }

        _autoTypeInProgress = true;
        if (capturedTarget == null) StatusText.Text = Text("AutoTypeSelectDestination");
        try
        {
            var target = capturedTarget ?? await WaitForAutoTypeTargetAsync(input, expected: null, token);
            var matches = await session.Storage.FindAutoTypeCandidatesAsync(session.Crypto, target.Title, row?.Id, row?.Revision, token);
            if (matches.Count != 1)
            {
                if (capturedTarget != null) ShowFromTray();
                StatusText.Text = Text(row == null ? "AutoTypeGlobalNoUniqueMatch" : "AutoTypeEntryUnavailable");
                return;
            }

            token.ThrowIfCancellationRequested();
            if (ReadAutoTypeTarget(input) != target) throw new OperationCanceledException();
            var entry = matches[0];
            string process = $"{AutoTypeService.GetWindowProcessName(target.ProcessId)} ({target.ProcessId})";
            ShowFromTray();
            bool accepted = await ConfirmAsync(Text("AutoTypeConfirmTitle"),
                Text("AutoTypeConfirmText", VisibleAutoTypeTitle(entry.Title), VisibleAutoTypeTitle(target.Title), process)
                + "\n\n" + Text("AutoTypeReturnToDestination"), acceptLabel: Text("AutoTypeConfirmAction"));
            if (!accepted)
            {
                StatusText.Text = Text("AutoTypeStopped");
                return;
            }
            token.ThrowIfCancellationRequested();
            StatusText.Text = Text("AutoTypeReturnToDestination");
            await WaitForAutoTypeTargetAsync(input, target, token);

            var confirmed = await session.Storage.LoadAutoTypeEntryAsync(entry.Id, entry.Revision, session.Crypto, token);
            try
            {
                await AutoTypeService.ExecuteAutoTypeAsync(confirmed, target.Handle, 250, token, input, target.Title, target.ProcessId);
                StatusText.Text = "";
            }
            finally { confirmed.ClearSecrets(); }
        }
        catch (Exception ex) when ((ex is OperationCanceledException && !token.IsCancellationRequested) ||
            ex is Win32Exception or ArgumentException or TimeoutException or AutoTypeLayoutException)
        {
            StatusText.Text = Text(ex is AutoTypeLayoutException ? "AutoTypeUnsupportedLayout" : "AutoTypeStopped");
        }
        finally { _autoTypeInProgress = false; }
    }

    private async Task<AutoTypeTarget> WaitForAutoTypeTargetAsync(IAutoTypeInput input, AutoTypeTarget? expected, CancellationToken token)
    {
        long start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(start) < TimeSpan.FromSeconds(12))
        {
            token.ThrowIfCancellationRequested();
            if (WindowState == WindowState.Minimized || !IsVisible) throw new OperationCanceledException();
            var target = ReadAutoTypeTarget(input);
            if (target != null && (expected == null || target == expected))
            {
                await Task.Delay(350, token);
                if (ReadAutoTypeTarget(input) == target) return target;
            }
            await Task.Delay(100, token);
        }
        throw new TimeoutException();
    }

    private static AutoTypeTarget? ReadAutoTypeTarget(IAutoTypeInput input)
    {
        IntPtr window = input.ForegroundWindow;
        if (window == IntPtr.Zero) return null;
        uint pid = input.ProcessId(window);
        if (pid == 0 || pid == Environment.ProcessId) return null;
        string title = input.WindowTitle(window);
        return !string.IsNullOrWhiteSpace(title) && input.ForegroundWindow == window && input.ProcessId(window) == pid
            ? new AutoTypeTarget(window, title, pid) : null;
    }

    private static string VisibleAutoTypeTitle(string title) =>
        string.Concat(title.Take(240).Select(character => char.IsControl(character) ||
            char.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.Format ? ' ' : character));

    private sealed record AutoTypeTarget(IntPtr Handle, string Title, uint ProcessId);
}
