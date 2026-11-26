using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.Shared.Services.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Rendering;

/// <summary>
/// XD01-429 — every decorator the map reads the active provider through (<see cref="ActiveAssetProvider"/>,
/// <see cref="ValidatingAssetProvider"/>, <see cref="ObservableAssetProvider"/>) forwards the
/// <see cref="ViewportQuery"/> reads with their zoom and token, so a superseded viewport's fetch is aborted at the
/// provider rather than stopped at the first decorator's default.
/// </summary>
public sealed class ViewportQueryForwardingTests
{
    private static readonly ViewportQuery Viewport = new(new BoundingBox(-40, -20, -38, -18), 13);

    public static TheoryData<string> Decorators => new() { "active", "validating", "observable" };

    private static IAssetProvider Decorate(string decorator, IAssetProvider inner)
    {
        switch (decorator)
        {
            case "active":
                var pool = new ProviderPool();
                var active = new ActiveAssetProvider(pool);
                pool.SetActive(pool.Add("A", inner).Id);
                return active;
            case "validating":
                return new ValidatingAssetProvider(inner);
            default:
                return new ObservableAssetProvider(inner, NullLogger<ObservableAssetProvider>.Instance, TimeProvider.System);
        }
    }

    [Theory]
    [MemberData(nameof(Decorators))]
    public async Task RawJsonChunks_ReachTheProviderWithTheViewportAndToken(string decorator)
    {
        var inner = new ViewportAwareLayerProvider(["[]"]);
        using var cts = new CancellationTokenSource();

        var chunks = new List<string>();
        await foreach (var chunk in Decorate(decorator, inner).GetInBoundsRawJsonChunksAsync(Viewport, cts.Token))
            chunks.Add(chunk);

        chunks.Should().Equal("[]");
        inner.ChunkViewport.Should().Be(Viewport);
        inner.ChunkToken.Should().Be(cts.Token);
    }

    [Theory]
    [MemberData(nameof(Decorators))]
    public async Task JsonElements_ReachTheProviderWithTheViewportAndToken(string decorator)
    {
        var inner = new ViewportAwareLayerProvider([]);
        using var cts = new CancellationTokenSource();

        await Decorate(decorator, inner).GetInBoundsJsonAsync(Viewport, cts.Token);

        inner.JsonViewport.Should().Be(Viewport);
        inner.JsonToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task OrganizationFiltering_IsNotBypassed_TheViewportReadsGoThroughItsFilteredBboxRead()
    {
        // OrganizationFilteredAssetProvider deliberately does not forward the viewport reads: IAssetProvider's
        // defaults route them to its own (filtered) bbox reads, never to the unfiltered inner provider's.
        var inner = new ViewportAwareLayerProvider(["[\"unfiltered\"]"]);
        IAssetProvider sut = new OrganizationFilteredAssetProvider(Guid.NewGuid(), inner);

        var chunks = new List<string>();
        await foreach (var chunk in sut.GetInBoundsRawJsonChunksAsync(Viewport))
            chunks.Add(chunk);

        chunks.Should().BeEmpty("the filtered decorator has no raw-JSON path, so the map falls back to its filtered JSON");
        inner.ChunkViewport.Should().BeNull();
    }
}
