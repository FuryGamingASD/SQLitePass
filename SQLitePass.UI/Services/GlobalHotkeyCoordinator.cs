using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace SQLitePass.UI.Services
{
    public sealed class GlobalHotkeyCoordinator : IDisposable
    {
        private const int HOTKEY_ID = 9001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_NOREPEAT = 0x4000;
        private const uint VK_A = 0x41;
        private const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        private readonly Window _window;
        private readonly Action<IntPtr, string> _onHotkeyTriggered;
        private readonly Action? _onUnavailable;
        public bool IsRegistered { get; private set; }
        private HwndSource? _hwndSource;
        private IntPtr _windowHandle = IntPtr.Zero;
        private bool _isDisposed = false;

        public GlobalHotkeyCoordinator(Window window, Action<IntPtr, string> onHotkeyTriggered, Action? onUnavailable = null)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _onHotkeyTriggered = onHotkeyTriggered ?? throw new ArgumentNullException(nameof(onHotkeyTriggered));
            _onUnavailable = onUnavailable;

            if (_window.IsLoaded)
            {
                Register();
            }
            else
            {
                _window.Loaded += OnWindowLoaded;
            }
        }
        private void OnWindowLoaded(object sender, RoutedEventArgs e) => Register();

        private void Register()
        {
            if (_isDisposed || _windowHandle != IntPtr.Zero) return;
            var helper = new WindowInteropHelper(_window);
            _windowHandle = helper.Handle;
            if (_windowHandle != IntPtr.Zero)
            {
                _hwndSource = HwndSource.FromHwnd(_windowHandle);
                _hwndSource?.AddHook(HwndHook);
                IsRegistered = _hwndSource != null && RegisterHotKey(_windowHandle, HOTKEY_ID, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_A);
                if (!IsRegistered)
                {
                    SQLitePass.Core.Services.AppLogger.LogWarning("Scorciatoia globale Auto-Type non registrata.");
                    _onUnavailable?.Invoke();
                }
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (IsRegistered && msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                IntPtr foregroundWin = GetForegroundWindow();
                var titleBuilder = new StringBuilder(256);
                GetWindowText(foregroundWin, titleBuilder, 256);
                string winTitle = titleBuilder.ToString();

                _onHotkeyTriggered?.Invoke(foregroundWin, winTitle);
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _window.Loaded -= OnWindowLoaded;

            if (_hwndSource != null && _windowHandle != IntPtr.Zero)
            {
                if (IsRegistered) UnregisterHotKey(_windowHandle, HOTKEY_ID);
                _hwndSource.RemoveHook(HwndHook);
                _hwndSource = null;
                _windowHandle = IntPtr.Zero;
            }
            IsRegistered = false;
        }
    }
}
