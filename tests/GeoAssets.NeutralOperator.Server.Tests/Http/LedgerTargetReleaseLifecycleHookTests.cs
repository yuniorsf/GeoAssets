using FluentAssertions;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.Modules;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Http;

/// <summary>
/// The kernel's half of an order's status change (XD01-382): with a broker, <c>OrderStateChangedV1</c> is staged for every
/// transition and a failed release is only logged; without one, the ledger call is the only release, so its failure fails
/// (and rolls back) the transition.
/// </summary>
public sealed class LedgerTargetReleaseLifecycleHookTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly OrderType PoleWork = SharedUseOrderTypes.CreatePoleWork();

    private static OrderTransitionContext Transition(string from, string to, Guid? organization = null) =>
        new(
            new ServiceOrder { Id = "ord-1", OrderTypeId = PoleWork.Id, OrganizationId = organization ?? TestIdentities.OwnerOrganization, Status = to },
            PoleWork, from, to, At);

    private static LedgerTargetReleaseLifecycleHook Hook(FakeTargetLedgerClient ledger, RecordingOutboxWriter outbox, bool broker) =>
        new(ledger, outbox, new NeutralOperatorMessaging(broker), NullLogger<LedgerTargetReleaseLifecycleHook>.Instance);

    [Fact]
    public async Task WithoutABroker_ATerminalTransition_ReleasesTheOrdersTargetsInTheLedger_AndStagesNothing()
    {
        var ledger = new FakeTargetLedgerClient();
        var outbox = new RecordingOutboxWriter();

        await Hook(ledger, outbox, broker: false).OnTransitionedAsync(Transition("Draft", "Cancelled"), CancellationToken.None);

        var release = ledger.Releases.Should().ContainSingle().Subject;
        release.Key.Should().Be(new TargetLedgerKey("neutral-operator", "ord-1"));
        release.OrganizationId.Should().Be(TestIdentities.OwnerOrganization);
        release.TargetIds.Should().BeNull("the whole order is released");
        release.Actor.Should().Be("neutral-operator");
        outbox.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutABroker_ANonTerminalTransition_CallsNothing()
    {
        var ledger = new FakeTargetLedgerClient();
        var outbox = new RecordingOutboxWriter();

        await Hook(ledger, outbox, broker: false).OnTransitionedAsync(Transition("Draft", "Pending"), CancellationToken.None);

        ledger.Releases.Should().BeEmpty();
        outbox.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutABroker_AnOrderWithoutAnOrganization_NeverReleases_AsTheLedgerRequiresOne()
    {
        var ledger = new FakeTargetLedgerClient();

        await Hook(ledger, new RecordingOutboxWriter(), broker: false)
            .OnTransitionedAsync(Transition("Draft", "Cancelled", organization: Guid.Empty), CancellationToken.None);

        ledger.Releases.Should().BeEmpty();
    }

    [Fact]
    public Task WithoutABroker_AFailedRelease_FailsTheTransition_SoItRollsBack()
    {
        var ledger = new FakeTargetLedgerClient { ReleaseFailure = new TargetLedgerUnavailableException("down") };

        var act = async () => await Hook(ledger, new RecordingOutboxWriter(), broker: false)
            .OnTransitionedAsync(Transition("Draft", "Cancelled"), CancellationToken.None);

        return act.Should().ThrowAsync<TargetLedgerUnavailableException>();
    }

    [Theory]
    [InlineData("Draft", "Pending", false)]
    [InlineData("Draft", "Cancelled", true)]
    [InlineData("Pending", "InProgress", false)]
    [InlineData("InProgress", "Completed", false)]
    [InlineData("Completed", "Approved", true)]
    public async Task WithABroker_EveryTransition_StagesOrderStateChanged_WithTheVerticalTheTerminalFlagAndTheOrganization(
        string from, string to, bool terminal)
    {
        var ledger = new FakeTargetLedgerClient();
        var outbox = new RecordingOutboxWriter();

        await Hook(ledger, outbox, broker: true).OnTransitionedAsync(Transition(from, to), CancellationToken.None);

        var staged = outbox.Messages.Should().ContainSingle().Subject;
        staged.MessageType.Should().Be(GeoAssets.Core.Messaging.Events.IntegrationEventTypes.OrderStateChangedV1);
        staged.PartitionKey.Should().Be("neutral-operator/ord-1");
        staged.Message.Should().Be(new OrderStateChangedEvent(
            "ord-1", PoleWork.Id, from, to, "neutral-operator", At,
            VerticalId: "neutral-operator", IsTerminal: terminal, OrganizationId: TestIdentities.OwnerOrganization));
        ledger.Releases.Should().HaveCount(terminal ? 1 : 0);
    }

    [Fact]
    public async Task WithABroker_AFailedRelease_IsOnlyLogged_BecauseTheStagedEventReleasesLater()
    {
        var ledger = new FakeTargetLedgerClient { ReleaseFailure = new TargetLedgerUnavailableException("down") };
        var outbox = new RecordingOutboxWriter();

        await Hook(ledger, outbox, broker: true).OnTransitionedAsync(Transition("Draft", "Cancelled"), CancellationToken.None);

        outbox.Messages.Should().ContainSingle().Which.Message.Should().BeOfType<OrderStateChangedEvent>().Which.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public Task WithABroker_AReleaseTheLedgerRefusesForAnotherReason_StillFailsTheTransition()
    {
        var ledger = new FakeTargetLedgerClient { ReleaseFailure = new InvalidOperationException("a bug") };

        var act = async () => await Hook(ledger, new RecordingOutboxWriter(), broker: true)
            .OnTransitionedAsync(Transition("Draft", "Cancelled"), CancellationToken.None);

        return act.Should().ThrowAsync<InvalidOperationException>();
    }
}
