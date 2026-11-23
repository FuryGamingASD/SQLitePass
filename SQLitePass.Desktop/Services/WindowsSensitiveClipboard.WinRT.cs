#if WINDOWS
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.ApplicationModel.DataTransfer;

namespace SQLitePass.Desktop.Services;

[SupportedOSPlatform("windows10.0.17763")]
internal sealed class WindowsSensitiveClipboard : ISensitiveClipboard
{
    private const uint RoInitSingleThreaded = 0;
    private const int ClipboardPrivacyFailureBuild = 26300;
    private readonly IntPtr _window;
    public ClipboardCopyBlockedReason CopyBlockedReason { get; private set; }

    private WindowsSensitiveClipboard(IntPtr window) => _window = window;

    internal static WindowsSensitiveClipboard Create(IntPtr window) => new(window);

    internal static bool IsClipboardRoamingEnabledForCurrentUser()
    {
        int initialized = RoInitialize(RoInitSingleThreaded);
        if (initialized < 0) Marshal.ThrowExceptionForHR(initialized);
        try { return Clipboard.IsRoamingEnabled(); }
        finally { RoUninitialize(); }
    }

    internal static bool IsClipboardPrivacyUnreliableOnCurrentBuild() =>
        Environment.OSVersion.Version.Build == ClipboardPrivacyFailureBuild;

    internal static uint GetClipboardSequenceNumberForVerification() => GetClipboardSequenceNumber();

    public ValueTask<IClipboardLease?> TryCopyAsync(string text)
    {
        CopyBlockedReason = ClipboardCopyBlockedReason.None;
        if (text.Contains('\0')) return ValueTask.FromResult<IClipboardLease?>(null);
        int initialized = RoInitialize(RoInitSingleThreaded);
        if (initialized < 0)
        {
            CopyBlockedReason = ClipboardCopyBlockedReason.SecureWindowsClipboardUnavailable;
            return ValueTask.FromResult<IClipboardLease?>(null);
        }
        bool clipboardSet = false;
        try
        {
            if (AppContext.GetData("SQLitePass.AllowClipboardRoamingProbe") is not true)
            {
                if (Clipboard.IsRoamingEnabled())
                {
                    CopyBlockedReason = ClipboardCopyBlockedReason.WindowsClipboardSyncEnabled;
                    return ValueTask.FromResult<IClipboardLease?>(null);
                }
                if (IsClipboardPrivacyUnreliableOnCurrentBuild())
                {
                    CopyBlockedReason = ClipboardCopyBlockedReason.SecureWindowsClipboardUnavailable;
                    return ValueTask.FromResult<IClipboardLease?>(null);
                }
            }

            var package = new DataPackage();
            package.SetText(text);
            var options = new ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false };
            clipboardSet = Clipboard.SetContentWithOptions(package, options);
            if (!clipboardSet) return ValueTask.FromResult<IClipboardLease?>(null);

            uint sequence = GetClipboardSequenceNumber();
            if (sequence == 0)
            {
                ClearIfOwnedByThisProcess();
                CopyBlockedReason = ClipboardCopyBlockedReason.SecureWindowsClipboardUnavailable;
                return ValueTask.FromResult<IClipboardLease?>(null);
            }
            return ValueTask.FromResult<IClipboardLease?>(new WindowsLease(_window, sequence));
        }
        catch (COMException)
        {
            if (clipboardSet) ClearIfOwnedByThisProcess();
            CopyBlockedReason = ClipboardCopyBlockedReason.SecureWindowsClipboardUnavailable;
            return ValueTask.FromResult<IClipboardLease?>(null);
        }
        finally { RoUninitialize(); }
    }

    private void ClearIfOwnedByThisProcess()
    {
        if (!OpenClipboard(_window)) return;
        try
        {
            IntPtr owner = GetClipboardOwner();
            if (owner == IntPtr.Zero) return;
            GetWindowThreadProcessId(owner, out uint ownerProcessId);
            if (ownerProcessId == Environment.ProcessId) EmptyClipboard();
        }
        finally { CloseClipboard(); }
    }

    public void Dispose() { }

    private sealed class WindowsLease(IntPtr window, uint sequence) : IClipboardLease
    {
        public ValueTask<ClipboardClearResult> TryClearAsync()
        {
            if (!OpenClipboard(window)) return ValueTask.FromResult(ClipboardClearResult.Busy);
            try
            {
                if (GetClipboardSequenceNumber() != sequence)
                    return ValueTask.FromResult(ClipboardClearResult.Replaced);
                return ValueTask.FromResult(EmptyClipboard() ? ClipboardClearResult.Cleared : ClipboardClearResult.Busy);
            }
            finally { CloseClipboard(); }
        }
    }

    [DllImport("combase.dll")] private static extern int RoInitialize(uint initType);
    [DllImport("combase.dll")] private static extern void RoUninitialize();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
}
#endif
