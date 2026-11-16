using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// Exercises <see cref="PostgresAssetProvider"/>'s layer methods
/// (<c>GetLayers</c>/<c>AddLayer</c>/<c>DeleteLayer</c>) against a real PostGIS instance
/// (XD01-154). The container's <c>layer</c> table already holds rows seeded by migrations, so
/// every assertion targets the ids this class creates, and <see cref="DisposeAsync"/> removes
/// exactly those ids.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresAssetProviderLayerTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private readonly List<Guid> _layerIds = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Layers.Where(r => _layerIds.Contains(r.Id)).ExecuteDeleteAsync();
    }

    private PostgresAssetProvider CreateSut()
    {
        var options = fixture.CreateOptions();
        return new(new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);
    }

    private Layer NewLayer(string name = "XD01-154 layer")
    {
        var layer = new Layer { Name = name };
        _layerIds.Add(layer.Id);
        return layer;
    }

    [Fact]
    public async Task AddLayer_ThenGetLayers_RoundTripsEveryStyleField()
    {
        var layer = NewLayer("Dashed trunk line");
        layer.GeometryType = GeometryType.LineString;
        layer.Color = "#aa00ff";
        layer.Radius = 12.5;
        layer.IconUrl = "icons/trunk.svg";
        layer.Weight = 4.5;
        layer.DashArray = "5, 5";
        layer.FillColor = "#00ffaa";
        layer.FillOpacity = 0.35;
        await using (var writer = CreateSut())
        {
            writer.AddLayer(layer);
        }

        // A second provider instance has an empty cache and change tracker, so this reads the row back from Postgres.
        await using var reader = CreateSut();
        var reloaded = reader.GetLayers().Should().ContainSingle(l => l.Id == layer.Id).Subject;

        reloaded.Should().BeEquivalentTo(layer);
    }

    [Fact]
    public async Task AddLayer_ThenGetLayers_RoundTripsNullDashArray()
    {
        var layer = NewLayer("Solid area");
        layer.GeometryType = GeometryType.Polygon;
        layer.DashArray = null;
        await using (var writer = CreateSut())
        {
            writer.AddLayer(layer);
        }

        await using var reader = CreateSut();
        var reloaded = reader.GetLayers().Should().ContainSingle(l => l.Id == layer.Id).Subject;

        reloaded.DashArray.Should().BeNull();
        reloaded.GeometryType.Should().Be(GeometryType.Polygon);
    }

    [Fact]
    public async Task AddLayer_WithExistingId_IsNoOp()
    {
        var original = NewLayer("Original");
        var duplicate = new Layer { Id = original.Id, Name = "Duplicate", Color = "#000000" };
        await using var sut = CreateSut();
        sut.AddLayer(original);

        var addOnSameInstance = () => sut.AddLayer(duplicate);
        addOnSameInstance.Should().NotThrow();

        // A fresh instance has not tracked the row, so the existence check has to hit the database.
        await using var other = CreateSut();
        var addOnOtherInstance = () => other.AddLayer(duplicate);
        addOnOtherInstance.Should().NotThrow();

        sut.GetLayers().Should().ContainSingle(l => l.Id == original.Id).Which.Name.Should().Be("Original");
        other.GetLayers().Should().ContainSingle(l => l.Id == original.Id).Which.Name.Should().Be("Original");
    }

    [Fact]
    public async Task DeleteLayer_RemovesLayer()
    {
        var layer = NewLayer();
        await using var sut = CreateSut();
        sut.AddLayer(layer);

        sut.DeleteLayer(layer.Id);

        await using var reader = CreateSut();
        reader.GetLayers().Should().NotContain(l => l.Id == layer.Id);
    }

    [Fact]
    public async Task DeleteLayer_UnknownId_IsNoOp()
    {
        var existing = NewLayer();
        await using var sut = CreateSut();
        sut.AddLayer(existing);

        var delete = () => sut.DeleteLayer(Guid.NewGuid());

        delete.Should().NotThrow();
        sut.GetLayers().Should().ContainSingle(l => l.Id == existing.Id);
    }

    [Fact]
    public async Task GetLayers_AfterWritesOnSameInstance_ReflectsEachWrite()
    {
        var layer = NewLayer();
        await using var sut = CreateSut();

        // Populates _layerCache before any write.
        sut.GetLayers().Should().NotContain(l => l.Id == layer.Id);

        sut.AddLayer(layer);
        sut.GetLayers().Should().ContainSingle(l => l.Id == layer.Id);

        sut.DeleteLayer(layer.Id);
        sut.GetLayers().Should().NotContain(l => l.Id == layer.Id);
    }
}
