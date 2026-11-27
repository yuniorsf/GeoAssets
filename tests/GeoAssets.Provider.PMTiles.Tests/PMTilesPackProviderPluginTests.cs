using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PMTiles.Packs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Provider.PMTiles.Tests;

/// <summary>
/// XD01-438 — the <c>pmtiles</c> plugin: a path-only <c>.pmtiles</c> field, a pack imported into the host's storage on
/// connect (its stored path kept for reconnecting offline), tiles read through the host's tile cache, and a friendly
/// refusal of anything else.
/// </summary>
public sealed class PMTilesPackProviderPluginTests : IDisposable
{
    private readonly TempDirectory _picked = new();
    private readonly TempDirectory _appData = new();
    private readonly PMTilesPackProviderPlugin _plugin = new();

    public void Dispose()
    {
        _picked.Dispose();
        _appData.Dispose();
    }

    private static ProviderConfig Picked(string path) => new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["name"] = "Lisboa",
        [PMTilesPackProviderPlugin.PackPathKey] = path,
        ["pack_name"] = Path.GetFileName(path),
    });

    private ServiceProvider Host(bool withStorage, ITileCache? cache = null)
    {
        var services = new ServiceCollection();
        if (withStorage) services.AddPMTilesPackStorage(Path.Combine(_appData.Path, "offline-packs"));
        if (cache is not null) services.AddSingleton(cache);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void ThePackField_IsARequiredPathOnlyPMTilesFile()
    {
        _plugin.Id.Should().Be("pmtiles");
        var pack = _plugin.ConfigFields.Single(f => string.Equals(f.Key, PMTilesPackProviderPlugin.PackField, StringComparison.Ordinal));

        pack.Type.Should().Be(ProviderFieldType.BinaryFile);
        pack.GetAcceptedExtensions().Should().Equal(".pmtiles");
        pack.PathOnly.Should().BeTrue("a multi-gigabyte pack is never read into memory by the form");
        pack.Required.Should().BeTrue();
        pack.IsProvidedIn(Picked("/data/a.pmtiles")).Should().BeTrue();
    }

    [Fact]
    public async Task Connecting_ImportsThePackIntoTheHostsStorage_AndKeepsTheStoredPathForReconnecting()
    {
        var source = TestPacks.Write(_picked.Path, [new TileAddress(0, 0, 0)], maxZoom: 0);
        var config = Picked(source);
        using var host = Host(withStorage: true);

        using var provider = (PMTilesPackAssetProvider)await _plugin.CreateAsync(config, host);

        var stored = config.Get(PMTilesPackProviderPlugin.PackPathKey);
        stored.Should().Be(Path.Combine(_appData.Path, "offline-packs", provider.Pack.Id + ".pmtiles"));
        File.Exists(stored).Should().BeTrue();
        config.ToReconnectValues(_plugin.ConfigFields).Should().Contain(PMTilesPackProviderPlugin.PackPathKey, stored);
        provider.GetAll().Should().ContainSingle("the overview is read on connect, so the map has something to fit to");
    }

    [Fact]
    public async Task Reconnecting_FromTheStoredPath_WorksWithThePickedFileGone()
    {
        var source = TestPacks.Write(_picked.Path, [new TileAddress(0, 0, 0)], maxZoom: 0);
        var config = Picked(source);
        using var host = Host(withStorage: true);
        ((PMTilesPackAssetProvider)await _plugin.CreateAsync(config, host)).Dispose();

        File.Delete(source);
        var reconnect = new ProviderConfig(config.ToReconnectValues(_plugin.ConfigFields));

        using var provider = (PMTilesPackAssetProvider)await _plugin.CreateAsync(reconnect, host);

        provider.GetAll().Should().ContainSingle();
        Directory.GetFiles(Path.Combine(_appData.Path, "offline-packs")).Should().ContainSingle();
    }

    [Fact]
    public async Task WithoutStorage_ThePackIsReadWhereItWasPicked()
    {
        var source = TestPacks.Write(_picked.Path, [new TileAddress(0, 0, 0)], maxZoom: 0);
        var config = Picked(source);
        using var host = Host(withStorage: false);

        using var provider = (PMTilesPackAssetProvider)await _plugin.CreateAsync(config, host);

        provider.Pack.Path.Should().Be(Path.GetFullPath(source));
    }

    [Fact]
    public async Task ThePacksTiles_AreReadThroughTheHostsTileCache()
    {
        var cache = new MemoryTileCache();
        using var host = Host(withStorage: false, cache);

        using var provider = (PMTilesPackAssetProvider)await _plugin.CreateAsync(Picked(TestPacks.Write(_picked.Path, [new TileAddress(0, 0, 0)], maxZoom: 0)), host);

        cache.Count.Should().Be(1);
        (await cache.GetAsync(PMTilesPackTiles.KeyOf(provider.Pack.Id, new TileAddress(0, 0, 0)), CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task NoPickedPack_IsRefused()
    {
        using var host = Host(withStorage: true);

        var act = () => _plugin.CreateAsync(new ProviderConfig(), host);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Select an offline map pack*");
    }

    [Fact]
    public async Task AFileThatIsNotAPack_IsRefusedWithItsName()
    {
        var path = Path.Combine(_picked.Path, "photo.pmtiles");
        await File.WriteAllBytesAsync(path, new byte[1024]);
        using var host = Host(withStorage: true);

        var act = () => _plugin.CreateAsync(Picked(path), host);

        (await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("'photo.pmtiles' can't be used as an offline map pack*"))
            .WithInnerException<PMTilesFormatException>();
    }

    [Fact]
    public async Task APackNoLongerOnTheDevice_AsksForAnotherPick()
    {
        using var host = Host(withStorage: true);

        var act = () => _plugin.CreateAsync(Picked(Path.Combine(_picked.Path, "gone.pmtiles")), host);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*'gone.pmtiles' is no longer on this device*");
    }
}
