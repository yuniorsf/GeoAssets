using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Xunit;
using static GeoAssets.Server.Tests.OrderTargetsApiAssertions;

namespace GeoAssets.Server.Tests;

/// <summary>
/// XD01-336 at the route level: the occupant of a shared-use notice accepts it and comments on it
/// through <c>POST /service-orders/{id}/actions</c>, and neither cancels it (action route or
/// <c>DELETE</c>) nor rewrites it (<c>PUT</c>); the owner's members and a grantee are untouched.
/// Order <c>ord-336</c> of organization A, of the notice type, is dispatched to organization C and
/// granted (view, annotate) to organization B; every request is re-seeded with that order, so no
/// request depends on the one before it.
/// </summary>
public sealed class ServiceOrderRecipientActionsEndpointTests
{
    private const string OrderId = "ord-336";

    private static string TokenFor(string relation, string role) => (relation, role) switch
    {
        ("owner", "crew") => "crew-a",
        ("owner", "supervisor") => "supervisor-a",
        ("owner", "administrator") => "admin",
        ("owner", _) => "reader",
        ("recipient", "crew") => "occupant-crew",
        ("recipient", "supervisor") => "occupant",
        ("recipient", "administrator") => "occupant-admin",
        ("recipient", _) => "occupant-plain",
        (_, "crew") => "crew-b",
        (_, "supervisor") => "supervisor-b",
        (_, "administrator") => "admin-b",
        _ => "outsider",
    };

    private static ServiceOrder StoredShape(Action<ServiceOrder>? change = null)
    {
        var order = new ServiceOrder
        {
            Id = OrderId,
            Title = "Notice",
            OrderTypeId = SharedUseOrderTypes.OccupantNoticeId,
            Status = ServiceOrderStatus.Pending,
            CreatedBy = "someone-else",
            OrganizationId = OrderTargetsApiHost.OrgA,
            CreatedAt = OrderTargetsApiHost.Now.UtcDateTime.AddDays(-1),
        };
        order.Dispatches.Add(new OrderDispatch(
            OrderTargetsApiHost.OrgC.ToString(), DispatchTargetType.Organization, "dispatcher", OrderTargetsApiHost.Now.UtcDateTime.AddHours(-1)));
        change?.Invoke(order);
        return order;
    }

    private static Task ReseedAsync(OrderTargetsApiHost host) => host.Orders.AddAsync(StoredShape());

    private static async Task<OrderTargetsApiHost> StartAsync()
    {
        var host = await OrderTargetsApiHost.StartAsync(mapOrderRoutes: true);
        await ReseedAsync(host);
        var grant = new OrganizationGrant
        {
            GranteeOrganizationId = OrderTargetsApiHost.OrgB,
            ResourceOrganizationId = OrderTargetsApiHost.OrgA,
            ResourceType = "ServiceOrder",
        };
        grant.AllowedActions.AddRange(["serviceorders:view", "serviceorders:annotate"]);
        host.Grants.Active.Add(grant);
        return host;
    }

    private static Task<HttpResponseMessage> ActAsync(HttpClient client, OrderActionType action, string? resultingStatus = null, string? comment = null) =>
        client.PostJsonAsync(
            $"service-orders/{OrderId}/actions",
            new { action = (int)action, performedBy = "caller", performedAt = OrderTargetsApiHost.Now.UtcDateTime, comment, resultingStatus });

    private sealed record Outcome(HttpStatusCode Status, string? StoredStatus, int LogEntries, bool Exists, string? Title, string Body);

    private static async Task<Outcome> OutcomeAsync(OrderTargetsApiHost host, HttpResponseMessage response)
    {
        var stored = await host.Orders.GetByIdAsync(OrderId);
        var outcome = new Outcome(
            response.StatusCode, stored?.Status, stored?.ActionLog.Count ?? 0, stored is not null, stored?.Title, await response.Content.ReadAsStringAsync());
        await ReseedAsync(host);
        return outcome;
    }

