using System.Net;
using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests;

/// <summary>
/// The requests <see cref="RestSharedUseDispatchClient"/> sends and how it reads the answers
/// (XD01-316) — above all that nothing about the caller's identity is on the wire, and that
/// every problem <c>code</c> of the shared-use routes comes back as the exception the
/// in-process service throws.
/// </summary>
public sealed class RestSharedUseDispatchClientTests
{
    private const string PreviewJson = """
        {
          "orderId": "ord-1", "orderTypeId": "shared-use-pole-work", "childOrderTypeId": "shared-use-occupant-notice",
          "rowVersion": "AQIDBA==", "statusAfterDispatch": "Pending",
          "targetCount": 9, "blockedTargetCount": 1, "targetsWithoutActivityCount": 0, "unnotifiablePoleCount": 2, "skippedEntryCount": 4,
          "newRecipientCount": 1, "blockReason": 3, "canDispatch": false,
          "recipients": { "items": [ { "occupantOrganizationId": "0c0c0c0c-0000-4000-8000-000000000001", "childOrderId": "child-1", "poleCount": 5, "alreadyDispatched": true } ],
                          "totalCount": 7 }
        }
        """;

    private const string ResultJson = """
        { "parentOrderId": "ord-1", "orderTypeId": "shared-use-pole-work", "parentStatus": "Pending", "childOrdersCreated": 3, "childOrdersAlreadyDispatched": 1,
          "notificationsEnqueued": 3, "targetCount": 9, "blockedTargetCount": 1, "unnotifiablePoleCount": 2, "skippedEntryCount": 4 }
        """;

    private static readonly WorkflowPrincipal Principal = new(
        "user-SECRET-ID", "org-SECRET-ID", ["Administrator"], ["group-SECRET-ID"], ["serviceorders:dispatch"]);

