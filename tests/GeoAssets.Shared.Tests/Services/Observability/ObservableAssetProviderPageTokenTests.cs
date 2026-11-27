using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Shared.Services.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Observability;

/// <summary>XD01-478: <see cref="ObservableAssetProvider"/>'s cancellable page read hands the caller's token to the inner's.</summary>
public class ObservableAssetProviderPageTokenTests
{
    [Fact]
    public async Task GetPageAsync_WithAToken_ForwardsItToTheInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Properties = { AssetTypeId = AssetType.Point.Id.ToString() } });
        var sut = new ObservableAssetProvider(inner, NullLogger<ObservableAssetProvider>.Instance, TimeProvider.System);
        using var cts = new CancellationTokenSource();

        (await sut.GetPageAsync(new AssetQuery(), cts.Token)).Items.Should().ContainSingle();

        await cts.CancelAsync();
        var act = () => sut.GetPageAsync(new AssetQuery(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>("the inner's read is handed the caller's token");
    }
}
