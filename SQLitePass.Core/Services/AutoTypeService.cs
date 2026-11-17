using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using SQLitePass.Core.Models;

namespace SQLitePass.Core.Services;

public interface IAutoTypeInput
{
    IntPtr ForegroundWindow { get; }
    string WindowTitle(IntPtr window);
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

    public static IntPtr CaptureForegroundWindow() => NativeInput.GetForegroundWindow();

    public static async Task ExecuteAutoTypeAsync(PwEntry? entry, IntPtr targetWindow, int initialDelayMs = 500,
        CancellationToken cancellationToken = default, IAutoTypeInput? input = null, string? expectedTitle = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (targetWindow == IntPtr.Zero) throw new InvalidOperationException("Selezionare esplicitamente una finestra destinataria.");
        var steps = Parse(entry); input ??= new NativeInput();
        cancellationToken.ThrowIfCancellationRequested();
        if (!input.Activate(targetWindow)) throw new InvalidOperationException("Impossibile attivare il destinatario.");
        await Task.Delay(Math.Clamp(initialDelayMs, 0, 5000), cancellationToken).ConfigureAwait(false);
        expectedTitle ??= input.WindowTitle(targetWindow);
        void VerifyTarget()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (input.ForegroundWindow != targetWindow || input.WindowTitle(targetWindow) != expectedTitle)
                throw new OperationCanceledException("Auto-Type interrotto: il destinatario è cambiato.");
        }
        foreach (var step in steps)
        {
            VerifyTarget();
            foreach (char character in step.Text)
            {
                VerifyTarget(); input.Character(character);
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
            if (step.Key != 0) { VerifyTarget(); input.Key(step.Key); }
            await Task.Delay(step.DelayMilliseconds > 0 ? step.DelayMilliseconds : 30, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class NativeInput : IAutoTypeInput
    {
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public UNION data; }
        [StructLayout(LayoutKind.Explicit)] private struct UNION
        {
            [FieldOffset(0)] public KEYBOARD keyboard;
            [FieldOffset(0)] public MOUSE mouse;
        }
        [StructLayout(LayoutKind.Sequential)] private struct KEYBOARD { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] private struct MOUSE { public int x, y; public uint data, flags, time; public IntPtr extra; }
        public IntPtr ForegroundWindow => GetForegroundWindow();
        public bool Activate(IntPtr window) => GetForegroundWindow() == window || SetForegroundWindow(window);
        public string WindowTitle(IntPtr window) { var title = new StringBuilder(1024); GetWindowText(window, title, title.Capacity); return title.ToString(); }
        public void Character(char character) => Send(new KEYBOARD { scan = character, flags = 4 });
        public void Key(ushort key) => Send(new KEYBOARD { vk = key });
        private static void Send(KEYBOARD down)
        {
            if (new[] { 0x10, 0x11, 0x12 }.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0))
                throw new OperationCanceledException("Rilascia Shift, Ctrl e Alt prima dell'Auto-Type.");
            var up = down; up.flags |= 2;
            INPUT[] events = [new() { type = 1, data = new UNION { keyboard = down } }, new() { type = 1, data = new UNION { keyboard = up } }];
            if (SendInput(2, events, Marshal.SizeOf<INPUT>()) != 2) throw new Win32Exception(Marshal.GetLastWin32Error(), "Invio tastiera non completato.");
        }
    }
}
