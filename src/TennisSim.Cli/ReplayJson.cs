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
