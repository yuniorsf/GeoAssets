using System.Collections.Concurrent;
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
/// XD01-474: <see cref="PostgresAssetProvider"/> is shared by every request (the shared catalog, an organization's
/// catalog), so many requests read its asset types, layers and layer rules at once — every WMS tile draws through
/// <c>RenderStyle.Of</c>. On a cold cache those reads used to all query the provider's one long-lived context at the same
/// time, which threw and left the context corrupted for every later request. They must all succeed, agree, and leave the
/// provider working.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresAssetProviderCatalogCacheConcurrencyTests(PostgresContainerFixture fixture) : IAsyncLifetime
{
    private const int Readers = 48;
    private const int Rounds = 4;

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

    private (Guid AssetTypeId, Guid LayerId, Guid RuleId) Seed()
    {
        var seeder = CreateSut();
        var assetType = new AssetType { Name = "XD01-474 concurrent type" };
        var layer = new Layer { Name = "XD01-474 concurrent layer", GeometryType = GeometryType.Point };
        var rule = new LayerRule { AssetTypeId = assetType.Id, LayerId = layer.Id, Priority = 1 };
        _assetTypeIds.Add(assetType.Id);
        _layerIds.Add(layer.Id);
        _layerRuleIds.Add(rule.Id);
        seeder.AddAssetType(assetType);
        seeder.AddLayer(layer);
        seeder.AddLayerRule(rule);
        return (assetType.Id, layer.Id, rule.Id);
    }

    /// <summary>Runs <paramref name="read"/> on <see cref="Readers"/> dedicated threads released together; returns their failures.</summary>
    private static ConcurrentBag<Exception> RunTogether(Action read)
    {
        var failures = new ConcurrentBag<Exception>();
        using var gate = new ManualResetEventSlim(false);
        var threads = Enumerable.Range(0, Readers).Select(_ => new Thread(() =>
        {
            gate.Wait();
            try { read(); }
            catch (Exception ex) { failures.Add(ex); }
        })).ToList();

        threads.ForEach(t => t.Start());
        gate.Set();
        threads.ForEach(t => t.Join());
        return failures;
    }

    [Fact]
    public void ColdCaches_ConcurrentReaders_AllSucceedAndAgree_AndTheProviderKeepsWorking()
    {
        var (assetTypeId, layerId, ruleId) = Seed();

        for (var round = 0; round < Rounds; round++)
        {
            var sut = CreateSut();
            var seen = new ConcurrentBag<(bool Type, bool Layer, bool Rule)>();

            var failures = RunTogether(() => seen.Add((
                sut.GetAssetTypes().Any(t => t.Id == assetTypeId),
                sut.GetLayers().Any(l => l.Id == layerId),
                sut.GetLayerRules(assetTypeId).Any(r => r.Id == ruleId))));

            failures.Should().BeEmpty("round {0}: concurrent cold-cache reads must not share one DbContext", round);
            seen.Should().HaveCount(Readers).And.OnlyContain(s => s.Type && s.Layer && s.Rule);

            // The provider still works afterwards: a write goes through its long-lived context and the next read sees it.
            var added = new Layer { Name = "XD01-474 after round " + round, GeometryType = GeometryType.Point };
            _layerIds.Add(added.Id);
            sut.AddLayer(added);
            sut.GetLayers().Should().Contain(l => l.Id == added.Id);
        }
    }

    [Fact]
    public void AWriteDuringConcurrentReads_IsSeenByTheNextRead()
    {
        var (assetTypeId, _, _) = Seed();
        var sut = CreateSut();
        var added = new LayerRule { AssetTypeId = assetTypeId, LayerId = _layerIds[0], Priority = 2 };
        _layerRuleIds.Add(added.Id);

        var failures = RunTogether(() =>
        {
            for (var i = 0; i < 20; i++) _ = sut.GetLayerRules(assetTypeId);
        });
        sut.AddLayerRule(added);

        failures.Should().BeEmpty();
        sut.GetLayerRules(assetTypeId).Should().Contain(r => r.Id == added.Id);
    }
}
