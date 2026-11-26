using System.Text.Json;

namespace GeoAssets.Ingestion.Profiles.NeoenergiaGse;

/// <summary>
/// The full field names of COELBA's GSE layers keyed by their truncated (10-character) DBF names, for deliveries
/// without ArcGIS lineage metadata. Loaded once from the embedded <c>coelba-field-aliases.json</c>.
/// </summary>
internal static class GseFieldAliases
{
    private const string ResourceName = "GeoAssets.Ingestion.Profiles.NeoenergiaGse.coelba-field-aliases.json";

    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly Lazy<Dictionary<string, IReadOnlyDictionary<string, string>>> ByLayer = new(Load);

    public static IReadOnlyDictionary<string, string> For(string layer) =>
        ByLayer.Value.GetValueOrDefault(layer) ?? Empty;

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Load()
    {
        using var stream = typeof(GseFieldAliases).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded resource '{ResourceName}' is missing.");
        var document = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)
            ?? throw new InvalidOperationException($"The embedded resource '{ResourceName}' is empty.");
        return document.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(entry.Value, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }
}
