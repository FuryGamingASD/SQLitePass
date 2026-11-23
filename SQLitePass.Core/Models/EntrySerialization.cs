using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
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
    internal static readonly JsonSerializerOptions OverviewOptions = WithProperties(IsOverviewProperty);
    internal static readonly JsonSerializerOptions GridOptions = GridProjection(sortByPassword: false);
    internal static readonly JsonSerializerOptions PasswordSortedGridOptions = GridProjection(sortByPassword: true);
    internal static readonly JsonSerializerOptions AutoTypeMatchOptions = Only(
        nameof(PwEntry.Id), nameof(PwEntry.GroupId), nameof(PwEntry.Revision),
        nameof(PwEntry.Title), nameof(PwEntry.Url), nameof(PwEntry.AutoTypeEnabled));
    internal static readonly JsonSerializerOptions AutoTypeExecutionOptions = Only(
        nameof(PwEntry.Id), nameof(PwEntry.GroupId), nameof(PwEntry.Revision), nameof(PwEntry.Title), nameof(PwEntry.UserName),
        nameof(PwEntry.Password), nameof(PwEntry.Url), nameof(PwEntry.Notes), nameof(PwEntry.AutoTypeEnabled),
        nameof(PwEntry.AutoTypeSequence), nameof(PwEntry.CustomFields));
    private static readonly JsonSerializerOptions SnapshotOptions = WithProperties(name => name != nameof(PwEntry.History));

    private static bool IsOverviewProperty(string name) =>
        name is not (nameof(PwEntry.Attachments) or nameof(PwEntry.History) or nameof(PwEntry.CustomFields));
    private static JsonSerializerOptions Only(params string[] properties) => WithProperties(properties.Contains);

    private static JsonSerializerOptions WithProperties(Func<string, bool> include, Action<JsonTypeInfo>? configure = null)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type != typeof(PwEntry)) return;
            foreach (var property in info.Properties.Where(p => !include(p.Name)).ToArray()) info.Properties.Remove(property);
            configure?.Invoke(info);
        });
        return new(Options) { TypeInfoResolver = resolver };
    }

    private static JsonSerializerOptions GridProjection(bool sortByPassword) => WithProperties(IsOverviewProperty, info =>
        {
            if (!sortByPassword)
                ReplaceWithLength(info, nameof(PwEntry.Password), (entry, length) => entry.GridPasswordLength = length);
            ReplaceWithLength(info, nameof(PwEntry.AutoTypeSequence), (entry, length) => entry.GridAutoTypeSequenceLength = length);
        });

    private static void ReplaceWithLength(JsonTypeInfo info, string name, Action<PwEntry, int> setLength)
    {
        info.Properties.Remove(info.Properties.Single(property => property.Name == name));
        var property = info.CreateJsonPropertyInfo(typeof(int), name);
        property.CustomConverter = new StringLengthConverter();
        property.Set = (entry, value) => setLength((PwEntry)entry, (int)value!);
        info.Properties.Add(property);
    }

    private sealed class StringLengthConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return 0;
            if (reader.TokenType != JsonTokenType.String) throw new JsonException("Expected a string in the vault entry.");
            if (!reader.HasValueSequence && !reader.ValueIsEscaped && Ascii.IsValid(reader.ValueSpan))
                return reader.ValueSpan.Length;

            int bytes = reader.HasValueSequence ? checked((int)reader.ValueSequence.Length) : reader.ValueSpan.Length;
            char[]? rented = null;
            Span<char> chars = bytes <= 256 ? stackalloc char[bytes] : (rented = ArrayPool<char>.Shared.Rent(bytes)).AsSpan(0, bytes);
            try { return reader.CopyString(chars); }
            catch (InvalidOperationException ex) { throw new JsonException("Invalid text in the vault entry.", ex); }
            finally
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(chars));
                if (rented != null) ArrayPool<char>.Shared.Return(rented);
            }
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
            throw new NotSupportedException("Grid projections are read-only.");
    }

    public static string Serialize(PwEntry entry, bool includeHistory = true)
    {
        if (!entry.HasLoadedDetails) throw new InvalidOperationException("Caricare i dettagli completi prima di copiare o salvare una voce.");
        return JsonSerializer.Serialize(entry, includeHistory ? Options : SnapshotOptions);
    }

    public static PwEntry Deserialize(string json) => JsonSerializer.Deserialize<PwEntry>(json, Options)
        ?? throw new InvalidDataException("Record del vault vuoto.");
}
