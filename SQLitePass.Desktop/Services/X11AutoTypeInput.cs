using System.Runtime.InteropServices;
using SQLitePass.Core.Services;
using static SQLitePass.Desktop.Services.X11AutoTypeNative;

namespace SQLitePass.Desktop.Services;

internal sealed class AutoTypeLayoutException : Exception;

internal sealed class X11AutoTypeInput : IAutoTypeInput, IDisposable
{
    private const uint CoreKeyboard = 0x100;
    private const uint ShiftMask = 1;
    private const uint KeyboardMapEvents = 3; // XkbNewKeyboardNotifyMask | XkbMapNotifyMask
    private const uint ShiftLeft = 0xffe1;
    private const uint LevelThreeShift = 0xfe03;
    private const uint NumLock = 0xff7f;
    private readonly IntPtr _display;
    private readonly X11AutoTypeWindows _windows;
    private readonly Dictionary<char, Stroke> _characters = [];
    private readonly Dictionary<ushort, Stroke> _keys = [];
    private Target? _target;
    private uint _keyboardState;

    private X11AutoTypeInput(IntPtr display)
    {
        _display = display;
        _windows = new X11AutoTypeWindows(display);
    }

    internal static X11AutoTypeInput? TryCreate()
    {
        // A remote X server cannot provide a trustworthy local process ID.
        string? displayName = Environment.GetEnvironmentVariable("DISPLAY");
        if (displayName == null || !(displayName.StartsWith(':') || displayName.StartsWith("unix:", StringComparison.Ordinal))) return null;
        IntPtr display = IntPtr.Zero;
        try
        {
            if (xkb_keysym_to_utf32('A') != 'A') return null;
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero || XTestQueryExtension(display, out _, out _, out _, out _) == 0 ||
                XResQueryExtension(display, out _, out _) == 0 || XResQueryVersion(display, out int major, out int minor) == 0 ||
                major < 1 || (major == 1 && minor < 2) || XkbSelectEvents(display, CoreKeyboard, KeyboardMapEvents, KeyboardMapEvents) == 0)
                return null;

            var input = new X11AutoTypeInput(display);
            display = IntPtr.Zero;
            return input;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
        finally { if (display != IntPtr.Zero) XCloseDisplay(display); }
    }

    public IntPtr ForegroundWindow => (IntPtr)_windows.ForegroundWindow;
    public string WindowTitle(IntPtr window) => _windows.GetTitle((uint)window);
    public uint ProcessId(IntPtr window) => _windows.GetProcessId((uint)window);

    public bool Activate(IntPtr window)
    {
        uint id = (uint)window;
        if (_windows.ForegroundWindow != id) return false;
        uint pid = _windows.GetProcessId(id);
        string title = _windows.GetTitle(id);
        if (pid == 0 || string.IsNullOrWhiteSpace(title)) return false;
        _target = new Target(id, pid, title);
        return true;
    }

    public void Prepare(IReadOnlyList<AutoTypeStep> steps)
    {
        _keyboardState = ReadKeyboardState();
        if (XPending(_display) != 0) throw new AutoTypeLayoutException();
        XDisplayKeycodes(_display, out int min, out int max);
        byte shiftCode = XKeysymToKeycode(_display, ShiftLeft);
        byte levelThreeCode = XKeysymToKeycode(_display, LevelThreeShift);
        uint levelThreeMask = XkbKeysymToModifiers(_display, LevelThreeShift);
        var modifiers = new List<Modifiers> { new(0, 0, 0) };
        if (shiftCode != 0) modifiers.Add(new(ShiftMask, shiftCode, 0));
        if (levelThreeCode != 0 && levelThreeMask is 32 or 64 or 128)
        {
            modifiers.Add(new(levelThreeMask, levelThreeCode, 0));
            if (shiftCode != 0) modifiers.Add(new(ShiftMask | levelThreeMask, shiftCode, levelThreeCode));
        }

        var wantedCharacters = steps.SelectMany(step => step.Text).ToHashSet();
        var wantedKeys = steps.Where(step => step.Key != 0).Select(step => step.Key).ToHashSet();
        _characters.Clear();
        _keys.Clear();
        foreach (var modifier in modifiers)
        {
            for (int code = min; code <= max; ++code)
            {
                if (code == modifier.First || code == modifier.Second ||
                    XkbLookupKeySym(_display, (byte)code, _keyboardState | modifier.Mask, out _, out nuint symbol) == 0)
                    continue;

                var stroke = new Stroke((uint)code, modifier.First, modifier.Second);
                uint scalar = xkb_keysym_to_utf32((uint)symbol);
                if (scalar is >= ' ' and <= char.MaxValue && !char.IsControl((char)scalar) && wantedCharacters.Contains((char)scalar))
                    _characters.TryAdd((char)scalar, stroke);
                ushort key = symbol switch { 0xff09 => 9, 0xff0d => 13, 0x20 => 32, _ => 0 };
                if (modifier.Mask == 0 && wantedKeys.Contains(key)) _keys.TryAdd(key, stroke);
            }
        }
        if (_characters.Count != wantedCharacters.Count || _keys.Count != wantedKeys.Count)
            throw new AutoTypeLayoutException();
    }

    public void Character(char character) => Send(_characters[character]);
    public void Key(ushort key) => Send(_keys[key]);

    private uint ReadKeyboardState()
    {
        uint allowedLocks = 2 | XkbKeysymToModifiers(_display, NumLock);
        if (XkbGetState(_display, CoreKeyboard, out var state) != 0 ||
            state.BaseModifiers != 0 || state.LatchedModifiers != 0 || (state.LockedModifiers & ~allowedLocks) != 0)
            throw new AutoTypeLayoutException();
        return ((uint)state.Group << 13) | state.LookupModifiers;
    }

    private void Send(Stroke stroke)
    {
        if (_target is not { } target) throw new OperationCanceledException();
        XGrabServer(_display);
        try
        {
            if (!_windows.IsFocused(target.Window, target.Process) || _windows.GetTitle(target.Window) != target.Title)
                throw new OperationCanceledException();
            if (ReadKeyboardState() != _keyboardState || XPending(_display) != 0)
                throw new AutoTypeLayoutException();

            // An active keyboard grab can redirect the global keyboard stream even with the expected window focused.
            if (XGrabKeyboard(_display, target.Window, 0, 1, 1, 0) != 0) throw new OperationCanceledException();
            XUngrabKeyboard(_display, 0);
            try
            {
                Emit(stroke.FirstModifier, pressed: true);
                Emit(stroke.SecondModifier, pressed: true);
                Emit(stroke.Keycode, pressed: true);
            }
            finally
            {
                Emit(stroke.Keycode, pressed: false);
                Emit(stroke.SecondModifier, pressed: false);
                Emit(stroke.FirstModifier, pressed: false);
            }
        }
        finally
        {
            XUngrabServer(_display);
            XFlush(_display);
        }
    }

    private void Emit(uint keycode, bool pressed)
    {
        if (keycode != 0 && XTestFakeKeyEvent(_display, keycode, pressed ? 1 : 0, 0) == 0)
            throw new OperationCanceledException();
    }

    public void Dispose()
    {
        _characters.Clear();
        _keys.Clear();
        XCloseDisplay(_display);
    }

    private sealed record Target(uint Window, uint Process, string Title);
    private readonly record struct Stroke(uint Keycode, byte FirstModifier, byte SecondModifier);
    private readonly record struct Modifiers(uint Mask, byte First, byte Second);
}
