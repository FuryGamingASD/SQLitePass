using System.Diagnostics;
using SQLitePass.Core.Models;

namespace SQLitePass.Core.Services;

public interface IAutoTypeInput
{
    IntPtr ForegroundWindow { get; }
    string WindowTitle(IntPtr window);
    uint ProcessId(IntPtr window);
    void Prepare(IReadOnlyList<AutoTypeStep> steps) { }
    bool Activate(IntPtr window);
    void Character(char character);
    void Key(ushort key);
}

public sealed record AutoTypeStep
{
    private string _text;

    public AutoTypeStep(string Text = "", ushort Key = 0, int DelayMilliseconds = 0)
    {
        _text = Text;
        this.Key = Key;
        this.DelayMilliseconds = DelayMilliseconds;
    }

    public string Text { get => _text; init => _text = value; }
    public ushort Key { get; init; }
    public int DelayMilliseconds { get; init; }

    internal void ClearSensitiveText() => _text = string.Empty;

    public void Deconstruct(out string Text, out ushort Key, out int DelayMilliseconds)
    {
        Text = this.Text;
        Key = this.Key;
        DelayMilliseconds = this.DelayMilliseconds;
    }
}

public static class AutoTypeService
{
    public static IReadOnlyList<AutoTypeStep> Parse(PwEntry entry)
    {
        return ParseSteps(entry);
    }

    public static void ValidateSequence(PwEntry entry)
    {
        var steps = ParseSteps(entry);
        ClearSteps(steps);
    }

