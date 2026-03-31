using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;

namespace GeoAssets.Provider.InMemory;

/// <summary>
/// Plugin that creates a local in-memory collection, optionally seeded
/// from a GeoJSON file chosen by the user.
/// </summary>
public sealed class InMemoryProviderPlugin : IProviderPlugin
{
    public string Id => "inmemory";
    public string DisplayName => "Local Collection";
    public string Description => "Start with an empty workspace or load a GeoJSON file";

    public IReadOnlyList<ProviderConfigField> ConfigFields =>
    [
        new("name",
            Label: "Collection name",
            Placeholder: "My collection",
            DefaultValue: "Local"),
        new("file",
            Label: "GeoJSON file (optional)",
            Type: ProviderFieldType.File)
    ];

    public Task<IAssetProvider> CreateAsync(
        ProviderConfig config,
        IServiceProvider services,
        CancellationToken ct = default)
    {
        var provider = new InMemoryAssetProvider();

        var json = config.Get("file_content");
        if (!string.IsNullOrWhiteSpace(json))
        {
            var imported = GeoJsonSerializer.Deserialize(json);
            if (imported is not null)
            {
                foreach (var t in imported.Metadata.AssetTypes.Where(t => !t.IsBuiltIn))
                    provider.AddAssetType(t);
                provider.AddRange(imported.Features);
            }
        }

        return Task.FromResult<IAssetProvider>(provider);
    }
}
