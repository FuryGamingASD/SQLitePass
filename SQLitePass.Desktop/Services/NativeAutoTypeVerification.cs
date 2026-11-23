using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.Desktop.Services;

internal static class NativeAutoTypeVerification
{
    private const string DestinationTitle = "SQLitePass Auto-Type verification target";
    private const string UserName = "SyntheticUser42";
    private const string Password = "Synthetic!@#€93";

    internal static async Task SeedShortcutEntryAsync(VaultSession seed, Guid folder)
    {
        var entry = new PwEntry
        {
            Title = DestinationTitle, GroupId = folder, UserName = UserName, Password = Password,
            AutoTypeEnabled = true, AutoTypeSequence = "{USERNAME}{TAB}{PASSWORD}{ENTER}"
        };
        try { await seed.Storage.SaveEntryAsync(entry, seed.Crypto); }
        finally { entry.ClearSecrets(); }
    }

    [SupportedOSPlatform("windows")]
    internal static async Task VerifyGlobalShortcutAsync(VaultWindow window, string directory, CancellationToken token)
    {
        string path = Path.Combine(directory, "autotype-shortcut.txt");
        using var peer = StartPeer(path, "complete");
        try
        {
            var input = new WindowsAutoTypeInput();
            IntPtr target = await WaitForPeerAsync(input, peer, token);
            if (input.ForegroundWindow != target) throw new InvalidOperationException("The shortcut target lost focus.");
            keybd_event(0x11, 0, 0, IntPtr.Zero);
            keybd_event(0x12, 0, 0, IntPtr.Zero);
            keybd_event(0x41, 0, 0, IntPtr.Zero);
            keybd_event(0x41, 0, 2, IntPtr.Zero);
            keybd_event(0x12, 0, 2, IntPtr.Zero);
            keybd_event(0x11, 0, 2, IntPtr.Zero);

            while (window.OwnedWindows.Count == 0) await Task.Delay(45, token);
            var dialog = window.OwnedWindows.Single();
            if (File.Exists(path) || !dialog.GetLogicalDescendants().OfType<TextBlock>().Any(label => label.Text?.Contains(DestinationTitle) == true))
                throw new InvalidOperationException("The shortcut did not ask for confirmation of the synthetic destination.");
            dialog.GetLogicalDescendants().OfType<Button>().Single(button => !button.IsCancel)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (input.ForegroundWindow != target)
            {
                input.Activate(target);
                await Task.Delay(45, token);
            }
            if (await ReadResultAsync(path, token) != UserName + "\n" + Password)
                throw new InvalidOperationException("Global Auto-Type did not send the verified synthetic sequence.");
            Console.WriteLine("Verified Windows Ctrl+Alt+A, destination confirmation and native Auto-Type into the verification peer.");
        }
        finally { await StopPeerAsync(peer, CancellationToken.None); }
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scanCode, uint flags, IntPtr extraInfo);

