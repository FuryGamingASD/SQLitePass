using System.Diagnostics;
using System.Text.RegularExpressions;
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

public sealed record AutoTypeStep(string Text = "", ushort Key = 0, int DelayMilliseconds = 0);

public static class AutoTypeService
{
    public static IReadOnlyList<AutoTypeStep> Parse(PwEntry entry)
    {
        if (!entry.AutoTypeEnabled) throw new InvalidOperationException("Auto-Type disabilitato per questa voce.");
        string sequence = string.IsNullOrWhiteSpace(entry.AutoTypeSequence) ? "{USERNAME}{TAB}{PASSWORD}{ENTER}" : entry.AutoTypeSequence;
        if (sequence.Length > 4096) throw new InvalidDataException("Sequenza Auto-Type troppo lunga.");
        var steps = new List<AutoTypeStep>();
        foreach (string part in Regex.Split(sequence, @"(\{[^}]+\})", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        {
            if (part.Length == 0) continue;
            if (!part.StartsWith('{')) { steps.Add(new(Text: part)); continue; }
            string token = part.ToUpperInvariant();
            string? text = token switch
            {
                "{USERNAME}" => entry.UserName, "{PASSWORD}" => entry.Password, "{TITLE}" => entry.Title,
                "{URL}" => entry.Url, "{NOTES}" => entry.Notes, _ => null
            };
            if (text != null) steps.Add(new(Text: text));
            else if (token is "{TAB}" or "{ENTER}" or "{SPACE}") steps.Add(new(Key: token == "{TAB}" ? (ushort)9 : token == "{ENTER}" ? (ushort)13 : (ushort)32));
            else if (token.StartsWith("{S:") && token.EndsWith('}'))
            {
                string name = part[3..^1];
                var field = entry.CustomFields.SingleOrDefault(f => string.Equals(f.Key, name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("Campo personalizzato Auto-Type non trovato.");
                steps.Add(new(Text: field.Value));
            }
            else if (token.StartsWith("{DELAY ") && token.EndsWith('}') && int.TryParse(token[7..^1], out int delay) && delay is > 0 and <= 10000)
                steps.Add(new(DelayMilliseconds: delay));
            else throw new InvalidDataException("Segnaposto Auto-Type non supportato: " + part);
        }
        if (steps.Sum(s => s.Text.Length) > 4096 || steps.Sum(s => s.DelayMilliseconds) > 30000)
            throw new InvalidDataException("La sequenza Auto-Type supera i limiti di durata o lunghezza.");
        return steps;
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
        var steps = Parse(entry);
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
            foreach (char character in step.Text)
            {
                VerifyTarget(); input.Character(character); VerifyTarget();
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
            if (step.Key != 0) { VerifyTarget(); input.Key(step.Key); VerifyTarget(); }
            await Task.Delay(step.DelayMilliseconds > 0 ? step.DelayMilliseconds : 30, cancellationToken).ConfigureAwait(false);
            VerifyTarget();
        }
    }

}
