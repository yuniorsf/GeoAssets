using System.Text.Json;
using System.Text.Json.Serialization;

namespace GeoAssets.Tools.SharedUseImport;

/// <summary>Reads the mapping file and writes the report (JSON, camelCase, enums by name).</summary>
internal static class SharedUseImportJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Reads and checks a mapping file.</summary>
    /// <exception cref="InvalidDataException">The file is not a valid mapping.</exception>
    public static async Task<SharedUseImportMapping> ReadMappingAsync(Stream stream, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stream);

        SharedUseImportMappingDocument? document;
        try
        {
            document = await JsonSerializer.DeserializeAsync<SharedUseImportMappingDocument>(stream, Options, ct).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The mapping file is not valid: {exception.Message}", exception);
        }

        return SharedUseImportMapping.From(document ?? throw new InvalidDataException("The mapping file is empty (null)."));
    }

    /// <summary>Writes <paramref name="report"/> to <paramref name="stream"/>.</summary>
    public static Task WriteReportAsync(Stream stream, SharedUseImportReport report, CancellationToken ct) =>
        JsonSerializer.SerializeAsync(stream, report, Options, ct);
}
