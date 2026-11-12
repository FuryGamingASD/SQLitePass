using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;
using SQLitePass.Core.Services;

namespace SQLitePass.UI.Services
{
    public sealed class InactivityLockCoordinator : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("kernel32.dll")]
        private static extern uint GetTickCount();

        private readonly DispatcherTimer _idleTimer;
        private readonly Action _onLockRequested;
        private readonly Func<bool> _canLockPredicate;
        private bool _isDisposed = false;

        public InactivityLockCoordinator(Action onLockRequested, Func<bool> canLockPredicate)
        {
            _onLockRequested = onLockRequested ?? throw new ArgumentNullException(nameof(onLockRequested));
            _canLockPredicate = canLockPredicate ?? (() => true);

            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _idleTimer.Tick += OnIdleTimerTick;
            _idleTimer.Start();

            SystemEvents.SessionSwitch += OnSessionSwitch;
        }

        private void OnIdleTimerTick(object? sender, EventArgs e)
        {
            if (!_canLockPredicate()) return;

            var settings = AppSettingsManager.Load();
            if (settings.AutoLockMinutes > 0)
            {
                var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
                if (GetLastInputInfo(ref lii))
                {
                    uint envTicks = GetTickCount();
                    uint idleMs = envTicks >= lii.dwTime ? (envTicks - lii.dwTime) : (uint.MaxValue - lii.dwTime + envTicks);
                    if (idleMs >= (uint)settings.AutoLockMinutes * 60 * 1000)
                    {
                        _onLockRequested();
                    }
                }
            }
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (!_canLockPredicate()) return;

            if (e.Reason == SessionSwitchReason.SessionLock)
            {
                var settings = AppSettingsManager.Load();
                if (settings.AutoLockOnWindowsLock)
                {
                    _onLockRequested();
                }
            }
        }

        public void Stop()
        {
            _idleTimer.Stop();
        }

        public void Start()
        {
            _idleTimer.Start();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _idleTimer.Stop();
            SystemEvents.SessionSwitch -= OnSessionSwitch;
        }
    }
}
