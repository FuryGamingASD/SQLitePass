using System.Runtime.InteropServices;
using System.Net.Sockets;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Tmds.DBus.Protocol;

namespace SQLitePass.Desktop.Services;

internal sealed class X11SensitiveClipboard : ISensitiveClipboard
{
    private static readonly DataFormat<string> SensitiveHint = DataFormat.CreateStringPlatformFormat("x-kde-passwordManagerHint");
    private readonly IClipboard _clipboard;
    private readonly IntPtr _display;
    private readonly nuint _selection;
    private readonly nuint _clipboardManagerSelection;
    private bool _disposed;
    public ClipboardCopyBlockedReason CopyBlockedReason { get; private set; }

    private X11SensitiveClipboard(IClipboard clipboard, IntPtr display)
    {
        _clipboard = clipboard;
        _display = display;
        _selection = XInternAtom(display, "CLIPBOARD", 0);
        _clipboardManagerSelection = XInternAtom(display, "CLIPBOARD_MANAGER", 0);
    }

    internal async Task<bool> IsClipboardManagerActiveAsync()
    {
        if (_clipboardManagerSelection != 0 && XGetSelectionOwner(_display, _clipboardManagerSelection) != 0) return true;
        if (DBusAddress.Session is not { Length: > 0 } sessionAddress) return false;
        try
        {
            using var bus = new DBusConnection(sessionAddress);
            await bus.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(700));
            string[] services = await bus.ListServicesAsync().WaitAsync(TimeSpan.FromMilliseconds(700));
            return services.Contains("org.xfce.clipman", StringComparer.Ordinal);
        }
        catch (Exception error) when (error is DBusExceptionBase or TimeoutException or IOException or SocketException or ObjectDisposedException or ArgumentException)
        {
            return true;
        }
    }
    internal nuint CurrentClipboardOwner => XGetSelectionOwner(_display, _selection);

    internal static X11SensitiveClipboard? TryCreate(IClipboard clipboard)
    {
        var display = XOpenDisplay(IntPtr.Zero);
        return display == IntPtr.Zero ? null : new X11SensitiveClipboard(clipboard, display);
    }

    public async ValueTask<IClipboardLease?> TryCopyAsync(string text)
    {
        CopyBlockedReason = ClipboardCopyBlockedReason.None;
        if (_disposed || text.Contains('\0')) return null;
        if (await IsClipboardManagerActiveAsync() && AppContext.GetData("SQLitePass.AllowLinuxClipboardManagerProbe") is not true)
        {
            CopyBlockedReason = ClipboardCopyBlockedReason.LinuxClipboardManagerActive;
            return null;
        }
        var item = DataTransferItem.CreateText(text);
        // A non-text format prevents Avalonia from persisting the selection in an X11 clipboard manager.
        item.Set(SensitiveHint, "secret");
        var transfer = new DataTransfer();
        transfer.Add(item);
        await _clipboard.SetDataAsync(transfer);

        nuint owner = XGetSelectionOwner(_display, _selection);
        if (owner == 0 || !ReferenceEquals(await _clipboard.TryGetInProcessDataAsync(), transfer)) return null;
        return new X11Lease(this, owner, transfer);
    }

    private async ValueTask<ClipboardClearResult> ClearAsync(nuint expectedOwner, IAsyncDataTransfer transfer)
    {
        if (_disposed || !ReferenceEquals(await _clipboard.TryGetInProcessDataAsync(), transfer))
            return ClipboardClearResult.Replaced;

        // X11 has no compare-and-clear request. Serialize these requests on the same connection,
        // and never await or call Avalonia while the server is grabbed.
        XGrabServer(_display);
        try
        {
            if (XGetSelectionOwner(_display, _selection) != expectedOwner) return ClipboardClearResult.Replaced;
            XSetSelectionOwner(_display, _selection, 0, 0);
            return ClipboardClearResult.Cleared;
        }
        finally
        {
            XUngrabServer(_display);
            XFlush(_display);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        XCloseDisplay(_display);
    }

    private sealed class X11Lease(X11SensitiveClipboard clipboard, nuint owner, IAsyncDataTransfer transfer) : IClipboardLease
    {
        public ValueTask<ClipboardClearResult> TryClearAsync() => clipboard.ClearAsync(owner, transfer);
    }

    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern nuint XInternAtom(IntPtr display, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int onlyIfExists);
    [DllImport("libX11.so.6")] private static extern nuint XGetSelectionOwner(IntPtr display, nuint selection);
    [DllImport("libX11.so.6")] private static extern int XSetSelectionOwner(IntPtr display, nuint selection, nuint owner, nuint time);
    [DllImport("libX11.so.6")] private static extern int XGrabServer(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XUngrabServer(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XFlush(IntPtr display);
}
