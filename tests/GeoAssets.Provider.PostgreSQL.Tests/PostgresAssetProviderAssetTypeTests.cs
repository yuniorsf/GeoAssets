using System.Text.Json.Nodes;
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
/// Exercises <see cref="PostgresAssetProvider"/>'s asset-type methods
/// (<c>GetAssetTypes</c>/<c>AddAssetType</c>/<c>DeleteAssetType</c>) against a real PostGIS
/// instance (XD01-154). The container's <c>asset_type</c> table already holds rows seeded by
/// migrations, so every assertion targets the ids this class creates, and
/// <see cref="DisposeAsync"/> removes exactly those ids.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresAssetProviderAssetTypeTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private readonly List<Guid> _assetTypeIds = [];
    private readonly List<Guid> _layerIds = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.AssetTypes.Where(r => _assetTypeIds.Contains(r.Id)).ExecuteDeleteAsync();
        await db.Layers.Where(r => _layerIds.Contains(r.Id)).ExecuteDeleteAsync();
    }

    private PostgresAssetProvider CreateSut()
    {
        var options = fixture.CreateOptions();
        return new(new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);
    }

    private AssetType NewAssetType(string name = "XD01-154 type", bool isBuiltIn = false)
    {
        var assetType = new AssetType { Name = name, IsBuiltIn = isBuiltIn };
        _assetTypeIds.Add(assetType.Id);
        return assetType;
    }

    private Layer NewLayer()
    {
        var layer = new Layer { Name = "XD01-154 default layer", GeometryType = GeometryType.Point };
        _layerIds.Add(layer.Id);
        return layer;
    }

    [Fact]
    public async Task AddAssetType_ThenGetAssetTypes_RoundTripsEveryMappedField()
    {
        var layer = NewLayer();
        var assetType = NewAssetType("Water tower");
        assetType.Color = "#123456";
        assetType.IconUrl = "icons/water-tower.svg";
        assetType.IsProtected = true;
        assetType.AllowedGeometryType = GeometryType.Polygon;
        assetType.DefaultLayerId = layer.Id;
        assetType.AttributesSchemaJson = """{"type":"object","required":["height"]}""";
        assetType.OrganizationId = Guid.NewGuid();
        await using (var writer = CreateSut())
        {
            writer.AddLayer(layer);
            writer.AddAssetType(assetType);
        }

        // A second provider instance has an empty cache and change tracker, so this reads the row back from Postgres.
        await using var reader = CreateSut();
        var reloaded = reader.GetAssetTypes().Should().ContainSingle(t => t.Id == assetType.Id).Subject;

        // AttributesSchemaJson is stored as jsonb, which keeps the document but not its exact
        // whitespace/key order — so it is compared as JSON, and everything else member by member.
        reloaded.Should().BeEquivalentTo(assetType, o => o.Excluding(t => t.AttributesSchemaJson));
        JsonNode.DeepEquals(JsonNode.Parse(reloaded.AttributesSchemaJson!), JsonNode.Parse(assetType.AttributesSchemaJson))
            .Should().BeTrue();
    }

    [Fact]
    public async Task AddAssetType_ThenGetAssetTypes_RoundTripsNullOptionalFields()
    {
        var assetType = NewAssetType();
        await using (var writer = CreateSut())
        {
            writer.AddAssetType(assetType);
        }

        await using var reader = CreateSut();
        var reloaded = reader.GetAssetTypes().Should().ContainSingle(t => t.Id == assetType.Id).Subject;

        reloaded.AllowedGeometryType.Should().BeNull();
        reloaded.DefaultLayerId.Should().BeNull();
        reloaded.AttributesSchemaJson.Should().BeNull();
        reloaded.IsProtected.Should().BeFalse();
        reloaded.OrganizationId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task AddAssetType_WithExistingId_IsNoOp()
    {
        var original = NewAssetType("Original");
        var duplicate = new AssetType { Id = original.Id, Name = "Duplicate", Color = "#000000" };
        await using var sut = CreateSut();
        sut.AddAssetType(original);

        var addOnSameInstance = () => sut.AddAssetType(duplicate);
        addOnSameInstance.Should().NotThrow();

        // A fresh instance has not tracked the row, so the existence check has to hit the database.
        await using var other = CreateSut();
        var addOnOtherInstance = () => other.AddAssetType(duplicate);
        addOnOtherInstance.Should().NotThrow();

        sut.GetAssetTypes().Should().ContainSingle(t => t.Id == original.Id).Which.Name.Should().Be("Original");
        other.GetAssetTypes().Should().ContainSingle(t => t.Id == original.Id).Which.Name.Should().Be("Original");
    }

    [Fact]
    public async Task DeleteAssetType_RemovesCustomType()
    {
        var assetType = NewAssetType();
        await using var sut = CreateSut();
        sut.AddAssetType(assetType);

        sut.DeleteAssetType(assetType.Id);

        await using var reader = CreateSut();
        reader.GetAssetTypes().Should().NotContain(t => t.Id == assetType.Id);
    }

    [Fact]
    public async Task DeleteAssetType_BuiltInType_IsNoOp()
    {
        var builtIn = NewAssetType("Built-in", isBuiltIn: true);
        await using var sut = CreateSut();
        sut.AddAssetType(builtIn);

        sut.DeleteAssetType(builtIn.Id);

        await using var reader = CreateSut();
        reader.GetAssetTypes().Should().ContainSingle(t => t.Id == builtIn.Id).Which.IsBuiltIn.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAssetType_UnknownId_IsNoOp()
    {
        var existing = NewAssetType();
        await using var sut = CreateSut();
        sut.AddAssetType(existing);

        var delete = () => sut.DeleteAssetType(Guid.NewGuid());

        delete.Should().NotThrow();
        sut.GetAssetTypes().Should().ContainSingle(t => t.Id == existing.Id);
    }

    [Fact]
    public async Task GetAssetTypes_AfterWritesOnSameInstance_ReflectsEachWrite()
    {
        var assetType = NewAssetType();
        await using var sut = CreateSut();

        // Populates _typeCache before any write.
        sut.GetAssetTypes().Should().NotContain(t => t.Id == assetType.Id);

        sut.AddAssetType(assetType);
        sut.GetAssetTypes().Should().ContainSingle(t => t.Id == assetType.Id);

        sut.DeleteAssetType(assetType.Id);
        sut.GetAssetTypes().Should().NotContain(t => t.Id == assetType.Id);
    }
}
