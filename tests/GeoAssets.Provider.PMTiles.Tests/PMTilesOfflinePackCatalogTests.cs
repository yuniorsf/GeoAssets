using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.OfflinePacks;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.Provider.PMTiles.Packs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Provider.PMTiles.Tests;

/// <summary>
/// XD01-464 — <see cref="PMTilesOfflinePackCatalog"/>: the stored packs with the pool entries drawing them, and a delete
/// that takes those entries off the map and out of the pool, closes their packs, deletes the file and evicts the pack's
/// cached tiles — and changes nothing while the active entry draws it.
/// </summary>
public sealed class PMTilesOfflinePackCatalogTests : IDisposable
{
    private static readonly TileAddress Tile = new(3, 4, 2);

    private readonly TempDirectory _picked = new();
    private readonly TempDirectory _appData = new();
    private readonly ProviderPool _pool = new();
    private readonly RecordingTileCache _cache = new();
    private readonly List<(Guid EntryId, bool InPool)> _removedFromMap = [];
    private readonly PMTilesPackStorage _storage;

    public PMTilesOfflinePackCatalogTests() => _storage = new PMTilesPackStorage(Path.Combine(_appData.Path, "offline-packs"));

    public void Dispose()
    {
        foreach (var entry in _pool.All)
            (entry.Provider.UnwrapToConcrete() as IDisposable)?.Dispose();
        _picked.Dispose();
        _appData.Dispose();
    }

    private PMTilesOfflinePackCatalog Catalog() => new(_storage, _pool, _cache);

    private Task RemoveFromMap(ProviderEntry entry)
    {
        _removedFromMap.Add((entry.Id, _pool.All.Contains(entry)));
        return Task.CompletedTask;
    }

    private string Picked(string path, string fileName)
    {
        var named = Path.Combine(_picked.Path, fileName);
        File.Move(path, named);
        return named;
    }

    private async Task<PMTilesPackAssetProvider> VectorProviderAsync(string fileName = "north-grid.pmtiles")
    {
        var bounds = string.Equals(fileName, "north-grid.pmtiles", StringComparison.Ordinal)
            ? (-38.6, -13.1, -38.3, -12.8)
            : (-40.0, -15.0, -39.0, -14.0);
        var source = Picked(TestPacks.Write(_picked.Path, [new TileAddress(0, 0, 0), Tile], maxZoom: 12, bounds: bounds), fileName);
        var provider = new PMTilesPackAssetProvider(await _storage.ImportAsync(source, CancellationToken.None), _cache);
        await provider.ReadAsync(new BoundingBox(-180, -85, 180, 85), mapZoom: 4);
        return provider;
    }

    private async Task<PMTilesRasterPackAssetProvider> RasterProviderAsync(string fileName = "basemap.pmtiles")
    {
        var source = Picked(TestRasterTiles.Archive(PMTilesTileType.Png, [new TileAddress(4, 7, 6)], minZoom: 4, maxZoom: 9,
            bounds: (-9.5, 38.6, -9.0, 38.9)).WriteTo(_picked.Path), fileName);
        return new PMTilesRasterPackAssetProvider(await _storage.ImportAsync(source, CancellationToken.None));
    }

    private ProviderEntry Connect(string name, IAssetProvider provider, string? packPath = null) =>
        _pool.Add(name, provider, PMTilesPackProviderPlugin.PluginId, packPath is null
            ? null
            : new Dictionary<string, string>(StringComparer.Ordinal) { [PMTilesPackProviderPlugin.PackPathKey] = packPath });

