using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Navigation;
using GeoAssets.Core.OfflinePacks;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.MAUI.Navigation;
using GeoAssets.MAUI.Services.Offline;
using GeoAssets.Provider.PMTiles;
using GeoAssets.Provider.PMTiles.Packs;
using GeoAssets.Provider.PMTiles.Tests;
using GeoAssets.Shared.Components.OfflinePacks;
using GeoAssets.Shared.Services.Offline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SQLite;
using Xunit;

namespace GeoAssets.MAUI.Tests;

/// <summary>
/// XD01-464 — deleting an offline map pack as MAUI composes it: the <c>pmtiles</c> plugin over the app's pack storage,
/// its tiles read through the SQLite tile cache, the pool entry it was connected as. The catalog the Offline maps panel
/// uses removes the entry, the file and the pack's cached rows, and leaves the basemap entries and other packs' rows.
/// </summary>
public sealed class OfflinePackDeletionTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "geoassets-maui-pack-delete-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero));
    private SQLiteAsyncConnection _db = null!;
    private ServiceProvider _host = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        _db = new SQLiteAsyncConnection(Path.Combine(_directory, "offline.db3"), storeDateTimeAsTicks: true);
        await _db.CreateTableAsync<LocalVectorTileRow>();

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton<IProviderPool, ProviderPool>();
        services.AddSingleton(new SqliteLocalVectorTileCache(() => Task.FromResult(_db), _clock));
        services.AddSingleton<ILocalVectorTileCache>(sp => sp.GetRequiredService<SqliteLocalVectorTileCache>());
        services.AddSingleton<ITileCache>(sp => sp.GetRequiredService<SqliteLocalVectorTileCache>());
        services.AddSingleton<ITileLayerVersionEviction>(sp => sp.GetRequiredService<SqliteLocalVectorTileCache>());
        services.AddGeoAssetsPMTilesPacks();
        services.AddPMTilesPackStorage(Path.Combine(_directory, "offline-packs"));
        _host = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        await _db.CloseAsync();
        Directory.Delete(_directory, recursive: true);
    }

    private async Task<ProviderEntry> ConnectAsync(string name, string picked)
    {
        var plugin = _host.GetServices<IProviderPlugin>().Single(p => string.Equals(p.Id, PMTilesPackProviderPlugin.PluginId, StringComparison.Ordinal));
        var config = new ProviderConfig(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = name,
            [PMTilesPackProviderPlugin.PackPathKey] = picked,
        });
        var provider = await plugin.CreateAsync(config, _host);
        return _host.GetRequiredService<IProviderPool>().Add(name, provider, plugin.Id, config.ToReconnectValues(plugin.ConfigFields));
    }

    private Task<int> CachedRowsOfAsync(string packId) =>
        _db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM local_vector_tiles WHERE Key LIKE ?", $"tile/offline-pack/pmtiles/pmtiles/{packId}/%");

    [Fact]
    public async Task DeletingAVectorPack_RemovesItsEntryFileAndCachedTiles_KeepingTheRest()
    {
        var pool = _host.GetRequiredService<IProviderPool>();
        var deleted = await ConnectAsync("North grid", TestPacks.Write(_directory, [new TileAddress(0, 0, 0)], maxZoom: 0, bounds: (-39, -14, -38, -13)));
        var kept = await ConnectAsync("South grid", TestPacks.Write(_directory, [new TileAddress(0, 0, 0)], maxZoom: 0, bounds: (-41, -16, -40, -15)));
        await _host.GetRequiredService<ILocalVectorTileCache>().PutAsync(new LocalVectorTile(4, 5, 5, [7], _clock.GetUtcNow().UtcDateTime));
        var deletedPack = ((PMTilesPackAssetProvider)deleted.Provider).Pack;
        var keptPack = ((PMTilesPackAssetProvider)kept.Provider).Pack;
        (await CachedRowsOfAsync(deletedPack.Id)).Should().BeGreaterThan(0, "connecting read the overview through the cache");

        using var scope = _host.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IOfflineMapPackCatalog>();
        var listing = await catalog.ListAsync(CancellationToken.None);
        listing.Packs.Should().HaveCount(2);
        listing.Packs.Single(p => string.Equals(p.Id, deletedPack.Id, StringComparison.Ordinal)).UsedBy.Should().ContainSingle().Which.Name.Should().Be("North grid");
        listing.Packs.Should().OnlyContain(p => p.ImportedAt == _clock.GetUtcNow(), "the import time is the app's clock");

        var removedFromMap = new List<Guid>();
        var result = await catalog.DeleteAsync(deletedPack.Id, entry =>
        {
            removedFromMap.Add(entry.Id);
            return Task.CompletedTask;
        }, CancellationToken.None);

        result.Should().Be(OfflineMapPackDeletion.Deleted);
        removedFromMap.Should().Equal(deleted.Id);
        pool.All.Should().Equal(kept);
        File.Exists(deletedPack.Path).Should().BeFalse();
        (await CachedRowsOfAsync(deletedPack.Id)).Should().Be(0);
        (await CachedRowsOfAsync(keptPack.Id)).Should().BeGreaterThan(0);
        (await _host.GetRequiredService<ILocalVectorTileCache>().GetAsync(4, 5, 5)).Should().NotBeNull("a basemap entry is not a pack's tile");
        (await catalog.ListAsync(CancellationToken.None)).Packs.Select(p => p.Id).Should().Equal(keptPack.Id);
        keptPack.Dispose();
    }

    [Fact]
    public void TheOfflineMapsMenuItem_OpensTheOfflinePacksPanel_AfterCollections()
    {
        var item = new OfflinePacksMenuItem();

        item.Should().BeAssignableTo<MenuPanelItem>();
        item.ComponentType.Should().Be<OfflinePacksPanel>();
        item.Id.Should().Be("offline-packs");
        item.LabelKey.Should().Be("offlinePacks.title");
        item.ParentId.Should().BeNull();
        item.SortOrder.Should().BeGreaterThan(50, "it follows Collections (50), where packs are added");
    }
}
