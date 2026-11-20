using System.Net;
using FluentAssertions;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Http;

/// <summary>
/// The NeutralOperator host's chassis routes (XD01-382, AC2) for who may do what: an order is created under its creator's
/// organization in this host's own database, a caller of another organization finds nothing, an anonymous one is refused.
/// </summary>
public sealed class ServiceOrderAccessTests
{
    private const string Order = "no-access-1";

    [Fact]
    public async Task CreateOrder_ForASharedUseOrder_Is201_AndStoresItUnderTheCallersOrganizationInTheHostsDatabase()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var owner = host.ClientFor("owner-admin");

        var created = await owner.CreateOrderAsync(Order);
        var read = await (await owner.GetOrderAsync(Order)).ReadJsonAsync();

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        read.GetProperty("organizationId").GetGuid().Should().Be(TestIdentities.OwnerOrganization);
        read.GetProperty("orderTypeId").GetString().Should().Be(NeutralOperatorApi.PoleWork);
        read.GetProperty("status").GetString().Should().Be("Draft");
        (await DatabaseProbe.ValueAsync(host.ConnectionString, $"""SELECT "OrganizationId"::text AS "Value" FROM "ServiceOrders" WHERE "Id" = '{Order}'"""))
            .Should().Be(TestIdentities.OwnerOrganization.ToString());
    }

    [Fact]
    public async Task CreateOrder_ForAnOrderTypeTheHostDoesNotServe_Is400()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var owner = host.ClientFor("owner-admin");

        var response = await owner.CreateOrderAsync(Order, orderTypeId: "field-inspection");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OrderRoutes_ForACallerOfAnotherOrganization_Answer404_JustAsForAnUnknownOrder()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var owner = host.ClientFor("owner-admin");
        using var outsider = host.ClientFor("outsider-admin");
        await owner.CreateOrderAsync(Order);
        await owner.ApplyAsync(Order, "pole-1");

        (await outsider.GetOrderAsync(Order)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await (await outsider.PreviewAsync(Order, "pole-1")).ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound);
        await (await outsider.ApplyAsync(Order, "pole-2")).ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound);
        (await outsider.GetTargetsAsync(Order)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.Kernel.To(HttpMethod.Post, "/reserve").Should().ContainSingle("only the owner's own apply reached the ledger");
        (await owner.GetOrderAsync(Order)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OrderRoutes_ForAnAnonymousCaller_Answer401_AndReachNeitherTheDatabaseNorTheKernelsLedger()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var owner = host.ClientFor("owner-admin");
        using var anonymous = host.ClientFor(null);
        await owner.CreateOrderAsync(Order);

        (await anonymous.GetOrderAsync(Order)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.CreateOrderAsync("no-access-2")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PreviewAsync(Order, "pole-1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.ApplyAsync(Order, "pole-1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await DatabaseProbe.ValuesAsync(host.ConnectionString, """SELECT "Id" AS "Value" FROM "ServiceOrders" """)).Should().Equal(Order);
        host.Kernel.To(HttpMethod.Post, "/ledger/").Should().BeEmpty();
    }

    [Fact]
    public async Task OrderRoutes_WithAnUnknownBearerToken_Answer401()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var stranger = host.ClientFor("nobody");

        (await stranger.GetOrderAsync(Order)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OrderRoutes_WhenTheKernelCannotAnswerWhoTheCallerIs_AreNotServed()
    {
        await using var host = await NeutralOperatorHost.StartAsync(kernel => kernel.IdentityFails = true);
        using var owner = host.ClientFor("owner-admin");

        var attempt = await Attempt.OfAsync(() => owner.GetOrderAsync(Order));

        attempt.Succeeded.Should().BeFalse("without the kernel's answer there is no caller identity, so no order is served");
    }
}
