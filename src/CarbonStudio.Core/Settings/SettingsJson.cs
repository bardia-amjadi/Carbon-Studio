using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CarbonStudio.Core.Settings;

/// <summary>Shared JSON configuration and layer-merge logic for settings files.</summary>
internal static class SettingsJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Deep-merges <paramref name="overlay"/> into <paramref name="target"/>. Objects merge recursively,
    /// arrays and scalars replace, and JSON null means "no override".
    /// </summary>
    public static void Merge(JsonObject target, JsonObject overlay)
    {
        foreach (var (key, value) in overlay)
        {
            if (value is null)
            {
                continue;
            }

            if (value is JsonObject overlayChild && target[key] is JsonObject targetChild)
            {
                Merge(targetChild, overlayChild);
            }
            else
            {
                target[key] = value.DeepClone();
            }
        }
    }

    public static JsonObject ToObject<T>(T value) =>
        JsonSerializer.SerializeToNode(value, Options)!.AsObject();

    public static T Deserialize<T>(JsonObject node) =>
        node.Deserialize<T>(Options) ?? throw new JsonException("The settings document was empty.");

    /// <summary>Parses a file's text into an object; throws <see cref="JsonException"/> if it is not a JSON object.</summary>
    public static JsonObject ParseObject(string json)
    {
        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        return node as JsonObject ?? throw new JsonException("The settings file must contain a JSON object.");
    }
}
