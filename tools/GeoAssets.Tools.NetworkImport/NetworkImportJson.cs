using System.Text.Json;
using System.Text.Json.Serialization;

namespace GeoAssets.Tools.NetworkImport;

/// <summary>The tool's JSON: Web defaults (camelCase), enums as camelCase strings; indented for the files it writes.</summary>
internal static class NetworkImportJson
{
    public static JsonSerializerOptions Files { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static JsonSerializerOptions Wire { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static async Task WriteFileAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var stream = File.Create(path);
        await using (stream.ConfigureAwait(false))
            await JsonSerializer.SerializeAsync(stream, value, Files, cancellationToken).ConfigureAwait(false);
    }
}
