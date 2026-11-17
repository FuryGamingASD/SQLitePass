using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using SQLitePass.Core.Services;

namespace SQLitePass.UI.Services;

public sealed class ClipboardCoordinator : IDisposable
{
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    public static ClipboardCoordinator Shared { get; } = new(_ => { });
    private DispatcherTimer? _timer;
    private uint? _sequence;
    private long _expires;
    private int _retries;
    private Action<string> _statusCallback;

    public ClipboardCoordinator(Action<string> statusCallback) => _statusCallback = statusCallback;
    public void SetStatusCallback(Action<string> callback) => _statusCallback = callback;

    public void CopyWithAutoClear(string text, string description, int customTimeoutSecs = 0)
    {
        if (string.IsNullOrEmpty(text)) return;
        int timeout = Math.Clamp(customTimeoutSecs > 0 ? customTimeoutSecs : AppSettingsManager.Load().ClipboardClearSeconds, 3, 300);
        try
        {
            var data = new DataObject(); data.SetData(DataFormats.UnicodeText, text);
            data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)), false);
            data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)), false);
            data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[] { 1 }), false);
            Clipboard.SetDataObject(data, copy: true);
            _sequence = GetClipboardSequenceNumber(); _expires = System.Diagnostics.Stopwatch.GetTimestamp() + timeout * System.Diagnostics.Stopwatch.Frequency; _retries = 0;
            _timer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, Tick, Dispatcher.CurrentDispatcher);
            _timer.Start(); _statusCallback(LocalizationManager.GetString("ClipboardCopiedFor", description, timeout));
        }
        catch (ExternalException) { _statusCallback(LocalizationManager.GetString("ClipboardBusy")); }
    }
    private void Tick(object? sender, EventArgs e)
    {
        int seconds = (int)Math.Ceiling((double)(_expires - System.Diagnostics.Stopwatch.GetTimestamp()) / System.Diagnostics.Stopwatch.Frequency);
        if (seconds > 0) _statusCallback(LocalizationManager.GetString("ClipboardClearedIn", seconds));
        else TryClear();
    }
    private void TryClear()
    {
        try
        {
            if (_sequence.HasValue && GetClipboardSequenceNumber() == _sequence.Value) Clipboard.Clear();
            _sequence = null; _timer?.Stop(); _statusCallback(LocalizationManager.GetString("ClipboardCleared"));
        }
        catch (ExternalException)
        {
            if (++_retries <= 5) { _expires = 0; _timer?.Start(); }
            else { _timer?.Stop(); _statusCallback(LocalizationManager.GetString("ClipboardClearFailed")); }
        }
    }
    public void ClearImmediately() => TryClear();
    public void Dispose() { TryClear(); if (_sequence == null) _timer?.Stop(); }
}
