using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SQLitePass.Core.Services;

namespace SQLitePass.UI.Services
{
    /// <summary>
    /// Protects application windows from unauthorized screen recording, screenshots, and remote spying.
    /// Uses native Win32 SetWindowDisplayAffinity (WDA_EXCLUDEFROMCAPTURE / WDA_MONITOR).
    /// </summary>
    public static class WindowSecurityService
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        private const uint WDA_NONE = 0x00000000;
        private const uint WDA_MONITOR = 0x00000001;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        public static void ApplyProtection(Window window, bool protect)
        {
            if (!OperatingSystem.IsWindows()) return;

            try
            {
                var helper = new WindowInteropHelper(window);
                IntPtr handle = helper.Handle;

                if (handle == IntPtr.Zero)
                {
                    window.SourceInitialized += (s, e) =>
                    {
                        IntPtr h = new WindowInteropHelper(window).Handle;
                        SetAffinity(h, protect);
                    };
                    return;
                }

                SetAffinity(handle, protect);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"Failed to apply screen capture protection on {window.GetType().Name}: {ex.Message}");
            }
        }

        private static void SetAffinity(IntPtr handle, bool protect)
        {
            if (handle == IntPtr.Zero) return;

            if (!protect)
            {
                SetWindowDisplayAffinity(handle, WDA_NONE);
                return;
            }

            // Attempt WDA_EXCLUDEFROMCAPTURE (Windows 10 2004+, Windows 11)
            if (!SetWindowDisplayAffinity(handle, WDA_EXCLUDEFROMCAPTURE))
            {
                // Fallback for earlier Windows 10 versions
                SetWindowDisplayAffinity(handle, WDA_MONITOR);
            }
        }
    }
}
