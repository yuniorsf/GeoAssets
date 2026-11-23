using FluentAssertions;
using GeoAssets.Identity.Authorization.Modules;
using GeoAssets.NeutralOperator.Modules;
using GeoAssets.Workflow.Hosting;
using GeoAssets.Workflow.Registry;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Registration;

/// <summary>
/// The bootstrap registration loop (XD01-382, AC2) on a fake clock: order types before permissions, retried with a growing,
/// capped, jittered backoff while the kernel fails, republished on a schedule once it knows the vertical, and stopped by cancellation.
/// </summary>
public sealed class VerticalRegistrationServiceTests
{
    private static readonly VerticalRegistrationOptions Production = new();

    [Fact]
    public async Task FirstPass_PublishesTheOrderTypesBeforeThePermissions_ThenIsRegistered()
    {
        await using var loop = new RegistrationLoop();

        await loop.StartAsync();
        await RegistrationLoop.UntilAsync(() => loop.State.Current.Status == VerticalRegistrationStatus.Registered);

        loop.Client.Kinds.Should().Equal(FakeVerticalRegistrationClient.OrderTypes, FakeVerticalRegistrationClient.Permissions);
        loop.Client.Calls.Should().OnlyContain(call => call.VerticalId == NeutralOperatorModule.ModuleId);
        loop.State.Current.RegisteredAt.Should().Be(loop.Time.GetUtcNow());
        loop.State.Current.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task FirstPass_PublishesTheVerticalsDescriptors_AndOnlyItsOwnPermissionContributors()
    {
        var foreign = new ForeignContributor();
        await using var loop = new RegistrationLoop(extraContributors: [foreign]);

        await loop.StartAsync();
        await RegistrationLoop.UntilAsync(() => loop.State.Current.Status == VerticalRegistrationStatus.Registered);

        var descriptors = (IReadOnlyList<OrderTypeDescriptor>)loop.Client.Calls[0].Payload;
        descriptors.Select(d => d.OrderTypeId).Should().BeEquivalentTo(NeutralOperatorModule.PublishedDescriptors(loop.OrderTypes).Select(d => d.OrderTypeId));
        var permissions = (ModulePermissionSet)loop.Client.Calls[1].Payload;
        permissions.RoleBundles.Should().ContainSingle().Which.RoleName.Should().Be("IspTenant");
        permissions.Permissions.Should().BeEmpty("the foreign contributor's permissions are not this vertical's to publish");
    }

    [Fact]
    public async Task BeforeTheFirstAttemptCompletes_TheStateIsPending_AndAnUnreachableKernelMakesItFailing()
    {
        await using var loop = new RegistrationLoop();
        loop.State.Current.Status.Should().Be(VerticalRegistrationStatus.Pending);
        loop.Client.Fails = _ => true;

        await loop.StartAsync();
        await RegistrationLoop.UntilAsync(() => loop.State.Current.Status == VerticalRegistrationStatus.Failing);

        loop.State.Current.ConsecutiveFailures.Should().Be(1);
        loop.State.Current.LastError.Should().Contain("order-types");
        loop.State.Current.RegisteredAt.Should().BeNull();
    }

    [Fact]
    public async Task WhenTheOrderTypesAreRefused_ThePermissionsAreNotPublished()
    {
        await using var loop = new RegistrationLoop();
        loop.Client.Fails = kind => string.Equals(kind, FakeVerticalRegistrationClient.OrderTypes, StringComparison.Ordinal);

        await loop.StartAsync();
        await RegistrationLoop.UntilAsync(() => loop.State.Current.Status == VerticalRegistrationStatus.Failing);

        loop.Client.Kinds.Should().Equal(FakeVerticalRegistrationClient.OrderTypes);
    }

    [Fact]
    public async Task WhenThePermissionsAreRefused_TheHostIsNotRegisteredYet_AndTheWholePassIsRepeated()
    {
        await using var loop = new RegistrationLoop();
        loop.Client.Fails = kind => string.Equals(kind, FakeVerticalRegistrationClient.Permissions, StringComparison.Ordinal);
        await loop.StartAsync();
        await RegistrationLoop.UntilAsync(() => loop.State.Current.Status == VerticalRegistrationStatus.Failing);
        loop.State.Current.RegisteredAt.Should().BeNull("registered means both were accepted");

        loop.Client.Fails = _ => false;
        await loop.Time.AdvanceWhenScheduledAsync(Production.MaxDelay);
        await RegistrationLoop.UntilAsync(() => loop.State.Current.Status == VerticalRegistrationStatus.Registered);

        loop.Client.Kinds.Should().Equal(
            FakeVerticalRegistrationClient.OrderTypes, FakeVerticalRegistrationClient.Permissions,
            FakeVerticalRegistrationClient.OrderTypes, FakeVerticalRegistrationClient.Permissions);
    }

    [Fact]
    public async Task Failures_AreRetriedWithGrowingDelays_ThatNeverExceedTheCap()
    {
        await using var loop = new RegistrationLoop();
        loop.Client.Fails = _ => true;
        await loop.StartAsync();
        await RegistrationLoop.UntilAsync(() => loop.State.Current.ConsecutiveFailures == 1);

        // The delay after the n-th failure is between half and all of min(1s * 2^(n-1), 60s): advancing by less than the
        // lower bound never retries, advancing to the upper bound always does.
        for (var failure = 1; failure <= 9; failure++)
        {
            var cap = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, failure - 1), Production.MaxDelay.TotalSeconds));
            var attempts = loop.Client.Calls.Count;

