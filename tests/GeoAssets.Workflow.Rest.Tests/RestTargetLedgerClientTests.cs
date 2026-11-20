using System.Net;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests;

/// <summary>
/// <see cref="RestTargetLedgerClient"/> (XD01-382): the requests it sends to the kernel's global target ledger — the vertical and the order
/// escaped into the path, the organization in the body or the query, the service principal's bearer, never an organization header — and
/// how it reads the answers: a conflict, an unavailable ledger, or the same exception the in-process selection throws.
/// </summary>
public sealed class RestTargetLedgerClientTests
{
    private static readonly Guid Organization = new("a0a0a0a0-0000-4000-8000-000000000382");
    private static readonly TargetLedgerKey Key = new("neutral-operator", "ord-1");

    private const string ResultJson = """
        { "orderTypeId": "shared-use-pole-work", "added": 2, "matched": 3, "eligible": 2, "alreadyInOrder": 1, "truncatedByLimit": 0, "warningCount": 1, "blockedCount": 0 }
        """;

    private const string PreviewJson = """
        { "orderTypeId": "shared-use-pole-work", "matched": 3, "eligible": 2, "alreadyInOrder": 1, "truncatedByLimit": 0, "warningCount": 0, "blockedCount": 0,
          "firstPage": { "items": [], "totalCount": 2 } }
        """;

    private const string ConflictsMember =
        "\"conflicts\": [ { \"featureId\": \"pole-1\", \"verticalId\": \"kernel\", \"exclusivity\": 2, \"orderId\": \"ord-9\", \"orderTypeId\": \"inspection\" }, "
        + "{ \"featureId\": \"pole-2\", \"verticalId\": \"field-service\", \"exclusivity\": 2, \"orderId\": null, \"orderTypeId\": null } ]";

    private static TargetLedgerReserveCommand Command(TargetLedgerKey? key = null) => new(
        key ?? Key, Organization, "shared-use-pole-work", "identifier-list",
        new Dictionary<string, object>(StringComparer.Ordinal) { ["featureIds"] = new[] { "pole-1", "pole-2" } },
        AcceptTruncation: true, Page: 2, PageSize: 25, Actor: "user-7");

    private static RestTargetLedgerClient Sut(RecordingKernelHandler handler, FakeServiceAuthTokenProvider? tokens = null) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://kernel.test/") }, tokens);

    private static RecordingKernelHandler Answering(string json) => new(_ => RecordingKernelHandler.Json(HttpStatusCode.OK, json));

    private static RecordingKernelHandler Failing(HttpStatusCode status, string code, string extraMembers = "") =>
        new(_ => RecordingKernelHandler.Problem(status, code, extraMembers));

