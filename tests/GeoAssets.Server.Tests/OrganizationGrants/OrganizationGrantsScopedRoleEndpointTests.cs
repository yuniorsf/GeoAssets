using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static GeoAssets.Server.Tests.OrganizationGrants.OrganizationGrantsHarness;

namespace GeoAssets.Server.Tests.OrganizationGrants;

/// <summary>
/// XD01-326 — the kernel's organization-grant write path accepts and returns <c>requiresScopedRole</c> and
/// <c>orderTypeIds</c>: both optional (absent = the organization-wide semantics for every order type, so every earlier
/// caller is unaffected), both part of what makes an upsert unchanged or an update, and both stored.
/// </summary>
public sealed class OrganizationGrantsScopedRoleEndpointTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static object Body(bool? requiresScopedRole = true, string[]? orderTypeIds = null, string? resourceType = "ServiceOrder") => new
    {
        key = KeyBody(resourceType: resourceType),
        allowedActions = new[] { "serviceorders:view", "serviceorders:execute" },
        grantedBy = "admin@test",
        requiresScopedRole,
        orderTypeIds = orderTypeIds ?? ["cut"],
    };

    private static async Task<OrganizationGrantUpsertResult> ReadAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<OrganizationGrantUpsertResult>(Web))!;
    }

    private static async Task<string> ErrorOfAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("error").GetString()!;
    }

    private static async Task<OrganizationGrantsHarness> StartedAsync()
    {
        var harness = new OrganizationGrantsHarness();
        await harness.StartAsync();
        return harness;
    }

    [Fact]
    public async Task Upsert_WithBothFields_CreatesTheGrant_StoresThem_AndReturnsThem()
    {
        await using var harness = await StartedAsync();

        var created = await ReadAsync(await harness.UpsertAsync(Body(orderTypeIds: ["cut", "reconnect"])));

        created.Outcome.Should().Be(OrganizationGrantUpsertOutcome.Created);
        created.Grant.RequiresScopedRole.Should().BeTrue();
        created.Grant.OrderTypeIds.Should().Equal("cut", "reconnect");

        harness.Db.ChangeTracker.Clear();
        var stored = await harness.Db.OrganizationGrants.SingleAsync();
        stored.RequiresScopedRole.Should().BeTrue();
        stored.OrderTypeIds.Should().Equal("cut", "reconnect");
    }

    [Fact]
    public async Task Upsert_WithoutEitherField_IsOrganizationWide_ForEveryOrderType()
    {
        await using var harness = await StartedAsync();

        var created = await ReadAsync(await harness.UpsertAsync(UpsertBody()));

        created.Grant.RequiresScopedRole.Should().BeFalse();
        created.Grant.OrderTypeIds.Should().BeEmpty();
        (await harness.Db.OrganizationGrants.SingleAsync()).OrderTypeIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Upsert_TheSameFieldsAgain_InAnotherOrder_AreUnchanged()
    {
        await using var harness = await StartedAsync();
        await harness.UpsertAsync(Body(orderTypeIds: ["cut", "reconnect"]));

        var again = await ReadAsync(await harness.UpsertAsync(Body(orderTypeIds: ["reconnect", "cut"])));

        again.Outcome.Should().Be(OrganizationGrantUpsertOutcome.Unchanged);
    }

    [Fact]
    public async Task Upsert_FlippingRequiresScopedRole_UpdatesTheSameGrant()
    {
        await using var harness = await StartedAsync();
        var created = await ReadAsync(await harness.UpsertAsync(Body()));

        var updated = await ReadAsync(await harness.UpsertAsync(Body(requiresScopedRole: false)));

        updated.Outcome.Should().Be(OrganizationGrantUpsertOutcome.Updated);
        updated.Grant.Id.Should().Be(created.Grant.Id);
        updated.Grant.RequiresScopedRole.Should().BeFalse();
        harness.Db.ChangeTracker.Clear();
        (await harness.Db.OrganizationGrants.SingleAsync()).RequiresScopedRole.Should().BeFalse();
    }

    [Fact]
    public async Task Upsert_ChangedOrderTypes_UpdatesTheSameGrant()
    {
        await using var harness = await StartedAsync();
        await harness.UpsertAsync(Body());

        var updated = await ReadAsync(await harness.UpsertAsync(Body(orderTypeIds: ["reconnect"])));

        updated.Outcome.Should().Be(OrganizationGrantUpsertOutcome.Updated);
        updated.Grant.OrderTypeIds.Should().Equal("reconnect");
        harness.Db.ChangeTracker.Clear();
        (await harness.Db.OrganizationGrants.SingleAsync()).OrderTypeIds.Should().Equal("reconnect");
    }

    [Fact]
    public async Task List_ReturnsBothFields()
    {
        await using var harness = await StartedAsync();
        await harness.UpsertAsync(Body());

        using var response = await harness.ListAsync(Grantee, Resource);

        var page = (await response.Content.ReadFromJsonAsync<OrganizationGrantPage>(Web))!;
        var grant = page.Items.Should().ContainSingle().Subject;
        grant.RequiresScopedRole.Should().BeTrue();
        grant.OrderTypeIds.Should().Equal("cut");
    }

    public static TheoryData<string, object, string> InvalidUpserts => new()
    {
        { "order types on a grant of every resource type", Body(resourceType: null), "orderTypeIds may only narrow a grant whose resourceType is \"ServiceOrder\"." },
        { "order types on a grant of features", Body(resourceType: "GeoFeature"), "orderTypeIds may only narrow a grant whose resourceType is \"ServiceOrder\"." },
        { "order types compared ordinally with the resource type", Body(resourceType: "serviceorder"), "orderTypeIds may only narrow a grant whose resourceType is \"ServiceOrder\"." },
        { "a blank order type", Body(orderTypeIds: ["cut", " "]), "orderTypeIds must not contain a blank order type id." },
        {
            "a null order type list",
            new { key = KeyBody(resourceType: "ServiceOrder"), allowedActions = new[] { "serviceorders:view" }, grantedBy = "admin@test", orderTypeIds = (string[]?)null },
            "orderTypeIds, when given, must be a list."
        },
    };

    [Theory]
    [MemberData(nameof(InvalidUpserts))]
    public async Task Upsert_InvalidOrderTypes_Returns400WithTheProblem_AndWritesNothing(string scenario, object body, string error)
    {
        await using var harness = await StartedAsync();

        using var response = await harness.UpsertAsync(body);

        (await ErrorOfAsync(response)).Should().Be(error, scenario);
        (await harness.Db.OrganizationGrants.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Upsert_RequiresScopedRole_NeedsNoOrderTypes_AndIsAcceptedForAnyResourceType()
    {
        await using var harness = await StartedAsync();

        var created = await ReadAsync(await harness.UpsertAsync(Body(orderTypeIds: [], resourceType: "GeoFeature")));

        created.Grant.RequiresScopedRole.Should().BeTrue();
        created.Grant.ResourceType.Should().Be("GeoFeature");
    }
}
