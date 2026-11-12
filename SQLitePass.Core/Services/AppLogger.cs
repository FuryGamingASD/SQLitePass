using System;
using System.IO;

namespace SQLitePass.Core.Services
{
    /// <summary>
    /// Thread-safe file logger writing to %APPDATA%\SQLitePass\logs\sqlitepass.log with automatic 5MB rotation.
    /// </summary>
    public static class AppLogger
    {
        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SQLitePass",
            "logs"
        );
        private static readonly string LogPath = Path.Combine(LogDir, "sqlitepass.log");
        private static readonly object LockObj = new();
        private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB

        public static void LogInfo(string message) => Write("INFO", message);
        public static void LogWarning(string message) => Write("WARN", message);
        public static void LogError(string message, Exception? ex = null)
        {
            string full = ex != null 
                ? $"{message}\n[Exception] {ex.GetType().FullName}: {ex.Message}\n[StackTrace]\n{ex.StackTrace}" 
                : message;
            Write("ERROR", full);
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (LockObj)
                {
                    if (!Directory.Exists(LogDir))
                        Directory.CreateDirectory(LogDir);

                    var fileInfo = new FileInfo(LogPath);
                    if (fileInfo.Exists && fileInfo.Length > MaxLogSizeBytes)
                    {
                        string oldLog = Path.Combine(LogDir, "sqlitepass.old.log");
                        if (File.Exists(oldLog)) File.Delete(oldLog);
                        File.Move(LogPath, oldLog);
                    }

                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string line = $"[{timestamp}] [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(LogPath, line);
                }
            }
            catch
            {
                // Never allow logging failures to crash the application
            }
        }
    }
}
