using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Microsoft.Win32;
using SQLitePass.Core.Services;

namespace SQLitePass.Desktop.Services;

internal interface IDesktopSession : IDisposable
{
    void Protect(Window window, bool protectCapture);
}

internal sealed class DesktopSession : IDesktopSession
{
    public static IDesktopSession Create(Action<bool> lockStateChanged)
    {
        if (OperatingSystem.IsWindows()) return new WindowsDesktopSession(lockStateChanged);
        if (OperatingSystem.IsLinux()) return new LinuxDesktopSession(lockStateChanged);
        return new DesktopSession();
    }

    public void Protect(Window window, bool protectCapture) { }
    public void Dispose() { }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsDesktopSession : IDesktopSession
{
    private readonly Action<bool> _lockStateChanged;
    public WindowsDesktopSession(Action<bool> lockStateChanged)
    {
        _lockStateChanged = lockStateChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock) _lockStateChanged(true);
        else if (e.Reason == SessionSwitchReason.SessionUnlock) _lockStateChanged(false);
    }

    public void Protect(Window window, bool protectCapture)
    {
        if (window.TryGetPlatformHandle() is { HandleDescriptor: "HWND", Handle: var handle } && handle != IntPtr.Zero)
        {
            ApplyCaptureProtection(handle, protectCapture);
            return;
        }
        window.Opened += (_, _) =>
        {
            if (window.TryGetPlatformHandle() is { HandleDescriptor: "HWND", Handle: var openedHandle } && openedHandle != IntPtr.Zero)
                ApplyCaptureProtection(openedHandle, protectCapture);
        };
    }

    private static void ApplyCaptureProtection(IntPtr handle, bool protectCapture)
    {
        const uint none = 0;
        const uint excludeFromCapture = 0x11;
        const uint monitorOnly = 0x01;
        bool applied = protectCapture
            ? SetWindowDisplayAffinity(handle, excludeFromCapture) || SetWindowDisplayAffinity(handle, monitorOnly)
            : SetWindowDisplayAffinity(handle, none);
        if (!applied) AppLogger.LogWarning("Could not apply the screen capture preference to this window.");
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    public void Dispose() => SystemEvents.SessionSwitch -= OnSessionSwitch;
}