    // role, relation, Accept, Annotate, Cancel (action route), DELETE, PUT of the title.
    public static TheoryData<string, string, bool, bool, bool, bool, bool> Matrix() => new()
    {
        { "none",          "owner",     false, false, false, false, false },
        { "none",          "recipient", true,  true,  false, false, false },
        { "none",          "grantee",   false, true,  false, false, true },
        { "crew",          "owner",     false, false, false, false, false },
        { "crew",          "recipient", true,  true,  false, false, false },
        { "crew",          "grantee",   false, true,  false, false, true },
        { "supervisor",    "owner",     false, true,  true,  true,  true },
        { "supervisor",    "recipient", true,  true,  false, false, false },
        { "supervisor",    "grantee",   false, true,  true,  true,  true },
        { "administrator", "owner",     true,  true,  true,  true,  true },
        { "administrator", "recipient", true,  true,  false, false, false },
        { "administrator", "grantee",   true,  true,  true,  true,  true },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Matrix_RoleAndRelation_DecideAcceptAnnotateCancelDeleteAndPut(
        string role, string relation, bool accept, bool annotate, bool cancel, bool delete, bool put)
    {
        await using var host = await StartAsync();
        using var client = host.ClientFor(TokenFor(relation, role));

        var accepted = await OutcomeAsync(host, await ActAsync(client, OrderActionType.Accept, ServiceOrderStatus.InProgress));
        var annotated = await OutcomeAsync(host, await ActAsync(client, OrderActionType.Annotate, comment: "seen"));
        var cancelled = await OutcomeAsync(host, await ActAsync(client, OrderActionType.Cancel, ServiceOrderStatus.Cancelled));
        var deleted = await OutcomeAsync(host, await client.DeleteAsync(Path($"service-orders/{OrderId}")));
        var replaced = await OutcomeAsync(host, await client.PutAsJsonAsync(Path($"service-orders/{OrderId}"), StoredShape(o => o.Title = "Rewritten")));

        Expect(accepted, accept, "Accept", new Outcome(HttpStatusCode.NoContent, ServiceOrderStatus.InProgress, 1, true, "Notice", ""));
        Expect(annotated, annotate, "Annotate", new Outcome(HttpStatusCode.NoContent, ServiceOrderStatus.Pending, 1, true, "Notice", ""));
        Expect(cancelled, cancel, "Cancel", new Outcome(HttpStatusCode.NoContent, ServiceOrderStatus.Cancelled, 1, true, "Notice", ""));
        Expect(deleted, delete, "DELETE", new Outcome(HttpStatusCode.NoContent, null, 0, false, null, ""));
        Expect(replaced, put, "PUT", new Outcome(HttpStatusCode.NoContent, ServiceOrderStatus.Pending, 0, true, "Rewritten", ""));
    }

    /// <summary>An allowed request had exactly its effect; a refused one is 403 and wrote nothing.</summary>
    private static void Expect(Outcome actual, bool allowed, string what, Outcome whenAllowed)
    {
        var expected = allowed
            ? whenAllowed
            : new Outcome(HttpStatusCode.Forbidden, ServiceOrderStatus.Pending, 0, true, "Notice", "");
        (actual with { Body = "" }).Should().Be(expected, "{0}: {1}", what, actual.Body);
    }

    // ── What the occupant is told, and what it cannot slip through ────────────

    [Fact]
    public async Task TheOccupant_CancellingOrDeletingItsNotice_Is403_WithTheReadOnlyReason()
    {
        await using var host = await StartAsync();
        using var occupant = host.ClientFor("occupant-admin");

        var cancel = await ActAsync(occupant, OrderActionType.Cancel, ServiceOrderStatus.Cancelled);
        var delete = await occupant.DeleteAsync(Path($"service-orders/{OrderId}"));

        foreach (var response in new[] { cancel, delete })
        {
            (await response.ReadJsonAsync(HttpStatusCode.Forbidden)).GetProperty("reason").GetString()
                .Should().Be(ServiceOrderRules.RecipientOrderIsReadOnlyReason);
        }

        var stored = await host.Orders.GetByIdAsync(OrderId);
        stored!.Status.Should().Be(ServiceOrderStatus.Pending);
        stored.ActionLog.Should().BeEmpty();
    }

    [Theory]
    [InlineData(OrderActionType.Annotate, ServiceOrderStatus.Cancelled)]
    [InlineData(OrderActionType.Accept, ServiceOrderStatus.Cancelled)]
    [InlineData(OrderActionType.Accept, ServiceOrderStatus.Completed)]
    public async Task TheOccupant_MovingItsNoticeOffTheActionsTransition_Is403_WithTheTransitionReason_AndWritesNothing(
        OrderActionType action, string resultingStatus)
    {
        await using var host = await StartAsync();
        using var occupant = host.ClientFor("occupant");

        var response = await ActAsync(occupant, action, resultingStatus, "sneaky");

        (await response.ReadJsonAsync(HttpStatusCode.Forbidden)).GetProperty("reason").GetString()
            .Should().Be(ServiceOrderRules.RecipientActionTransitionIsFixedReason);
        var stored = await host.Orders.GetByIdAsync(OrderId);
        stored!.Status.Should().Be(ServiceOrderStatus.Pending);
        stored.ActionLog.Should().BeEmpty();
    }

    [Fact]
    public async Task TheOccupant_AcceptsThenComments_AndBothAreLoggedWithTheComment()
    {
        await using var host = await StartAsync();
        using var occupant = host.ClientFor("occupant-crew");

        (await ActAsync(occupant, OrderActionType.Accept, ServiceOrderStatus.InProgress)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ActAsync(occupant, OrderActionType.Annotate, comment: "crew scheduled for Monday")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stored = await host.Orders.GetByIdAsync(OrderId);
        stored!.Status.Should().Be(ServiceOrderStatus.InProgress);
        stored.ActionLog.Select(e => (e.Action, e.ResultingStatus, e.Comment)).Should().Equal(
            (OrderActionType.Accept, ServiceOrderStatus.InProgress, (string?)null),
            (OrderActionType.Annotate, (string?)null, "crew scheduled for Monday"));
    }
}
