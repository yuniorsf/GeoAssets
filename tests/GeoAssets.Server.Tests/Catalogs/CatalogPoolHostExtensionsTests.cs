using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Server.Catalogs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Catalogs;

public sealed class CatalogPoolHostExtensionsTests
{
    private readonly TestProviderPlugin _plugin = new();

    private ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [$"{CatalogConfiguration.ProvidersKey}:0:Id"] = "primary",
                [$"{CatalogConfiguration.ProvidersKey}:0:Plugin"] = TestProviderPlugin.DefaultId,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IProviderPlugin>(_plugin);
        services.AddServerCatalogPool(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task AddServerCatalogPool_RegistersTheSamePoolAsICatalogPoolAndCatalogPool()
    {
        await using var provider = BuildProvider();

        provider.GetRequiredService<ICatalogPool>().Should().BeSameAs(provider.GetRequiredService<CatalogPool>());
    }

    [Fact]
    public async Task InitializeCatalogPoolAsync_MakesTheDefaultCatalogTheAssetProviderSingleton()
    {
        await using var provider = BuildProvider();

        await provider.InitializeCatalogPoolAsync();

        provider.GetRequiredService<IAssetProvider>().Should().BeSameAs(provider.GetRequiredService<ICatalogPool>().DefaultCatalog);
        _plugin.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task DisposingTheServiceProvider_DisposesTheProviderOnce()
    {
        var provider = BuildProvider();
        await provider.InitializeCatalogPoolAsync();
        provider.GetRequiredService<IAssetProvider>();

        await provider.DisposeAsync();

        _plugin.Created.Should().ContainSingle().Which.DisposeCount.Should().Be(1);
    }

    [Fact]
    public Task InitializeCatalogPoolAsync_NullServices_Throws()
    {
        var act = () => CatalogPoolHostExtensions.InitializeCatalogPoolAsync(null!);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void AddServerCatalogPool_NullServices_Throws()
    {
        var act = () => CatalogPoolHostExtensions.AddServerCatalogPool(null!, new ConfigurationBuilder().Build());

        act.Should().Throw<ArgumentNullException>();
    }
}