    internal static Window CreatePeerWindow(string path, string scenario)
    {
        var username = new TextBox { TabIndex = 0 };
        var password = new TextBox { TabIndex = 1 };
        var target = new Window
        {
            Title = DestinationTitle, Width = 400, Height = 180,
            Content = new StackPanel { Margin = new Thickness(16), Spacing = 12, Children = { username, password } }
        };
        target.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            target.Activate();
            username.Focus();
        }, DispatcherPriority.Background);

        void SaveResult()
        {
            string incoming = path + ".tmp";
            File.WriteAllText(incoming, username.Text + "\n" + password.Text);
            File.Move(incoming, path, overwrite: true);
        }

        if (scenario == "focus-change") username.TextChanged += (_, _) =>
        {
            if (string.IsNullOrEmpty(username.Text)) return;
            target.Hide();
            SaveResult();
        };
        else target.AddHandler(InputElement.KeyDownEvent, (_, args) =>
        {
            if (args.Key == Key.Enter) SaveResult();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        return target;
    }

    internal static async Task VerifyAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var directory = new DirectoryInfo((string)AppContext.GetData("SQLitePass.SettingsDirectory")!);
        using var linuxInput = OperatingSystem.IsLinux() ? X11AutoTypeInput.TryCreate() : null;
        IAutoTypeInput input = OperatingSystem.IsWindows() ? new WindowsAutoTypeInput(allowActivation: false) :
            linuxInput ?? throw new InvalidOperationException("The isolated desktop lacks an X11 Auto-Type adapter.");

        string completeResult = Path.Combine(directory.FullName, "autotype-complete.txt");
        using (var peer = StartPeer(completeResult, "complete"))
        {
            try
            {
                IntPtr target = await WaitForPeerAsync(input, peer, timeout.Token);
                if (linuxInput != null)
                {
                    await ExpectAbortAsync<AutoTypeLayoutException>(async () =>
                        await SendAsync(new PwEntry { AutoTypeSequence = "Prefix🙂" }, target, peer));
                }

                await SendAsync(new PwEntry { UserName = UserName, Password = Password, AutoTypeSequence = "{USERNAME}{TAB}{PASSWORD}{ENTER}" }, target, peer);
                if (await ReadResultAsync(completeResult, timeout.Token) != UserName + "\n" + Password)
                    throw new InvalidOperationException("Auto-Type did not enter the full synthetic sequence into the two fields.");
            }
            finally { await StopPeerAsync(peer, timeout.Token); }
        }

        string abortResult = Path.Combine(directory.FullName, "autotype-focus-change.txt");
        using (var peer = StartPeer(abortResult, "focus-change"))
        {
            try
            {
                IntPtr target = await WaitForPeerAsync(input, peer, timeout.Token);
                await ExpectAbortAsync<OperationCanceledException>(async () =>
                    await SendAsync(new PwEntry { AutoTypeSequence = "Z{DELAY 500}MustNotType" }, target, peer));
                if (await ReadResultAsync(abortResult, timeout.Token) != "Z\n")
                    throw new InvalidOperationException("Auto-Type did not stop after the destination lost focus.");
            }
            finally { await StopPeerAsync(peer, timeout.Token); }
        }

        Console.WriteLine("Verified native Auto-Type into a separate process and abort on focus loss.");

        Task SendAsync(PwEntry entry, IntPtr target, Process peer) => AutoTypeService.ExecuteAutoTypeAsync(entry, target, 250,
            timeout.Token, input, DestinationTitle, (uint)peer.Id);
    }

    private static async Task ExpectAbortAsync<TException>(Func<Task> send) where TException : Exception
    {
        try { await send(); }
        catch (TException) { return; }
        throw new InvalidOperationException("Auto-Type proceeded when it should have stopped.");
    }

    private static Process StartPeer(string result, string scenario)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        start.ArgumentList.Add("--verify-autotype-peer");
        start.ArgumentList.Add(result);
        start.ArgumentList.Add(scenario);
        return Process.Start(start) ?? throw new InvalidOperationException("Cannot start the Auto-Type verification peer.");
    }

    private static async Task StopPeerAsync(Process peer, CancellationToken token)
    {
        if (!peer.HasExited) peer.Kill(entireProcessTree: true);
        await peer.WaitForExitAsync(token);
    }

    private static async Task<IntPtr> WaitForPeerAsync(IAutoTypeInput input, Process peer, CancellationToken token)
    {
        using var focusDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        focusDeadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            while (!peer.HasExited)
            {
                IntPtr window = input.ForegroundWindow;
                if (window != IntPtr.Zero && input.ProcessId(window) == peer.Id && input.WindowTitle(window) == DestinationTitle) return window;
                await Task.Delay(50, focusDeadline.Token);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("Auto-Type did not identify the focused verification window and its process.");
        }
        throw new InvalidOperationException("The Auto-Type verification peer exited before accepting focus.");
    }

    private static async Task<string> ReadResultAsync(string path, CancellationToken token)
    {
        using var resultDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        resultDeadline.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            while (!File.Exists(path)) await Task.Delay(50, resultDeadline.Token);
            return await File.ReadAllTextAsync(path, resultDeadline.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("The Auto-Type verification peer did not acknowledge the sequence.");
        }
    }
}
