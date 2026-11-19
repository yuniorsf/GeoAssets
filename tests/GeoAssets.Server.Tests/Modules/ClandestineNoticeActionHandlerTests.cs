using System.Net;
using FluentAssertions;
using GeoAssets.Server.Modules.NeutralOperator;
using GeoAssets.Workflow.Isp.NeutralOperator;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Xunit;

namespace GeoAssets.Server.Tests.Modules;

/// <summary>
/// <see cref="ClandestineNoticeActionHandler"/> (XD01-364) against a fake <see cref="IClandestineAttachmentService"/>:
/// which actions it claims, where each takes a notice, what it calls on the service as the caller, and every refusal it
/// answers. <c>TargetStatus</c> and <c>FromException</c> are internal to the server, so each of their rows is reached
/// through <see cref="ClandestineNoticeActionHandler.HandleAsync"/>.
/// </summary>
public sealed class ClandestineNoticeActionHandlerTests
{
    private const string OrderId = "notice-1";
    private const string UserId = "user-7";

    private static readonly DateTime At = new(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc);
    private static readonly OrderType Notice = ClandestineAttachmentOrderTypes.CreateClandestineNotice();

    private sealed class FakeNoticeService : IClandestineAttachmentService
    {
        public List<string> Calls { get; } = [];

        public Exception? Throws { get; init; }

        private Task<ClandestineFlagResult> Record(string call, string? performedBy)
        {
            Calls.Add($"{call}|{performedBy}");
            return Throws is null
                ? Task.FromResult(new ClandestineFlagResult(OrderId, "any", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch))
                : Task.FromException<ClandestineFlagResult>(Throws);
        }

        public Task<ClandestineFlagResult> FlagAsync(string serviceOrderId, string? performedBy = null, CancellationToken ct = default) =>
            Record("Flag", performedBy);

        public Task<ClandestineFlagResult> RegularizeAsync(string serviceOrderId, string? performedBy = null, CancellationToken ct = default) =>
            Record("Regularize", performedBy);

        public Task<ClandestineFlagResult> DisputeAsync(string serviceOrderId, string evidenceReference, string? performedBy = null, CancellationToken ct = default) =>
            Record($"Dispute:{evidenceReference}", performedBy);

        public Task<ClandestineFlagResult> ResolveDisputeAsync(string serviceOrderId, bool upheld, string? performedBy = null, CancellationToken ct = default) =>
            Record($"Resolve:{upheld}", performedBy);

