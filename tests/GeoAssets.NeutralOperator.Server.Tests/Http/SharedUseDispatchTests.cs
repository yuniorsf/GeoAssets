using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Http;

/// <summary>
/// The shared-use fan-out (XD01-382, AC2) end to end in this host: targets reserved in the kernel's ledger and mirrored here,
/// the occupancy of those poles in the vertical's own tables, and a dispatch that creates one child order per partner
/// organization in this host's own database — without asking the kernel for anything but the ledger and the caller's identity.
/// </summary>
public sealed class SharedUseDispatchTests
{
    private const string Order = "no-dispatch-1";

    private static async Task<(NeutralOperatorHost Host, HttpClient Owner)> StartWithPolesAsync()
    {
        var host = await NeutralOperatorHost.StartAsync();
        var owner = host.ClientFor("owner-admin");
        await owner.CreateOrderAsync(Order);
        await owner.ApplyAsync(Order, "pole-1", "pole-2");
        await OccupancySeed.OccupyAsync(host.ConnectionString, TestIdentities.OwnerOrganization, "pole-1", TestIdentities.OccupantOne);
        await OccupancySeed.OccupyAsync(host.ConnectionString, TestIdentities.OwnerOrganization, "pole-2", TestIdentities.OccupantOne, TestIdentities.OccupantTwo);
        return (host, owner);
    }

    private static Task<HttpResponseMessage> DispatchAsync(HttpClient owner, string rowVersion) =>
        owner.PostAsJsonAsync(NeutralOperatorApi.Route($"service-orders/{Order}/shared-use/dispatch"), new { rowVersion });

    private static async Task<string> RowVersionAsync(HttpClient owner) =>
        (await (await owner.GetOrderAsync(Order)).ReadJsonAsync()).GetProperty("rowVersion").GetString()!;

    [Fact]
    public async Task Dispatch_CreatesOneChildOrderPerPartnerOrganization_InTheHostsOwnDatabase()
    {
        var (host, owner) = await StartWithPolesAsync();
        await using var _ = host;
        using var __ = owner;

        var result = await (await DispatchAsync(owner, await RowVersionAsync(owner))).ReadJsonAsync();

        result.GetProperty("childOrdersCreated").GetInt32().Should().Be(2);
        result.GetProperty("parentStatus").GetString().Should().Be("Pending");
        var expectedChildren = new[] { TestIdentities.OccupantOne, TestIdentities.OccupantTwo }
            .Select(occupant => SharedUseDispatchPlanner.ChildOrderId(Order, occupant)).ToList();
        (await DatabaseProbe.ValuesAsync(host.ConnectionString, $"""SELECT "Id" AS "Value" FROM "ServiceOrders" WHERE "ParentOrderId" = '{Order}'"""))
            .Should().BeEquivalentTo(expectedChildren);
        (await DatabaseProbe.ValuesAsync(host.ConnectionString, $"""SELECT d."TargetId" AS "Value" FROM "OrderDispatches" d JOIN "ServiceOrders" o ON o."Id" = d."ServiceOrderId" WHERE o."ParentOrderId" = '{Order}'"""))
            .Select(Guid.Parse).Should().BeEquivalentTo([TestIdentities.OccupantOne, TestIdentities.OccupantTwo]);
        (await DatabaseProbe.ValuesAsync(host.ConnectionString, $"""SELECT DISTINCT ("OrderTypeId" || '|' || "OrganizationId" || '|' || "Status") AS "Value" FROM "ServiceOrders" WHERE "ParentOrderId" = '{Order}'"""))
            .Should().Equal([$"shared-use-occupant-notice|{TestIdentities.OwnerOrganization}|Pending"], "the notices stay the pole owner's, dispatched to the occupants");
        (await DatabaseProbe.ValueAsync(host.ConnectionString, $"""SELECT count(*)::text AS "Value" FROM "ServiceOrderTargets" t JOIN "ServiceOrders" o ON o."Id" = t."ServiceOrderId" WHERE o."ParentOrderId" = '{Order}'"""))
            .Should().Be("3", "occupant one has two poles, occupant two one");
    }

    [Fact]
    public async Task Dispatch_AsksTheKernelForNothingButTheCallersIdentityAndTheLedger_AndReleasesNothing()
    {
        var (host, owner) = await StartWithPolesAsync();
        await using var _ = host;
        using var __ = owner;

        await (await DispatchAsync(owner, await RowVersionAsync(owner))).ReadJsonAsync();

        host.Kernel.Requests.Select(r => r.Path).Where(p => !p.StartsWith("/api/identity/", StringComparison.Ordinal))
            .Should().OnlyContain(p => p.StartsWith("/api/targets/ledger/", StringComparison.Ordinal) || p.StartsWith("/api/registry/", StringComparison.Ordinal));
        host.Kernel.To(HttpMethod.Post, "/release").Should().BeEmpty("a dispatch is not a closing transition");
        (await owner.StatusOfAsync(Order)).Should().Be("Pending");
    }

    [Fact]
    public async Task Dispatch_Repeated_FindsEveryChildInPlace_AndCreatesNothingMore()
    {
        var (host, owner) = await StartWithPolesAsync();
        await using var _ = host;
        using var __ = owner;
        await (await DispatchAsync(owner, await RowVersionAsync(owner))).ReadJsonAsync();

        var again = await (await DispatchAsync(owner, await RowVersionAsync(owner))).ReadJsonAsync();

        again.GetProperty("childOrdersCreated").GetInt32().Should().Be(0);
        again.GetProperty("childOrdersAlreadyDispatched").GetInt32().Should().Be(2);
        (await DatabaseProbe.ValueAsync(host.ConnectionString, $"""SELECT count(*)::text AS "Value" FROM "ServiceOrders" WHERE "ParentOrderId" = '{Order}'""")).Should().Be("2");
    }

    [Fact]
    public async Task DispatchPlan_NamesTheRecipientsTheDispatchWouldNotify_AndWritesNothing()
    {
        var (host, owner) = await StartWithPolesAsync();
        await using var _ = host;
        using var __ = owner;

        var plan = await (await owner.GetAsync(NeutralOperatorApi.Route($"service-orders/{Order}/shared-use/dispatch-plan"))).ReadJsonAsync();

        plan.GetProperty("canDispatch").GetBoolean().Should().BeTrue();
        plan.GetProperty("targetCount").GetInt32().Should().Be(2);
        plan.GetProperty("newRecipientCount").GetInt32().Should().Be(2);
        (await DatabaseProbe.ValueAsync(host.ConnectionString, $"""SELECT count(*)::text AS "Value" FROM "ServiceOrders" WHERE "ParentOrderId" = '{Order}'""")).Should().Be("0");
    }

    [Fact]
    public async Task Dispatch_ByACallerOfAnotherOrganization_Is404_AndCreatesNothing()
    {
        var (host, owner) = await StartWithPolesAsync();
        await using var _ = host;
        using var __ = owner;
        using var outsider = host.ClientFor("outsider-admin");
        var rowVersion = await RowVersionAsync(owner);

        var response = await DispatchAsync(outsider, rowVersion);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        (await DatabaseProbe.ValueAsync(host.ConnectionString, $"""SELECT count(*)::text AS "Value" FROM "ServiceOrders" WHERE "ParentOrderId" = '{Order}'""")).Should().Be("0");
    }
}
