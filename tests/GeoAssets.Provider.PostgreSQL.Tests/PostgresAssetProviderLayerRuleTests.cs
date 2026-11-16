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
/// Exercises <see cref="PostgresAssetProvider"/>'s layer-rule methods
/// (<c>GetLayerRules</c>/<c>AddLayerRule</c>/<c>DeleteLayerRule</c>) against a real PostGIS
/// instance (XD01-154), including the <c>Conditions</c> child-collection projection. Each test
/// creates its own parent asset type(s) and layer, because <c>layer_rule</c> has foreign keys to
/// both; <see cref="DisposeAsync"/> removes exactly the ids this class created.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresAssetProviderLayerRuleTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private readonly List<Guid> _assetTypeIds = [];
    private readonly List<Guid> _layerIds = [];
    private readonly List<Guid> _layerRuleIds = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.LayerRules.Where(r => _layerRuleIds.Contains(r.Id)).ExecuteDeleteAsync();
        await db.AssetTypes.Where(r => _assetTypeIds.Contains(r.Id)).ExecuteDeleteAsync();
        await db.Layers.Where(r => _layerIds.Contains(r.Id)).ExecuteDeleteAsync();
    }

    private PostgresAssetProvider CreateSut()
    {
        var options = fixture.CreateOptions();
        return new(new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);
    }

    private Guid AddAssetType(PostgresAssetProvider sut)
    {
        var assetType = new AssetType { Name = "XD01-154 rule owner" };
        _assetTypeIds.Add(assetType.Id);
        sut.AddAssetType(assetType);
        return assetType.Id;
    }

    private Guid AddLayer(PostgresAssetProvider sut)
    {
        var layer = new Layer { Name = "XD01-154 rule target", GeometryType = GeometryType.Point };
        _layerIds.Add(layer.Id);
        sut.AddLayer(layer);
        return layer.Id;
    }

    private LayerRule NewRule(Guid assetTypeId, Guid layerId, int priority = 0, params LayerRuleCondition[] conditions)
    {
        var rule = new LayerRule { AssetTypeId = assetTypeId, LayerId = layerId, Priority = priority, Conditions = [.. conditions] };
        _layerRuleIds.Add(rule.Id);
        return rule;
    }

    private async Task<int> CountConditionRowsAsync(Guid layerRuleId)
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        return await db.LayerRuleConditions.CountAsync(c => c.LayerRuleId == layerRuleId);
    }

    [Fact]
    public async Task AddLayerRule_ThenGetLayerRules_RoundTripsSeveralConditions()
    {
        Guid assetTypeId;
        LayerRule rule;
        await using (var writer = CreateSut())
        {
            assetTypeId = AddAssetType(writer);
            rule = NewRule(
                assetTypeId, AddLayer(writer), priority: 7,
                new LayerRuleCondition { Attribute = "status", Operator = LayerRuleOperator.Equals, Value = "active" },
                new LayerRuleCondition { Attribute = "owner", Operator = LayerRuleOperator.NotEquals, Value = "unknown" },
                new LayerRuleCondition { Attribute = "height", Operator = LayerRuleOperator.GreaterThanOrEqual, Value = "10" },
                new LayerRuleCondition { Attribute = "load", Operator = LayerRuleOperator.LessThanOrEqual, Value = "80.5" });
            writer.AddLayerRule(rule);
        }

        // A second provider instance has an empty cache and change tracker, so the rule and its
        // conditions are materialized from Postgres rather than echoed back from memory.
        await using var reader = CreateSut();
        var reloaded = reader.GetLayerRules(assetTypeId).Should().ContainSingle().Subject;

        reloaded.Should().BeEquivalentTo(rule);
        reloaded.Conditions.Should().HaveCount(4);
    }

    [Fact]
    public async Task AddLayerRule_WithoutConditions_RoundTripsEmptyConditions()
    {
        Guid assetTypeId;
        LayerRule rule;
        await using (var writer = CreateSut())
        {
            assetTypeId = AddAssetType(writer);
            rule = NewRule(assetTypeId, AddLayer(writer), priority: 3);
            writer.AddLayerRule(rule);
        }

        await using var reader = CreateSut();
        var reloaded = reader.GetLayerRules(assetTypeId).Should().ContainSingle().Subject;

        reloaded.Should().BeEquivalentTo(rule);
        reloaded.Conditions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLayerRules_ReturnsOnlyRulesOfRequestedAssetType()
    {
        await using var sut = CreateSut();
        var layerId = AddLayer(sut);
        var firstTypeId = AddAssetType(sut);
        var secondTypeId = AddAssetType(sut);
        var firstRuleA = NewRule(firstTypeId, layerId, priority: 1);
        var firstRuleB = NewRule(firstTypeId, layerId, priority: 2);
        var secondRule = NewRule(secondTypeId, layerId, priority: 1);
        sut.AddLayerRule(firstRuleA);
        sut.AddLayerRule(firstRuleB);
        sut.AddLayerRule(secondRule);

        await using var reader = CreateSut();

        reader.GetLayerRules(firstTypeId).Select(r => r.Id).Should().BeEquivalentTo([firstRuleA.Id, firstRuleB.Id]);
        reader.GetLayerRules(secondTypeId).Select(r => r.Id).Should().BeEquivalentTo([secondRule.Id]);
        reader.GetLayerRules(Guid.NewGuid()).Should().BeEmpty();
    }

    [Fact]
    public async Task AddLayerRule_WithExistingId_IsNoOp()
    {
        await using var sut = CreateSut();
        var assetTypeId = AddAssetType(sut);
        var layerId = AddLayer(sut);
        var original = NewRule(
            assetTypeId, layerId, priority: 1,
            new LayerRuleCondition { Attribute = "status", Value = "active" });
        var duplicate = new LayerRule
        {
            Id = original.Id,
            AssetTypeId = assetTypeId,
            LayerId = layerId,
            Priority = 99,
            Conditions = [new LayerRuleCondition { Attribute = "other", Value = "x" }],
        };
        sut.AddLayerRule(original);

        var addOnSameInstance = () => sut.AddLayerRule(duplicate);
        addOnSameInstance.Should().NotThrow();

        // A fresh instance has not tracked the row, so the existence check has to hit the database.
        await using var other = CreateSut();
        var addOnOtherInstance = () => other.AddLayerRule(duplicate);
        addOnOtherInstance.Should().NotThrow();

        other.GetLayerRules(assetTypeId).Should().ContainSingle().Which.Should().BeEquivalentTo(original);
        (await CountConditionRowsAsync(original.Id)).Should().Be(1);
    }

    [Fact]
    public async Task DeleteLayerRule_RemovesRuleAndItsConditionRows()
    {
        await using var sut = CreateSut();
        var assetTypeId = AddAssetType(sut);
        var layerId = AddLayer(sut);
        var rule = NewRule(
            assetTypeId, layerId, priority: 1,
            new LayerRuleCondition { Attribute = "status", Value = "active" },
            new LayerRuleCondition { Attribute = "height", Operator = LayerRuleOperator.GreaterThanOrEqual, Value = "10" });
        var survivor = NewRule(
            assetTypeId, layerId, priority: 2,
            new LayerRuleCondition { Attribute = "status", Value = "retired" });
        sut.AddLayerRule(rule);
        sut.AddLayerRule(survivor);
        (await CountConditionRowsAsync(rule.Id)).Should().Be(2);

        sut.DeleteLayerRule(rule.Id);

        await using var reader = CreateSut();
        reader.GetLayerRules(assetTypeId).Should().ContainSingle().Which.Id.Should().Be(survivor.Id);
        (await CountConditionRowsAsync(rule.Id)).Should().Be(0);
        (await CountConditionRowsAsync(survivor.Id)).Should().Be(1);
    }

    [Fact]
    public async Task DeleteLayerRule_UnknownId_IsNoOp()
    {
        await using var sut = CreateSut();
        var assetTypeId = AddAssetType(sut);
        var existing = NewRule(assetTypeId, AddLayer(sut));
        sut.AddLayerRule(existing);

        var delete = () => sut.DeleteLayerRule(Guid.NewGuid());

        delete.Should().NotThrow();
        sut.GetLayerRules(assetTypeId).Should().ContainSingle().Which.Id.Should().Be(existing.Id);
    }

    [Fact]
    public async Task GetLayerRules_AfterWritesOnSameInstance_ReflectsEachWrite()
    {
        await using var sut = CreateSut();
        var assetTypeId = AddAssetType(sut);
        var rule = NewRule(
            assetTypeId, AddLayer(sut), priority: 1,
            new LayerRuleCondition { Attribute = "status", Value = "active" });

        // Populates _layerRuleCache before any write.
        sut.GetLayerRules(assetTypeId).Should().BeEmpty();

        sut.AddLayerRule(rule);
        sut.GetLayerRules(assetTypeId).Should().ContainSingle().Which.Should().BeEquivalentTo(rule);

        sut.DeleteLayerRule(rule.Id);
        sut.GetLayerRules(assetTypeId).Should().BeEmpty();
    }
}
