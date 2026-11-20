using System.Net;
using FluentAssertions;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Http;

/// <summary>
/// An order's terminal transition (XD01-382, AC2): the kernel's ledger releases its targets exactly once, in the transition's own
/// transaction. This host has no broker, so the ledger call is the only release — a kernel that cannot be reached rolls the
/// whole transition back, and the caller retries.
/// </summary>
public sealed class OrderClosureTests
{
    private const string Order = "no-closure-1";

    [Fact]
    public async Task Cancel_ReleasesTheOrdersTargetsInTheLedgerOnce_AndInTheHostsOwnTargetsToo()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var owner = host.ClientFor("owner-admin");
        await owner.CreateOrderAsync(Order);
        await owner.ApplyAsync(Order, "pole-1", "pole-2");

        var response = await owner.CancelAsync(Order);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.StatusOfAsync(Order)).Should().Be("Cancelled");
        var release = host.Kernel.To(HttpMethod.Post, "/release").Should().ContainSingle().Subject;
        release.Path.Should().Be($"/api/targets/ledger/neutral-operator/{Order}/release");
        release.Json.GetProperty("organizationId").GetGuid().Should().Be(TestIdentities.OwnerOrganization);
        release.Json.GetProperty("actor").GetString().Should().Be("neutral-operator");
        (release.Json.TryGetProperty("targetIds", out var ids) ? ids.ValueKind : System.Text.Json.JsonValueKind.Null)
            .Should().Be(System.Text.Json.JsonValueKind.Null, "the whole order is released, not chosen targets");
        release.Headers.Keys.Should().NotContain(name => name.Contains("organization", StringComparison.OrdinalIgnoreCase));
        host.Kernel.LedgerOf(Order).Should().OnlyContain(entry => entry.ReleasedAt != null).And.HaveCount(2);
        (await DatabaseProbe.ValueAsync(host.ConnectionString, """SELECT count(*)::text AS "Value" FROM "ServiceOrderTargets" WHERE "ReleasedAt" IS NOT NULL""")).Should().Be("2");
    }

    [Fact]
    public async Task Cancel_OfAnOrderTheLedgerHoldsNothingOf_StillClosesIt_AsTheLedgersAnswerIsNothingToRelease()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var owner = host.ClientFor("owner-admin");
        await owner.CreateOrderAsync(Order);

        var response = await owner.CancelAsync(Order);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.StatusOfAsync(Order)).Should().Be("Cancelled");
        host.Kernel.To(HttpMethod.Post, "/release").Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_WhenTheLedgerCannotReleaseAndThereIsNoBroker_RollsTheTransitionBack_ThenSucceedsOnceTheKernelIsBack(bool unreachable)
    {
        await using var host = await NeutralOperatorHost.StartAsync(
            kernel => kernel.ReleaseFault = unreachable ? KernelFault.Unreachable : KernelFault.ServerError);
        using var owner = host.ClientFor("owner-admin");
        await owner.CreateOrderAsync(Order);
        await owner.ApplyAsync(Order, "pole-1", "pole-2");

        var failed = await Attempt.OfAsync(() => owner.CancelAsync(Order));

        failed.Succeeded.Should().BeFalse("the release is the only one this host guarantees");
        failed.Failure.Should().BeNull("a ledger outage on the closing transition is answered, not thrown");
        failed.Status.Should().Be(HttpStatusCode.ServiceUnavailable, "the ledger being down is a retryable 503 targets.ledger-unavailable");
        (await owner.StatusOfAsync(Order)).Should().Be("Draft", "the transition rolled back with the failed release");
        host.Kernel.LedgerOf(Order).Should().OnlyContain(entry => entry.ReleasedAt == null);
        (await DatabaseProbe.ValueAsync(host.ConnectionString, """SELECT count(*)::text AS "Value" FROM "ServiceOrderTargets" WHERE "ReleasedAt" IS NULL""")).Should().Be("2");
        (await DatabaseProbe.ValueAsync(host.ConnectionString, $"""SELECT count(*)::text AS "Value" FROM "OrderActionLogs" WHERE "ServiceOrderId" = '{Order}' AND "ResultingStatus" IS NOT NULL""")).Should().Be("0");

        host.Kernel.ReleaseFault = KernelFault.None;
        var retried = await owner.CancelAsync(Order);

        retried.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.StatusOfAsync(Order)).Should().Be("Cancelled");
        host.Kernel.LedgerOf(Order).Should().OnlyContain(entry => entry.ReleasedAt != null);
    }
}
