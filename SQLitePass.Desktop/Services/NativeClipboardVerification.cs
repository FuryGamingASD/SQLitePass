using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace SQLitePass.Desktop.Services;

// Runs exclusively in a verification desktop, using synthetic clipboard data.
internal static class NativeClipboardVerification
{
    private const string InitialText = "SQLitePass clipboard fixture Ω 🎛";
    private const string ReplacementText = "Synthetic clipboard from another application";
    private static bool InspectWindowsHistory => OperatingSystem.IsWindows() &&
        Environment.GetEnvironmentVariable("SQLITEPASS_VERIFY_WINDOWS_CLIPBOARD_HISTORY") == "1";
    private static bool UseWaylandPeer => OperatingSystem.IsLinux() &&
        Environment.GetEnvironmentVariable("SQLITEPASS_VERIFY_WAYLAND_CLIPBOARD") == "1";

    internal static async Task VerifyAsync(VaultWindow window)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(InspectWindowsHistory ? 120 : 32));
        var token = timeout.Token;
        var clipboard = window.NativeClipboard ?? throw new InvalidOperationException("Native clipboard backend unavailable.");
        var read = window.Clipboard ?? throw new InvalidOperationException("Native clipboard reader unavailable.");
        var expired = new TaskCompletionSource<ClipboardClearResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnExpired(ClipboardClearResult result) => expired.TrySetResult(result);
        clipboard.Expired += OnExpired;
        try
        {
            if (!await clipboard.CopyAsync(InitialText, TimeSpan.FromSeconds(InspectWindowsHistory ? 100 : 18), token))
                throw new InvalidOperationException("The native clipboard rejected a synthetic copy.");
            string copied = await ReadFromPeerAsync(token);
            while (UseWaylandPeer && copied.Length == 0)
            {
                await Task.Delay(40, token);
                copied = await ReadFromPeerAsync(token);
            }
            if (copied != InitialText)
                throw new InvalidOperationException("An external process did not receive the copied text intact.");
            await VerifyPrivacyFormatsAsync(window, token);
            if (InspectWindowsHistory)
            {
                window.FindControl<TextBlock>("LoginSubtitle")!.Text =
                    "Clipboard fixture active for 75 seconds. Press Win+V on both Windows PCs and look for: " + InitialText;
                Console.WriteLine("Windows history probe active for 75 seconds. Check Win+V on both signed-in test PCs for the synthetic fixture.");
                await Task.Delay(TimeSpan.FromSeconds(75), token);
            }
            clipboard.ShortenTimeout(TimeSpan.FromSeconds(6));
            var result = await expired.Task.WaitAsync(TimeSpan.FromSeconds(8), token);
            if (result != ClipboardClearResult.Cleared)
                throw new InvalidOperationException($"The session clipboard did not clear at its deadline. Result: {result}.");
            if (!string.IsNullOrEmpty(await read.TryGetTextAsync()))
                throw new InvalidOperationException("The native clipboard still held text after the deadline.");
            await VerifyWaylandPeerClearedAsync(token);
        }
        finally { clipboard.Expired -= OnExpired; }

        if (!await clipboard.CopyAsync(InitialText, TimeSpan.FromSeconds(30), token))
            throw new InvalidOperationException("Could not seed the clipboard replacement test.");
        using var peer = await ReplaceFromPeerAsync(token);
        try
        {
            while (await read.TryGetTextAsync() != ReplacementText)
                await Task.Delay(40, token);
            if (await clipboard.ClearAsync() != ClipboardClearResult.Replaced || await read.TryGetTextAsync() != ReplacementText)
                throw new InvalidOperationException("An external clipboard replacement was not preserved.");
        }
        finally { await StopPeerAsync(peer); }

        if (!await clipboard.CopyAsync(InitialText, TimeSpan.FromSeconds(30), token))
            throw new InvalidOperationException("Could not seed the vault-lock clipboard test.");
        await window.LockVaultAsync();
        if (!string.IsNullOrEmpty(await read.TryGetTextAsync()))
            throw new InvalidOperationException("Locking the vault did not clear its clipboard item.");
        await VerifyWaylandPeerClearedAsync(token);
        Console.WriteLine("Verified native clipboard round-trip, privacy formats, shortened expiry, replacement and vault lock.");
        if (UseWaylandPeer) Console.WriteLine("Verified clipboard round-trip and clearing between XWayland and a Wayland-native peer.");
    }

    private static async Task VerifyWaylandPeerClearedAsync(CancellationToken token)
    {
        if (!UseWaylandPeer) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        while ((await ReadFromPeerAsync(deadline.Token)).Length != 0) await Task.Delay(40, deadline.Token);
    }

    private static async Task VerifyPrivacyFormatsAsync(VaultWindow window, CancellationToken token)
    {
        if (OperatingSystem.IsWindows())
        {
            using var backend = WindowsSensitiveClipboard.Create(window.TryGetPlatformHandle()!.Handle);
            if (!backend.UsesPrivacyFormats()) throw new InvalidOperationException("Windows clipboard privacy formats were missing.");
        }
        else
        {
            string targets = await RunPeerAsync("xclip", ["-selection", "clipboard", "-target", "TARGETS", "-out"], token);
            if (!targets.Split('\n', StringSplitOptions.TrimEntries).Contains("x-kde-passwordManagerHint", StringComparer.Ordinal))
                throw new InvalidOperationException("The X11 clipboard did not advertise the sensitive-data hint.");
        }
    }

    private static Task<string> ReadFromPeerAsync(CancellationToken token)
    {
        if (OperatingSystem.IsWindows())
            return RunPeerAsync("pwsh", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); [Console]::Write((Get-Clipboard -Raw))"], token);
        if (UseWaylandPeer) return RunPeerAsync("wl-paste", ["--no-newline", "--type", "text"], token, allowEmptySelection: true);
        return RunPeerAsync("xclip", ["-selection", "clipboard", "-out"], token);
    }

    private static async Task<Process?> ReplaceFromPeerAsync(CancellationToken token)
    {
        if (OperatingSystem.IsWindows())
        {
            await RunPeerAsync("pwsh", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "Set-Clipboard -Value 'Synthetic clipboard from another application'"], token);
            return null;
        }

        var start = UseWaylandPeer
            ? Peer("wl-copy", ["--foreground", "--type", "text/plain;charset=utf-8"])
            : Peer("xclip", ["-selection", "clipboard", "-in", "-quiet"]);
        start.RedirectStandardInput = true;
        start.StandardInputEncoding = new UTF8Encoding(false);
        var process = Process.Start(start) ?? throw new InvalidOperationException("Could not run the X11 clipboard peer.");
        try
        {
            await process.StandardInput.WriteAsync(ReplacementText.AsMemory(), token);
            process.StandardInput.Close();
            return process;
        }
        catch
        {
            await StopPeerAsync(process);
            process.Dispose();
            throw;
        }
    }

    private static async Task<string> RunPeerAsync(string executable, string[] arguments, CancellationToken token, bool allowEmptySelection = false)
    {
        var start = Peer(executable, arguments);
        start.RedirectStandardOutput = true;
        start.StandardOutputEncoding = Encoding.UTF8;
        start.RedirectStandardError = true;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the external clipboard probe.");
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(token);
            var errors = process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            if (process.ExitCode != 0)
            {
                string error = (await errors).Trim();
                if (allowEmptySelection && process.ExitCode == 1 && error.EndsWith("Nothing is copied", StringComparison.Ordinal)) return "";
                throw new InvalidOperationException($"The external clipboard probe {executable} returned {process.ExitCode}. {error}");
            }
            return await output;
        }
        finally { await StopPeerAsync(process); }
    }

    private static ProcessStartInfo Peer(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    private static async Task StopPeerAsync(Process? process)
    {
        if (process == null || process.HasExited) return;
        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) when (process.HasExited) { }
        await process.WaitForExitAsync();
    }
}
