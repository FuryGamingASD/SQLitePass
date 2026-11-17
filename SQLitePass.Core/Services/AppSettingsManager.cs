using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace SQLitePass.Core.Services
{
    public class AppSettings
    {
        public long Revision { get; set; }
        internal string? OriginalJson { get; set; }
        public string? LastOpenedVaultPath { get; set; }
        public List<string> RecentVaults { get; set; } = new();
        public Dictionary<string, bool> GroupExpansion { get; set; } = new();
        public Dictionary<string, WindowSizePreference> WindowSizes { get; set; } = new();
        public bool EnableDiagnosticLogging { get; set; }

        // Clipboard
        public int ClipboardClearSeconds { get; set; } = 12;

        // Tray & Window Behavior
        public bool MinimizeToTray { get; set; } = true;
        public bool CloseToTray { get; set; } = false;
        public bool AlwaysOnTop { get; set; } = false;
        public bool StartWithWindows { get; set; } = false;

        // Auto-Lock
        public int AutoLockMinutes { get; set; } = 5; // 0 = disabled
        public bool AutoLockOnMinimize { get; set; } = false;
        public bool AutoLockOnWindowsLock { get; set; } = true;
        public bool ProtectScreenCapture { get; set; } = true;

        // UI & Display
        public bool DarkMode { get; set; } = true;
        public string Language { get; set; } = "";
        public bool ShowPasswordColumnDefault { get; set; } = false;

        // Backup
        public bool EnableDualFileBackup { get; set; } = true;
        public string? SecondaryBackupDir { get; set; }
    }

    public sealed record WindowSizePreference(double Width, double Height);

    public static class AppSettingsManager
    {
        private static readonly string AppDataFolder = AppContext.GetData("SQLitePass.SettingsDirectory") as string ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SQLitePass"
        );
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegistryName = "SQLitePass";

        private static readonly AppSettingsStore Store = new(AppDataFolder);
        public static AppSettings Load() => Store.Load();
        public static AppSettings Update(Action<AppSettings> change) => Store.Update(change);

        public static string? Save(AppSettings settings)
        {
            Store.Save(settings);
            try { ApplyStartWithWindows(settings.StartWithWindows); return null; }
            catch (IOException)
            {
                AppLogger.LogWarning("Preferenze salvate; registrazione dell'avvio automatico non aggiornata.");
                return "Le preferenze sono state salvate, ma non è stato possibile aggiornare l'avvio automatico di Windows. Controllare questa opzione prima di fare affidamento sull'avvio automatico.";
            }
        }

        public static void AddRecentVault(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            Store.Update(settings =>
            {
                settings.RecentVaults.RemoveAll(x => string.Equals(x, filePath, StringComparison.OrdinalIgnoreCase));
                settings.RecentVaults.Insert(0, filePath);
                settings.RecentVaults = settings.RecentVaults.Take(10).ToList();
                settings.LastOpenedVaultPath = filePath;
            });
        }

        private static void ApplyStartWithWindows(bool enable)
        {
            if (!OperatingSystem.IsWindows()) return;

            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunRegistryKey, writable: true);
                if (key == null) throw new IOException("Registro di avvio automatico non disponibile.");

                if (enable)
                {
                    string? exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(AppRegistryName, $"\"{exePath}\" --minimized");
                    }
                }
                else
                {
                    key.DeleteValue(AppRegistryName, throwOnMissingValue: false);
                }
            }
            catch (Exception ex) { throw new IOException("Impossibile aggiornare l’avvio automatico di Windows.", ex); }
        }
    }
}
