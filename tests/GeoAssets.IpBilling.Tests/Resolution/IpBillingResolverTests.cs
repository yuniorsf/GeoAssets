using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.IpBilling.AssetTypes;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;
using GeoAssets.IpBilling.Resolution;
using Xunit;

namespace GeoAssets.IpBilling.Tests.Resolution;

public class IpBillingResolverTests
{
    private sealed class FakeTransformerIndexRepository(params IpTransformerIndex[] entries) : IIpTransformerIndexRepository
    {
        public Task<IpTransformerIndex?> GetAsync(string municipioIbgeCode, string codigoInstalacao, CancellationToken ct = default)
            => Task.FromResult(entries.FirstOrDefault(e =>
                string.Equals(e.MunicipioIbgeCode, municipioIbgeCode, StringComparison.Ordinal) &&
                string.Equals(e.CodigoInstalacao, codigoInstalacao, StringComparison.Ordinal)));

        public Task<IReadOnlyList<IpTransformerIndex>> GetByMunicipioAsync(string municipioIbgeCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(IpTransformerIndex entry, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(IpTransformerIndex entry, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeGeoCoreClient : IGeoCoreClient
    {
        private readonly Dictionary<(string FeatureId, string? EdgeKind, string? TargetAssetTypeId), IReadOnlyList<string>> _neighbors = [];
        public int BatchCallCount { get; private set; }

        public void SetNeighbors(string featureId, string edgeKind, string targetAssetTypeId, params string[] neighborIds)
            => _neighbors[(featureId, edgeKind, targetAssetTypeId)] = neighborIds;

        public Task<GeoFeature?> GetFeatureByIdAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<GeoFeature>> QueryFeaturesAsync(AssetQuery query, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetNeighborIdsAsync(
            string featureId, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default)
            => Task.FromResult(_neighbors.GetValueOrDefault((featureId, edgeKind, targetAssetTypeId)) ?? []);

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetNeighborIdsBatchAsync(
            IReadOnlyList<string> featureIds, string? edgeKind = null, string? targetAssetTypeId = null, CancellationToken ct = default)
        {
            BatchCallCount++;
            var dict = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var id in featureIds)
                dict[id] = _neighbors.GetValueOrDefault((id, edgeKind, targetAssetTypeId)) ?? [];
            return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(dict);
        }
    }

    [Fact]
    public async Task DbTransformerResolver_MatchingEntry_ReturnsTransformerFeatureId()
    {
        var expectedId = Guid.NewGuid();
        var repo = new FakeTransformerIndexRepository(new IpTransformerIndex
        {
            Id                   = Guid.NewGuid(),
            MunicipioIbgeCode    = "3550308",
            CodigoInstalacao     = "INST-001",
            TransformerFeatureId = expectedId
        });
        var sut = new DbTransformerResolver(repo);

        var resolved = await sut.ResolveTransformerFeatureIdAsync("3550308", "INST-001");

        resolved.Should().Be(expectedId);
    }

    [Fact]
    public async Task DbTransformerResolver_UnknownEntry_ReturnsNull()
    {
        var sut = new DbTransformerResolver(new FakeTransformerIndexRepository());

        var resolved = await sut.ResolveTransformerFeatureIdAsync("3550308", "UNKNOWN");

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task HttpPoleResolver_SingleTransformer_QueriesFeedsEdgeAndPosteAssetType()
    {
        var txId = Guid.NewGuid();
        var pole1 = Guid.NewGuid();
        var pole2 = Guid.NewGuid();
        var coreClient = new FakeGeoCoreClient();
        coreClient.SetNeighbors(
            txId.ToString(),
            HttpPoleResolver.FeedsEdgeKind,
            IpBillingAssetTypes.Poste.Id.ToString(),
            pole1.ToString(),
            pole2.ToString(),
            "not-a-guid");

        var sut = new HttpPoleResolver(coreClient);

        var poles = await sut.ResolvePoleFeatureIdsAsync(txId);

        poles.Should().Equal(pole1, pole2);
    }

    [Fact]
    public async Task HttpPoleResolver_BatchTransformers_ResolvesAllInSingleRoundTrip()
    {
        var tx1 = Guid.NewGuid();
        var tx2 = Guid.NewGuid();
        var pole1 = Guid.NewGuid();
        var pole2 = Guid.NewGuid();
        var coreClient = new FakeGeoCoreClient();
        coreClient.SetNeighbors(
            tx1.ToString(),
            HttpPoleResolver.FeedsEdgeKind,
            IpBillingAssetTypes.Poste.Id.ToString(),
            pole1.ToString());
        coreClient.SetNeighbors(
            tx2.ToString(),
            HttpPoleResolver.FeedsEdgeKind,
            IpBillingAssetTypes.Poste.Id.ToString(),
            pole2.ToString());

        var sut = new HttpPoleResolver(coreClient);

        var result = await sut.ResolvePoleFeatureIdsBatchAsync([tx1, tx2]);

        coreClient.BatchCallCount.Should().Be(1);
        result[tx1].Should().Equal(pole1);
        result[tx2].Should().Equal(pole2);
    }
}

