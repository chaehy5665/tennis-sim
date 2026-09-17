using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using TennisSim.Core;

namespace TennisSim.Cli;

public static class ReplayJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type == typeof(Vec3))
                foreach (var property in info.Properties.Where(p => p.Set == null).ToArray()) info.Properties.Remove(property);
            // New optional fields are omitted when unused so a legacy-path replay keeps its previous
            // bytes. None of these three can be null-valued in a legacy record.
            if (info.Type == typeof(BallState))
            {
                var spin = info.Properties.FirstOrDefault(p => string.Equals(p.Name, "AngularVelocity", StringComparison.OrdinalIgnoreCase));
                if (spin != null) spin.ShouldSerialize = (_, value) => value is Vec3 v && (v.X != 0 || v.Y != 0 || v.Z != 0);
            }
            if (info.Type == typeof(MatchEvent) || info.Type == typeof(MatchInput))
            {
                string name = info.Type == typeof(MatchEvent) ? "Bounce" : "Surface";
                var optional = info.Properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (optional != null) optional.ShouldSerialize = (_, value) => value != null;
            }
        });
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = resolver,
            Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
        };
    }
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new ArgumentException("Empty JSON");
    public static void Save<T>(string path, T value)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory != null) Directory.CreateDirectory(directory);
        File.WriteAllText(path, Serialize(value));
    }
    public static MatchRecord Load(string path)
    {
        var record = Deserialize<MatchRecord>(File.ReadAllText(path));
        if (record.SchemaVersion != "1.0") throw new ArgumentException("Unsupported replay schema");
        return record;
    }
}
