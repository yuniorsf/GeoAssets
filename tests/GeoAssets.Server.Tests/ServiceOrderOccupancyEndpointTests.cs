using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using GeoAssets.NeutralOperator.Domain;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Server.Modules.NeutralOperator;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GeoAssets.Server.Tests.OrderTargetsApiAssertions;

namespace GeoAssets.Server.Tests;

/// <summary>
/// The staged-occupancy routes (XD01-315) through the real <c>MapServiceOrderOccupancyApi()</c>
/// in a TestServer: the real <see cref="OrderOccupancyStagingService"/>, the production
/// authorization pipeline, and the TestKit's in-memory stores scoped by the request's token.
/// Order <c>ord-1</c> of organization A holds two pole targets; pole <c>f-001</c> has two
/// contracted slots on file.
/// </summary>
public sealed class ServiceOrderOccupancyEndpointTests
{
    private const string Target = "ord-1-t001";
    private const string Pole = "f-001";

    private static readonly Guid NewEntry = new("0e0e0e0e-0000-4000-8000-000000000315");

    private static async Task<OrderTargetsApiHost> HostAsync(string status = ServiceOrderStatus.Draft, int regulated = 2, int beyond = 0)
    {
        var host = await OrderTargetsApiHost.StartAsync(mapOccupancyRoutes: true);
        await host.SeedOrderAsync(status: status, createdBy: OrderTargetsApiHost.Identities["creator"].UserId.ToString());
        await host.SeedTargetsAsync("ord-1", 2);
        if (regulated + beyond > 0) await SeedOccupancyAsync(host, Pole, regulated, beyond);
        return host;
    }

    private static async Task<PoleOccupancy> SeedOccupancyAsync(OrderTargetsApiHost host, string pole, int regulated, int beyond = 0)
    {
        var store = host.Occupancy.ForOrganization(OrderTargetsApiHost.OrgA);
        var current = await store.GetForPoleAsync(pole, CancellationToken.None);
        var entries = Enumerable.Range(1, regulated)
            .Select(slot => new PoleOccupancyEntryBuilder().ForPole(pole).OwnedBy(OrderTargetsApiHost.OrgA).OnSlot(slot).Build())
            .Concat(Enumerable.Range(0, beyond).Select(_ => new PoleOccupancyEntryBuilder().ForPole(pole).OwnedBy(OrderTargetsApiHost.OrgA).BeyondRegulatedSlots().Build()))
            .ToList();
        return await store.ReplaceEntriesAsync(pole, current?.Version ?? 0, entries, CancellationToken.None);
    }

    /// <summary>An Add on the wire: enums as numbers, as every route of this API writes them.</summary>
    private static object AddChange(int? slotIndex, Guid? entryId = null, PoleAttachmentOccupantStatus status = PoleAttachmentOccupantStatus.AuthorizedContracted) => new
    {
        kind = (int)OccupancyChangeKind.Add,
        slotIndex,
        entryId,
        status = (int)status,
        occupantOrganizationId = OccupancyChangeBuilder.DefaultOccupantOrganizationId,
        operatorCnpj = "98.765.432/0001-10",
        cableDiameterMm = 9.5m,
        cableLoadDaN = 80m,
    };

    private static object OnSlot(OccupancyChangeKind kind, int slotIndex) => new { kind = (int)kind, slotIndex };

    private static Task<HttpResponseMessage> StageAsync(HttpClient client, object[] changes, string orderId = "ord-1", string targetId = Target) =>
        client.PutAsJsonAsync(Path($"service-orders/{orderId}/targets/{targetId}/occupancy-changes"), new { changes });

    private static Task<HttpResponseMessage> StageRawAsync(HttpClient client, string body) =>
        client.PutAsync(Path($"service-orders/ord-1/targets/{Target}/occupancy-changes"), new StringContent(body, Encoding.UTF8, "application/json"));

