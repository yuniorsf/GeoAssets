using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
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

    // ── Layers / layer rules (XD01-153) ──────────────────────────────────────

    private static HttpResponseMessage JsonBody<T>(T body) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(body, options: GeoJsonSerializer.Options) };

    [Fact]
    public async Task InitializeAsync_RequestsLayersAndLayerRules_AndPopulatesTheCache()
    {
        var layer = new Layer { Name = "Poles", Color = "#ff0000" };
        var rule = new LayerRule
        {
            AssetTypeId = AssetType.Point.Id,
            LayerId = layer.Id,
            Priority = 1,
            Conditions = [new LayerRuleCondition { Attribute = "status", Operator = LayerRuleOperator.NotEquals, Value = "retired" }]
        };
        var otherTypeRule = new LayerRule { AssetTypeId = AssetType.Line.Id, LayerId = layer.Id };
        var handler = new FakeHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/geoassets/layers" => JsonBody(new[] { layer }),
            "/api/geoassets/layer-rules" => JsonBody(new[] { rule, otherTypeRule }),
            _ => JsonResponse("[]")
        });
        var factory = new RestProviderFactory(new FakeHttpClientFactory(handler));

        // InitializeAsync is internal — CreateAsync is its only caller and the public way in.
        var sut = await factory.CreateAsync("http://localhost/api/geoassets");

        handler.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Get);
        handler.Requests.Select(r => r.RequestUri!.PathAndQuery)
            .Should().Contain(["/api/geoassets/layers", "/api/geoassets/layer-rules"]);
        sut.GetLayers().Should().ContainSingle().Which.Should().BeEquivalentTo(layer);
        sut.GetLayerRules(AssetType.Point.Id).Should().ContainSingle().Which.Should().BeEquivalentTo(rule);
        sut.GetLayerRules(AssetType.Line.Id).Should().ContainSingle().Which.Id.Should().Be(otherTypeRule.Id);
    }

    [Fact]
    public async Task AddLayer_UpdatesTheCache_AndPostsToLayers()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        var sut = CreateSut(handler);
        var layer = new Layer { Name = "Cables" };

        sut.AddLayer(layer);

        sut.GetLayers().Should().ContainSingle().Which.Should().BeSameAs(layer);
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.Should().Be("/api/geoassets/layers");
        var sent = await request.Content!.ReadFromJsonAsync<Layer>(GeoJsonSerializer.Options);
        sent.Should().BeEquivalentTo(layer);
    }

    [Fact]
    public void DeleteLayer_UpdatesTheCache_AndIssuesDeleteForThatId()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = CreateSut(handler);
        var layer = new Layer { Name = "Cables" };
        sut.AddLayer(layer);

        sut.DeleteLayer(layer.Id);

        sut.GetLayers().Should().BeEmpty();
        handler.Requests.Should().HaveCount(2);
        var request = handler.Requests[^1];
        request.Method.Should().Be(HttpMethod.Delete);
        request.RequestUri!.AbsolutePath.Should().Be($"/api/geoassets/layers/{layer.Id}");
    }

    [Fact]
    public async Task AddLayerRule_UpdatesTheCache_AndPostsToLayerRules_WithConditions()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        var sut = CreateSut(handler);
        var rule = new LayerRule
        {
            AssetTypeId = AssetType.Point.Id,
            LayerId = Guid.NewGuid(),
            Conditions = [new LayerRuleCondition { Attribute = "voltageKv", Operator = LayerRuleOperator.GreaterThanOrEqual, Value = "13.8" }]
        };

        sut.AddLayerRule(rule);

        sut.GetLayerRules(AssetType.Point.Id).Should().ContainSingle().Which.Should().BeSameAs(rule);
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.Should().Be("/api/geoassets/layer-rules");
        var sent = await request.Content!.ReadFromJsonAsync<LayerRule>(GeoJsonSerializer.Options);
        sent.Should().BeEquivalentTo(rule);
    }

    [Fact]
    public void DeleteLayerRule_UpdatesTheCache_AndIssuesDeleteForThatId()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = CreateSut(handler);
        var rule = new LayerRule { AssetTypeId = AssetType.Point.Id, LayerId = Guid.NewGuid() };
        sut.AddLayerRule(rule);

        sut.DeleteLayerRule(rule.Id);

        sut.GetLayerRules(AssetType.Point.Id).Should().BeEmpty();
        handler.Requests.Should().HaveCount(2);
        var request = handler.Requests[^1];
        request.Method.Should().Be(HttpMethod.Delete);
        request.RequestUri!.AbsolutePath.Should().Be($"/api/geoassets/layer-rules/{rule.Id}");
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
        handler.Requests.Select(r => r.RequestUri!.Query).Distinct(StringComparer.Ordinal).Should().HaveCount(4);
    }

    // ── Completion-order streaming ───────────────────────────────────────────

    [Fact]
    public async Task GetInBoundsRawJsonChunksAsync_YieldsChunksInCompletionOrder_NotDispatchOrder()
    {
        var gate = new Lock();
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
        var gate = new Lock();
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

    // ── Asset type zoom range (XD01-311) ─────────────────────────────────────

    [Theory]
    [InlineData(13, 15, true, true)]
    [InlineData(15, null, true, false)]
    [InlineData(null, 13, false, true)]
    [InlineData(null, null, false, false)]
    public async Task AddAssetType_PostsTheZoomRange_AndKeepsItInTheLocalCache(int? min, int? max, bool sendsMin, bool sendsMax)
    {
        var posted = new TaskCompletionSource<(string Path, string Body)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new FakeHttpMessageHandler(async request =>
        {
            posted.TrySetResult((request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync()));
            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        var sut = CreateSut(handler);
        var assetType = new AssetType { Name = "Pole", MinZoom = min, MaxZoom = max };

        sut.AddAssetType(assetType);
        var (path, body) = await posted.Task.WaitAsync(TimeSpan.FromSeconds(30));

        path.Should().EndWith("/asset-types");
        using var json = JsonDocument.Parse(body);
        json.RootElement.TryGetProperty("minZoom", out var sentMin).Should().Be(sendsMin);
        json.RootElement.TryGetProperty("maxZoom", out var sentMax).Should().Be(sendsMax);
        if (sendsMin) sentMin.GetInt32().Should().Be(min);
        if (sendsMax) sentMax.GetInt32().Should().Be(max);

        // What the server answers GET /asset-types with is read back with the same options.
        var reloaded = JsonSerializer.Deserialize<AssetType>(body, GeoAssets.Core.Services.GeoJsonSerializer.Options)!;
        (reloaded.MinZoom, reloaded.MaxZoom).Should().Be((min, max));

        var cached = sut.GetAssetTypes().Single(t => t.Id == assetType.Id);
        (cached.MinZoom, cached.MaxZoom).Should().Be((min, max));
    }

    // ── Asset type roles (XD01-367) ──────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_RolesFromGetAssetTypes_LandOnTheCachedAssetTypes()
    {
        var withRoles = new AssetType { Name = "Support", Roles = ["occupiable-support", "isp:pole"] };
        var handler = new FakeHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/geoassets/asset-types" => JsonBody(new[] { withRoles }),
            _ => JsonResponse("[]")
        });
        var factory = new RestProviderFactory(new FakeHttpClientFactory(handler));

        var sut = await factory.CreateAsync("http://localhost/api/geoassets");

        sut.GetAssetTypes().Single(t => t.Id == withRoles.Id).Roles.Should().Equal("occupiable-support", "isp:pole");
        ((IAssetProvider)sut).GetAssetTypesWithRole("isp:pole").Select(t => t.Id).Should().Equal(withRoles.Id);
    }

    [Fact]
    public async Task InitializeAsync_ABuiltInServerTypeWithRoles_IsCached_AndFoundByRole()
    {
        var pole = new AssetType
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000004"),
            Name = "Poste",
            IsBuiltIn = true,
            Roles = ["occupiable-support"],
        };
        var handler = new FakeHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/geoassets/asset-types" => JsonBody(new[] { pole }),
            _ => JsonResponse("[]")
        });
        var factory = new RestProviderFactory(new FakeHttpClientFactory(handler));

        var sut = await factory.CreateAsync("http://localhost/api/geoassets");

        sut.GetAssetTypes().Should().ContainSingle(t => t.Id == pole.Id);
        ((IAssetProvider)sut).GetAssetTypesWithRole("occupiable-support").Select(t => t.Id).Should().Equal(pole.Id);
    }

    [Fact]
    public async Task InitializeAsync_TheServersGenericDefaults_AreNotDuplicatedInTheCache()
    {
        var handler = new FakeHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/geoassets/asset-types" => JsonBody(AssetType.Defaults.ToArray()),
            _ => JsonResponse("[]")
        });
        var factory = new RestProviderFactory(new FakeHttpClientFactory(handler));

        var sut = await factory.CreateAsync("http://localhost/api/geoassets");

        var ids = sut.GetAssetTypes().Select(t => t.Id).ToList();
        ids.Should().OnlyHaveUniqueItems();
        ids.Should().Contain([AssetType.Point.Id, AssetType.Line.Id, AssetType.Area.Id]);
    }

    [Theory]
    [InlineData("""[{"id":"11111111-1111-1111-1111-111111111111","name":"Legacy","roles":null}]""")]
    [InlineData("""[{"id":"11111111-1111-1111-1111-111111111111","name":"Legacy"}]""")]
    public async Task InitializeAsync_NullOrMissingRoles_ReadAsEmpty(string assetTypesJson)
    {
        var handler = new FakeHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/geoassets/asset-types" => JsonResponse(assetTypesJson),
            _ => JsonResponse("[]")
        });
        var factory = new RestProviderFactory(new FakeHttpClientFactory(handler));

        var sut = await factory.CreateAsync("http://localhost/api/geoassets");

        var legacy = sut.GetAssetTypes().Single(t => string.Equals(t.Name, "Legacy", StringComparison.Ordinal));
        legacy.Roles.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task AddAssetType_PostsTheRoles()
    {
        var posted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new FakeHttpMessageHandler(async request =>
        {
            posted.TrySetResult(await request.Content!.ReadAsStringAsync());
            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        var sut = CreateSut(handler);
        var assetType = new AssetType { Name = "Support", Roles = ["occupiable-support", "isp:pole"] };

        sut.AddAssetType(assetType);
        var body = await posted.Task.WaitAsync(TimeSpan.FromSeconds(30));

        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("roles").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("occupiable-support", "isp:pole");
        sut.GetAssetTypes().Single(t => t.Id == assetType.Id).Roles.Should().Equal("occupiable-support", "isp:pole");
    }
}
