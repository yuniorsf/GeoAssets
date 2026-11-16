using System.Net;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests;

/// <summary>
/// The requests <see cref="RestServiceOrderTargetRepository"/> sends and how it reads the
/// answers (XD01-308). Behaviour against the real endpoints, and the shared store contract, are
/// covered in <c>GeoAssets.Server.Tests</c>; these pin the wire.
/// </summary>
public sealed class RestServiceOrderTargetRepositoryTests
{
    private static readonly Guid Org = new("d0d0d0d0-0000-4000-8000-000000000308");

    private const string EmptyPage = """{ "items": [], "page": 1, "pageSize": 50, "totalCount": 0 }""";

    private static RestServiceOrderTargetRepository Sut(BodyRecordingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/api/workflow/") });

    private static ServiceOrderTarget Target(string id = "tgt-1", string orderId = "ord-1") => new()
    {
        Id = id,
        OrderId = orderId,
        FeatureId = "feat-1",
        AssetTypeId = "pole",
        Alias = "P-1",
        ActivityIds = ["inspect"],
        Status = OrderTargetStatus.InProgress,
        ConflictLevel = OrderTargetConflictLevel.Warning,
        Note = null,
        Longitude = -38.5,
        Latitude = -12.9,
        AddedAt = new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc),
        AddedBy = "user-1",
        OrganizationId = Org,
        Version = 3,
    };

    // ── GetPageAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPageAsync_SendsEveryQueryMember_Escaped_AndMapsTheEnvelopeToAPagedResult()
    {
        var handler = new BodyRecordingHandler(_ => BodyRecordingHandler.Json(HttpStatusCode.OK, """
            { "items": [ { "id": "tgt-1", "orderId": "ord/1 #a", "featureId": "feat-1", "assetTypeId": "pole", "activityIds": ["inspect"],
                           "status": "Pending", "conflictLevel": 2, "addedAt": "2026-11-16T09:00:00Z", "addedBy": "user-1",
                           "organizationId": "d0d0d0d0-0000-4000-8000-000000000308", "version": 4 } ],
              "page": 2, "pageSize": 25, "totalCount": 26 }
            """));
        var query = new OrderTargetQuery("ord/1 #a", page: 2, pageSize: 25, sortBy: "addedAt", descending: true,
            status: OrderTargetStatus.Pending, conflictLevel: OrderTargetConflictLevel.Blocked);

        var page = await Sut(handler).GetPageAsync(query, CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.PathAndQuery.Should().Be(
            "/api/workflow/service-orders/ord%2F1%20%23a/targets?page=2&pageSize=25&sortBy=addedAt&descending=true&status=Pending&conflictLevel=Blocked");
        page.TotalCount.Should().Be(26);
        page.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Id = "tgt-1", OrderId = "ord/1 #a", ConflictLevel = OrderTargetConflictLevel.Blocked, Version = 4, Alias = (string?)null });
    }

    [Fact]
    public async Task GetPageAsync_OmitsTheOptionalQueryMembersThatAreNotSet()
    {
        var handler = new BodyRecordingHandler(_ => BodyRecordingHandler.Json(HttpStatusCode.OK, EmptyPage));

        await Sut(handler).GetPageAsync(new OrderTargetQuery("ord-1"), CancellationToken.None);

        handler.Requests.Single().PathAndQuery.Should().Be("/api/workflow/service-orders/ord-1/targets?page=1&pageSize=50");
    }

    [Fact]
    public async Task GetPageAsync_UnknownOrder_ReadsAsAnEmptyPage_LikeAStore_AndAnUndefinedStatusNeedsNoRequest()
    {
        var handler = new BodyRecordingHandler(_ => BodyRecordingHandler.Problem(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound));
        var sut = Sut(handler);

        var unknownOrder = await sut.GetPageAsync(new OrderTargetQuery("ord-missing"), CancellationToken.None);
        var undefinedStatus = await sut.GetPageAsync(new OrderTargetQuery("ord-1", status: "pending"), CancellationToken.None);

        unknownOrder.Should().BeEquivalentTo(new { TotalCount = 0 });
        unknownOrder.Items.Should().BeEmpty();
        undefinedStatus.Items.Should().BeEmpty();
        handler.Requests.Should().ContainSingle("the undefined status never reaches the host");
    }

    [Fact]
    public async Task GetPageAsync_DeniedOrBrokenResponse_IsTranslated()
    {
        var denied = new BodyRecordingHandler(_ => BodyRecordingHandler.Problem(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied));
        var broken = new BodyRecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        (await FluentActions.Invoking(() => Sut(denied).GetPageAsync(new OrderTargetQuery("ord-1"), CancellationToken.None))
            .Should().ThrowAsync<GeoAssets.Workflow.Rules.OrderActionDeniedException>()).Which.Action.Should().Be(OrderActionType.View);
        await FluentActions.Invoking(() => Sut(broken).GetPageAsync(new OrderTargetQuery("ord-1"), CancellationToken.None))
            .Should().ThrowAsync<HttpRequestException>();
    }

    // ── GetByIdAsync / CountAsync / GetExtentAsync ────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ReturnsTheTarget_AndNullForEitherNotFoundCode()
    {
        var found = new BodyRecordingHandler(_ => BodyRecordingHandler.Json(
            HttpStatusCode.OK, JsonSerializer.Serialize(Target(), GeoAssets.Core.Services.GeoJsonSerializer.Options)));
        var noTarget = new BodyRecordingHandler(_ => BodyRecordingHandler.Problem(HttpStatusCode.NotFound, WorkflowProblemCodes.TargetNotFound));
        var noOrder = new BodyRecordingHandler(_ => BodyRecordingHandler.Problem(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound));
        var plain404 = new BodyRecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        (await Sut(found).GetByIdAsync("ord-1", "tgt/1", CancellationToken.None)).Should().BeEquivalentTo(Target());
        found.Requests.Single().PathAndQuery.Should().Be("/api/workflow/service-orders/ord-1/targets/tgt%2F1");
        (await Sut(noTarget).GetByIdAsync("ord-1", "tgt-1", CancellationToken.None)).Should().BeNull();
        (await Sut(noOrder).GetByIdAsync("ord-1", "tgt-1", CancellationToken.None)).Should().BeNull();
        await FluentActions.Invoking(() => Sut(plain404).GetByIdAsync("ord-1", "tgt-1", CancellationToken.None))
            .Should().ThrowAsync<HttpRequestException>("a 404 that is not one of ours is not read as 'no such target'");
    }

    [Fact]
    public async Task CountAsync_ReadsTheCount_AndZeroForAnUnknownOrder()
    {
        var counted = new BodyRecordingHandler(_ => BodyRecordingHandler.Json(HttpStatusCode.OK, """{ "count": 17 }"""));
        var unknown = new BodyRecordingHandler(_ => BodyRecordingHandler.Problem(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound));

        (await Sut(counted).CountAsync("ord 1", CancellationToken.None)).Should().Be(17);
        counted.Requests.Single().PathAndQuery.Should().Be("/api/workflow/service-orders/ord%201/targets/count");
        (await Sut(unknown).CountAsync("ord-missing", CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task GetExtentAsync_204_ReturnsNull_200_ParsesTheBoundingBox_UnknownOrder_ReturnsNull()
    {
        var none = new BodyRecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var some = new BodyRecordingHandler(_ => BodyRecordingHandler.Json(HttpStatusCode.OK, """{ "bbox": [-39.5, -13.25, -38, -12] }"""));
        var unknown = new BodyRecordingHandler(_ => BodyRecordingHandler.Problem(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound));

        (await Sut(none).GetExtentAsync("ord-1", CancellationToken.None)).Should().BeNull();
        (await Sut(some).GetExtentAsync("ord-1", CancellationToken.None)).Should().Be(new OrderTargetExtent(-39.5, -13.25, -38, -12));
        some.Requests.Single().PathAndQuery.Should().Be("/api/workflow/service-orders/ord-1/targets/extent");
        (await Sut(unknown).GetExtentAsync("ord-1", CancellationToken.None)).Should().BeNull();
    }

    [Theory]
    [InlineData("""{ "bbox": [1, 2, 3] }""")]
    [InlineData("""{ "bbox": [1, 2, 3, "4"] }""")]
    [InlineData("""{ "bbox": "1,2,3,4" }""")]
    [InlineData("""{ "box": [1, 2, 3, 4] }""")]
    [InlineData("""[1, 2, 3, 4]""")]
    public Task GetExtentAsync_MalformedBoundingBox_ThrowsJsonException(string body)
    {
        var handler = new BodyRecordingHandler(_ => BodyRecordingHandler.Json(HttpStatusCode.OK, body));

        var act = () => Sut(handler).GetExtentAsync("ord-1", CancellationToken.None);

        return act.Should().ThrowAsync<JsonException>();
    }

    // ── FindOpenUsagesAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task FindOpenUsagesAsync_SendsDistinctIdsInChunksOfFiveHundred_AndConcatenatesTheAnswers()
    {
        var handler = new BodyRecordingHandler(request =>
        {
            var first = request.Json.GetProperty("featureIds")[0].GetString();
            return BodyRecordingHandler.Json(HttpStatusCode.OK, $$"""
                { "items": [ { "featureId": "{{first}}", "orderId": "ord-9", "orderTypeId": "survey", "orderStatus": "Pending", "exclusivity": 1 } ],
                  "page": 1, "pageSize": 200, "totalCount": 1 }
                """);
        });
        var ids = Enumerable.Range(1, 1201).Select(i => $"f-{i:0000}").Append("f-0001").Append(" ").ToArray();

        var usages = await Sut(handler).FindOpenUsagesAsync(ids, "ord-1", CancellationToken.None);

        handler.Requests.Should().HaveCount(3).And.OnlyContain(r =>
            r.Method == HttpMethod.Post && string.Equals(r.PathAndQuery, "/api/workflow/service-orders/targets/usages", StringComparison.Ordinal));
        handler.Requests.Select(r => r.Json.GetProperty("featureIds").GetArrayLength()).Should().Equal(500, 500, 201);
        handler.Requests[0].Json.GetProperty("excludingOrderId").GetString().Should().Be("ord-1");
        handler.Requests[0].Json.GetProperty("pageSize").GetInt32().Should().Be(200);
        usages.Select(u => u.FeatureId).Should().Equal("f-0001", "f-0501", "f-1001");
        usages[0].Should().Be(new OrderTargetUsage("f-0001", "ord-9", "survey", "Pending", null, OrderTargetExclusivity.WarnOnOverlap));
    }

    [Fact]
    public async Task FindOpenUsagesAsync_DrainsEveryPageOfAChunk()
    {
        var handler = new BodyRecordingHandler(request =>
        {
            var page = request.Json.GetProperty("page").GetInt32();
            var items = page <= 2
                ? string.Join(",", Enumerable.Range(1, page == 1 ? 200 : 50).Select(i =>
                    $$"""{ "featureId": "f-1", "orderId": "ord-{{page}}-{{i}}", "orderTypeId": "t", "orderStatus": "Pending", "exclusivity": 0 }"""))
                : "";
            return BodyRecordingHandler.Json(HttpStatusCode.OK, $$"""{ "items": [{{items}}], "page": {{page}}, "pageSize": 200, "totalCount": 250 }""");
        });

        var usages = await Sut(handler).FindOpenUsagesAsync(["f-1"], "ord-1", CancellationToken.None);

        usages.Should().HaveCount(250);
        handler.Requests.Select(r => r.Json.GetProperty("page").GetInt32()).Should().Equal(1, 2);
    }

    [Fact]
    public async Task FindOpenUsagesAsync_NoIds_SendsNothing()
    {
        var handler = new BodyRecordingHandler(_ => throw new InvalidOperationException("no request expected"));

        (await Sut(handler).FindOpenUsagesAsync([], "ord-1", CancellationToken.None)).Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    // ── UpdateAsync / RemoveAsync / AddRangeAsync ─────────────────────────────

    [Fact]
    public async Task UpdateAsync_PatchesTheEditableMembersOnly_AlwaysSendingTheNote_AndNothingAboutWhoIsCalling()
    {
        var handler = new BodyRecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await Sut(handler).UpdateAsync(Target("tgt 1", "ord-1"), CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Patch);
        request.PathAndQuery.Should().Be("/api/workflow/service-orders/ord-1/targets/tgt%201");
        request.Json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("activityIds", "note", "status", "version");
        request.Json.GetProperty("note").ValueKind.Should().Be(JsonValueKind.Null, "a null note must be sent to clear it");
        request.Json.GetProperty("activityIds").EnumerateArray().Select(a => a.GetString()).Should().Equal("inspect");
        request.Json.GetProperty("status").GetString().Should().Be(OrderTargetStatus.InProgress);
        request.Json.GetProperty("version").GetInt32().Should().Be(3);
    }

    public static TheoryData<HttpStatusCode, string, Type> UpdateFailures() => new()
    {
        { HttpStatusCode.NotFound, WorkflowProblemCodes.TargetNotFound, typeof(KeyNotFoundException) },
        { HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound, typeof(KeyNotFoundException) },
        { HttpStatusCode.Conflict, WorkflowProblemCodes.TargetConcurrentModification, typeof(ServiceOrderConcurrencyException) },
        { HttpStatusCode.BadRequest, WorkflowProblemCodes.ValidationFailed, typeof(ArgumentException) },
        { HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied, typeof(GeoAssets.Workflow.Rules.OrderActionDeniedException) },
    };

    [Theory]
    [MemberData(nameof(UpdateFailures))]
    public async Task UpdateAsync_Failure_IsTranslatedToTheStoresExceptionType(HttpStatusCode status, string code, Type expected)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(expected);
        var handler = new BodyRecordingHandler(_ => BodyRecordingHandler.Problem(status, code));

        var act = () => Sut(handler).UpdateAsync(Target(), CancellationToken.None);

        (await act.Should().ThrowAsync<Exception>()).Which.Should().BeOfType(expected);
    }

    [Fact]
    public async Task RemoveAsync_PostsDistinctIdsInChunks_AndSumsTheRemovedCounts()
    {
        var handler = new BodyRecordingHandler(request =>
            BodyRecordingHandler.Json(HttpStatusCode.OK, $$"""{ "removed": {{request.Json.GetProperty("targetIds").GetArrayLength() - 1}} }"""));
        var ids = Enumerable.Range(1, 600).Select(i => $"t-{i}").Append("t-1").ToArray();

        var removed = await Sut(handler).RemoveAsync("ord/1", ids, CancellationToken.None);

        removed.Should().Be(499 + 99);
        handler.Requests.Should().HaveCount(2).And.OnlyContain(r =>
            r.Method == HttpMethod.Post && string.Equals(r.PathAndQuery, "/api/workflow/service-orders/ord%2F1/targets/remove", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RemoveAsync_NoIds_SendsNothing_AndAnUnknownOrderRemovesNothing()
    {
        var silent = new BodyRecordingHandler(_ => throw new InvalidOperationException("no request expected"));
        var unknown = new BodyRecordingHandler(_ => BodyRecordingHandler.Problem(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound));

        (await Sut(silent).RemoveAsync("ord-1", [], CancellationToken.None)).Should().Be(0);
        (await Sut(unknown).RemoveAsync("ord-missing", ["t-1"], CancellationToken.None)).Should().Be(0);
        silent.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AddRangeAsync_IsNotOffered_AndNeverSendsARequest()
    {
        var handler = new BodyRecordingHandler(_ => throw new InvalidOperationException("no request expected"));
        var sut = Sut(handler);

        (await sut.AddRangeAsync("ord-1", [], CancellationToken.None)).Should().Be(0);
        await FluentActions.Invoking(() => sut.AddRangeAsync("ord-1", [Target()], CancellationToken.None)).Should().ThrowAsync<NotSupportedException>();
        handler.Requests.Should().BeEmpty();
    }

    // ── Guards before any request ─────────────────────────────────────────────

    [Fact]
    public async Task EveryMember_ChecksItsArguments_BeforeSendingAnything()
    {
        var handler = new BodyRecordingHandler(_ => throw new InvalidOperationException("no request expected"));
        var sut = Sut(handler);
        var none = CancellationToken.None;

        await FluentActions.Invoking(() => sut.GetPageAsync(null!, none)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => sut.GetByIdAsync(" ", "t", none)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Invoking(() => sut.GetByIdAsync("o", "", none)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Invoking(() => sut.CountAsync("", none)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Invoking(() => sut.GetExtentAsync(" ", none)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Invoking(() => sut.FindOpenUsagesAsync(null!, "o", none)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => sut.FindOpenUsagesAsync(["f"], " ", none)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Invoking(() => sut.AddRangeAsync("", [], none)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Invoking(() => sut.AddRangeAsync("o", null!, none)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => sut.UpdateAsync(null!, none)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => sut.RemoveAsync(" ", ["t"], none)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Invoking(() => sut.RemoveAsync("o", null!, none)).Should().ThrowAsync<ArgumentNullException>();
        FluentActions.Invoking(() => new OrderTargetQuery("o", page: 0)).Should().Throw<ArgumentOutOfRangeException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task EveryMember_WithAnAlreadyCancelledToken_ThrowsOperationCanceledException_WithoutSendingAnything()
    {
        var handler = new BodyRecordingHandler(_ => throw new InvalidOperationException("no request expected"));
        var sut = Sut(handler);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var cancelled = cancellation.Token;

        Func<Task>[] calls =
        [
            () => sut.GetPageAsync(new OrderTargetQuery("o"), cancelled),
            () => sut.GetByIdAsync("o", "t", cancelled),
            () => sut.CountAsync("o", cancelled),
            () => sut.GetExtentAsync("o", cancelled),
            () => sut.FindOpenUsagesAsync(["f"], "o", cancelled),
            () => sut.AddRangeAsync("o", [Target()], cancelled),
            () => sut.UpdateAsync(Target(), cancelled),
            () => sut.RemoveAsync("o", ["t"], cancelled),
        ];

        foreach (var call in calls)
            await call.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().BeEmpty();
    }
}