    // ── Requests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReserveAsync_PostsToTheReserveRoute_WithTheOrganizationAndTheSelectionInTheBody_AndReadsTheResult()
    {
        var handler = Answering(ResultJson);

        var result = await Sut(handler).ReserveAsync(Command(), CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Path.Should().Be("/api/targets/ledger/neutral-operator/ord-1/reserve");
        var body = request.Json;
        body.GetProperty("organizationId").GetGuid().Should().Be(Organization);
        body.GetProperty("orderTypeId").GetString().Should().Be("shared-use-pole-work");
        body.GetProperty("strategyId").GetString().Should().Be("identifier-list");
        body.GetProperty("parameters").GetProperty("featureIds").EnumerateArray().Select(e => e.GetString()).Should().Equal("pole-1", "pole-2");
        body.GetProperty("preview").GetBoolean().Should().BeFalse();
        body.GetProperty("acceptTruncation").GetBoolean().Should().BeTrue();
        body.GetProperty("page").GetInt32().Should().Be(2);
        body.GetProperty("pageSize").GetInt32().Should().Be(25);
        body.GetProperty("actor").GetString().Should().Be("user-7");
        result.Added.Should().Be(2);
        result.Matched.Should().Be(3);
        result.AlreadyInOrder.Should().Be(1);
        result.WarningCount.Should().Be(1);
    }

    [Fact]
    public async Task PreviewAsync_PostsToTheSameRoute_AsAPreview_AndReadsThePreview()
    {
        var handler = Answering(PreviewJson);

        var preview = await Sut(handler).PreviewAsync(Command(), CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Path.Should().Be("/api/targets/ledger/neutral-operator/ord-1/reserve");
        request.Json.GetProperty("preview").GetBoolean().Should().BeTrue();
        preview.Eligible.Should().Be(2);
        preview.FirstPage.TotalCount.Should().Be(2);
    }

    [Theory]
    [InlineData("ord/1", "ord%2F1")]
    [InlineData("ord 1", "ord%201")]
    [InlineData("a?b#c", "a%3Fb%23c")]
    [InlineData("../admin", "..%2Fadmin")]
    public async Task EveryRoute_EscapesTheOrderIdIntoOnePathSegment(string orderId, string escaped)
    {
        var handler = new RecordingKernelHandler(request => request.Path.EndsWith("/usages", StringComparison.Ordinal)
            ? RecordingKernelHandler.Json(HttpStatusCode.OK, """{ "items": [], "skip": 0, "take": 10, "totalCount": 0 }""")
            : request.Path.EndsWith("/reserve", StringComparison.Ordinal)
                ? RecordingKernelHandler.Json(HttpStatusCode.OK, ResultJson)
                : RecordingKernelHandler.Json(HttpStatusCode.OK, """{ "verticalId": "v", "orderId": "o", "changed": 0, "targets": [] }"""));
        var sut = Sut(handler);
        var key = new TargetLedgerKey("neutral-operator", orderId);

        await sut.ReserveAsync(Command(key), CancellationToken.None);
        await sut.ReleaseAsync(key, Organization, null, null, CancellationToken.None);
        await sut.CommitAsync(key, Organization, null, null, CancellationToken.None);
        await sut.GetUsagesAsync(key, Organization, 0, 10, CancellationToken.None);

        handler.Requests.Select(r => r.Original).Should().OnlyContain(
            original => original.Contains($"/api/targets/ledger/neutral-operator/{escaped}/", StringComparison.Ordinal),
            "the order id stays one segment of the path, whatever it holds");
        handler.Requests.Select(r => r.Path.Split('/').Length).Should().OnlyContain(segments => segments == 7, "a hostile id adds no segment");
    }

    [Fact]
    public async Task EveryRoute_EscapesTheVerticalIdToo()
    {
        var handler = Answering(ResultJson);

        await Sut(handler).ReserveAsync(Command(new TargetLedgerKey("a/b c", "ord-1")), CancellationToken.None);

        handler.Requests.Single().Original.Should().Contain("/api/targets/ledger/a%2Fb%20c/ord-1/reserve");
    }

    [Theory]
    [InlineData("release")]
    [InlineData("commit")]
    public async Task ReleaseAndCommit_PostTheOrganizationTheTargetsAndTheActorInTheBody_AndReturnWhatChanged(string operation)
    {
        var handler = Answering("""{ "verticalId": "neutral-operator", "orderId": "ord-1", "changed": 2, "targets": [ { "targetId": "t-1", "featureId": "pole-1" }, { "targetId": "t-2", "featureId": "pole-2" } ] }""");
        var sut = Sut(handler);

        var changed = string.Equals(operation, "release", StringComparison.Ordinal)
            ? await sut.ReleaseAsync(Key, Organization, ["t-1", "t-2"], "neutral-operator", CancellationToken.None)
            : await sut.CommitAsync(Key, Organization, ["t-1", "t-2"], "neutral-operator", CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Path.Should().Be($"/api/targets/ledger/neutral-operator/ord-1/{operation}");
        request.Json.GetProperty("organizationId").GetGuid().Should().Be(Organization);
        request.Json.GetProperty("targetIds").EnumerateArray().Select(e => e.GetString()).Should().Equal("t-1", "t-2");
        request.Json.GetProperty("actor").GetString().Should().Be("neutral-operator");
        changed.Should().BeEquivalentTo(
            [new GeoAssets.Workflow.Notifications.TargetLedgerEventItem("t-1", "pole-1"), new GeoAssets.Workflow.Notifications.TargetLedgerEventItem("t-2", "pole-2")],
            options => options.WithStrictOrdering());
    }

    [Theory]
    [InlineData("release")]
    [InlineData("commit")]
    public async Task ReleaseAndCommit_OfAnOrderTheLedgerHoldsNothingOf_AreNull_NotAnError(string operation)
    {
        var handler = Failing(HttpStatusCode.NotFound, "orders.not-found");
        var sut = Sut(handler);

        var changed = string.Equals(operation, "release", StringComparison.Ordinal)
            ? await sut.ReleaseAsync(Key, Organization, null, null, CancellationToken.None)
            : await sut.CommitAsync(Key, Organization, null, null, CancellationToken.None);

        changed.Should().BeNull();
    }

    [Fact]
    public async Task GetUsagesAsync_GetsOnePage_WithTheOrganizationInTheQuery_AndReleasedTargetsIncluded()
    {
        var handler = Answering("""
            { "items": [
                { "target": { "id": "t-1", "orderId": "ord-1", "verticalId": "neutral-operator", "featureId": "pole-1", "assetTypeId": "pole", "status": "Pending",
                              "addedAt": "2026-11-20T12:00:00Z", "addedBy": "u", "organizationId": "a0a0a0a0-0000-4000-8000-000000000382" }, "releasedAt": null },
                { "target": { "id": "t-2", "orderId": "ord-1", "verticalId": "neutral-operator", "featureId": "pole-2", "assetTypeId": "pole", "status": "Pending",
                              "addedAt": "2026-11-20T12:00:00Z", "addedBy": "u", "organizationId": "a0a0a0a0-0000-4000-8000-000000000382" }, "releasedAt": "2026-11-20T13:00:00Z" } ],
              "skip": 1000, "take": 1000, "totalCount": 1002 }
            """);

        var page = await Sut(handler).GetUsagesAsync(Key, Organization, 1000, 1000, CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.Path.Should().Be("/api/targets/ledger/neutral-operator/ord-1/usages");
        request.Query.Should().Be($"?organizationId={Organization:D}&skip=1000&take=1000");
        request.Body.Should().BeNull();
        page.TotalCount.Should().Be(1002);
        page.Items.Select(e => e.Target.FeatureId).Should().Equal("pole-1", "pole-2");
        page.Items.Select(e => e.ReleasedAt is null).Should().Equal(true, false);
    }

    [Fact]
    public async Task GetUsagesAsync_OfAnOrderTheLedgerHoldsNothingOf_IsAnEmptyPage()
    {
        var page = await Sut(Failing(HttpStatusCode.NotFound, "orders.not-found")).GetUsagesAsync(Key, Organization, 0, 10, CancellationToken.None);

        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetUsagesAsync_RefusesAnOutOfRangePage_BeforeAnyRequest()
    {
        var handler = Answering("{}");
        var sut = Sut(handler);

        var negativeSkip = async () => await sut.GetUsagesAsync(Key, Organization, -1, 10, CancellationToken.None);
        var noTake = async () => await sut.GetUsagesAsync(Key, Organization, 0, 0, CancellationToken.None);

        await negativeSkip.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await noTake.Should().ThrowAsync<ArgumentOutOfRangeException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ReserveAsync_WithABlankOrderTypeOrStrategy_IsRefusedBeforeAnyRequest()
    {
        var handler = Answering(ResultJson);
        var sut = Sut(handler);

        var blankType = async () => await sut.ReserveAsync(Command() with { OrderTypeId = " " }, CancellationToken.None);
        var blankStrategy = async () => await sut.PreviewAsync(Command() with { StrategyId = "" }, CancellationToken.None);

        await blankType.Should().ThrowAsync<ArgumentException>();
        await blankStrategy.Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
    }

    // ── Authentication and the organization header ────────────────────────────

    [Fact]
    public async Task EveryCall_CarriesAFreshServiceBearerToken_AndNoOrganizationHeader()
    {
        var handler = new RecordingKernelHandler(request => request.Path.EndsWith("/usages", StringComparison.Ordinal)
            ? RecordingKernelHandler.Json(HttpStatusCode.OK, """{ "items": [], "skip": 0, "take": 10, "totalCount": 0 }""")
            : request.Path.EndsWith("/reserve", StringComparison.Ordinal)
                ? RecordingKernelHandler.Json(HttpStatusCode.OK, ResultJson)
                : RecordingKernelHandler.Json(HttpStatusCode.OK, """{ "verticalId": "v", "orderId": "o", "changed": 0, "targets": [] }"""));
        var tokens = new FakeServiceAuthTokenProvider();
        var sut = Sut(handler, tokens);

        await sut.ReserveAsync(Command(), CancellationToken.None);
        await sut.ReleaseAsync(Key, Organization, null, null, CancellationToken.None);
        await sut.GetUsagesAsync(Key, Organization, 0, 10, CancellationToken.None);

        handler.Requests.Select(r => r.Authorization).Should().Equal("Bearer service-token-1", "Bearer service-token-2", "Bearer service-token-3");
        handler.Requests.SelectMany(r => r.Headers.Keys).Should().NotContain(
            name => name.Contains("organization", StringComparison.OrdinalIgnoreCase) || name.Contains("tenant", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WithoutATokenProvider_NoAuthorizationIsSent_AndTheKernelsAnswerIs401()
    {
        var handler = new RecordingKernelHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var act = async () => await Sut(handler).ReserveAsync(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<TargetLedgerUnavailableException>();
        handler.Requests.Single().Authorization.Should().BeNull();
    }

    // ── Failures ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReserveAndPreview_On409TargetsConflict_ThrowTheConflictWithTheBlockingHolders(bool preview)
    {
        var handler = Failing(
            HttpStatusCode.Conflict, TargetLedger.ConflictProblemCode,
            ConflictsMember);
        var sut = Sut(handler);

        Func<Task> act = preview
            ? () => sut.PreviewAsync(Command(), CancellationToken.None)
            : () => sut.ReserveAsync(Command(), CancellationToken.None);

        var conflict = (await act.Should().ThrowAsync<TargetLedgerConflictException>()).Which;
        conflict.Conflicts.Should().HaveCount(2);
        conflict.Conflicts[0].Should().Be(new TargetLedgerConflict("pole-1", "kernel", (OrderTargetExclusivity)2, "ord-9", "inspection"));
        conflict.Conflicts[1].OrderId.Should().BeNull("another organization's holder is not named");
    }

    [Fact]
    public async Task Reserve_On409WithoutAConflictsList_StillThrowsTheConflict_WithNoHolders()
    {
        var act = async () => await Sut(Failing(HttpStatusCode.Conflict, TargetLedger.ConflictProblemCode)).ReserveAsync(Command(), CancellationToken.None);

        (await act.Should().ThrowAsync<TargetLedgerConflictException>()).Which.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public async Task Reserve_When404OrderTypeNotFound_TheVerticalsRegistrationIsPending_SoItIsUnavailable_NotNotFound()
    {
        var act = async () => await Sut(Failing(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderTypeNotFound)).ReserveAsync(Command(), CancellationToken.None);

        (await act.Should().ThrowAsync<TargetLedgerUnavailableException>()).Which.Message.Should().Contain("shared-use-pole-work").And.Contain("neutral-operator").And.Contain("pending");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task EveryOperation_WhenTheKernelAnswersWithoutAProblem_IsUnavailable(HttpStatusCode status)
    {
        var sut = Sut(new RecordingKernelHandler(_ => new HttpResponseMessage(status)));

        var reserve = async () => await sut.ReserveAsync(Command(), CancellationToken.None);
        var preview = async () => await sut.PreviewAsync(Command(), CancellationToken.None);
        var release = async () => await sut.ReleaseAsync(Key, Organization, null, null, CancellationToken.None);
        var commit = async () => await sut.CommitAsync(Key, Organization, null, null, CancellationToken.None);
        var usages = async () => await sut.GetUsagesAsync(Key, Organization, 0, 10, CancellationToken.None);

        await reserve.Should().ThrowAsync<TargetLedgerUnavailableException>();
        await preview.Should().ThrowAsync<TargetLedgerUnavailableException>();
        await release.Should().ThrowAsync<TargetLedgerUnavailableException>();
        await commit.Should().ThrowAsync<TargetLedgerUnavailableException>();
        await usages.Should().ThrowAsync<TargetLedgerUnavailableException>();
    }

    [Fact]
    public async Task EveryOperation_WhenTheConnectionFails_IsUnavailable_WithTheTransportFailureInside()
    {
        var sut = Sut(new RecordingKernelHandler(_ => throw new HttpRequestException("connection refused")));

        var reserve = async () => await sut.ReserveAsync(Command(), CancellationToken.None);
        var release = async () => await sut.ReleaseAsync(Key, Organization, null, null, CancellationToken.None);
        var usages = async () => await sut.GetUsagesAsync(Key, Organization, 0, 10, CancellationToken.None);

        (await reserve.Should().ThrowAsync<TargetLedgerUnavailableException>()).Which.InnerException.Should().BeOfType<HttpRequestException>();
        (await release.Should().ThrowAsync<TargetLedgerUnavailableException>()).Which.InnerException.Should().BeOfType<HttpRequestException>();
        await usages.Should().ThrowAsync<TargetLedgerUnavailableException>();
    }

    [Fact]
    public async Task EveryOperation_WhenTheCallerCancels_ThrowsTheCancellation_NotUnavailable()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var sut = Sut(Answering(ResultJson));

        var act = async () => await sut.ReserveAsync(Command(), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public Task Reserve_WhenTheKernelAnswers5xxWithAProblemOfAKnownCode_ThrowsTheExceptionTheInProcessSelectionThrows()
    {
        var act = async () => await Sut(Failing(HttpStatusCode.ServiceUnavailable, WorkflowProblemCodes.TargetLedgerUnavailable)).ReserveAsync(Command(), CancellationToken.None);

        return act.Should().ThrowAsync<TargetLedgerUnavailableException>();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "orders.not-found", typeof(KeyNotFoundException))]
    [InlineData(HttpStatusCode.NotFound, "targets.not-found", typeof(KeyNotFoundException))]
    [InlineData(HttpStatusCode.BadRequest, "selection.unknown-strategy", typeof(KeyNotFoundException))]
    [InlineData(HttpStatusCode.BadRequest, "validation.failed", typeof(ArgumentException))]
    [InlineData(HttpStatusCode.BadRequest, "paging.out-of-range", typeof(ArgumentOutOfRangeException))]
    [InlineData(HttpStatusCode.UnprocessableEntity, "orders.no-target-policy", typeof(InvalidOperationException))]
    [InlineData(HttpStatusCode.Conflict, "orders.invalid-state", typeof(InvalidServiceOrderTransitionException))]
    [InlineData(HttpStatusCode.Forbidden, "auth.denied", typeof(OrderActionDeniedException))]
    public async Task Reserve_OnAnyOtherKnownProblem_ThrowsTheExceptionTheInProcessSelectionThrows(HttpStatusCode status, string code, Type expected)
    {
        var act = async () => await Sut(Failing(status, code)).ReserveAsync(Command(), CancellationToken.None);

        (await act.Should().ThrowAsync<Exception>()).Which.Should().BeAssignableTo(expected);
    }

    [Fact]
    public Task Reserve_On200WithAnEmptyBody_FailsLoudly_RatherThanReturningNothing()
    {
        var act = async () => await Sut(Answering("null")).ReserveAsync(Command(), CancellationToken.None);

        return act.Should().ThrowAsync<System.Text.Json.JsonException>();
    }
}
