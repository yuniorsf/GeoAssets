using System.Net;
using FluentAssertions;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Http;

/// <summary>
/// Applying a selection (XD01-382, AC2): the kernel's ledger reserves the targets, then the host mirrors what the ledger holds
/// into its own <c>ServiceOrderTargets</c> under the ledger's target ids, idempotently, and records the selection on the order.
/// A conflict is passed through; an unreachable ledger is a 503.
/// </summary>
public sealed class TargetSelectionApplyTests
{
    private const string Order = "no-apply-1";

    [Fact]
    public async Task Apply_ReservesInTheLedgerFirst_ThenMirrorsItsUsagesIntoTheHostsTargets_UnderTheLedgersTargetIds()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var owner = host.ClientFor("owner-admin");
        await owner.CreateOrderAsync(Order);

        var result = await (await owner.ApplyAsync(Order, "pole-1", "pole-2")).ReadJsonAsync();
        var targets = await (await owner.GetTargetsAsync(Order)).ReadJsonAsync();

        result.GetProperty("added").GetInt32().Should().Be(2);
        var ledgerCalls = host.Kernel.Requests.Where(r => r.Path.StartsWith("/api/targets/ledger/", StringComparison.Ordinal)).ToList();
        ledgerCalls.Select(r => $"{r.Method} {r.Path.Split('/')[^1]}").Should().Equal("POST reserve", "GET usages");
        ledgerCalls[0].Json.GetProperty("preview").GetBoolean().Should().BeFalse();
        ledgerCalls[0].Json.GetProperty("organizationId").GetGuid().Should().Be(TestIdentities.OwnerOrganization);
        ledgerCalls[1].Query.Should().Contain($"organizationId={TestIdentities.OwnerOrganization:D}").And.Contain("skip=0").And.Contain("take=1000");

