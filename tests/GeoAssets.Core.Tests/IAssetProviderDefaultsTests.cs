using FluentAssertions;
using GeoAssets.Core.Interfaces;
using Xunit;

namespace GeoAssets.Core.Tests;

/// <summary>
/// Exercises <see cref="IAssetProvider"/>'s own default-implemented members —
/// <see cref="IAssetProvider.GetInBoundsRawJsonChunksAsync"/> here — via
/// <see cref="TestAssetProvider"/>, which deliberately does not override it (XD01-172).
/// Callers must be typed as <see cref="IAssetProvider"/>, not the concrete class: default
/// interface members are only reachable through the interface type.
/// </summary>
public class IAssetProviderDefaultsTests
{
    [Fact]
    public async Task GetInBoundsRawJsonChunksAsync_ProviderDoesNotSupportRawJson_YieldsNoChunks()
    {
        IAssetProvider sut = new TestAssetProvider(); // RawJsonResult left null — "unsupported", same as Postgres today

        var chunks = new List<string>();
        await foreach (var chunk in sut.GetInBoundsRawJsonChunksAsync(0, 0, 1, 1))
            chunks.Add(chunk);

        chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task GetInBoundsRawJsonChunksAsync_ProviderSupportsRawJson_YieldsExactlyOneChunk()
    {
        IAssetProvider sut = new TestAssetProvider { RawJsonResult = """[{"id":"a"}]""" };

        var chunks = new List<string>();
        await foreach (var chunk in sut.GetInBoundsRawJsonChunksAsync(0, 0, 1, 1))
            chunks.Add(chunk);

        chunks.Should().ContainSingle().Which.Should().Be("""[{"id":"a"}]""");
    }

    [Fact]
    public async Task GetInBoundsRawJsonChunksAsync_EmptyButSupportedResult_StillYieldsOneChunk()
    {
        // A "[]" result is non-null — proves the default distinguishes "unsupported" (null) from
        // "supported but this bbox happens to be empty" (a real, if trivial, chunk).
        IAssetProvider sut = new TestAssetProvider { RawJsonResult = "[]" };

        var chunks = new List<string>();
        await foreach (var chunk in sut.GetInBoundsRawJsonChunksAsync(0, 0, 1, 1))
            chunks.Add(chunk);

        chunks.Should().ContainSingle().Which.Should().Be("[]");
    }
}