    private static List<AutoTypeStep> ParseSteps(PwEntry entry)
    {
        if (!entry.AutoTypeEnabled) throw new InvalidOperationException("Auto-Type disabilitato per questa voce.");
        string sequence = string.IsNullOrWhiteSpace(entry.AutoTypeSequence) ? "{USERNAME}{TAB}{PASSWORD}{ENTER}" : entry.AutoTypeSequence;
        if (sequence.Length > 4096) throw new InvalidDataException("Sequenza Auto-Type troppo lunga.");
        var steps = new List<AutoTypeStep>();
        try
        {
            int literalStart = 0;
            int scan = 0;
            while (scan < sequence.Length)
            {
                int open = sequence.IndexOf('{', scan);
                if (open < 0) break;
                int close = sequence.IndexOf('}', open + 1);
                if (close < 0) break;
                if (close == open + 1)
                {
                    scan = open + 1;
                    continue;
                }

                if (open > literalStart) steps.Add(new(Text: sequence[literalStart..open]));
                ReadOnlySpan<char> token = sequence.AsSpan(open, close - open + 1);
                if (token.Equals("{USERNAME}", StringComparison.OrdinalIgnoreCase)) steps.Add(new(Text: entry.UserName));
                else if (token.Equals("{PASSWORD}", StringComparison.OrdinalIgnoreCase)) steps.Add(new(Text: entry.Password));
                else if (token.Equals("{TITLE}", StringComparison.OrdinalIgnoreCase)) steps.Add(new(Text: entry.Title));
                else if (token.Equals("{URL}", StringComparison.OrdinalIgnoreCase)) steps.Add(new(Text: entry.Url));
                else if (token.Equals("{NOTES}", StringComparison.OrdinalIgnoreCase)) steps.Add(new(Text: entry.Notes));
                else if (token.Equals("{TAB}", StringComparison.OrdinalIgnoreCase)) steps.Add(new(Key: 9));
                else if (token.Equals("{ENTER}", StringComparison.OrdinalIgnoreCase)) steps.Add(new(Key: 13));
                else if (token.Equals("{SPACE}", StringComparison.OrdinalIgnoreCase)) steps.Add(new(Key: 32));
                else if (token.StartsWith("{S:", StringComparison.OrdinalIgnoreCase))
                {
                    string name = token[3..^1].ToString();
                    var field = entry.CustomFields.SingleOrDefault(f => string.Equals(f.Key, name, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException("Campo personalizzato Auto-Type non trovato.");
                    steps.Add(new(Text: field.Value));
                }
                else if (token.StartsWith("{DELAY ", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(token[7..^1], out int delay) && delay is > 0 and <= 10000)
                    steps.Add(new(DelayMilliseconds: delay));
                else throw new InvalidDataException("Segnaposto Auto-Type non supportato.");

                scan = literalStart = close + 1;
            }

            if (literalStart < sequence.Length) steps.Add(new(Text: sequence[literalStart..]));
            if (steps.Sum(s => s.Text.Length) > 4096 || steps.Sum(s => s.DelayMilliseconds) > 30000)
                throw new InvalidDataException("La sequenza Auto-Type supera i limiti di durata o lunghezza.");
            return steps;
        }
        catch
        {
            ClearSteps(steps);
            throw;
        }
    }

    private static void ClearSteps(List<AutoTypeStep> steps)
    {
        foreach (var step in steps) step.ClearSensitiveText();
        steps.Clear();
    }

    public static IntPtr CaptureForegroundWindow() => OperatingSystem.IsWindows() ? WindowsAutoTypeInput.GetForegroundWindow() : IntPtr.Zero;
    public static uint GetWindowProcessId(IntPtr window) => OperatingSystem.IsWindows() ? WindowsAutoTypeInput.GetWindowProcessId(window) : 0;
    public static string GetWindowProcessName(uint processId)
    {
        if (processId == 0 || processId > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(processId));
        using var process = Process.GetProcessById((int)processId);
        return process.ProcessName;
    }

    public static bool IsForegroundTarget(IntPtr targetWindow, string expectedTitle, uint expectedProcessId)
    {
        if (!OperatingSystem.IsWindows() || targetWindow == IntPtr.Zero || expectedProcessId == 0) return false;
        var input = new WindowsAutoTypeInput(allowActivation: false);
        return input.ForegroundWindow == targetWindow && input.ProcessId(targetWindow) == expectedProcessId &&
            input.WindowTitle(targetWindow) == expectedTitle;
    }

    public static async Task ExecuteAutoTypeAsync(PwEntry? entry, IntPtr targetWindow, int initialDelayMs = 500,
        CancellationToken cancellationToken = default, IAutoTypeInput? input = null, string? expectedTitle = null,
        uint? expectedProcessId = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (targetWindow == IntPtr.Zero) throw new InvalidOperationException("Selezionare esplicitamente una finestra destinataria.");
        input ??= OperatingSystem.IsWindows() ? new WindowsAutoTypeInput() : throw new PlatformNotSupportedException();
        cancellationToken.ThrowIfCancellationRequested();
        expectedTitle ??= input.WindowTitle(targetWindow);
        expectedProcessId ??= input.ProcessId(targetWindow);
        if (expectedProcessId == 0) throw new InvalidOperationException("La finestra destinataria non è più disponibile.");
        if (input.ProcessId(targetWindow) != expectedProcessId || input.WindowTitle(targetWindow) != expectedTitle)
            throw new OperationCanceledException("Auto-Type interrotto: il destinatario è cambiato.");
        var steps = ParseSteps(entry);
        try
        {
            input.Prepare(steps);
            cancellationToken.ThrowIfCancellationRequested();
            if (!input.Activate(targetWindow)) throw new InvalidOperationException("Impossibile attivare il destinatario.");
            await Task.Delay(Math.Clamp(initialDelayMs, 0, 5000), cancellationToken).ConfigureAwait(false);
            void VerifyTarget()
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (input.ForegroundWindow != targetWindow || input.WindowTitle(targetWindow) != expectedTitle ||
                    input.ProcessId(targetWindow) != expectedProcessId)
                    throw new OperationCanceledException("Auto-Type interrotto: il destinatario è cambiato.");
            }
            foreach (var step in steps)
            {
                VerifyTarget();
                for (int index = 0; index < step.Text.Length; index++)
                {
                    char character = step.Text[index];
                    VerifyTarget(); input.Character(character); VerifyTarget();
                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                }
                if (step.Key != 0) { VerifyTarget(); input.Key(step.Key); VerifyTarget(); }
                await Task.Delay(step.DelayMilliseconds > 0 ? step.DelayMilliseconds : 30, cancellationToken).ConfigureAwait(false);
                VerifyTarget();
            }
        }
        finally { ClearSteps(steps); }
    }

}
