using Avalonia.Controls;
using Avalonia.Interactivity;
using SQLitePass.Core.Services;
using SQLitePass.Desktop.Services;

namespace SQLitePass.Desktop;

public sealed partial class VaultWindow
{
    private DesktopTray? _tray;
    private bool _nativeIntegrationsStarted;
    private bool _hiddenInTray;
    private bool _exitRequested;

    internal bool TrayCanHide => _tray?.CanHide == true;

    private void InitializeTray()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) return;
        try
        {
            _tray = new DesktopTray(Text, ShowFromTray, LockFromTray, RequestExit, RestoreIfTrayLost);
        }
        catch (Exception error) { AppLogger.LogError("The system tray is unavailable", error); }
    }

    private bool HideInTray()
    {
        if (_tray?.CanHide != true || _closing || _ownedDialog != null) return false;
        _hiddenInTray = true;
        Hide();
        return true;
    }

    internal void ShowFromTray()
    {
        if (_closing) return;
        _hiddenInTray = false;
        if (!IsVisible)
        {
            Show();
            _desktopSession?.Protect(this, _settings.ProtectScreenCapture);
        }
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        _minimized = false;
        Activate();
    }

    private void RestoreIfTrayLost()
    {
        if (_hiddenInTray) ShowFromTray();
    }

    private async void LockFromTray()
    {
        if (!_closing) await LockVaultAsync();
    }

    private void ExitApplication_Click(object? sender, RoutedEventArgs e) => RequestExit();

    internal void StartInTray()
    {
        WindowState = WindowState.Minimized;
        Opened += HideAfterStartup;

        void HideAfterStartup(object? sender, EventArgs args)
        {
            Opened -= HideAfterStartup;
            if (!HideInTray()) ShowFromTray();
        }
    }

    internal void RequestExit()
    {
        _exitRequested = true;
        Close();
    }
}