    [Fact]
    public async Task TheListing_ShowsEveryPack_WhichEntriesDrawIt_AndTheTotalStorage()
    {
        var active = _pool.Add("Field work", new EmptyProvider());
        _pool.SetActive(active.Id);
        var vector = await VectorProviderAsync();
        var raster = await RasterProviderAsync();
        var vectorEntry = Connect("North grid", vector);
        var decorated = Connect("North grid (copy)", new ValidatingAssetProvider(vector));
        using var unused = await VectorProviderAsync("spare-area.pmtiles");

        var listing = await Catalog().ListAsync(CancellationToken.None);

        listing.Packs.Should().HaveCount(3);
        listing.TotalBytes.Should().Be(listing.Packs.Sum(p => p.SizeBytes));
        listing.TotalBytes.Should().Be(await _storage.GetUsedBytesAsync(CancellationToken.None));

        var northGrid = listing.Packs.Single(p => string.Equals(p.Id, vector.Pack.Id, StringComparison.Ordinal));
        northGrid.DisplayName.Should().Be("North grid");
        northGrid.FileName.Should().Be("north-grid.pmtiles");
        northGrid.Kind.Should().Be(OfflineMapPackKind.Vector);
        northGrid.Bounds.Should().Be(new BoundingBox(-38.6, -13.1, -38.3, -12.8));
        (northGrid.MinZoom, northGrid.MaxZoom).Should().Be((0, 12));
        northGrid.SizeBytes.Should().Be(new FileInfo(vector.Pack.Path).Length);
        northGrid.UsedBy.Should().Equal(
            new OfflineMapPackUse(vectorEntry.Id, "North grid", IsActive: false),
            new OfflineMapPackUse(decorated.Id, "North grid (copy)", IsActive: false));

        var basemap = listing.Packs.Single(p => string.Equals(p.Id, raster.Pack.Id, StringComparison.Ordinal));
        basemap.Kind.Should().Be(OfflineMapPackKind.Raster);
        basemap.UsedBy.Should().BeEmpty("no entry draws it");
        basemap.DisplayName.Should().Be("basemap", "without an entry it is named after the picked file");
        (basemap.MinZoom, basemap.MaxZoom).Should().Be((4, 9));

        listing.Packs.Single(p => string.Equals(p.Id, unused.Pack.Id, StringComparison.Ordinal)).DisplayName.Should().Be("spare-area");
        raster.Dispose();
    }

    [Fact]
    public async Task AnEntryWhoseProviderIsNotThePack_IsMatchedByItsStoredPackPath()
    {
        using var vector = await VectorProviderAsync();
        var byPath = Connect("Reconnecting", new EmptyProvider(), vector.Pack.Path);
        _pool.Add("Other plugin", new EmptyProvider(), "rest",
            new Dictionary<string, string>(StringComparer.Ordinal) { [PMTilesPackProviderPlugin.PackPathKey] = vector.Pack.Path });

        var listing = await Catalog().ListAsync(CancellationToken.None);

        listing.Packs.Should().ContainSingle().Which.UsedBy.Select(u => u.EntryId).Should().Equal(byPath.Id);
    }

    [Fact]
    public async Task ADamagedPack_IsListedAsUnreadable_NamedByItsId()
    {
        Directory.CreateDirectory(_storage.Directory);
        await File.WriteAllBytesAsync(Path.Combine(_storage.Directory, "0123456789abcdef.pmtiles"), new byte[64]);

        var pack = (await Catalog().ListAsync(CancellationToken.None)).Packs.Should().ContainSingle().Which;

        pack.Kind.Should().Be(OfflineMapPackKind.Unreadable);
        pack.DisplayName.Should().Be("0123456789abcdef");
        pack.Bounds.Should().BeNull();
        pack.MinZoom.Should().BeNull();
        pack.MaxZoom.Should().BeNull();
    }

