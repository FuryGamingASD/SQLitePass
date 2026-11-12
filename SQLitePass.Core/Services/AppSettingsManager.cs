using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace SQLitePass.Core.Services
{
    public class AppSettings
    {
        public string? LastOpenedVaultPath { get; set; }
        public List<string> RecentVaults { get; set; } = new();

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
        public string Language { get; set; } = "it";
        public bool ShowPasswordColumnDefault { get; set; } = false;

        // Backup
        public bool EnableDualFileBackup { get; set; } = true;
        public string? SecondaryBackupDir { get; set; }
    }

    public static class AppSettingsManager
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SQLitePass"
        );
        private static readonly string SettingsFilePath = Path.Combine(AppDataFolder, "appsettings.json");

        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegistryName = "SQLitePass";

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                    return new AppSettings();

                string json = File.ReadAllText(SettingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(AppDataFolder);
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);

                // Sync Windows Startup registry key
                ApplyStartWithWindows(settings.StartWithWindows);
            }
            catch { }
        }

        public static void AddRecentVault(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            var settings = Load();
            settings.RecentVaults.RemoveAll(x => string.Equals(x, filePath, StringComparison.OrdinalIgnoreCase));
            settings.RecentVaults.Insert(0, filePath);
            if (settings.RecentVaults.Count > 10)
                settings.RecentVaults.RemoveAt(settings.RecentVaults.Count - 1);
            settings.LastOpenedVaultPath = filePath;
            Save(settings);
        }

        private static void ApplyStartWithWindows(bool enable)
        {
            if (!OperatingSystem.IsWindows()) return;

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: true);
                if (key == null) return;

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
            catch { }
        }
    }
}
