using System.Globalization;
using System.Net;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Rendering;
using Xunit;

namespace GeoAssets.Provider.Rest.Tests;

/// <summary>
/// XD01-429 — <see cref="RestAssetProvider"/>'s <c>features/bounds</c> reads: coordinates are always written
/// invariantly (a <c>pt-BR</c> client used to send <c>-38,5</c>), a viewport's zoom travels with it, and cancelling a
/// superseded viewport aborts its HTTP request instead of letting it finish.
/// </summary>
public sealed class RestAssetProviderViewportTests
{
    private const string ExpectedQuery = "?minLon=-38.5&minLat=-13.25&maxLon=-38.4&maxLat=-12.8";
    private static readonly BoundingBox Bounds = new(-38.5, -13.25, -38.4, -12.8);

    private static RestAssetProvider CreateSut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/geoassets/") });

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    /// <summary>Runs <paramref name="act"/> with <c>pt-BR</c> — decimal comma — as the thread's culture.</summary>
    private static async Task InPortugueseAsync(Func<Task> act)
    {
        var (culture, uiCulture) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");
        try
        {
            (-38.5).ToString(CultureInfo.CurrentCulture).Should().Be("-38,5", "the test must really run under a decimal comma");
            await act();
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (culture, uiCulture);
        }
    }

    private static async Task<List<string>> DrainAsync(IAsyncEnumerable<string> chunks)
    {
        var drained = new List<string>();
        await foreach (var chunk in chunks) drained.Add(chunk);
        return drained;
    }

    [Fact]
    public async Task EveryBoundsRead_WritesTheBoundsInvariantly_UnderADecimalCommaCulture()
    {
        var handler = new FakeHttpMessageHandler(_ => Json("[]"));
        var sut = CreateSut(handler);

        await InPortugueseAsync(async () =>
        {
            await sut.GetInBoundsAsync(Bounds.MinLon, Bounds.MinLat, Bounds.MaxLon, Bounds.MaxLat);
            await sut.GetInBoundsRawJsonAsync(Bounds.MinLon, Bounds.MinLat, Bounds.MaxLon, Bounds.MaxLat);
            await sut.GetInBoundsJsonAsync(Bounds.MinLon, Bounds.MinLat, Bounds.MaxLon, Bounds.MaxLat);
            await DrainAsync(sut.GetInBoundsRawJsonChunksAsync(Bounds.MinLon, Bounds.MinLat, Bounds.MaxLon, Bounds.MaxLat));
            await sut.GetInBoundsJsonAsync(new ViewportQuery(Bounds, 12));
            await DrainAsync(sut.GetInBoundsRawJsonChunksAsync(new ViewportQuery(Bounds, 12)));
        });

        handler.Requests.Select(r => r.RequestUri!.Query).Should().Equal(
            ExpectedQuery, ExpectedQuery, ExpectedQuery, ExpectedQuery, ExpectedQuery + "&zoom=12", ExpectedQuery + "&zoom=12");
        handler.Requests.Should().OnlyContain(r => r.RequestUri!.AbsolutePath == "/api/geoassets/features/bounds");
    }

    [Fact]
    public async Task ViewportReads_WithoutAZoom_SendNone()
    {
        var handler = new FakeHttpMessageHandler(_ => Json("[]"));
        var sut = CreateSut(handler);

        await sut.GetInBoundsJsonAsync(new ViewportQuery(Bounds));
        await DrainAsync(sut.GetInBoundsRawJsonChunksAsync(new ViewportQuery(Bounds)));

        handler.Requests.Select(r => r.RequestUri!.Query).Should().Equal(ExpectedQuery, ExpectedQuery);
    }

    [Fact]
    public async Task ViewportChunks_EveryTileCarriesTheZoom()
    {
        var handler = new FakeHttpMessageHandler(_ => Json("[]"));
        var sut = CreateSut(handler);

        var chunks = await DrainAsync(sut.GetInBoundsRawJsonChunksAsync(new ViewportQuery(new BoundingBox(-50, -30, -30, -10), 7)));

        chunks.Should().HaveCount(4, "a 400 deg² view is fetched as four tiles");
        handler.Requests.Should().HaveCount(4).And.OnlyContain(r => r.RequestUri!.Query.EndsWith("&zoom=7", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ViewportChunks_Cancelled_AbortTheRequestInFlight()
    {
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var aborted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            sent.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                aborted.TrySetResult();
                throw;
            }
            return Json("[]");
        });
        var sut = CreateSut(handler);
        using var cts = new CancellationTokenSource();

        var drain = DrainAsync(sut.GetInBoundsRawJsonChunksAsync(new ViewportQuery(Bounds, 12), cts.Token));
        await sent.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await cts.CancelAsync();

        await drain.Invoking(d => d).Should().ThrowAsync<OperationCanceledException>();
        await aborted.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ViewportJson_Cancelled_AbortsTheRequestInFlight()
    {
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            sent.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Json("[]");
        });
        var sut = CreateSut(handler);
        using var cts = new CancellationTokenSource();

        var read = sut.GetInBoundsJsonAsync(new ViewportQuery(Bounds, 12), cts.Token);
        await sent.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await cts.CancelAsync();

        await read.Invoking(r => r).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ViewportChunks_CancelledWhileWaitingForTheThrottle_NeverSendTheirRequests()
    {
        // Four tile requests hold the throttle; a superseded viewport's tiles queued behind them are never sent.
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fourSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sentCount = 0;
        var handler = new FakeHttpMessageHandler(async (_, _) =>
        {
            if (Interlocked.Increment(ref sentCount) == 4) fourSent.TrySetResult();
            await release.Task;
            return Json("[]");
        });
        var sut = CreateSut(handler);
        var large = new ViewportQuery(new BoundingBox(-50, -30, -30, -10), 7);
        using var cts = new CancellationTokenSource();

        var holding = DrainAsync(sut.GetInBoundsRawJsonChunksAsync(large));
        await fourSent.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var superseded = DrainAsync(sut.GetInBoundsRawJsonChunksAsync(large, cts.Token));
        await cts.CancelAsync();

        await superseded.Invoking(s => s).Should().ThrowAsync<OperationCanceledException>();
        release.SetResult();
        await holding;
        handler.Requests.Should().HaveCount(4);
    }

    [Fact]
    public async Task ThroughTheInterface_TheViewportOverloadsAreTheProvidersOwn()
    {
        var handler = new FakeHttpMessageHandler(_ => Json("[]"));
        IAssetProvider sut = CreateSut(handler);

        await sut.GetInBoundsJsonAsync(new ViewportQuery(Bounds, 9));
        await DrainAsync(sut.GetInBoundsRawJsonChunksAsync(new ViewportQuery(Bounds, 9)));

        handler.Requests.Should().HaveCount(2).And.OnlyContain(r => r.RequestUri!.Query.EndsWith("&zoom=9", StringComparison.Ordinal));
    }
}
