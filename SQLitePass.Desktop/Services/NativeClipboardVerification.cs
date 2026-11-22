using System.Diagnostics;
using System.Text;
using Avalonia.Input.Platform;

namespace SQLitePass.Desktop.Services;

// Runs exclusively in a verification desktop, using synthetic clipboard data.
internal static class NativeClipboardVerification
{
    private const string InitialText = "SQLitePass clipboard fixture Ω 🎛";
    private const string ReplacementText = "Synthetic clipboard from another application";

    internal static async Task VerifyAsync(VaultWindow window)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(32));
        var token = timeout.Token;
        var clipboard = window.NativeClipboard ?? throw new InvalidOperationException("Native clipboard backend unavailable.");
        var read = window.Clipboard ?? throw new InvalidOperationException("Native clipboard reader unavailable.");
        var expired = new TaskCompletionSource<ClipboardClearResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnExpired(ClipboardClearResult result) => expired.TrySetResult(result);
        clipboard.Expired += OnExpired;
        try
        {
            if (!await clipboard.CopyAsync(InitialText, TimeSpan.FromSeconds(6), token))
                throw new InvalidOperationException("The native clipboard rejected a synthetic copy.");
            if (await ReadFromPeerAsync(token) != InitialText)
                throw new InvalidOperationException("An external process did not receive the copied text intact.");
            await VerifyPrivacyFormatsAsync(window, token);
            if (await expired.Task.WaitAsync(token) != ClipboardClearResult.Cleared || !string.IsNullOrEmpty(await read.TryGetTextAsync()))
                throw new InvalidOperationException("The session clipboard did not clear at its deadline.");
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
        Console.WriteLine("Verified native clipboard round-trip, privacy formats, expiry, replacement and vault lock.");
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

    private static Task<string> ReadFromPeerAsync(CancellationToken token) => OperatingSystem.IsWindows()
        ? RunPeerAsync("pwsh", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); [Console]::Write((Get-Clipboard -Raw))"], token)
        : RunPeerAsync("xclip", ["-selection", "clipboard", "-out"], token);

    private static async Task<Process?> ReplaceFromPeerAsync(CancellationToken token)
    {
        if (OperatingSystem.IsWindows())
        {
            await RunPeerAsync("pwsh", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "Set-Clipboard -Value 'Synthetic clipboard from another application'"], token);
            return null;
        }

        var start = Peer("xclip", ["-selection", "clipboard", "-in", "-quiet"]);
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

    private static async Task<string> RunPeerAsync(string executable, string[] arguments, CancellationToken token)
    {
        var start = Peer(executable, arguments);
        start.RedirectStandardOutput = true;
        start.StandardOutputEncoding = Encoding.UTF8;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the external clipboard probe.");
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            if (process.ExitCode != 0) throw new InvalidOperationException("The external clipboard probe returned a failure.");
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
