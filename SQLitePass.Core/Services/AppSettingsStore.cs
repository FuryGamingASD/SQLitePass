using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SQLitePass.Core.Services;

public sealed class SettingsConflictException() : IOException(
    "Le stesse preferenze sono state modificate in un'altra finestra o istanza. Chiudere e riaprire le impostazioni prima di riprovare.");

public sealed class AppSettingsStore(string directory)
{
    private readonly object _sync = new();
    private readonly string _directory = Path.GetFullPath(directory);
    private readonly string _path = Path.Combine(Path.GetFullPath(directory), "appsettings.json");
    private static readonly System.Reflection.PropertyInfo[] Properties = typeof(AppSettings).GetProperties();

    private FileStream AcquireLease()
    {
        Directory.CreateDirectory(_directory);
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33 && timer.Elapsed < TimeSpan.FromSeconds(5))
            { Thread.Sleep(15); }
        }
    }

    public AppSettings Load()
    {
        lock (_sync)
        {
            if (!Directory.Exists(_directory)) return Track(new AppSettings());
            using var lease = AcquireLease();
            return Read(out _);
        }
    }

    private AppSettings Read(out bool primaryValid)
    {
        primaryValid = false;
        foreach (string candidate in new[] { _path, _path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(candidate))
                    ?? throw new JsonException("Impostazioni vuote.");
                if (settings.Revision < 0) throw new JsonException("Versione delle impostazioni non valida.");
                Normalize(settings); primaryValid = candidate == _path; return Track(settings);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { AppLogger.LogWarning("Impostazioni non leggibili; tentativo di recupero dalla copia precedente."); }
        }
        return Track(new AppSettings());
    }

    private static AppSettings Track(AppSettings settings)
    {
        settings.OriginalJson = JsonSerializer.Serialize(settings); return settings;
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_sync)
        {
            using var lease = AcquireLease();
            var current = Read(out bool primaryValid);
            var merged = Merge(settings, current);
            Persist(merged, primaryValid);
            foreach (var property in Properties) property.SetValue(settings, property.GetValue(merged));
            settings.OriginalJson = merged.OriginalJson;
        }
    }

    // Read-modify-write is protected by the same OS lease across store instances/processes.
    public AppSettings Update(Action<AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (_sync)
        {
            using var lease = AcquireLease();
            var settings = Read(out bool primaryValid);
            long revision = settings.Revision;
            change(settings); settings.Revision = revision;
            Persist(settings, primaryValid); return settings;
        }
    }

    private static AppSettings Merge(AppSettings requested, AppSettings current)
    {
        if (requested.OriginalJson == null)
        {
            if (requested.Revision != current.Revision) throw new SettingsConflictException();
            var replacement = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(requested))!;
            replacement.Revision = current.Revision; return replacement;
        }
        var baseline = JsonNode.Parse(requested.OriginalJson)!.AsObject();
        var desired = JsonSerializer.SerializeToNode(requested)!.AsObject();
        var latest = JsonSerializer.SerializeToNode(current)!.AsObject();
        foreach (var (key, value) in desired)
        {
            if (key == nameof(AppSettings.Revision) || JsonNode.DeepEquals(value, baseline[key])) continue;
            if (!JsonNode.DeepEquals(latest[key], baseline[key]) && !JsonNode.DeepEquals(latest[key], value))
                throw new SettingsConflictException();
            latest[key] = value?.DeepClone();
        }
        return latest.Deserialize<AppSettings>()!;
    }

    private void Persist(AppSettings settings, bool primaryValid)
    {
        Normalize(settings); settings.Revision = checked(settings.Revision + 1);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(true);
            }
            // Never replace the last readable backup with a corrupt primary.
            if (File.Exists(_path)) File.Replace(temporary, _path, primaryValid ? _path + ".bak" : null);
            else File.Move(temporary, _path);
            Track(settings);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void Normalize(AppSettings settings)
    {
        settings.ClipboardClearSeconds = Math.Clamp(settings.ClipboardClearSeconds, 3, 300);
        if (settings.AutoLockMinutes < 0 || settings.AutoLockMinutes > 60) settings.AutoLockMinutes = 5;
        settings.RecentVaults = (settings.RecentVaults ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
        settings.GroupExpansion = (settings.GroupExpansion ?? []).Where(pair =>
            pair.Key.Length == 65 && pair.Key[32] == '/' && Guid.TryParseExact(pair.Key[..32], "N", out _) &&
            Guid.TryParseExact(pair.Key[33..], "N", out _)).TakeLast(4096).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        settings.WindowSizes = (settings.WindowSizes ?? []).Where(pair => pair.Key.Length <= 80 && pair.Value != null &&
            double.IsFinite(pair.Value.Width) && double.IsFinite(pair.Value.Height) && pair.Value.Width is >= 320 and <= 7680 && pair.Value.Height is >= 200 and <= 4320)
            .Take(20).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (!new[] { "", "it", "en", "fr", "de", "es" }.Contains(settings.Language)) settings.Language = "";
        if (string.IsNullOrWhiteSpace(settings.SecondaryBackupDir)) settings.SecondaryBackupDir = null;
    }
}
