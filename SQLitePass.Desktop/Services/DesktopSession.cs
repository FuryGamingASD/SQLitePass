using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Microsoft.Win32;
using SQLitePass.Core.Services;

namespace SQLitePass.Desktop.Services;

internal interface IDesktopSession : IDisposable
{
    void Protect(Window window);
}

internal sealed class DesktopSession : IDesktopSession
{
    public static IDesktopSession Create(Action<bool> lockStateChanged, bool protectCapture)
    {
        if (OperatingSystem.IsWindows()) return new WindowsDesktopSession(lockStateChanged, protectCapture);
        if (OperatingSystem.IsLinux()) return new LinuxDesktopSession(lockStateChanged);
        return new DesktopSession();
    }

    public void Protect(Window window) { }
    public void Dispose() { }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsDesktopSession : IDesktopSession
{
    private readonly Action<bool> _lockStateChanged;
    private readonly bool _protectCapture;

    public WindowsDesktopSession(Action<bool> lockStateChanged, bool protectCapture)
    {
        _lockStateChanged = lockStateChanged;
        _protectCapture = protectCapture;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock) _lockStateChanged(true);
        else if (e.Reason == SessionSwitchReason.SessionUnlock) _lockStateChanged(false);
    }

    public void Protect(Window window)
    {
        if (!_protectCapture) return;
        if (window.TryGetPlatformHandle() is { HandleDescriptor: "HWND", Handle: var handle } && handle != IntPtr.Zero)
        {
            ApplyCaptureExclusion(handle);
            return;
        }
        window.Opened += (_, _) =>
        {
            if (window.TryGetPlatformHandle() is { HandleDescriptor: "HWND", Handle: var openedHandle } && openedHandle != IntPtr.Zero)
                ApplyCaptureExclusion(openedHandle);
        };
    }

    private static void ApplyCaptureExclusion(IntPtr handle)
    {
        const uint excludeFromCapture = 0x11;
        const uint monitorOnly = 0x01;
        if (!SetWindowDisplayAffinity(handle, excludeFromCapture) && !SetWindowDisplayAffinity(handle, monitorOnly))
            AppLogger.LogWarning("Screen capture exclusion is unavailable for this window.");
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    public void Dispose() => SystemEvents.SessionSwitch -= OnSessionSwitch;
}
