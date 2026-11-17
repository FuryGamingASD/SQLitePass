using System.Windows.Threading;

namespace SQLitePass.UI.Services;

// A monotonic deadline is checked on every timer tick and before a reveal is used again.
internal sealed class TimedSecretReveal : IDisposable
{
    internal static readonly TimeSpan Duration = TimeSpan.FromSeconds(15);
    private readonly Action<bool> _display;
    private readonly TimeProvider _clock;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private long _started;
    private bool _revealed;
    private bool _disposed;

    public TimedSecretReveal(Action<bool> display, TimeProvider? clock = null)
    { _display = display; _clock = clock ?? TimeProvider.System; _timer.Tick += Tick; }
    public bool IsRevealed { get { CheckDeadline(); return _revealed; } }
    public void Toggle() { if (IsRevealed) Hide(); else Reveal(); }
    public void Reveal()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _started = _clock.GetTimestamp(); _revealed = true; _timer.Start(); _display(true);
    }
    public void Hide()
    {
        _timer.Stop();
        if (!_revealed) return;
        _revealed = false; _display(false);
    }
    internal void CheckDeadline()
    { if (_revealed && _clock.GetElapsedTime(_started) >= Duration) Hide(); }
    private void Tick(object? sender, EventArgs e) => CheckDeadline();
    public void Dispose() { if (_disposed) return; Hide(); _timer.Tick -= Tick; _disposed = true; }
}
