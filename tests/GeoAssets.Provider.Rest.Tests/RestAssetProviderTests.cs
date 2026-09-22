using System.Net;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using Xunit;

namespace GeoAssets.Provider.Rest.Tests;

/// <summary>
/// Exercises <see cref="RestAssetProvider.SplitIntoTiles"/> and
/// <see cref="RestAssetProvider.GetInBoundsRawJsonChunksAsync"/> — the tiled/parallel bounds-fetch
/// added in XD01-172 to bring down the multi-second single-request fetch times XD01-159's audit
/// measured for large viewports.
/// </summary>
public class RestAssetProviderTests
{
    private static RestAssetProvider CreateSut(FakeHttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/geoassets/") };
        return new RestAssetProvider(client);
    }

    private static HttpResponseMessage JsonResponse(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    [Fact]
    public void RestAssetProvider_IsMarkedAsSyncProvider()
    {
        // Backed by a real timing number from XD01-159's live REST audit: "Repository.GetAll —
        // 44 features in 0 ms" — reads are served from the in-memory LocalFeatureCache populated
        // once at InitializeAsync time, no I/O on the sync read surface (XD01-161).
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("[]"));
        var sut = CreateSut(handler);

        sut.Should().BeAssignableTo<ISyncProvider>();
    }

    // ── SplitIntoTiles (pure) ────────────────────────────────────────────────

    [Fact]
    public void SplitIntoTiles_DefaultDivisions_Produces2x2GridCoveringTheOriginalBbox()
    {
        var tiles = RestAssetProvider.SplitIntoTiles(0, 0, 10, 20);

        tiles.Should().HaveCount(4);
        tiles.Should().Contain((0, 0, 5, 10));
        tiles.Should().Contain((5, 0, 10, 10));
        tiles.Should().Contain((0, 10, 5, 20));
        tiles.Should().Contain((5, 10, 10, 20));
    }

    [Fact]
    public void SplitIntoTiles_OneDivision_ReturnsTheOriginalBboxUnchanged()
    {
        var tiles = RestAssetProvider.SplitIntoTiles(1, 2, 3, 4, gridDivisions: 1);

        tiles.Should().ContainSingle().Which.Should().Be((1, 2, 3, 4));
    }

    [Fact]
    public void SplitIntoTiles_DegenerateBbox_DoesNotThrow()
    {
        var tiles = RestAssetProvider.SplitIntoTiles(5, 5, 5, 5);

        tiles.Should().HaveCount(4);
        tiles.Should().OnlyContain(t => t.MinLon == 5 && t.MaxLon == 5 && t.MinLat == 5 && t.MaxLat == 5);
    }

    // ── GetInBoundsRawJsonChunksAsync — threshold / request count ───────────

    [Fact]
    public async Task GetInBoundsRawJsonChunksAsync_SmallBbox_IssuesExactlyOneRequest()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("[]"));
        var sut = CreateSut(handler);

        var chunks = new List<string>();
        await foreach (var chunk in sut.GetInBoundsRawJsonChunksAsync(0, 0, 1, 1)) // area 1 <= threshold
            chunks.Add(chunk);

        handler.Requests.Should().ContainSingle();
        chunks.Should().ContainSingle().Which.Should().Be("[]");
    }

    [Fact]
    public async Task GetInBoundsRawJsonChunksAsync_LargeBbox_IssuesFourConcurrentRequests()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("[]"));
        var sut = CreateSut(handler);

        var chunks = new List<string>();
        await foreach (var chunk in sut.GetInBoundsRawJsonChunksAsync(0, 0, 10, 10)) // area 100 > threshold
            chunks.Add(chunk);

        handler.Requests.Should().HaveCount(4);
        chunks.Should().HaveCount(4);
        handler.Requests.Select(r => r.RequestUri!.Query).Distinct().Should().HaveCount(4);
    }

    // ── Completion-order streaming ───────────────────────────────────────────

    [Fact]
    public async Task GetInBoundsRawJsonChunksAsync_YieldsChunksInCompletionOrder_NotDispatchOrder()
    {
        var gate = new object();
        var tcsByDispatchIndex = new List<TaskCompletionSource<HttpResponseMessage>>();

        async Task<HttpResponseMessage> Respond(HttpRequestMessage _)
        {
            TaskCompletionSource<HttpResponseMessage> tcs;
            lock (gate)
            {
                tcs = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
                tcsByDispatchIndex.Add(tcs);
            }
            // A genuine await — unlike a blocking wait, this lets the other 3 tiles' requests
            // dispatch too instead of serializing dispatch itself behind this one.
            return await tcs.Task;
        }

        var handler = new FakeHttpMessageHandler(Respond);
        var sut = CreateSut(handler);

        var received = new List<string>();
        // Deterministic handshake instead of a fixed delay — a fixed wall-clock pause between
        // SetResult calls is a guess about how fast the reader's continuation chain propagates,
        // which is exactly the kind of margin that flakes on a slower/more contended CI runner
        // (confirmed: this test flaked in CI with this originally using Task.Delay(20)). Waiting
        // on a signal the reader itself releases after each chunk is unconditionally correct.
        var chunkReceived = new SemaphoreSlim(0);
        var consumeTask = Task.Run(async () =>
        {
            await foreach (var chunk in sut.GetInBoundsRawJsonChunksAsync(0, 0, 10, 10)) // 4 tiles
            {
                received.Add(chunk);
                chunkReceived.Release();
            }
        });

        // Wait for all 4 tile requests to be dispatched (each awaiting its own TCS).
        while (true)
        {
            lock (gate)
                if (tcsByDispatchIndex.Count == 4) break;
            await Task.Delay(10);
        }

        // Complete them in reverse dispatch order, waiting for the reader to actually observe
        // each chunk before completing the next TCS — proves chunks arrive in completion order,
        // not the order SplitIntoTiles generated them in.
        List<TaskCompletionSource<HttpResponseMessage>> ordered;
        lock (gate) ordered = [.. tcsByDispatchIndex];
        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            ordered[i].SetResult(JsonResponse($$"""[{"dispatchIndex":{{i}}}]"""));
            (await chunkReceived.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        }

        await consumeTask;

        received.Should().HaveCount(4);
        for (var i = 0; i < 4; i++)
            received[i].Should().Contain($"\"dispatchIndex\":{3 - i}");
    }

    // ── Throttle ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetInBoundsRawJsonChunksAsync_OverlappingLargeCalls_NeverExceedsFourConcurrentRequests()
    {
        var gate = new object();
        var concurrent = 0;
        var maxConcurrent = 0;

        async Task<HttpResponseMessage> Respond(HttpRequestMessage _)
        {
            lock (gate) { concurrent++; maxConcurrent = Math.Max(maxConcurrent, concurrent); }
            await Task.Delay(50); // hold the "in-flight" window open long enough for others to queue up
            lock (gate) concurrent--;
            return JsonResponse("[]");
        }

        var handler = new FakeHttpMessageHandler(Respond);
        var sut = CreateSut(handler);

        // Two overlapping large-bbox calls = 4 tiles each = 8 tile requests total, throttle = 4.
        async Task Consume(double minLon, double minLat, double maxLon, double maxLat)
        {
            await foreach (var _ in sut.GetInBoundsRawJsonChunksAsync(minLon, minLat, maxLon, maxLat)) { }
        }

        await Task.WhenAll(Consume(0, 0, 10, 10), Consume(20, 20, 30, 30));

        handler.Requests.Should().HaveCount(8);
        maxConcurrent.Should().BeGreaterThan(1).And.BeLessOrEqualTo(4);
    }
}
