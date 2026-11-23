using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using SQLitePass.Core.Services;

namespace SQLitePass.Desktop.Services;

internal enum ClipboardClearResult { Cleared, Replaced, Busy }
internal enum ClipboardCopyBlockedReason { None, WindowsClipboardSyncEnabled, LinuxClipboardManagerActive, SecureWindowsClipboardUnavailable }

internal interface IClipboardLease
{
    ValueTask<ClipboardClearResult> TryClearAsync();
}

internal interface ISensitiveClipboard : IDisposable
{
    ClipboardCopyBlockedReason CopyBlockedReason => ClipboardCopyBlockedReason.None;
    ValueTask<IClipboardLease?> TryCopyAsync(string text);
}

internal sealed class SessionClipboard : IDisposable
{
    private readonly ISensitiveClipboard _backend;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private IClipboardLease? _lease;
    private long _copiedAt;
    private TimeSpan _timeout;
    private bool _disposed;

    internal SessionClipboard(ISensitiveClipboard backend)
    {
        _backend = backend;
        _timer.Tick += ExpireClipboard;
    }

    internal event Action<ClipboardClearResult>? Expired;
    internal ClipboardCopyBlockedReason CopyBlockedReason => _backend.CopyBlockedReason;
    internal Task<bool> HasX11ClipboardManagerAsync() => _backend is X11SensitiveClipboard x11
        ? x11.IsClipboardManagerActiveAsync()
        : Task.FromResult(false);
    internal nuint CurrentX11ClipboardOwner => _backend is X11SensitiveClipboard x11 ? x11.CurrentClipboardOwner : 0;

    internal static SessionClipboard? Create(Window window)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle == null || handle.Handle == IntPtr.Zero) return null;
        if (OperatingSystem.IsWindows() && handle.HandleDescriptor == "HWND")
            return new(WindowsSensitiveClipboard.Create(handle.Handle));
        if (OperatingSystem.IsLinux() && handle.HandleDescriptor == "XID" && window.Clipboard is { } clipboard &&
            X11SensitiveClipboard.TryCreate(clipboard) is { } x11)
            return new(x11);
        return null;
    }

    internal async Task<bool> CopyAsync(string text, TimeSpan timeout, CancellationToken token = default)
    {
        await _operations.WaitAsync(token);
        try
        {
            if (_disposed || text.Length == 0) return false;
            token.ThrowIfCancellationRequested();
            var lease = await _backend.TryCopyAsync(text);
            if (lease == null) return false;
            _lease = lease;
            _copiedAt = Stopwatch.GetTimestamp();
            _timeout = timeout;
            _timer.Start();
            return true;
        }
        finally { _operations.Release(); }
    }

    internal Task<ClipboardClearResult> ClearAsync() => ClearAsync(expected: null);

    internal void ShortenTimeout(TimeSpan timeout)
    {
        if (_lease != null && timeout < _timeout) _timeout = timeout;
    }

    private async Task<ClipboardClearResult> ClearAsync(IClipboardLease? expected)
    {
        await _operations.WaitAsync();
        try
        {
            if (_disposed || _lease == null || (expected != null && !ReferenceEquals(expected, _lease))) return ClipboardClearResult.Replaced;
            _timer.Stop();
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var result = await _lease.TryClearAsync();
                if (result != ClipboardClearResult.Busy)
                {
                    _lease = null;
                    return result;
                }
                if (attempt < 5) await Task.Delay(80);
            }
            return ClipboardClearResult.Busy;
        }
        catch (Exception error)
        {
            AppLogger.LogError("Unable to clear the session clipboard", error);
            return ClipboardClearResult.Busy;
        }
        finally { _operations.Release(); }
    }

    private async void ExpireClipboard(object? sender, EventArgs e)
    {
        if (_lease is not { } lease || Stopwatch.GetElapsedTime(_copiedAt) < _timeout) return;
        _timer.Stop();
        var result = await ClearAsync(lease);
        if (!_disposed) Expired?.Invoke(result);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _backend.Dispose();
    }
}