        public Task<IReadOnlyList<ClandestineFlagResult>> GetElapsedUnregularizedAsync(DateTimeOffset asOfUtc, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ClandestineDisputeResult>> GetOverdueDisputesAsync(DateTimeOffset asOfUtc, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<IServiceOrder>> GetActiveFlaggedForOrganizationAsync(Guid organizationId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static OrderActionLog Entry(OrderActionType action, string? comment = null, string? resultingStatus = null) =>
        new(action, "whoever-the-body-says", At, comment, resultingStatus);

    private static OrderActionContext Context(string status, OrderActionLog entry) => new(
        new ServiceOrder { Id = OrderId, OrderTypeId = ClandestineAttachmentOrderTypes.ClandestineNoticeId, Status = status },
        Notice,
        entry,
        new WorkflowPrincipal(UserId, Guid.NewGuid().ToString(), [], [], []));

    // ── CanHandle ─────────────────────────────────────────────────────────────

    [Fact]
    public void CanHandle_NullEntry_Throws()
    {
        var act = () => new ClandestineNoticeActionHandler(new FakeNoticeService()).CanHandle(Notice, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(OrderActionType.FlagClandestine, true)]
    [InlineData(OrderActionType.Dispute, true)]
    [InlineData(OrderActionType.Approve, true)]
    [InlineData(OrderActionType.Reject, true)]
    [InlineData(OrderActionType.Annotate, false)]
    [InlineData(OrderActionType.Cancel, false)]
    public void CanHandle_NoticeType_ClaimsOnlyTheFourClandestineActions(OrderActionType action, bool expected) =>
        new ClandestineNoticeActionHandler(new FakeNoticeService()).CanHandle(Notice, Entry(action)).Should().Be(expected);

    [Fact]
    public void CanHandle_AnotherOrderType_ClaimsNothing()
    {
        var handler = new ClandestineNoticeActionHandler(new FakeNoticeService());

        handler.CanHandle(TenantAttachmentRequestOrderTypes.CreateAttachmentRequest(), Entry(OrderActionType.Approve)).Should().BeFalse();
    }

    [Fact]
    public void CanHandle_UnknownOrderType_ClaimsNothing() =>
        new ClandestineNoticeActionHandler(new FakeNoticeService()).CanHandle(null, Entry(OrderActionType.Approve)).Should().BeFalse();

    // ── HandleAsync: where each action takes a notice, and what it calls ──────

    [Fact]
    public Task HandleAsync_NullContext_Throws()
    {
        var act = () => new ClandestineNoticeActionHandler(new FakeNoticeService()).HandleAsync(null!, CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(OrderActionType.FlagClandestine, ClandestineAttachmentWorkflowGraph.Active, null, "Flag|" + UserId)]
    [InlineData(OrderActionType.FlagClandestine, ClandestineAttachmentWorkflowGraph.Active, ClandestineAttachmentWorkflowGraph.CaboARevelia, "Flag|" + UserId)]
    [InlineData(OrderActionType.Dispute, ClandestineAttachmentWorkflowGraph.CaboARevelia, ClandestineAttachmentWorkflowGraph.Disputed, "Dispute:evidence|" + UserId)]
    [InlineData(OrderActionType.Approve, ClandestineAttachmentWorkflowGraph.CaboARevelia, ClandestineAttachmentWorkflowGraph.Regularized, "Regularize|" + UserId)]
    [InlineData(OrderActionType.Approve, ClandestineAttachmentWorkflowGraph.Disputed, ClandestineAttachmentWorkflowGraph.Regularized, "Resolve:True|" + UserId)]
    [InlineData(OrderActionType.Reject, ClandestineAttachmentWorkflowGraph.Disputed, ClandestineAttachmentWorkflowGraph.CaboARevelia, "Resolve:False|" + UserId)]
    [InlineData(OrderActionType.Reject, ClandestineAttachmentWorkflowGraph.Disputed, null, "Resolve:False|" + UserId)]
    public async Task HandleAsync_ActionAlongItsTransition_CallsTheServiceAsTheCaller_AndApplies(
        OrderActionType action, string status, string? resultingStatus, string expectedCall)
    {
        var service = new FakeNoticeService();

        var outcome = await new ClandestineNoticeActionHandler(service).HandleAsync(
            Context(status, Entry(action, "evidence", resultingStatus)), CancellationToken.None);

        outcome.Should().BeSameAs(OrderActionOutcome.Applied);
        service.Calls.Should().Equal(expectedCall);
    }

    [Theory]
    [InlineData(OrderActionType.FlagClandestine, ClandestineAttachmentWorkflowGraph.CaboARevelia)]
    [InlineData(OrderActionType.Dispute, ClandestineAttachmentWorkflowGraph.Active)]
    [InlineData(OrderActionType.Dispute, ClandestineAttachmentWorkflowGraph.Disputed)]
    [InlineData(OrderActionType.Approve, ClandestineAttachmentWorkflowGraph.Active)]
    [InlineData(OrderActionType.Reject, ClandestineAttachmentWorkflowGraph.CaboARevelia)]
    [InlineData(OrderActionType.Annotate, ClandestineAttachmentWorkflowGraph.CaboARevelia)]
    public async Task HandleAsync_ActionThatTakesTheNoticeNowhere_IsAnInvalidTransition_AndCallsNothing(
        OrderActionType action, string status)
    {
        var service = new FakeNoticeService();

        var outcome = await new ClandestineNoticeActionHandler(service).HandleAsync(
            Context(status, Entry(action, "evidence", "Anywhere")), CancellationToken.None);

        outcome.Should().BeEquivalentTo(OrderActionOutcome.InvalidTransition(status, "Anywhere"));
        service.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_ActionThatTakesTheNoticeNowhereAndRequestsNoStatus_IsAnInvalidTransitionToNothing()
    {
        var outcome = await new ClandestineNoticeActionHandler(new FakeNoticeService()).HandleAsync(
            Context(ClandestineAttachmentWorkflowGraph.Active, Entry(OrderActionType.Reject)), CancellationToken.None);

        outcome.Should().BeEquivalentTo(OrderActionOutcome.InvalidTransition(ClandestineAttachmentWorkflowGraph.Active, null));
    }

    [Fact]
    public async Task HandleAsync_ResultingStatusThatIsNotTheTarget_IsAnInvalidTransitionToTheRequestedStatus_AndCallsNothing()
    {
        var service = new FakeNoticeService();

        var outcome = await new ClandestineNoticeActionHandler(service).HandleAsync(
            Context(
                ClandestineAttachmentWorkflowGraph.CaboARevelia,
                Entry(OrderActionType.Approve, resultingStatus: ClandestineAttachmentWorkflowGraph.Disputed)),
            CancellationToken.None);

        outcome.Should().BeEquivalentTo(
            OrderActionOutcome.InvalidTransition(ClandestineAttachmentWorkflowGraph.CaboARevelia, ClandestineAttachmentWorkflowGraph.Disputed));
        service.Calls.Should().BeEmpty();
    }

    // ── Evidence ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_DisputeWithoutEvidence_Is400WithTheEvidenceRequiredCode_AndCallsNothing(string? comment)
    {
        var service = new FakeNoticeService();

        var outcome = await new ClandestineNoticeActionHandler(service).HandleAsync(
            Context(ClandestineAttachmentWorkflowGraph.CaboARevelia, Entry(OrderActionType.Dispute, comment)), CancellationToken.None);

        outcome.Kind.Should().Be(OrderActionOutcomeKind.Problem);
        outcome.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        outcome.Code.Should().Be(ClandestineProblemCodes.EvidenceRequired).And.Be("clandestine.evidence-required");
        service.Calls.Should().BeEmpty();
    }

    // ── What the service throws ───────────────────────────────────────────────

    private static Task<OrderActionOutcome> HandleFailingAsync(Exception thrown) =>
        new ClandestineNoticeActionHandler(new FakeNoticeService { Throws = thrown }).HandleAsync(
            Context(ClandestineAttachmentWorkflowGraph.CaboARevelia, Entry(OrderActionType.Approve)), CancellationToken.None);

    [Fact]
    public async Task HandleAsync_WindowElapsed_Is409WithTheWindowElapsedCode()
    {
        var elapsed = new ClandestineNoticeWindowElapsedException(OrderId, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(31));

        var outcome = await HandleFailingAsync(elapsed);

        outcome.Kind.Should().Be(OrderActionOutcomeKind.Problem);
        outcome.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        outcome.Code.Should().Be(ClandestineProblemCodes.WindowElapsed).And.Be("clandestine.window-elapsed");
        outcome.Detail.Should().Be(elapsed.Message);
    }

    [Fact]
    public async Task HandleAsync_OrderNoLongerExists_IsNotFound() =>
        (await HandleFailingAsync(new KeyNotFoundException())).Should().BeSameAs(OrderActionOutcome.NotFound);

    [Fact]
    public async Task HandleAsync_OrderChangedMeanwhile_IsAConcurrentModification() =>
        (await HandleFailingAsync(new ServiceOrderConcurrencyException(OrderId))).Should().BeSameAs(OrderActionOutcome.ConcurrentModification);

    [Fact]
    public async Task HandleAsync_IllegalTransition_IsAnInvalidTransitionBetweenTheStatusesItNames() =>
        (await HandleFailingAsync(new InvalidServiceOrderTransitionException("From", "To")))
            .Should().BeEquivalentTo(OrderActionOutcome.InvalidTransition("From", "To"));

    [Fact]
    public async Task HandleAsync_ServiceRefusesTheStatus_IsAnInvalidTransitionFromTheOrdersStatusToTheTarget() =>
        (await HandleFailingAsync(new InvalidOperationException("Cannot regularize.")))
            .Should().BeEquivalentTo(OrderActionOutcome.InvalidTransition(
                ClandestineAttachmentWorkflowGraph.CaboARevelia, ClandestineAttachmentWorkflowGraph.Regularized));

    [Fact]
    public Task HandleAsync_UnmappedException_Propagates()
    {
        var act = () => HandleFailingAsync(new FormatException("not a client-facing failure"));

        return act.Should().ThrowAsync<FormatException>();
    }
}