    /// <summary>Every route, as a request for a given order and target.</summary>
    public static TheoryData<string> Routes() => ["GET occupancy", "GET changes", "PUT changes", "DELETE target changes", "DELETE order changes"];

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string route, string orderId = "ord-1", string targetId = Target) => route switch
    {
        "GET occupancy" => client.GetAsync(Path($"service-orders/{orderId}/targets/{targetId}/occupancy")),
        "GET changes" => client.GetAsync(Path($"service-orders/{orderId}/occupancy-changes")),
        "PUT changes" => StageAsync(client, [AddChange(4, NewEntry)], orderId, targetId),
        "DELETE target changes" => client.DeleteAsync(Path($"service-orders/{orderId}/targets/{targetId}/occupancy-changes")),
        "DELETE order changes" => client.DeleteAsync(Path($"service-orders/{orderId}/occupancy-changes")),
        _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Unknown route."),
    };

    // ── GET occupancy ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOccupancy_NothingStaged_ReturnsThePoleNow_AndAPreviewEqualToIt()
    {
        await using var host = await HostAsync(regulated: 6, beyond: 1);
        using var client = host.ClientFor("admin");

        var body = await (await SendAsync(client, "GET occupancy")).ReadJsonAsync();

        body.GetProperty("orderId").GetString().Should().Be("ord-1");
        body.GetProperty("targetId").GetString().Should().Be(Target);
        body.GetProperty("poleFeatureId").GetString().Should().Be(Pole);
        body.GetProperty("poleOwnerOrganizationId").GetGuid().Should().Be(OrderTargetsApiHost.OrgA);
        body.GetProperty("currentVersion").GetInt32().Should().Be(1);
        var current = body.GetProperty("current");
        current.GetProperty("entries").GetArrayLength().Should().Be(7);
        current.GetProperty("entries")[0].GetProperty("slotIndex").GetInt32().Should().Be(1);
        current.GetProperty("entries")[6].TryGetProperty("slotIndex", out _).Should().BeFalse("an entry beyond the regulated slots has no slot index");
        current.GetProperty("occupiedCount").GetInt32().Should().Be(7);
        current.GetProperty("isOverCapacity").GetBoolean().Should().BeTrue();
        current.GetProperty("hasUnidentifiedOccupant").GetBoolean().Should().BeFalse();
        current.GetProperty("class").GetInt32().Should().Be((int)OccupancyClass.OverCapacity);
        body.GetProperty("preview").GetRawText().Should().Be(current.GetRawText());
        body.TryGetProperty("staged", out _).Should().BeFalse();
        body.TryGetProperty("baselineVersion", out _).Should().BeFalse();
        body.GetProperty("isBaselineStale").GetBoolean().Should().BeFalse();
        body.GetProperty("warnings").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetOccupancy_APoleWithNothingOnFile_IsEmptyAtVersionZero()
    {
        await using var host = await HostAsync(regulated: 0);
        using var client = host.ClientFor("admin");

        var body = await (await SendAsync(client, "GET occupancy")).ReadJsonAsync();

        body.GetProperty("currentVersion").GetInt32().Should().Be(0);
        body.GetProperty("current").GetProperty("entries").GetArrayLength().Should().Be(0);
        body.GetProperty("current").GetProperty("class").GetInt32().Should().Be((int)OccupancyClass.None);
    }

    [Fact]
    public async Task GetOccupancy_AfterThePoleMoved_FlagsTheStaleBaselineAsAWarning_NotAnError()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");
        await (await StageAsync(client, [AddChange(4, NewEntry)])).ReadJsonAsync();
        await SeedOccupancyAsync(host, Pole, regulated: 3);

        var body = await (await SendAsync(client, "GET occupancy")).ReadJsonAsync();

        body.GetProperty("currentVersion").GetInt32().Should().Be(2);
        body.GetProperty("baselineVersion").GetInt32().Should().Be(1);
        body.GetProperty("isBaselineStale").GetBoolean().Should().BeTrue();
        body.GetProperty("warnings").Strings().Should().Equal(OccupancyProblemCodes.StaleBaseline);
        body.GetProperty("preview").GetProperty("occupiedCount").GetInt32().Should().Be(4);
        body.GetProperty("staged").GetProperty("changes").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GetOccupancy_StagedChangesThatNoLongerApply_HaveNoPreview_AndAWarningSayingWhy()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");
        await (await StageAsync(client, [OnSlot(OccupancyChangeKind.Remove, 2)])).ReadJsonAsync();
        await SeedOccupancyAsync(host, Pole, regulated: 1);

        var body = await (await SendAsync(client, "GET occupancy")).ReadJsonAsync();

        body.TryGetProperty("preview", out _).Should().BeFalse();
        body.GetProperty("warnings").Strings().Should().Equal(OccupancyProblemCodes.StaleBaseline, OccupancyProblemCodes.EntryNotFound);
    }

    // ── PUT changes ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Stage_StoresTheSetUnderThePoleOwner_AndReturnsTheViewWithThePreview_WithoutTouchingThePole()
    {
        await using var host = await HostAsync(regulated: 6);
        using var client = host.ClientFor("admin");
        var live = host.Occupancy.Snapshot().Single();

        var body = await (await StageAsync(client, [AddChange(null, NewEntry), OnSlot(OccupancyChangeKind.MarkUnidentified, 2)])).ReadJsonAsync();

        var stored = host.ChangeSets.Snapshot().Should().ContainSingle().Subject;
        stored.Should().BeEquivalentTo(new
        {
            OrderId = "ord-1", TargetId = Target, PoleFeatureId = Pole, PoleOwnerOrganizationId = OrderTargetsApiHost.OrgA,
            BaselineVersion = 1, Status = OccupancyChangeSetStatus.Staged,
            StagedBy = OrderTargetsApiHost.Identities["admin"].UserId.ToString(), StagedAt = OrderTargetsApiHost.Now,
        });
        stored.Changes.Select(change => (change.Kind, change.SlotIndex, change.EntryId)).Should().Equal(
            (OccupancyChangeKind.Add, null, NewEntry), (OccupancyChangeKind.MarkUnidentified, 2, null));
        body.GetProperty("staged").GetProperty("id").GetGuid().Should().Be(stored.Id);
        body.GetProperty("staged").GetProperty("status").GetInt32().Should().Be((int)OccupancyChangeSetStatus.Staged);
        body.GetProperty("baselineVersion").GetInt32().Should().Be(1);
        body.GetProperty("isBaselineStale").GetBoolean().Should().BeFalse();
        var preview = body.GetProperty("preview");
        preview.GetProperty("occupiedCount").GetInt32().Should().Be(7);
        preview.GetProperty("isOverCapacity").GetBoolean().Should().BeTrue();
        preview.GetProperty("hasUnidentifiedOccupant").GetBoolean().Should().BeTrue();
        preview.GetProperty("class").GetInt32().Should().Be((int)OccupancyClass.Unidentified);
        body.GetProperty("current").GetProperty("occupiedCount").GetInt32().Should().Be(6);
        host.Occupancy.Snapshot().Should().ContainSingle().Which.Should().BeSameAs(live, "staging never writes the live occupancy");
    }

    [Fact]
    public async Task Stage_IsIdempotent_AndReplacesWhatWasStaged()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");
        await (await StageAsync(client, [OnSlot(OccupancyChangeKind.Remove, 1)])).ReadJsonAsync();

        var first = await (await StageAsync(client, [AddChange(4, NewEntry)])).ReadJsonAsync();
        var second = await (await StageAsync(client, [AddChange(4, NewEntry)])).ReadJsonAsync();

        host.ChangeSets.Snapshot().Should().ContainSingle().Which.Changes.Should().ContainSingle().Which.EntryId.Should().Be(NewEntry);
        second.GetProperty("preview").GetRawText().Should().Be(first.GetProperty("preview").GetRawText());
        second.GetProperty("staged").GetProperty("changes").GetRawText().Should().Be(first.GetProperty("staged").GetProperty("changes").GetRawText());
    }

    [Fact]
    public async Task Stage_AnAddWithoutAnEntryId_GetsOneFromTheHost()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");

        var body = await (await StageAsync(client, [AddChange(null), AddChange(5)])).ReadJsonAsync();

        var ids = host.ChangeSets.Snapshot().Single().Changes.Select(change => change.EntryId).ToList();
        ids.Should().OnlyHaveUniqueItems().And.NotContainNulls().And.NotContain(Guid.Empty);
        body.GetProperty("preview").GetProperty("occupiedCount").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task Stage_TheCallerComesFromTheToken_NeverFromTheBody()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("creator");

        var response = await client.PutAsJsonAsync(
            Path($"service-orders/ord-1/targets/{Target}/occupancy-changes"),
            new { changes = new[] { AddChange(4, NewEntry) }, userId = "admin", stagedBy = "admin", principal = new { roleNames = new[] { "Administrator" } } });

        await response.ReadJsonAsync();
        host.ChangeSets.Snapshot().Single().StagedBy.Should().Be(OrderTargetsApiHost.Identities["creator"].UserId.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "changes": null }""")]
    [InlineData("""{ "changes": [null] }""")]
    [InlineData("""{ "changes": [{ "slotIndex": 1 }] }""")]
    [InlineData("""{ "changes": [{ "kind": "Remove", "slotIndex": 1 }] }""")]
    [InlineData("""{ "changes": "all" }""")]
    public async Task Stage_BodyThatIsNotAChangeList_Returns400ValidationFailed_AndStagesNothing(string body)
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");

        var response = await StageRawAsync(client, body);

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.ValidationFailed);
        host.ChangeSets.Snapshot().Should().BeEmpty();
    }

    public static TheoryData<string, object[]> InvalidChanges => new()
    {
        { "no changes", [] },
        { "slot 0", [OnSlot(OccupancyChangeKind.Remove, 0)] },
        { "slot 7", [AddChange(7, NewEntry)] },
        { "two changes on one slot", [OnSlot(OccupancyChangeKind.Remove, 1), OnSlot(OccupancyChangeKind.MarkUnidentified, 1)] },
        { "add on an occupied slot", [AddChange(1, NewEntry)] },
        { "illegal status move", [new { kind = (int)OccupancyChangeKind.Modify, slotIndex = 1, status = (int)PoleAttachmentOccupantStatus.ActiveReservation }] },
        { "reservation beyond the slots", [AddChange(null, NewEntry, PoleAttachmentOccupantStatus.ActiveReservation)] },
        { "unknown kind", [new { kind = 42, slotIndex = 1 }] },
        { "slot and entry both", [new { kind = (int)OccupancyChangeKind.Remove, slotIndex = 1, entryId = NewEntry }] },
        { "four decimals", [new { kind = (int)OccupancyChangeKind.Add, slotIndex = 4, entryId = NewEntry, status = 1, cableDiameterMm = 1.2345m }] },
    };

    [Theory]
    [MemberData(nameof(InvalidChanges))]
    public async Task Stage_ChangesThatAreMalformedOrDoNotApply_Return400InvalidChange_AndStageNothing(string why, object[] changes)
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");

        var response = await StageAsync(client, changes);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, OccupancyProblemCodes.InvalidChange);
        problem.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace(why);
        host.ChangeSets.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task Stage_ChangeOnAnEmptySlot_OrAnUnknownEntry_Returns404EntryNotFound_NamingWhatIsMissing()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");

        var emptySlot = await StageAsync(client, [OnSlot(OccupancyChangeKind.Remove, 5)]);
        var unknownEntry = await StageAsync(client, [new { kind = (int)OccupancyChangeKind.MarkUnidentified, entryId = NewEntry }]);

        (await emptySlot.ShouldBeProblemAsync(HttpStatusCode.NotFound, OccupancyProblemCodes.EntryNotFound)).GetProperty("slotIndex").GetInt32().Should().Be(5);
        (await unknownEntry.ShouldBeProblemAsync(HttpStatusCode.NotFound, OccupancyProblemCodes.EntryNotFound)).GetProperty("entryId").GetGuid().Should().Be(NewEntry);
        host.ChangeSets.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task Stage_OverCapacityEntries_AreStagedAndPreviewed_NeverRejected()
    {
        await using var host = await HostAsync(regulated: 6, beyond: 2);
        using var client = host.ClientFor("admin");
        var beyond = host.Occupancy.Snapshot().Single().OverCapacityEntries;

        var response = await StageAsync(client,
        [
            AddChange(null), AddChange(null), AddChange(null),
            new { kind = (int)OccupancyChangeKind.MarkUnidentified, entryId = beyond[0].Id },
            new { kind = (int)OccupancyChangeKind.Remove, entryId = beyond[1].Id },
        ]);

        var preview = (await response.ReadJsonAsync()).GetProperty("preview");
        preview.GetProperty("occupiedCount").GetInt32().Should().Be(10);
        preview.GetProperty("isOverCapacity").GetBoolean().Should().BeTrue();
        preview.GetProperty("hasUnidentifiedOccupant").GetBoolean().Should().BeTrue();
        preview.GetProperty("entries").GetArrayLength().Should().Be(10);
    }

    [Theory]
    [InlineData(ServiceOrderStatus.Completed)]
    [InlineData(ServiceOrderStatus.Cancelled)]
    public async Task Stage_OnATerminalOrder_Returns409OrdersInvalidState_AndStagesNothing(string status)
    {
        await using var host = await HostAsync(status);
        using var client = host.ClientFor("admin");

        var response = await SendAsync(client, "PUT changes");

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, WorkflowProblemCodes.OrderInvalidState);
        problem.GetProperty("from").GetString().Should().Be(status);
        host.ChangeSets.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task Stage_OnAReleasedTarget_Returns409TargetReleased_AndStagesNothing()
    {
        await using var host = await HostAsync();
        host.ChangeSets.MarkReleased(Target);
        using var client = host.ClientFor("admin");

        var response = await SendAsync(client, "PUT changes");

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, OccupancyProblemCodes.TargetReleased);
        host.ChangeSets.Snapshot().Should().BeEmpty();
    }

    // ── The target ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("GET occupancy")]
    [InlineData("PUT changes")]
    [InlineData("DELETE target changes")]
    public async Task TargetRoutes_UnknownTarget_OrATargetOfAnotherOrder_Return404TargetsNotFound(string route)
    {
        await using var host = await HostAsync();
        await host.SeedOrderAsync("ord-2");
        await host.SeedTargetsAsync("ord-2", 1);
        using var client = host.ClientFor("admin");

        var unknown = await SendAsync(client, route, targetId: "t-missing");
        var ofAnotherOrder = await SendAsync(client, route, targetId: "ord-2-t001");

        (await unknown.ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.TargetNotFound)).GetProperty("targetId").GetString().Should().Be("t-missing");
        await ofAnotherOrder.ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.TargetNotFound);
        host.ChangeSets.Snapshot().Should().BeEmpty();
    }

    [Theory]
    [InlineData("GET occupancy")]
    [InlineData("PUT changes")]
    [InlineData("DELETE target changes")]
    public async Task TargetRoutes_ATargetThatIsNotAPole_Return422NotAPole(string route)
    {
        await using var host = await HostAsync();
        await host.Store.AddRangeAsync(
            "ord-1",
            [new ServiceOrderTargetBuilder().WithId("ord-1-mh").WithOrderId("ord-1").WithFeatureId("mh-1").WithAssetTypeId("manhole").WithOrganizationId(OrderTargetsApiHost.OrgA).Build()],
            CancellationToken.None);
        using var client = host.ClientFor("admin");

        var response = await SendAsync(client, route, targetId: "ord-1-mh");

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, OccupancyProblemCodes.NotAPole);
        problem.GetProperty("assetTypeId").GetString().Should().Be("manhole");
        host.ChangeSets.Snapshot().Should().BeEmpty();
    }

    // ── Who ───────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task EveryRoute_UnknownOrder_Returns404OrdersNotFound(string route)
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");

        var response = await SendAsync(client, route, orderId: "ord-0");

        (await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound)).GetProperty("orderId").GetString().Should().Be("ord-0");
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task XD01_332_EveryRoute_AdministratorOfAnotherOrganization_GetsTheSameResponseAsForAnUnknownOrder_AndNothingChanges(string route)
    {
        ArgumentNullException.ThrowIfNull(route);
        await using var host = await HostAsync();
        using (var owner = host.ClientFor("admin"))
            await (await StageAsync(owner, [OnSlot(OccupancyChangeKind.Remove, 1)])).ReadJsonAsync();
        var before = host.ChangeSets.Snapshot().Single();
        using var client = host.ClientFor("admin-b");

        var foreign = await SendAsync(client, route, "ord-1");
        var unknown = await SendAsync(client, route, "ord-0");

        var problem = await foreign.ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound);
        problem.TryGetProperty("reason", out _).Should().BeFalse();
        (await foreign.Content.ReadAsStringAsync()).Replace("ord-1", "ord-0", StringComparison.Ordinal)
            .Should().Be(await unknown.Content.ReadAsStringAsync());
        foreign.Content.Headers.ContentType.Should().Be(unknown.Content.Headers.ContentType);
        host.ChangeSets.Snapshot().Should().ContainSingle().Which.Should().BeSameAs(before);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task EveryRoute_CrossOrganizationGrantee_Gets403_OccupancyIsNotShownToNorEditedByAnotherOrganization(string route)
    {
        ArgumentNullException.ThrowIfNull(route);
        await using var host = await HostAsync();
        using (var owner = host.ClientFor("admin"))
            await (await StageAsync(owner, [OnSlot(OccupancyChangeKind.Remove, 1)])).ReadJsonAsync();
        var before = host.ChangeSets.Snapshot().Single();
        host.Grants.Active.Add(new()
        {
            GranteeOrganizationId = OrderTargetsApiHost.OrgB,
            ResourceOrganizationId = OrderTargetsApiHost.OrgA,
            ResourceType = "ServiceOrder",
            AllowedActions = { "serviceorders:view", "serviceorders:annotate" },
        });
        using var client = host.ClientFor("admin-b");
        (await client.GetAsync(Path("service-orders/ord-1/targets"))).StatusCode.Should().Be(HttpStatusCode.OK, "the grant does show the grantee the order's targets");

        var response = await SendAsync(client, route);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied);
        problem.GetProperty("reason").GetString().Should().Contain("organization that owns");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("0c0c0c0c", "no occupant identity leaves the pole owner's organization");
        host.ChangeSets.Snapshot().Should().ContainSingle().Which.Should().BeSameAs(before);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task EveryRoute_MemberOfTheOrganizationWithoutAnyRoleOnTheOrder_Gets403WithTheRulesReason(string route)
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("reader");

        var response = await SendAsync(client, route);

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied);
        problem.GetProperty("reason").GetString().Should().NotBeNullOrWhiteSpace();
        host.ChangeSets.Snapshot().Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task EveryRoute_TheOrdersCreator_IsServed(string route)
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("creator");

        var response = await SendAsync(client, route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("GET occupancy")]
    [InlineData("GET changes")]
    public async Task ReadRoutes_WithoutServiceOrdersRead_Return403(string route)
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("no-permission");

        var response = await SendAsync(client, route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EveryMappedOccupancyRoute_RejectsARequestWithoutAToken_With401()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor(token: null);
        var routes = host.Server.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(route => route.RoutePattern.RawText!.Contains("occupancy", StringComparison.Ordinal))
            .ToList();

        routes.Should().HaveCount(5, "XD01-315 maps five routes");
        foreach (var route in routes)
        {
            var method = route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single();
            var path = route.RoutePattern.RawText!
                .Replace("{id}", "ord-1", StringComparison.Ordinal)
                .Replace("{targetId}", Target, StringComparison.Ordinal);
            using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(new { }) };

            var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "{0} {1} must not be reachable anonymously", method, path);
        }
    }

    // ── GET changes ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetChanges_ReturnsOnePageOfTheStagedSets_InThePagedEnvelope_OrderedByPole()
    {
        await using var host = await OrderTargetsApiHost.StartAsync(mapOccupancyRoutes: true);
        await host.SeedOrderAsync();
        await host.SeedTargetsAsync("ord-1", 5);
        using var client = host.ClientFor("admin");
        foreach (var index in new[] { 3, 1, 5, 2, 4 })
            await (await StageAsync(client, [AddChange(4)], targetId: $"ord-1-t00{index}")).ReadJsonAsync();
        (await client.DeleteAsync(Path("service-orders/ord-1/targets/ord-1-t002/occupancy-changes"))).StatusCode.Should().Be(HttpStatusCode.OK);

        var staged = await (await client.GetAsync(Path("service-orders/ord-1/occupancy-changes?page=2&pageSize=2"))).ReadJsonAsync();
        var byDefault = await (await client.GetAsync(Path("service-orders/ord-1/occupancy-changes"))).ReadJsonAsync();
        var all = await (await client.GetAsync(Path("service-orders/ord-1/occupancy-changes?status=all"))).ReadJsonAsync();
        var discarded = await (await client.GetAsync(Path("service-orders/ord-1/occupancy-changes?status=discarded"))).ReadJsonAsync();

        staged.GetProperty("page").GetInt32().Should().Be(2);
        staged.GetProperty("pageSize").GetInt32().Should().Be(2);
        staged.GetProperty("totalCount").GetInt32().Should().Be(4);
        staged.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("poleFeatureId").GetString()).Should().Equal("f-004", "f-005");
        byDefault.GetProperty("page").GetInt32().Should().Be(1);
        byDefault.GetProperty("pageSize").GetInt32().Should().Be(50);
        byDefault.GetProperty("totalCount").GetInt32().Should().Be(4, "the default lists what is staged");
        all.GetProperty("totalCount").GetInt32().Should().Be(5);
        all.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("poleFeatureId").GetString())
            .Should().Equal("f-001", "f-002", "f-003", "f-004", "f-005");
        var dropped = discarded.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        dropped.GetProperty("status").GetInt32().Should().Be((int)OccupancyChangeSetStatus.Discarded);
        dropped.GetProperty("closedAt").GetDateTimeOffset().Should().Be(OrderTargetsApiHost.Now);
        dropped.GetProperty("changes").GetArrayLength().Should().Be(1);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("page=abc")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=201")]
    [InlineData("pageSize=1.5")]
    [InlineData("page=2147483647&pageSize=200")]
    public async Task GetChanges_PagingOutOfRange_Returns400PagingOutOfRange(string query)
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");

        var response = await client.GetAsync(Path($"service-orders/ord-1/occupancy-changes?{query}"));

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.PagingOutOfRange);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("9")]
    [InlineData("")]
    public async Task GetChanges_UnknownStatus_Returns400ValidationFailed(string status)
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");

        var response = await client.GetAsync(Path($"service-orders/ord-1/occupancy-changes?status={status}"));

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.ValidationFailed);
    }

    // ── DELETE ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DiscardForATarget_DiscardsItsStagedSet_AndRepeatingItDiscardsNothing()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");
        await (await StageAsync(client, [AddChange(4, NewEntry)])).ReadJsonAsync();

        var first = await (await SendAsync(client, "DELETE target changes")).ReadJsonAsync();
        var second = await (await SendAsync(client, "DELETE target changes")).ReadJsonAsync();

        first.GetProperty("discarded").GetInt32().Should().Be(1);
        second.GetProperty("discarded").GetInt32().Should().Be(0);
        host.ChangeSets.Snapshot().Should().ContainSingle().Which.Status.Should().Be(OccupancyChangeSetStatus.Discarded);
        (await (await SendAsync(client, "GET occupancy")).ReadJsonAsync()).TryGetProperty("staged", out _).Should().BeFalse();
    }

    [Fact]
    public async Task DiscardForTheOrder_DiscardsEveryStagedSet_AndOnAClosedOrderItIsHarmless()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");
        await (await StageAsync(client, [AddChange(4, NewEntry)])).ReadJsonAsync();
        await (await StageAsync(client, [AddChange(1)], targetId: "ord-1-t002")).ReadJsonAsync();

        var discarded = await (await SendAsync(client, "DELETE order changes")).ReadJsonAsync();
        ((ServiceOrder)(await host.Orders.GetByIdAsync("ord-1"))!).Status = ServiceOrderStatus.Cancelled;
        var onClosed = await (await SendAsync(client, "DELETE order changes")).ReadJsonAsync();

        discarded.GetProperty("discarded").GetInt32().Should().Be(2);
        onClosed.GetProperty("discarded").GetInt32().Should().Be(0);
        host.ChangeSets.Snapshot().Should().HaveCount(2).And.OnlyContain(set => set.Status == OccupancyChangeSetStatus.Discarded);
    }

    [Fact]
    public async Task ClosingTheOrder_LeavesNothingStaged_AndTheReadSaysSo()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor("admin");
        await (await StageAsync(client, [AddChange(4, NewEntry)])).ReadJsonAsync();

        ((ServiceOrder)(await host.Orders.GetByIdAsync("ord-1"))!).Status = ServiceOrderStatus.Cancelled;
        var view = await (await SendAsync(client, "GET occupancy")).ReadJsonAsync();
        var staged = await (await SendAsync(client, "GET changes")).ReadJsonAsync();

        view.TryGetProperty("staged", out _).Should().BeFalse();
        staged.GetProperty("totalCount").GetInt32().Should().Be(0);
        host.ChangeSets.Snapshot().Should().ContainSingle().Which.Status.Should().Be(OccupancyChangeSetStatus.Discarded);
    }

    // ── Host wiring ───────────────────────────────────────────────────────────

    [Fact]
    public void AddServerOrderOccupancyStaging_NamesOnlyTheConfiguredDeprecatedPoleAssetTypes_TheRoleSaysTheRest()
    {
        static OrderOccupancyOptions Options(params (string Key, string? Value)[] settings)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
                .Build();
            var services = new ServiceCollection();
            services.AddServerOrderOccupancyStaging(configuration);
            using var provider = services.BuildServiceProvider();
            return provider.GetRequiredService<OrderOccupancyOptions>();
        }

        Options().PoleAssetTypeIds.Should().BeEmpty();
        Options(($"{OrderOccupancyHostExtensions.PoleAssetTypeIdsKey}:0", " ")).PoleAssetTypeIds.Should().BeEmpty();
        Options(($"{OrderOccupancyHostExtensions.PoleAssetTypeIdsKey}:0", "wood-pole"), ($"{OrderOccupancyHostExtensions.PoleAssetTypeIdsKey}:1", " concrete-pole "))
            .PoleAssetTypeIds.Should().BeEquivalentTo(["wood-pole", "concrete-pole"]);
        ((Action)(() => OrderOccupancyHostExtensions.AddServerOrderOccupancyStaging(null!, new ConfigurationBuilder().Build()))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new ServiceCollection().AddServerOrderOccupancyStaging(null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Responses_NeverCarryAnEntryOfAnotherPole()
    {
        await using var host = await HostAsync();
        await SeedOccupancyAsync(host, "f-002", regulated: 5);
        using var client = host.ClientFor("admin");

        var body = await (await SendAsync(client, "GET occupancy")).ReadJsonAsync();

        body.GetProperty("current").GetProperty("entries").EnumerateArray()
            .Select(entry => entry.GetProperty("poleFeatureId").GetString()).Should().OnlyContain(pole => pole == Pole).And.HaveCount(2);
    }
}
