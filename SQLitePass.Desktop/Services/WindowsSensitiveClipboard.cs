using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace SQLitePass.Desktop.Services;

[SupportedOSPlatform("windows")]
internal sealed class WindowsSensitiveClipboard : ISensitiveClipboard
{
    private const uint UnicodeText = 13;
    private const uint MoveableMemory = 0x0002;
    private readonly IntPtr _window;
    private readonly uint _excludeFromMonitoring;
    private readonly uint _excludeFromHistory;
    private readonly uint _excludeFromCloud;
    private readonly uint _leaseFormat;

    private WindowsSensitiveClipboard(IntPtr window)
    {
        _window = window;
        _excludeFromMonitoring = RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing");
        _excludeFromHistory = RegisterClipboardFormatW("CanIncludeInClipboardHistory");
        _excludeFromCloud = RegisterClipboardFormatW("CanUploadToCloudClipboard");
        _leaseFormat = RegisterClipboardFormatW("SQLitePass.SessionClipboardLease");
    }

    internal static WindowsSensitiveClipboard Create(IntPtr window) => new(window);

    public ValueTask<IClipboardLease?> TryCopyAsync(string text)
    {
        // The native text format is NUL-terminated. Reject values that cannot round-trip intact.
        if (text.Contains('\0') || _excludeFromMonitoring == 0 || _excludeFromHistory == 0 || _excludeFromCloud == 0 || _leaseFormat == 0 ||
            !OpenClipboard(_window)) return ValueTask.FromResult<IClipboardLease?>(null);
        try
        {
            if (!EmptyClipboard()) return ValueTask.FromResult<IClipboardLease?>(null);
            byte[] bytes = new byte[checked((text.Length + 1) * sizeof(char))];
            try
            {
                Encoding.Unicode.GetBytes(text.AsSpan(), bytes);
                byte[] leaseId = Guid.NewGuid().ToByteArray();
                if (SetBytes(_excludeFromMonitoring, [1]) && SetBytes(_excludeFromHistory, [0, 0, 0, 0]) &&
                    SetBytes(_excludeFromCloud, [0, 0, 0, 0]) && SetBytes(_leaseFormat, leaseId) && SetBytes(UnicodeText, bytes))
                    return ValueTask.FromResult<IClipboardLease?>(new WindowsLease(_window, _leaseFormat, leaseId));
                EmptyClipboard();
                return ValueTask.FromResult<IClipboardLease?>(null);
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        finally { CloseClipboard(); }
    }

    private static bool SetBytes(uint format, byte[] value)
    {
        IntPtr buffer = GlobalAlloc(MoveableMemory, (nuint)value.Length);
        if (buffer == IntPtr.Zero) return false;
        try
        {
            IntPtr memory = GlobalLock(buffer);
            if (memory == IntPtr.Zero) return false;
            try { Marshal.Copy(value, 0, memory, value.Length); }
            finally { GlobalUnlock(buffer); }
            if (SetClipboardData(format, buffer) == IntPtr.Zero) return false;
            buffer = IntPtr.Zero; // The system owns the memory after SetClipboardData succeeds.
            return true;
        }
        finally
        {
            if (buffer != IntPtr.Zero) GlobalFree(buffer);
        }
    }

    internal bool UsesPrivacyFormats() => IsClipboardFormatAvailable(_excludeFromMonitoring) &&
        IsClipboardFormatAvailable(_excludeFromHistory) && IsClipboardFormatAvailable(_excludeFromCloud);

    public void Dispose() { }

    private sealed class WindowsLease(IntPtr window, uint format, byte[] leaseId) : IClipboardLease
    {
        public ValueTask<ClipboardClearResult> TryClearAsync()
        {
            if (!OpenClipboard(window)) return ValueTask.FromResult(ClipboardClearResult.Busy);
            try
            {
                if (GetClipboardOwner() != window || !MatchesLease())
                    return ValueTask.FromResult(ClipboardClearResult.Replaced);
                return ValueTask.FromResult(EmptyClipboard() ? ClipboardClearResult.Cleared : ClipboardClearResult.Busy);
            }
            finally { CloseClipboard(); }
        }

        private bool MatchesLease()
        {
            IntPtr handle = GetClipboardData(format);
            if (handle == IntPtr.Zero || GlobalSize(handle) < (nuint)leaseId.Length) return false;
            IntPtr memory = GlobalLock(handle);
            if (memory == IntPtr.Zero) return false;
            try
            {
                for (int index = 0; index < leaseId.Length; index++)
                    if (Marshal.ReadByte(memory, index) != leaseId[index]) return false;
                return true;
            }
            finally { GlobalUnlock(handle); }
        }
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint RegisterClipboardFormatW(string format);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, nuint size);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(IntPtr handle);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr handle);
}
