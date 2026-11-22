namespace SQLitePass.Core.Services;

internal static class FileLease
{
    internal static bool IsContended(IOException exception)
    {
        // FileStream reports Win32 sharing/lock violations or Linux's raw EWOULDBLOCK errno.
        if (OperatingSystem.IsWindows()) return (exception.HResult & 0xffff) is 32 or 33;
        return OperatingSystem.IsLinux() && exception.HResult == 11;
    }
}
