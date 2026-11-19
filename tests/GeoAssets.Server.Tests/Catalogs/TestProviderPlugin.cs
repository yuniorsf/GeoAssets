using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;

namespace GeoAssets.Server.Tests.Catalogs;

/// <summary>An in-memory <see cref="IProviderPlugin"/> that records the config it was given and every provider it built.</summary>
internal sealed class TestProviderPlugin(string id) : IProviderPlugin
{
    public const string DefaultId = "test-memory";

    public TestProviderPlugin() : this(DefaultId)
    {
    }

    public string Id { get; } = id;
    public string DisplayName => "Test in-memory";
    public string Description => "Test double";
    public IReadOnlyList<ProviderConfigField> ConfigFields => [];

    public List<ProviderConfig> Configs { get; } = [];
    public List<CatalogTestProvider> Created { get; } = [];

    public Task<IAssetProvider> CreateAsync(ProviderConfig config, IServiceProvider services, CancellationToken ct = default)
    {
        Configs.Add(config);
        var provider = new CatalogTestProvider($"{Id}#{Created.Count}");
        Created.Add(provider);
        return Task.FromResult<IAssetProvider>(provider);
    }
}