    private static RestSharedUseDispatchClient Sut(BodyRecordingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/api/workflow/") });

    private static BodyRecordingHandler Answering(string json) => new(_ => BodyRecordingHandler.Json(HttpStatusCode.OK, json));

    private static BodyRecordingHandler Failing(HttpStatusCode status, string code, string extraMembers = "") =>
        new(_ => BodyRecordingHandler.Problem(status, code, extraMembers));

    // ── Requests and responses ────────────────────────────────────────────────

    [Fact]
    public async Task PreviewAsync_GetsThePlanRoute_WithPaging_AndReadsTheWholePreview()
    {
        var handler = Answering(PreviewJson);

        var preview = await Sut(handler).PreviewAsync("ord/1", 2, 25, Principal, CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.PathAndQuery.Should().Be("/api/workflow/service-orders/ord%2F1/shared-use/dispatch-plan?page=2&pageSize=25");
        request.Body.Should().BeNull();
        preview.Should().BeEquivalentTo(new
        {
            OrderId = "ord-1",
            OrderTypeId = SharedUseOrderTypes.PoleWorkId,
            ChildOrderTypeId = SharedUseOrderTypes.OccupantNoticeId,
            StatusAfterDispatch = ServiceOrderStatus.Pending,
            TargetCount = 9,
            BlockedTargetCount = 1,
            TargetsWithoutActivityCount = 0,
            UnnotifiablePoleCount = 2,
            SkippedEntryCount = 4,
            NewRecipientCount = 1,
            BlockReason = (SharedUseDispatchBlockReason?)SharedUseDispatchBlockReason.NoEligibleOccupants,
            CanDispatch = false,
        });
        preview.RowVersion.Should().Equal(1, 2, 3, 4);
        preview.Recipients.TotalCount.Should().Be(7);
        preview.Recipients.Items.Should().Equal(
            new SharedUseDispatchRecipient(new Guid("0c0c0c0c-0000-4000-8000-000000000001"), "child-1", 5, true));
    }

    [Fact]
    public async Task GetSkippedEntriesAsync_GetsItsRoute_AndReadsThePage()
    {
        var entryId = new Guid("0e0e0e0e-0000-4000-8000-000000000001");
        var handler = Answering($$"""
            { "items": [ { "poleFeatureId": "pole-1", "entryId": "{{entryId}}", "slotIndex": 3, "status": 3, "reason": 0 },
                         { "poleFeatureId": "pole-2", "entryId": "{{entryId}}", "status": 1, "reason": 1 } ],
              "page": 1, "pageSize": 50, "totalCount": 12 }
            """);

        var page = await Sut(handler).GetSkippedEntriesAsync("ord-1", 1, 50, Principal, CancellationToken.None);

        handler.Requests.Should().ContainSingle().Which.PathAndQuery
            .Should().Be("/api/workflow/service-orders/ord-1/shared-use/dispatch-plan/skipped-entries?page=1&pageSize=50");
        page.TotalCount.Should().Be(12);
        page.Items.Should().Equal(
            new SharedUseSkippedEntry("pole-1", entryId, 3, PoleAttachmentOccupantStatus.ClandestineUnauthorized, SharedUseSkipReason.NoActiveContract),
            new SharedUseSkippedEntry("pole-2", entryId, null, PoleAttachmentOccupantStatus.AuthorizedContracted, SharedUseSkipReason.UnknownOrganization));
    }

    [Fact]
    public async Task GetUnnotifiablePolesAsync_GetsItsRoute_AndReadsThePage_AnAbsentItemsListIsEmpty()
    {
        var handler = Answering("""{ "items": ["pole-2", "pole-9"], "page": 3, "pageSize": 2, "totalCount": 6 }""");
        var empty = Answering("""{ "page": 9, "pageSize": 2, "totalCount": 6 }""");

        var page = await Sut(handler).GetUnnotifiablePolesAsync("ord-1", 3, 2, Principal, CancellationToken.None);
        var beyond = await Sut(empty).GetUnnotifiablePolesAsync("ord-1", 9, 2, Principal, CancellationToken.None);

        handler.Requests.Should().ContainSingle().Which.PathAndQuery
            .Should().Be("/api/workflow/service-orders/ord-1/shared-use/dispatch-plan/unnotifiable-poles?page=3&pageSize=2");
        page.Items.Should().Equal("pole-2", "pole-9");
        page.TotalCount.Should().Be(6);
        beyond.Items.Should().BeEmpty();
        beyond.TotalCount.Should().Be(6);
    }

    [Fact]
    public async Task DispatchAsync_PostsOnlyTheRowVersion_AsBase64_AndNothingAboutTheCaller()
    {
        var handler = Answering(ResultJson);

        var result = await Sut(handler).DispatchAsync("ord 1", [1, 2, 3, 4], Principal, CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be("/api/workflow/service-orders/ord%201/shared-use/dispatch");
        request.Json.EnumerateObject().Select(member => member.Name).Should().Equal("rowVersion");
        request.Json.GetProperty("rowVersion").GetString().Should().Be("AQIDBA==");
        request.Body.Should().NotContain("SECRET");
        result.Should().BeEquivalentTo(new SharedUseDispatchResult
        {
            ParentOrderId = "ord-1",
            OrderTypeId = SharedUseOrderTypes.PoleWorkId,
            ParentStatus = ServiceOrderStatus.Pending,
            ChildOrdersCreated = 3,
            ChildOrdersAlreadyDispatched = 1,
            NotificationsEnqueued = 3,
            TargetCount = 9,
            BlockedTargetCount = 1,
            UnnotifiablePoleCount = 2,
            SkippedEntryCount = 4,
        });
    }

    [Fact]
    public async Task DispatchAsync_AnEmptyRowVersion_IsSentAsAnEmptyString_NotLeftOut()
    {
        var handler = Answering(ResultJson);

        await Sut(handler).DispatchAsync("ord-1", [], Principal, CancellationToken.None);

        handler.Requests.Should().ContainSingle().Which.Json.GetProperty("rowVersion").GetString().Should().BeEmpty();
    }

    // ── Guards: before any request ────────────────────────────────────────────

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 201)]
    public async Task PlanReads_PagingOutOfRange_ThrowArgumentOutOfRange_WithoutARequest(int page, int pageSize)
    {
        var handler = Answering(PreviewJson);
        var sut = Sut(handler);

        await ((Func<Task>)(() => sut.PreviewAsync("ord-1", page, pageSize, Principal, CancellationToken.None))).Should().ThrowAsync<ArgumentOutOfRangeException>();
        await ((Func<Task>)(() => sut.GetSkippedEntriesAsync("ord-1", page, pageSize, Principal, CancellationToken.None))).Should().ThrowAsync<ArgumentOutOfRangeException>();
        await ((Func<Task>)(() => sut.GetUnnotifiablePolesAsync("ord-1", page, pageSize, Principal, CancellationToken.None))).Should().ThrowAsync<ArgumentOutOfRangeException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task EveryMember_BlankOrNullArguments_Throw_WithoutARequest()
    {
        var handler = Answering(PreviewJson);
        var sut = Sut(handler);

        await ((Func<Task>)(() => sut.PreviewAsync(" ", 1, 50, Principal, CancellationToken.None))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => sut.PreviewAsync("ord-1", 1, 50, null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => sut.GetSkippedEntriesAsync("", 1, 50, Principal, CancellationToken.None))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => sut.GetUnnotifiablePolesAsync("ord-1", 1, 50, null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => sut.DispatchAsync(" ", [], Principal, CancellationToken.None))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => sut.DispatchAsync("ord-1", null!, Principal, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => sut.DispatchAsync("ord-1", [], null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task EveryMember_TokenAlreadyCancelled_ThrowsOperationCanceled_WithoutARequest()
    {
        var handler = Answering(PreviewJson);
        var sut = Sut(handler);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await ((Func<Task>)(() => sut.PreviewAsync("ord-1", 1, 50, Principal, cancelled.Token))).Should().ThrowAsync<OperationCanceledException>();
        await ((Func<Task>)(() => sut.GetSkippedEntriesAsync("ord-1", 1, 50, Principal, cancelled.Token))).Should().ThrowAsync<OperationCanceledException>();
        await ((Func<Task>)(() => sut.GetUnnotifiablePolesAsync("ord-1", 1, 50, Principal, cancelled.Token))).Should().ThrowAsync<OperationCanceledException>();
        await ((Func<Task>)(() => sut.DispatchAsync("ord-1", [], Principal, cancelled.Token))).Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().BeEmpty();
    }

    // ── Failures: the exception the in-process service throws ─────────────────

    [Theory]
    [InlineData(SharedUseProblemCodes.NoTargets, SharedUseDispatchBlockReason.NoTargets)]
    [InlineData(SharedUseProblemCodes.AllTargetsBlocked, SharedUseDispatchBlockReason.AllTargetsBlocked)]
    [InlineData(SharedUseProblemCodes.NoOccupants, SharedUseDispatchBlockReason.NoEligibleOccupants)]
    public async Task ABlockedDispatch_ComesBackWithItsReason_AndNoTargets(string code, SharedUseDispatchBlockReason reason)
    {
        var act = () => Sut(Failing(HttpStatusCode.UnprocessableEntity, code)).DispatchAsync("ord-1", [], Principal, CancellationToken.None);

        var blocked = (await act.Should().ThrowAsync<SharedUseDispatchBlockedException>()).Which;
        blocked.Reason.Should().Be(reason);
        blocked.OrderId.Should().Be("ord-1");
        blocked.TargetIds.Should().BeEmpty();
        blocked.TargetCount.Should().Be(0);
    }

    [Fact]
    public async Task ADispatchBlockedByTargetsWithoutActivity_ComesBackWithTheTargetsTheHostListed_AndTheirTrueCount()
    {
        var handler = Failing(
            HttpStatusCode.UnprocessableEntity, SharedUseProblemCodes.TargetWithoutActivity, "\"targetCount\": 130, \"targetIds\": [\"t-1\", \"t-2\"]");

        var act = () => Sut(handler).DispatchAsync("ord-1", [], Principal, CancellationToken.None);

        var blocked = (await act.Should().ThrowAsync<SharedUseDispatchBlockedException>()).Which;
        blocked.Reason.Should().Be(SharedUseDispatchBlockReason.TargetsWithoutActivity);
        blocked.TargetIds.Should().Equal("t-1", "t-2");
        blocked.TargetCount.Should().Be(130);
    }

    [Fact]
    public async Task OrderTypeProblems_ComeBackTyped_NamingTheOrderType()
    {
        var missing = Failing(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderTypeNotFound, "\"orderTypeId\": \"ghost\"");
        var noFanOut = Failing(HttpStatusCode.UnprocessableEntity, SharedUseProblemCodes.NoFanOut, "\"orderTypeId\": \"plain\"");

        var first = () => Sut(missing).DispatchAsync("ord-1", [], Principal, CancellationToken.None);
        var second = () => Sut(noFanOut).PreviewAsync("ord-1", 1, 50, Principal, CancellationToken.None);

        var notRegistered = (await first.Should().ThrowAsync<OrderTypeNotRegisteredException>()).Which;
        notRegistered.OrderTypeId.Should().Be("ghost");
        notRegistered.Should().BeAssignableTo<KeyNotFoundException>();
        (await second.Should().ThrowAsync<OrderTypeHasNoFanOutException>()).Which.OrderTypeId.Should().Be("plain");
    }

    [Fact]
    public async Task OrderTypeProblems_WithoutTheirOrderTypeMember_StillComeBackTyped()
    {
        var missing = Failing(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderTypeNotFound);
        var noFanOut = Failing(HttpStatusCode.UnprocessableEntity, SharedUseProblemCodes.NoFanOut);

        var first = () => Sut(missing).DispatchAsync("ord-1", [], Principal, CancellationToken.None);
        var second = () => Sut(noFanOut).DispatchAsync("ord-1", [], Principal, CancellationToken.None);

        (await first.Should().ThrowAsync<OrderTypeNotRegisteredException>()).Which.OrderTypeId.Should().BeEmpty();
        (await second.Should().ThrowAsync<OrderTypeHasNoFanOutException>()).Which.OrderTypeId.Should().BeEmpty();
    }

    [Fact]
    public async Task TheSharedProblems_ComeBackAsTheSharedTranslationGivesThem()
    {
        var notFound = () => Sut(Failing(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound)).DispatchAsync("ord-1", [], Principal, CancellationToken.None);
        var denied = () => Sut(Failing(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied, "\"reason\": \"owner only\"")).DispatchAsync("ord-1", [], Principal, CancellationToken.None);
        var state = () => Sut(Failing(HttpStatusCode.Conflict, WorkflowProblemCodes.OrderInvalidState, "\"from\": \"Completed\", \"to\": \"Completed\"")).DispatchAsync("ord-1", [], Principal, CancellationToken.None);
        var stale = () => Sut(Failing(HttpStatusCode.Conflict, WorkflowProblemCodes.TargetConcurrentModification, "\"orderId\": \"ord-1\"")).DispatchAsync("ord-1", [], Principal, CancellationToken.None);
        var paging = () => Sut(Failing(HttpStatusCode.BadRequest, WorkflowProblemCodes.PagingOutOfRange)).PreviewAsync("ord-1", 1, 50, Principal, CancellationToken.None);
        var invalid = () => Sut(Failing(HttpStatusCode.BadRequest, WorkflowProblemCodes.ValidationFailed)).DispatchAsync("ord-1", [], Principal, CancellationToken.None);

        (await notFound.Should().ThrowAsync<KeyNotFoundException>()).Which.Should().NotBeOfType<OrderTypeNotRegisteredException>();
        var refusal = (await denied.Should().ThrowAsync<OrderActionDeniedException>()).Which;
        refusal.Action.Should().Be(OrderActionType.Dispatch);
        refusal.Evaluation!.Reason.Should().Be("owner only");
        (await state.Should().ThrowAsync<InvalidServiceOrderTransitionException>()).Which.From.Should().Be("Completed");
        (await stale.Should().ThrowAsync<ServiceOrderConcurrencyException>()).Which.OrderId.Should().Be("ord-1");
        await paging.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await invalid.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AFailureWithNoProblemBody_IsAnHttpRequestException(HttpStatusCode status)
    {
        var handler = new BodyRecordingHandler(_ => new HttpResponseMessage(status));

        var act = () => Sut(handler).DispatchAsync("ord-1", [], Principal, CancellationToken.None);

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(status);
    }

    [Fact]
    public async Task AProblemWithACodeThisClientDoesNotKnow_IsAnHttpRequestException()
    {
        var handler = Failing(HttpStatusCode.Conflict, "something.new");

        var act = () => Sut(handler).PreviewAsync("ord-1", 1, 50, Principal, CancellationToken.None);

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.Conflict);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ASuccessWithAnEmptyBody_IsAJsonException()
    {
        var handler = new BodyRecordingHandler(_ => BodyRecordingHandler.Json(HttpStatusCode.OK, "null"));

        var act = () => Sut(handler).DispatchAsync("ord-1", [], Principal, CancellationToken.None);

        await act.Should().ThrowAsync<System.Text.Json.JsonException>();
        handler.Requests.Should().ContainSingle();
    }

    // ── Codes ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ProblemCodes_MapEveryBlockReasonToItsCodeAndBack_AndNothingElse()
    {
        foreach (var reason in Enum.GetValues<SharedUseDispatchBlockReason>())
            SharedUseProblemCodes.ReasonOf(SharedUseProblemCodes.For(reason)).Should().Be(reason);

        Enum.GetValues<SharedUseDispatchBlockReason>().Select(SharedUseProblemCodes.For).Should().OnlyHaveUniqueItems();
        SharedUseProblemCodes.ReasonOf(SharedUseProblemCodes.NoFanOut).Should().BeNull();
        SharedUseProblemCodes.ReasonOf("orders.not-found").Should().BeNull();
        SharedUseProblemCodes.ReasonOf(null).Should().BeNull();
        ((Action)(() => SharedUseProblemCodes.For((SharedUseDispatchBlockReason)99))).Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── Registration ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AddWorkflowRest_RegistersOneClientBehindBothPorts_OnTheNamedGeoAssetsServerClient()
    {
        var handler = Answering(PreviewJson);
        var services = new ServiceCollection();
        services.AddHttpClient("GeoAssetsServer").ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddWorkflowRest("http://test/api/workflow");
        await using var provider = services.BuildServiceProvider();

        var plans = provider.GetRequiredService<ISharedUseDispatchPlanReader>();
        await plans.PreviewAsync("ord-1", 1, 50, Principal, CancellationToken.None);

        plans.Should().BeOfType<RestSharedUseDispatchClient>().And.BeSameAs(provider.GetRequiredService<ISharedUseDispatchService>());
        handler.Requests.Should().ContainSingle().Which.PathAndQuery
            .Should().Be("/api/workflow/service-orders/ord-1/shared-use/dispatch-plan?page=1&pageSize=50");
    }
}