    [Fact]
    public async Task DeletingAVectorPack_TakesItsEntriesOffTheMapAndOutOfThePool_DeletesTheFile_AndEvictsItsTiles()
    {
        var active = _pool.Add("Field work", new EmptyProvider());
        _pool.SetActive(active.Id);
        var vector = await VectorProviderAsync();
        using var other = await VectorProviderAsync("spare-area.pmtiles");
        var first = Connect("North grid", vector);
        var second = Connect("North grid (copy)", new ValidatingAssetProvider(vector));
        var kept = Connect("Spare area", other);
        _cache.Entries.Keys.Should().Contain(key => key.Contains(vector.Pack.Id, StringComparison.Ordinal));
        _cache.Entries.Keys.Should().Contain(key => key.Contains(other.Pack.Id, StringComparison.Ordinal));

        var result = await Catalog().DeleteAsync(vector.Pack.Id, RemoveFromMap, CancellationToken.None);

        result.Should().Be(OfflineMapPackDeletion.Deleted);
        _removedFromMap.Should().Equal((first.Id, true), (second.Id, true));
        _pool.All.Should().Equal(active, kept);
        File.Exists(vector.Pack.Path).Should().BeFalse();
        _cache.EvictedPrefixes.Should().Equal($"tile/offline-pack/pmtiles/pmtiles/{vector.Pack.Id}/");
        _cache.Entries.Keys.Should().NotContain(key => key.Contains(vector.Pack.Id, StringComparison.Ordinal));
        _cache.Entries.Keys.Should().Contain(key => key.Contains(other.Pack.Id, StringComparison.Ordinal), "another pack's tiles stay");
        var read = () => vector.Pack.Archive.GetTileAsync(Tile, CancellationToken.None);
        await read.Should().ThrowAsync<ObjectDisposedException>("the deleted entries' provider was closed");
        (await Catalog().ListAsync(CancellationToken.None)).Packs.Select(p => p.Id).Should().Equal(other.Pack.Id);
    }

