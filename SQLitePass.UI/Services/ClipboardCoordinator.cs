using System;
using System.Windows;
using System.Windows.Threading;
using SQLitePass.Core.Services;

namespace SQLitePass.UI.Services
{
    /// <summary>
    /// Coordinates clipboard copy operations, auto-clearing sensitive data after timeout,
    /// and providing live countdown notifications.
    /// </summary>
    public sealed class ClipboardCoordinator : IDisposable
    {
        private DispatcherTimer? _timer;
        private int _secondsRemaining;
        private string? _lastCopiedText;
        private readonly Action<string> _statusCallback;

        public ClipboardCoordinator(Action<string> statusCallback)
        {
            _statusCallback = statusCallback ?? throw new ArgumentNullException(nameof(statusCallback));
        }

        public void CopyWithAutoClear(string text, string description, int customTimeoutSecs = 0)
        {
            if (string.IsNullOrEmpty(text)) return;

            var settings = AppSettingsManager.Load();
            int timeoutSecs = customTimeoutSecs > 0 
                ? customTimeoutSecs 
                : (settings.ClipboardClearSeconds > 0 ? settings.ClipboardClearSeconds : 12);

            try
            {
                Clipboard.SetText(text);
                _lastCopiedText = text;
                _secondsRemaining = timeoutSecs;

                _timer?.Stop();
                _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _timer.Tick += (s, e) =>
                {
                    _secondsRemaining--;
                    if (_secondsRemaining > 0)
                    {
                        _statusCallback($"📋 {description} negli appunti ({_secondsRemaining}s)");
                    }
                    else
                    {
                        _timer?.Stop();
                        SafeClearClipboard();
                        _statusCallback("📋 Appunti cancellati per sicurezza");
                    }
                };
                _timer.Start();

                _statusCallback($"📋 {description} negli appunti ({timeoutSecs}s)");
                AppLogger.LogInfo($"Copied {description} to clipboard with {timeoutSecs}s auto-clear");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Clipboard operation failed", ex);
                _statusCallback("⚠️ Impossibile accedere agli appunti");
            }
        }

        public void ClearImmediately()
        {
            _timer?.Stop();
            SafeClearClipboard();
            _statusCallback("📋 Appunti cancellati");
            AppLogger.LogInfo("Clipboard cleared immediately by user");
        }

        private void SafeClearClipboard()
        {
            try
            {
                // Only clear if the user hasn't copied something else in the meantime
                if (_lastCopiedText != null && Clipboard.GetText() == _lastCopiedText)
                {
                    Clipboard.Clear();
                }
            }
            catch { }
            finally
            {
                _lastCopiedText = null;
            }
        }

        public void Dispose()
        {
            _timer?.Stop();
            _timer = null;
        }
    }
}
