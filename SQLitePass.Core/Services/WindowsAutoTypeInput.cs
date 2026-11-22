using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace SQLitePass.Core.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsAutoTypeInput(bool allowActivation = true) : IAutoTypeInput
{
    private static readonly int[] ModifierKeys = [0x10, 0x11, 0x12, 0x5B, 0x5C];
    private IntPtr _target;

    public IntPtr ForegroundWindow => GetForegroundWindow();
    public uint ProcessId(IntPtr window) => GetWindowProcessId(window);

    public bool Activate(IntPtr window)
    {
        if (GetForegroundWindow() != window && (!allowActivation || !SetForegroundWindow(window))) return false;
        _target = window;
        return true;
    }

    public string WindowTitle(IntPtr window)
    {
        var title = new StringBuilder(1024);
        GetWindowText(window, title, title.Capacity);
        return title.ToString();
    }

    internal static uint GetWindowProcessId(IntPtr window) => GetWindowThreadProcessId(window, out var processId) == 0 ? 0 : processId;

    public void Character(char character) => Send(new KeyboardInput { ScanCode = character, Flags = 4 });
    public void Key(ushort key) => Send(new KeyboardInput { VirtualKey = key });

    private void Send(KeyboardInput down)
    {
        if (ModifierKeys.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0) || _target == IntPtr.Zero || GetForegroundWindow() != _target)
            throw new OperationCanceledException("Auto-Type keyboard modifiers or focus changed.");

        var up = down;
        up.Flags |= 2;
        Input[] events =
        [
            new() { Type = 1, Data = new InputUnion { Keyboard = down } },
            new() { Type = 1, Data = new InputUnion { Keyboard = up } }
        ];
        if (SendInput(2, events, Marshal.SizeOf<Input>()) != 2)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Auto-Type keyboard input could not be sent.");
    }

    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort VirtualKey, ScanCode; public uint Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public IntPtr Extra; }
}