            await loop.Time.AdvanceWhenScheduledAsync(cap / 2 - TimeSpan.FromMilliseconds(10));
            (await loop.SettledCallsAsync()).Should().Be(attempts, $"after failure {failure} the retry waits at least {cap / 2}");

            await loop.Time.AdvanceWhenScheduledAsync(cap / 2 + TimeSpan.FromMilliseconds(10));
            await RegistrationLoop.UntilAsync(() => loop.State.Current.ConsecutiveFailures == failure + 1);
        }

        loop.State.Current.ConsecutiveFailures.Should().Be(10);
    }

    [Fact]
    public void Backoff_IsHalfToAllOfTheDoublingDelay_CappedAtTheMaximum()
    {
        foreach (var failures in Enumerable.Range(1, 12))
        {
            var cap = Math.Min(Production.InitialDelay.TotalMilliseconds * Math.Pow(2, failures - 1), Production.MaxDelay.TotalMilliseconds);
            for (var sample = 0; sample < 100; sample++)
            {
                VerticalRegistrationService.Backoff(Production, failures).TotalMilliseconds
                    .Should().BeInRange(cap * 0.5, cap, $"failure {failures}");
            }
        }
    }

    [Fact]
    public void Backoff_ForAVeryLongRunOfFailures_StaysAtTheCap_WithoutOverflowing()
    {
        VerticalRegistrationService.Backoff(Production, 10_000).TotalSeconds.Should().BeInRange(30, 60);
        VerticalRegistrationService.Backoff(Production, 0).TotalSeconds.Should().BeInRange(0.5, 1);
    }

    [Fact]
    public void Backoff_Jitter_DoesNotRetryEveryHostInStep()
    {
        var delays = Enumerable.Range(0, 200).Select(_ => VerticalRegistrationService.Backoff(Production, 3)).Distinct().ToList();

        delays.Count.Should().BeGreaterThan(20, "two hosts restarted together must not retry on the same instants");
    }

    [Fact]
    public async Task OnceRegistered_TheRegistrationIsRepublishedEveryRefreshInterval()
    {
        await using var loop = new RegistrationLoop();
        await loop.StartAsync();
        await RegistrationLoop.UntilAsync(() => loop.State.Current.Status == VerticalRegistrationStatus.Registered);

        await loop.Time.AdvanceWhenScheduledAsync(Production.RefreshInterval - TimeSpan.FromSeconds(1));
        (await loop.SettledCallsAsync()).Should().Be(2, "nothing is republished before the refresh interval has passed");

        await loop.Time.AdvanceWhenScheduledAsync(TimeSpan.FromSeconds(1));
        await RegistrationLoop.UntilAsync(() => loop.Client.Calls.Count == 4);
        loop.Client.Kinds.Should().Equal(
            FakeVerticalRegistrationClient.OrderTypes, FakeVerticalRegistrationClient.Permissions,
            FakeVerticalRegistrationClient.OrderTypes, FakeVerticalRegistrationClient.Permissions);
        loop.State.Current.RegisteredAt.Should().Be(loop.Time.GetUtcNow(), "the registration time follows each republication");

        await loop.Time.AdvanceWhenScheduledAsync(Production.RefreshInterval);
        await RegistrationLoop.UntilAsync(() => loop.Client.Calls.Count == 6);
    }

    [Fact]
    public async Task ARefreshTheKernelRefuses_MakesTheHostNotReadyAgain_AndIsRetriedWithBackoff_UntilItIsRegisteredAgain()
    {
        await using var loop = new RegistrationLoop();
        await loop.StartAsync();
        await RegistrationLoop.UntilAsync(() => loop.State.Current.Status == VerticalRegistrationStatus.Registered);
        loop.Client.Fails = _ => true;

        await loop.Time.AdvanceWhenScheduledAsync(Production.RefreshInterval);
        await RegistrationLoop.UntilAsync(() => loop.State.Current.Status == VerticalRegistrationStatus.Failing);
        loop.Client.Fails = _ => false;
        await loop.Time.AdvanceWhenScheduledAsync(Production.InitialDelay);
        await RegistrationLoop.UntilAsync(() => loop.State.Current.Status == VerticalRegistrationStatus.Registered);

        loop.State.Current.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task Cancellation_StopsTheLoop_WhetherItIsWaitingToRetryOrToRefresh()
    {
        await using var failing = new RegistrationLoop();
        failing.Client.Fails = _ => true;
        await failing.StartAsync();
        await RegistrationLoop.UntilAsync(() => failing.State.Current.ConsecutiveFailures == 1);
        await using var healthy = new RegistrationLoop();
        await healthy.StartAsync();
        await RegistrationLoop.UntilAsync(() => healthy.State.Current.Status == VerticalRegistrationStatus.Registered);

        await failing.StopAsync();
        await healthy.StopAsync();
        await failing.Time.AdvanceWhenScheduledAsync(TimeSpan.FromHours(1));
        await healthy.Time.AdvanceWhenScheduledAsync(TimeSpan.FromHours(1));

        (await failing.SettledCallsAsync()).Should().Be(1, "a stopped loop makes no further attempt");
        (await healthy.SettledCallsAsync()).Should().Be(2, "a stopped loop republishes nothing");
    }

    [Fact]
    public async Task Cancellation_AfterAFailedAttempt_KeepsTheRecordedFailureAsItWas()
    {
        await using var loop = new RegistrationLoop();
        loop.Client.Fails = _ => true;
        await loop.StartAsync();
        await RegistrationLoop.UntilAsync(() => loop.State.Current.ConsecutiveFailures == 1);

        await loop.StopAsync();

        loop.State.Current.ConsecutiveFailures.Should().Be(1);
    }

    [Fact]
    public async Task CustomOptions_SetTheRetryAndRefreshSchedule()
    {
        var options = new VerticalRegistrationOptions
        {
            InitialDelay = TimeSpan.FromSeconds(10),
            MaxDelay = TimeSpan.FromSeconds(20),
            RefreshInterval = TimeSpan.FromMinutes(1),
        };
        await using var loop = new RegistrationLoop(options);
        loop.Client.Fails = _ => true;
        await loop.StartAsync();
        await RegistrationLoop.UntilAsync(() => loop.State.Current.ConsecutiveFailures == 1);

        await loop.Time.AdvanceWhenScheduledAsync(TimeSpan.FromSeconds(4));
        (await loop.SettledCallsAsync()).Should().Be(1, "the first retry waits at least half of the 10 s initial delay");
        await loop.Time.AdvanceWhenScheduledAsync(TimeSpan.FromSeconds(6));
        await RegistrationLoop.UntilAsync(() => loop.State.Current.ConsecutiveFailures == 2);

        loop.Client.Fails = _ => false;
        await loop.Time.AdvanceWhenScheduledAsync(options.MaxDelay);
        await RegistrationLoop.UntilAsync(() => loop.State.Current.Status == VerticalRegistrationStatus.Registered);
        var calls = loop.Client.Calls.Count;
        await loop.Time.AdvanceWhenScheduledAsync(options.RefreshInterval);
        await RegistrationLoop.UntilAsync(() => loop.Client.Calls.Count == calls + 2);
    }

    private sealed class ForeignContributor : IModulePermissionContributor
    {
        public string VerticalId => "field-service";

        public IEnumerable<PermissionDefinition> Permissions { get; } = [new("field:read", "field", "read", "permissions.field.read")];

        public IEnumerable<RoleBundleContribution> RoleBundles { get; } = [];
    }
}
