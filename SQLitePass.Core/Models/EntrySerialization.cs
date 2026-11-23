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
    internal static readonly JsonSerializerOptions AutoTypeMatchOptions = Only(
        nameof(PwEntry.Id), nameof(PwEntry.GroupId), nameof(PwEntry.Revision),
        nameof(PwEntry.Title), nameof(PwEntry.Url), nameof(PwEntry.AutoTypeEnabled));
    internal static readonly JsonSerializerOptions AutoTypeExecutionOptions = Only(
        nameof(PwEntry.Id), nameof(PwEntry.GroupId), nameof(PwEntry.Revision), nameof(PwEntry.Title), nameof(PwEntry.UserName),
        nameof(PwEntry.Password), nameof(PwEntry.Url), nameof(PwEntry.Notes), nameof(PwEntry.AutoTypeEnabled),
        nameof(PwEntry.AutoTypeSequence), nameof(PwEntry.CustomFields));
    private static readonly JsonSerializerOptions SnapshotOptions = Without(nameof(PwEntry.History));

    private static JsonSerializerOptions Without(params string[] properties) => WithProperties(name => !properties.Contains(name));
    private static JsonSerializerOptions Only(params string[] properties) => WithProperties(properties.Contains);

    private static JsonSerializerOptions WithProperties(Func<string, bool> include)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type != typeof(PwEntry)) return;
            foreach (var property in info.Properties.Where(p => !include(p.Name)).ToArray()) info.Properties.Remove(property);
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
