using System.Collections.Concurrent;
using System.Net;
using System.Text;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.WFS;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Web.Tests.Extensions;

/// <summary>
/// XD01-478: a WFS entry's viewport reads hand the map pass's token to the GetFeature request, so a superseded viewport
/// (whose pass the map cancels when a newer one starts, XD01-429) aborts its request instead of leaving it running on the
/// server — and a caller cancels only its own request, never another caller's.
/// </summary>
public sealed class WfsProviderViewportCancellationTests
{
    private const string FeatureCollection =
        """{"type":"FeatureCollection","features":[{"type":"Feature","id":"f1","geometry":{"type":"Point","coordinates":[-38.5,-12.5]},"properties":{"name":"f1","assetTypeId":"pole"}}]}""";

    private static readonly ViewportQuery Viewport = new(new BoundingBox(-39, -13, -38, -12), 14);

    /// <summary>Holds every GetFeature until <see cref="Release"/> or its own cancellation, recording which were cancelled.</summary>
    private sealed class HoldingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<Uri> Started { get; } = new();

        public ConcurrentQueue<Uri> Cancelled { get; } = new();

        public SemaphoreSlim Arrived { get; } = new(0);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Started.Enqueue(uri);
            Arrived.Release();
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.Enqueue(uri);
                throw;
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(FeatureCollection, Encoding.UTF8, "application/json") };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Arrived.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static IAssetProvider Provider(HoldingHandler handler) =>
        new WfsProviderFactory(new SingleClientFactory(handler), NullLogger<WfsAssetProvider>.Instance).Create("http://wfs.test");

    private static async Task<List<string>> ChunksAsync(IAssetProvider provider, CancellationToken ct)
    {
        var chunks = new List<string>();
        await foreach (var chunk in provider.GetInBoundsRawJsonChunksAsync(Viewport, ct))
            chunks.Add(chunk);
        return chunks;
    }

    [Fact]
    public async Task ASupersededViewportsRawJsonRead_AbortsItsGetFeatureRequest()
    {
        using var handler = new HoldingHandler();
        var provider = Provider(handler);
        using var superseded = new CancellationTokenSource();

        var read = ChunksAsync(provider, superseded.Token);
        (await handler.Arrived.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue();
        await superseded.CancelAsync();

        var act = () => read.WaitAsync(TimeSpan.FromSeconds(30));
        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Cancelled.Should().ContainSingle().Which.Query.Should().Contain("REQUEST=GetFeature").And.Contain("BBOX=");
    }

    [Fact]
    public async Task ASupersededViewportsJsonRead_AbortsItsGetFeatureRequest()
    {
        using var handler = new HoldingHandler();
        var provider = Provider(handler);
        using var superseded = new CancellationTokenSource();

        var read = provider.GetInBoundsJsonAsync(Viewport, superseded.Token);
        (await handler.Arrived.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue();
        await superseded.CancelAsync();

        var act = () => read.WaitAsync(TimeSpan.FromSeconds(30));
        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Cancelled.Should().ContainSingle();
    }

    [Fact]
    public async Task CancellingOneCaller_LeavesAnotherCallersRequestRunning_ToItsAnswer()
    {
        using var handler = new HoldingHandler();
        var provider = Provider(handler);
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();

        var cancelled = ChunksAsync(provider, first.Token);
        (await handler.Arrived.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue();
        var kept = provider.GetInBoundsJsonAsync(Viewport, second.Token);
        (await handler.Arrived.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue();

        await first.CancelAsync();
        var act = () => cancelled.WaitAsync(TimeSpan.FromSeconds(30));
        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Release.SetResult();

        var features = await kept.WaitAsync(TimeSpan.FromSeconds(30));
        features.Should().ContainSingle().Which.GetProperty("id").GetString().Should().Be("f1");
        handler.Cancelled.Should().ContainSingle();
    }

    [Fact]
    public async Task AnUncancelledViewportRead_YieldsTheFeaturesAsOneRawJsonChunk()
    {
        using var handler = new HoldingHandler();
        handler.Release.SetResult();

        var chunks = await ChunksAsync(Provider(handler), CancellationToken.None);

        chunks.Should().ContainSingle().Which.Should().Contain("\"f1\"");
        handler.Started.Should().ContainSingle().Which.Query.Should().Contain("COUNT=10000");
    }
}