        var expectedIds = new[] { "pole-1", "pole-2" }.Select(f => TargetLedger.TargetId("neutral-operator", Order, f)).ToList();
        targets.GetProperty("totalCount").GetInt32().Should().Be(2);
        targets.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("id").GetString()).Should().BeEquivalentTo(expectedIds);
        host.Kernel.LedgerOf(Order).Select(e => e.Target.Id).Should().BeEquivalentTo(expectedIds, "the local ids are the ledger's");
        (await DatabaseProbe.ValuesAsync(host.ConnectionString, $"""SELECT ("Id" || '|' || "VerticalId" || '|' || "ServiceOrderId" || '|' || "OrganizationId")::text AS "Value" FROM "ServiceOrderTargets" ORDER BY "FeatureId" """))
            .Should().Equal(expectedIds.Select(id => $"{id}|kernel|{Order}|{TestIdentities.OwnerOrganization}"));
    }

    [Fact]
    public async Task Apply_RecordsTheSelectionOnTheOrder_AndAnAnnotateEntryInItsActionLog()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var owner = host.ClientFor("owner-admin");
        await owner.CreateOrderAsync(Order);

        await owner.ApplyAsync(Order, "pole-1");
        var order = await (await owner.GetOrderAsync(Order)).ReadJsonAsync();

        order.GetProperty("selectionSpec").GetProperty("strategyId").GetString().Should().Be(NeutralOperatorApi.Strategy);
        order.GetProperty("actionLog").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("comment").GetString().Should().Contain("Target selection 'identifier-list'").And.Contain("added 1");
    }

    [Fact]
    public async Task Apply_Twice_ReservesNothingNewAndMirrorsNothingTwice()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var owner = host.ClientFor("owner-admin");
        await owner.CreateOrderAsync(Order);
        await owner.ApplyAsync(Order, "pole-1", "pole-2");

        var again = await (await owner.ApplyAsync(Order, "pole-1", "pole-2")).ReadJsonAsync();

        again.GetProperty("added").GetInt32().Should().Be(0);
        again.GetProperty("alreadyInOrder").GetInt32().Should().Be(2);
        (await DatabaseProbe.ValueAsync(host.ConnectionString, """SELECT count(*)::text AS "Value" FROM "ServiceOrderTargets" """)).Should().Be("2");
        host.Kernel.LedgerOf(Order).Should().HaveCount(2);
    }

    [Fact]
    public async Task Apply_AddingToAnExistingSelection_MirrorsTheNewTargetsOnly()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var owner = host.ClientFor("owner-admin");
        await owner.CreateOrderAsync(Order);
        await owner.ApplyAsync(Order, "pole-1");

        await (await owner.ApplyAsync(Order, "pole-1", "pole-2")).ReadJsonAsync();

        (await DatabaseProbe.ValuesAsync(host.ConnectionString, """SELECT "FeatureId" AS "Value" FROM "ServiceOrderTargets" ORDER BY "FeatureId" """))
            .Should().Equal("pole-1", "pole-2");
    }

    [Fact]
    public async Task Apply_WithMoreTargetsThanOneUsagesPage_MirrorsEveryPage()
    {
        const int Poles = 1_001;
        await using var host = await NeutralOperatorHost.StartAsync();
        using var owner = host.ClientFor("owner-admin");
        await owner.CreateOrderAsync(Order);

        await (await owner.ApplyAsync(Order, [.. Enumerable.Range(1, Poles).Select(i => $"pole-{i:0000}")])).ReadJsonAsync();

        (await DatabaseProbe.ValueAsync(host.ConnectionString, """SELECT count(*)::text AS "Value" FROM "ServiceOrderTargets" """)).Should().Be("1001");
        host.Kernel.To(HttpMethod.Get, "/usages").Select(r => r.Query).Should().HaveCount(2)
            .And.Contain(q => q.Contains("skip=0", StringComparison.Ordinal))
            .And.Contain(q => q.Contains("skip=1000", StringComparison.Ordinal));
    }

    // PRODUCTION DEFECT (XD01-382 2B finding): the chassis' WorkflowProblemResults.FromException translates
    // TargetLedgerUnavailableException (503) but not TargetLedgerConflictException, so a ledger conflict escapes the apply route
    // as an unhandled exception (a 500) instead of the 409 targets.conflict the ledger answered. Un-skip once the chassis maps it.
    [Fact]
    public async Task Apply_WhenTheLedgerAnswers409_PassesTheConflictThrough_AndMirrorsNothing()
    {
        await using var host = await NeutralOperatorHost.StartAsync(kernel => kernel.ReserveFault = KernelFault.Conflict);
        using var owner = host.ClientFor("owner-admin");
        await owner.CreateOrderAsync(Order);

        var problem = await (await owner.ApplyAsync(Order, "pole-1")).ShouldBeProblemAsync(HttpStatusCode.Conflict, TargetLedger.ConflictProblemCode);

        problem.GetProperty("conflicts").EnumerateArray().Should().ContainSingle().Which.GetProperty("featureId").GetString().Should().Be("pole-1");
        (await DatabaseProbe.ValueAsync(host.ConnectionString, """SELECT count(*)::text AS "Value" FROM "ServiceOrderTargets" """)).Should().Be("0");
        (await (await owner.GetOrderAsync(Order)).ReadJsonAsync()).GetProperty("actionLog").GetArrayLength().Should().Be(0, "a refused selection is not recorded");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Apply_WhenTheLedgerIsDownOrUnreachable_Is503LedgerUnavailable_AndWritesNothing(bool unreachable)
    {
        await using var host = await NeutralOperatorHost.StartAsync(
            kernel => kernel.ReserveFault = unreachable ? KernelFault.Unreachable : KernelFault.ServerError);
        using var owner = host.ClientFor("owner-admin");
        await owner.CreateOrderAsync(Order);

        await (await owner.ApplyAsync(Order, "pole-1")).ShouldBeProblemAsync(HttpStatusCode.ServiceUnavailable, WorkflowProblemCodes.TargetLedgerUnavailable);

        (await DatabaseProbe.ValueAsync(host.ConnectionString, """SELECT count(*)::text AS "Value" FROM "ServiceOrderTargets" """)).Should().Be("0");
        (await owner.StatusOfAsync(Order)).Should().Be("Draft");
    }

    [Fact]
    public async Task Apply_OnACancelledOrder_IsRefused_AndNeverReachesTheLedger()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var owner = host.ClientFor("owner-admin");
        await owner.CreateOrderAsync(Order);
        await owner.CancelAsync(Order);

        var response = await owner.ApplyAsync(Order, "pole-1");

        response.IsSuccessStatusCode.Should().BeFalse();
        host.Kernel.To(HttpMethod.Post, "/reserve").Should().BeEmpty();
    }

    [Fact]
    public async Task Apply_ByACallerWithoutTheTargetWriteRight_IsDenied_AndNeverReachesTheLedger()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var owner = host.ClientFor("owner-admin");
        using var reader = host.ClientFor("owner-reader");
        await owner.CreateOrderAsync(Order);

        var response = await reader.ApplyAsync(Order, "pole-1");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        host.Kernel.To(HttpMethod.Post, "/reserve").Should().BeEmpty();
    }
}
