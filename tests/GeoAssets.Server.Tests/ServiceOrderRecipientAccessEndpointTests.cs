using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Xunit;
using static GeoAssets.Server.Tests.OrderTargetsApiAssertions;

namespace GeoAssets.Server.Tests;

/// <summary>
/// XD01-333 at the route level: what the organization an order was dispatched to may do through
/// <c>PUT /service-orders/{id}</c>, the target PATCH and the target remove routes depends on the
/// order type's recipient access and on the acting user's role; the owner's members and a
/// grantee are untouched by it. Order <c>ord-333</c> of organization A, of one of the three
/// matrix types, is dispatched to organization C and granted (view, annotate) to organization B
/// before each fact; every request is re-seeded with that order, so no request depends on the
/// one before it.
/// </summary>
public sealed class ServiceOrderRecipientAccessEndpointTests
{
    private const string OrderId = "ord-333";

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

    private static ServiceOrder StoredShape(OrderRecipientAccess level, Action<ServiceOrder>? change = null)
    {
        var order = new ServiceOrder
        {
            Id = OrderId,
            Title = "Survey",
            OrderTypeId = OrderTargetsApiHost.RecipientAccessTypeId(level),
            Status = ServiceOrderStatus.Pending,
            CreatedBy = "someone-else",
            OrganizationId = OrderTargetsApiHost.OrgA,
            CreatedAt = OrderTargetsApiHost.Now.UtcDateTime.AddDays(-1),
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["region"] = "north" },
        };
        order.Dispatches.Add(new OrderDispatch(
            OrderTargetsApiHost.OrgC.ToString(), DispatchTargetType.Organization, "dispatcher", OrderTargetsApiHost.Now.UtcDateTime.AddHours(-1)));
        change?.Invoke(order);
        return order;
    }

    /// <summary>Puts the order back as it was stored, dispatch included.</summary>
    private static Task ReseedAsync(OrderTargetsApiHost host, OrderRecipientAccess level) => host.Orders.AddAsync(StoredShape(level));

    private static async Task<OrderTargetsApiHost> StartAsync(OrderRecipientAccess level)
    {
        var host = await OrderTargetsApiHost.StartAsync(mapOrderRoutes: true);
        await ReseedAsync(host, level);
        await host.SeedTargetsAsync(OrderId, 2);
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

    private static ServiceOrderTarget Target(OrderTargetsApiHost host, int number) =>
        host.Store.Snapshot().Single(t => string.Equals(t.Id, $"{OrderId}-t{number:000}", StringComparison.Ordinal));

    private static Task<HttpResponseMessage> PatchNoteAsync(HttpClient client, ServiceOrderTarget target, string note) =>
        client.PatchRawAsync($"service-orders/{OrderId}/targets/{target.Id}", $$"""{"note":"{{note}}","version":{{target.Version}}}""");

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, ServiceOrder body) =>
        client.PutAsJsonAsync(Path($"service-orders/{OrderId}"), body);

    // level, role, relation, PATCH a target's work, remove a target, PUT the order's content, PUT a change of its type.
    public static TheoryData<OrderRecipientAccess, string, string, bool, bool, bool, bool> Matrix() => new()
    {
        { OrderRecipientAccess.Inform,  "none",          "owner",     false, false, false, false },
        { OrderRecipientAccess.Inform,  "none",          "recipient", false, false, false, false },
        { OrderRecipientAccess.Inform,  "none",          "grantee",   false, false, false, false },
        { OrderRecipientAccess.Inform,  "crew",          "owner",     true,  true,  true,  true },
        { OrderRecipientAccess.Inform,  "crew",          "recipient", false, false, false, false },
        { OrderRecipientAccess.Inform,  "crew",          "grantee",   true,  true,  true,  true },
        { OrderRecipientAccess.Inform,  "supervisor",    "owner",     true,  true,  true,  true },
        { OrderRecipientAccess.Inform,  "supervisor",    "recipient", false, false, false, false },
        { OrderRecipientAccess.Inform,  "supervisor",    "grantee",   true,  true,  true,  true },
        { OrderRecipientAccess.Inform,  "administrator", "owner",     true,  true,  true,  true },
        { OrderRecipientAccess.Inform,  "administrator", "recipient", false, false, false, false },
        { OrderRecipientAccess.Inform,  "administrator", "grantee",   true,  true,  true,  true },
        { OrderRecipientAccess.Execute, "none",          "owner",     false, false, false, false },
        { OrderRecipientAccess.Execute, "none",          "recipient", false, false, false, false },
        { OrderRecipientAccess.Execute, "none",          "grantee",   false, false, false, false },
        { OrderRecipientAccess.Execute, "crew",          "owner",     true,  true,  true,  true },
        { OrderRecipientAccess.Execute, "crew",          "recipient", true,  false, false, false },
        { OrderRecipientAccess.Execute, "crew",          "grantee",   true,  true,  true,  true },
        { OrderRecipientAccess.Execute, "supervisor",    "owner",     true,  true,  true,  true },
        { OrderRecipientAccess.Execute, "supervisor",    "recipient", true,  false, false, false },
        { OrderRecipientAccess.Execute, "supervisor",    "grantee",   true,  true,  true,  true },
        { OrderRecipientAccess.Execute, "administrator", "owner",     true,  true,  true,  true },
        { OrderRecipientAccess.Execute, "administrator", "recipient", true,  false, false, false },
        { OrderRecipientAccess.Execute, "administrator", "grantee",   true,  true,  true,  true },
        { OrderRecipientAccess.Edit,    "none",          "owner",     false, false, false, false },
        { OrderRecipientAccess.Edit,    "none",          "recipient", false, false, false, false },
        { OrderRecipientAccess.Edit,    "none",          "grantee",   false, false, false, false },
        { OrderRecipientAccess.Edit,    "crew",          "owner",     true,  true,  true,  true },
        { OrderRecipientAccess.Edit,    "crew",          "recipient", true,  false, true,  false },
        { OrderRecipientAccess.Edit,    "crew",          "grantee",   true,  true,  true,  true },
        { OrderRecipientAccess.Edit,    "supervisor",    "owner",     true,  true,  true,  true },
        { OrderRecipientAccess.Edit,    "supervisor",    "recipient", true,  false, true,  false },
        { OrderRecipientAccess.Edit,    "supervisor",    "grantee",   true,  true,  true,  true },
        { OrderRecipientAccess.Edit,    "administrator", "owner",     true,  true,  true,  true },
        { OrderRecipientAccess.Edit,    "administrator", "recipient", true,  false, true,  false },
        { OrderRecipientAccess.Edit,    "administrator", "grantee",   true,  true,  true,  true },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Matrix_LevelRoleAndRelation_DecideTheOrderAndTargetWriteRoutes(
        OrderRecipientAccess level, string role, string relation, bool recordWork, bool removeTargets, bool replaceContent, bool replaceType)
    {
        await using var host = await StartAsync(level);
        using var client = host.ClientFor(TokenFor(relation, role));

        var patch = await PatchNoteAsync(client, Target(host, 1), "by the caller");
        var remove = await client.PostJsonAsync($"service-orders/{OrderId}/targets/remove", new { targetIds = new[] { Target(host, 2).Id } });
        var removedCount = host.Store.Snapshot().Count(t => string.Equals(t.OrderId, OrderId, StringComparison.Ordinal));
        await ReseedAsync(host, level);
        var content = await PutAsync(client, StoredShape(level, o => o.Title = "Edited"));
        var titleAfterContent = (await host.Orders.GetByIdAsync(OrderId))!.Title;
        await ReseedAsync(host, level);
        var type = await PutAsync(client, StoredShape(level, o => o.OrderTypeId = OrderTargetsApiHost.PoleSurvey));
        var typeAfter = (await host.Orders.GetByIdAsync(OrderId))!.OrderTypeId;

        if (recordWork)
        {
            patch.StatusCode.Should().Be(HttpStatusCode.NoContent, await patch.Content.ReadAsStringAsync());
            Target(host, 1).Note.Should().Be("by the caller");
        }
        else
        {
            await patch.ShouldBeProblemAsync(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied);
            Target(host, 1).Note.Should().BeNull();
        }

        if (removeTargets)
        {
            (await remove.ReadJsonAsync()).GetProperty("removed").GetInt32().Should().Be(1);
            removedCount.Should().Be(1);
        }
        else
        {
            await remove.ShouldBeProblemAsync(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied);
            removedCount.Should().Be(2);
        }

        content.StatusCode.Should().Be(replaceContent ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden, await content.Content.ReadAsStringAsync());
        titleAfterContent.Should().Be(replaceContent ? "Edited" : "Survey");
        type.StatusCode.Should().Be(replaceType ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden, await type.Content.ReadAsStringAsync());
        typeAfter.Should().Be(replaceType ? OrderTargetsApiHost.PoleSurvey : OrderTargetsApiHost.RecipientAccessTypeId(level));
    }

    // ── The reasons a recipient is given ──────────────────────────────────────

    [Theory]
    [InlineData(OrderRecipientAccess.Inform)]
    [InlineData(OrderRecipientAccess.Execute)]
    public async Task Put_ByARecipientBelowEdit_Is403_WithTheReadOnlyReason(OrderRecipientAccess level)
    {
        await using var host = await StartAsync(level);
        using var client = host.ClientFor("occupant-admin");

        var response = await PutAsync(client, StoredShape(level, o => o.Title = "Edited"));

        (await response.ReadJsonAsync(HttpStatusCode.Forbidden)).GetProperty("reason").GetString()
            .Should().Be(ServiceOrderRules.RecipientOrderIsReadOnlyReason);
    }

    public static TheoryData<string> FixedFieldChanges() => ["ParentOrderId", "Status", "AssignedTo", "scopeId"];

    [Theory]
    [MemberData(nameof(FixedFieldChanges))]
    public async Task Put_ByARecipientOfAnEditType_ChangingWhatTheOrderIs_Is403_WithTheFixedFieldsReason_AndChangesNothing(string field)
    {
        await using var host = await StartAsync(OrderRecipientAccess.Edit);
        using var client = host.ClientFor("occupant");

        var response = await PutAsync(client, StoredShape(OrderRecipientAccess.Edit, o =>
        {
            o.Title = "Edited";
            switch (field)
            {
                case "ParentOrderId": o.ParentOrderId = "someone-elses-order"; break;
                case "Status": o.Status = ServiceOrderStatus.InProgress; break;
                case "AssignedTo": o.AssignedTo = "occupant-crew"; break;
                default: o.Attributes["scopeId"] = "municipio-1"; break;
            }
        }));

        (await response.ReadJsonAsync(HttpStatusCode.Forbidden)).GetProperty("reason").GetString()
            .Should().Be(ServiceOrderRules.RecipientOrderFieldsAreFixedReason);
        (await host.Orders.GetByIdAsync(OrderId))!.Title.Should().Be("Survey");
    }

    [Theory]
    [InlineData(OrderRecipientAccess.Inform)]
    [InlineData(OrderRecipientAccess.Execute)]
    [InlineData(OrderRecipientAccess.Edit)]
    public async Task AddingOrRemovingTargets_IsRefusedToARecipientAtEveryLevel_WithTheTargetsReadOnlyReason(OrderRecipientAccess level)
    {
        await using var host = await StartAsync(level);
        using var client = host.ClientFor("occupant");

        var remove = await client.PostJsonAsync($"service-orders/{OrderId}/targets/remove", new { targetIds = new[] { Target(host, 2).Id } });
        var apply = await client.PostJsonAsync($"service-orders/{OrderId}/targets/selection/apply", FenceBody());

        foreach (var response in new[] { remove, apply })
        {
            (await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied))
                .GetProperty("reason").GetString().Should().Be(ServiceOrderRules.RecipientTargetsAreReadOnlyReason);
        }
    }

    [Fact]
    public async Task Patch_ByARecipientOfAnExecuteType_RecordsActivitiesStatusAndNote()
    {
        await using var host = await StartAsync(OrderRecipientAccess.Execute);
        using var client = host.ClientFor("occupant-crew");
        var target = Target(host, 1);

        var response = await client.PatchRawAsync(
            $"service-orders/{OrderId}/targets/{target.Id}",
            $$"""{"activityIds":["inspect"],"status":"{{OrderTargetStatus.InProgress}}","note":"crew on site","version":{{target.Version}}}""");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var stored = Target(host, 1);
        stored.ActivityIds.Should().Equal("inspect");
        stored.Status.Should().Be(OrderTargetStatus.InProgress);
        stored.Note.Should().Be("crew on site");
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrderRecipientAccess.Inform, HttpStatusCode.Forbidden)]
    [InlineData(OrderRecipientAccess.Execute, HttpStatusCode.Forbidden)]
    [InlineData(OrderRecipientAccess.Edit, HttpStatusCode.NoContent)]
    public async Task Actions_ARecipientAccepts_OnlyAnOrderOfAnEditType(OrderRecipientAccess level, HttpStatusCode expected)
    {
        await using var host = await StartAsync(level);
        using var client = host.ClientFor("occupant");

        var accept = await client.PostJsonAsync(
            $"service-orders/{OrderId}/actions",
            new { action = (int)OrderActionType.Accept, performedBy = "occupant", performedAt = OrderTargetsApiHost.Now.UtcDateTime });

        accept.StatusCode.Should().Be(expected, await accept.Content.ReadAsStringAsync());
    }
}
