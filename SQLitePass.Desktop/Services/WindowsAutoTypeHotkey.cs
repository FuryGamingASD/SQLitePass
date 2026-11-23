using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace SQLitePass.Desktop.Services;

internal sealed class WindowsAutoTypeHotkey : IDisposable
{
    private const int HotkeyId = 9001;
    private const uint HotkeyMessage = 0x0312;
    private const uint ControlAltNoRepeat = 0x4003;
    private const uint VirtualKeyA = 0x41;
    private readonly Window _window;
    private readonly Action _trigger;
    private IntPtr _handle;

    private WindowsAutoTypeHotkey(Window window, IntPtr handle, Action trigger)
    {
        _window = window;
        _handle = handle;
        _trigger = trigger;
    }

    internal static WindowsAutoTypeHotkey? TryRegister(Window window, Action trigger)
    {
        if (!OperatingSystem.IsWindows() ||
            window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND", Handle: var handle } ||
            handle == IntPtr.Zero || !RegisterHotKey(handle, HotkeyId, ControlAltNoRepeat, VirtualKeyA))
            return null;

        var shortcut = new WindowsAutoTypeHotkey(window, handle, trigger);
        try
        {
            Win32Properties.AddWndProcHookCallback(window, shortcut.HandleMessage);
            return shortcut;
        }
        catch
        {
            UnregisterHotKey(handle, HotkeyId);
            throw;
        }
    }

    private IntPtr HandleMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == HotkeyMessage && wParam == HotkeyId && _handle != IntPtr.Zero)
        {
            handled = true;
            _trigger();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        UnregisterHotKey(_handle, HotkeyId);
        _handle = IntPtr.Zero;
        Win32Properties.RemoveWndProcHookCallback(_window, HandleMessage);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr handle, int id);
}