    [Fact]
    public async Task DeletingARasterPack_ClosesItsEntry_AndDeletesTheFile()
    {
        var raster = await RasterProviderAsync();
        var entry = Connect("Basemap", raster);

        var result = await Catalog().DeleteAsync(raster.Pack.Id, RemoveFromMap, CancellationToken.None);

        result.Should().Be(OfflineMapPackDeletion.Deleted);
        _removedFromMap.Should().Equal((entry.Id, true));
        _pool.All.Should().BeEmpty();
        File.Exists(raster.Pack.Path).Should().BeFalse();
        var read = () => raster.Pack.Archive.GetTileAsync(new TileAddress(4, 7, 6), CancellationToken.None);
        await read.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task APackTheActiveEntryDraws_IsNotDeleted()
    {
        using var vector = await VectorProviderAsync();
        var active = Connect("North grid", vector);
        _pool.SetActive(active.Id);

        var result = await Catalog().DeleteAsync(vector.Pack.Id, RemoveFromMap, CancellationToken.None);

        result.Should().Be(OfflineMapPackDeletion.InUseByActiveEntry);
        _removedFromMap.Should().BeEmpty();
        _pool.All.Should().Equal(active);
        File.Exists(vector.Pack.Path).Should().BeTrue();
        _cache.EvictedPrefixes.Should().BeEmpty();
        (await vector.Pack.Archive.GetTileAsync(Tile, CancellationToken.None)).Should().NotBeNull("the pack stays open");
        (await Catalog().ListAsync(CancellationToken.None)).Packs.Single().UsedBy.Should().ContainSingle().Which.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task APackNoEntryDraws_IsDeleted_AndAnUnknownOneIsNotFound()
    {
        var raster = await RasterProviderAsync();
        raster.Dispose();

        (await Catalog().DeleteAsync(raster.Pack.Id, RemoveFromMap, CancellationToken.None)).Should().Be(OfflineMapPackDeletion.Deleted);
        (await Catalog().DeleteAsync(raster.Pack.Id, RemoveFromMap, CancellationToken.None)).Should().Be(OfflineMapPackDeletion.NotFound);
        _removedFromMap.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutATileEviction_ThePackIsStillDeleted()
    {
        using var vector = await VectorProviderAsync();

        var result = await new PMTilesOfflinePackCatalog(_storage, _pool).DeleteAsync(vector.Pack.Id, RemoveFromMap, CancellationToken.None);

        result.Should().Be(OfflineMapPackDeletion.Deleted);
        File.Exists(vector.Pack.Path).Should().BeFalse();
    }

    [Fact]
    public async Task ABadIdOrNoCallback_IsRefused()
    {
        var catalog = Catalog();

        await FluentActions.Invoking(() => catalog.DeleteAsync("../escape", RemoveFromMap, CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Invoking(() => catalog.DeleteAsync("0123456789abcdef", null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        FluentActions.Invoking(() => new PMTilesOfflinePackCatalog(null!, _pool)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new PMTilesOfflinePackCatalog(_storage, null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddPMTilesPackStorage_RegistersTheCatalog_OverTheHostsPoolAndTileEviction()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProviderPool>(_pool);
        services.AddSingleton<ITileLayerVersionEviction>(_cache);
        services.AddPMTilesPackStorage(Path.Combine(_appData.Path, "offline-packs"));
        using var host = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = host.CreateScope();

        scope.ServiceProvider.GetRequiredService<IOfflineMapPackCatalog>().Should().BeOfType<PMTilesOfflinePackCatalog>();
        host.GetRequiredService<PMTilesPackStorage>().Directory.Should().Be(Path.Combine(_appData.Path, "offline-packs"));
        FluentActions.Invoking(() => new ServiceCollection().AddPMTilesPackStorage(" ")).Should().Throw<ArgumentException>();
    }

    private sealed class EmptyProvider : IAssetProvider
    {
        public event EventHandler<GeoFeature>? FeatureAdded      { add { } remove { } }
        public event EventHandler<GeoFeature>? FeatureUpdated    { add { } remove { } }
        public event EventHandler<string>?     FeatureDeleted    { add { } remove { } }
        public event EventHandler?             CollectionChanged { add { } remove { } }

        public GeoFeature? GetById(string id) => null;
        public IReadOnlyList<GeoFeature> GetAll() => [];
        public IReadOnlyList<GeoFeature> GetByAssetType(string assetTypeId) => [];
        public IReadOnlyList<GeoFeature> Search(string query) => [];
        public IReadOnlyList<GeoFeature> GetWithin(Core.Models.Geometry.GeoGeometry bounds) => [];
        public IReadOnlyList<GeoFeature> GetIntersecting(Core.Models.Geometry.GeoGeometry geometry) => [];
        public IReadOnlyList<GeoFeature> GetNearby(Core.Models.Geometry.GeoPoint center, double distanceDegrees) => [];
        public Task<IReadOnlyList<GeoFeature>> GetInBoundsAsync(double minLon, double minLat, double maxLon, double maxLat) => Task.FromResult<IReadOnlyList<GeoFeature>>([]);
        public Task<IReadOnlyList<System.Text.Json.JsonElement>> GetInBoundsJsonAsync(double minLon, double minLat, double maxLon, double maxLat) => Task.FromResult<IReadOnlyList<System.Text.Json.JsonElement>>([]);
        public Task<string?> GetInBoundsRawJsonAsync(double minLon, double minLat, double maxLon, double maxLat) => Task.FromResult<string?>(null);
        public IReadOnlyList<AssetType> GetAssetTypes() => [];
        public IReadOnlyList<Layer> GetLayers() => [];
        public IReadOnlyList<LayerRule> GetLayerRules(Guid assetTypeId) => [];
        public void Add(GeoFeature feature) { }
        public void Update(GeoFeature feature) { }
        public void AddRange(IEnumerable<GeoFeature> features) { }
        public void Delete(string id) { }
        public void Clear() { }
        public void LoadAll(IEnumerable<GeoFeature> features) { }
        public void AddAssetType(AssetType assetType) { }
        public void DeleteAssetType(Guid id) { }
        public void AddLayer(Layer layer) { }
        public void DeleteLayer(Guid id) { }
        public void AddLayerRule(LayerRule layerRule) { }
        public void DeleteLayerRule(Guid id) { }
    }
}
