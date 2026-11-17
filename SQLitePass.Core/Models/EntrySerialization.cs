using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace SQLitePass.Core.Models;

public static class EntrySerialization
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
        MaxDepth = 32
    };
    internal static readonly JsonSerializerOptions OverviewOptions = Without(nameof(PwEntry.Attachments), nameof(PwEntry.History), nameof(PwEntry.CustomFields));
    private static readonly JsonSerializerOptions SnapshotOptions = Without(nameof(PwEntry.History));

    private static JsonSerializerOptions Without(params string[] properties)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type != typeof(PwEntry)) return;
            foreach (var property in info.Properties.Where(p => properties.Contains(p.Name)).ToArray()) info.Properties.Remove(property);
        });
        return new(Options) { TypeInfoResolver = resolver };
    }

    public static string Serialize(PwEntry entry, bool includeHistory = true)
    {
        if (!entry.HasLoadedDetails) throw new InvalidOperationException("Caricare i dettagli completi prima di copiare o salvare una voce.");
        return JsonSerializer.Serialize(entry, includeHistory ? Options : SnapshotOptions);
    }

    public static PwEntry Deserialize(string json) => JsonSerializer.Deserialize<PwEntry>(json, Options)
        ?? throw new InvalidDataException("Record del vault vuoto.");
}
