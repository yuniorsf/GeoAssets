using System.Net;
using FluentAssertions;
using GeoAssets.Workflow.Targets;
using Xunit;
using static GeoAssets.Server.Tests.OrderTargetsApiAssertions;

namespace GeoAssets.Server.Tests;

/// <summary>
/// The edit routes of <c>MapServiceOrderTargetsApi()</c> (XD01-308): <c>PATCH</c> one target
/// (R7) and remove targets (R8) — happy paths, every status code they map, and that the order
/// rules, evaluated for the caller the token names, decide who may edit.
/// </summary>
public sealed class ServiceOrderTargetsEditEndpointTests
{
    private static async Task<OrderTargetsApiHost> HostWithTargetsAsync(int targets = 2)
    {
        var host = await OrderTargetsApiHost.StartAsync();
        await host.SeedOrderAsync();
        await host.SeedTargetsAsync("ord-1", targets);
        return host;
    }

    private static ServiceOrderTarget Stored(OrderTargetsApiHost host, string targetId) =>
        host.Store.Snapshot().Single(t => string.Equals(t.Id, targetId, StringComparison.Ordinal));

    // ── R7: PATCH /service-orders/{id}/targets/{targetId} ─────────────────────

    [Fact]
    public async Task PatchTarget_ActivitiesStatusAndNote_Returns204_WritesThem_AndBumpsTheVersion()
    {
        await using var host = await HostWithTargetsAsync();
        using var client = host.ClientFor("admin");

        var response = await client.PatchRawAsync(
            "service-orders/ord-1/targets/ord-1-t001",
            """{ "activityIds": ["replace", "INSPECT", "replace"], "status": "InProgress", "note": "north side", "version": 0 }""");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var stored = Stored(host, "ord-1-t001");
        stored.ActivityIds.Should().Equal("replace", "inspect");
        stored.Status.Should().Be(OrderTargetStatus.InProgress);
        stored.Note.Should().Be("north side");
        stored.Version.Should().Be(1);
        Stored(host, "ord-1-t002").Version.Should().Be(0, "only the addressed target is edited");
    }

    [Fact]
    public async Task PatchTarget_MembersLeftOut_AreNotChanged_AndANullNoteClearsIt()
    {
        await using var host = await HostWithTargetsAsync();
        using var client = host.ClientFor("admin");
        (await client.PatchRawAsync(
            "service-orders/ord-1/targets/ord-1-t001",
            """{ "activityIds": ["inspect"], "status": "InProgress", "note": "keep me", "version": 0 }""")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var onlyVersion = await client.PatchRawAsync("service-orders/ord-1/targets/ord-1-t001", """{ "version": 1 }""");
        var afterOnlyVersion = Stored(host, "ord-1-t001");
        var nullNote = await client.PatchRawAsync("service-orders/ord-1/targets/ord-1-t001", """{ "note": null, "version": 2 }""");

        onlyVersion.StatusCode.Should().Be(HttpStatusCode.NoContent);
        afterOnlyVersion.Should().BeEquivalentTo(new { Status = OrderTargetStatus.InProgress, Note = "keep me", Version = 2 });
        afterOnlyVersion.ActivityIds.Should().Equal("inspect");
        nullNote.StatusCode.Should().Be(HttpStatusCode.NoContent);
        Stored(host, "ord-1-t001").Note.Should().BeNull();
        Stored(host, "ord-1-t001").ActivityIds.Should().Equal("inspect");
    }

    [Theory]
    [InlineData("""{ "activityIds": ["paint"], "version": 0 }""")]
    [InlineData("""{ "status": "Done", "version": 0 }""")]
    [InlineData("""{ "status": "NotAStatus", "version": 0 }""")]
    [InlineData("""{ "status": " ", "version": 0 }""")]
    [InlineData("""{ "note": "x" }""")]
    [InlineData("""{ "version": "zero" }""")]
    [InlineData("""not json""")]
    public async Task PatchTarget_InvalidEdit_Returns400ValidationFailed_AndWritesNothing(string body)
    {
        await using var host = await HostWithTargetsAsync();
        var before = Stored(host, "ord-1-t001");
        using var client = host.ClientFor("admin");

        var response = await client.PatchRawAsync("service-orders/ord-1/targets/ord-1-t001", body);

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.ValidationFailed);
        Stored(host, "ord-1-t001").Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task PatchTarget_NoteLongerThanTheStoreAccepts_Returns400ValidationFailed()
    {
        await using var host = await HostWithTargetsAsync();
        using var client = host.ClientFor("admin");
        var longNote = new string('n', 2049);

        var tooLong = await client.PatchRawAsync("service-orders/ord-1/targets/ord-1-t001", $$"""{ "note": "{{longNote}}", "version": 0 }""");
        var atTheLimit = await client.PatchRawAsync("service-orders/ord-1/targets/ord-1-t001", $$"""{ "note": "{{longNote[..2048]}}", "version": 0 }""");

        await tooLong.ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.ValidationFailed);
        atTheLimit.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task PatchTarget_StaleVersion_Returns409ConcurrentModification_AndWritesNothing()
    {
        await using var host = await HostWithTargetsAsync();
        using var client = host.ClientFor("admin");
        (await client.PatchRawAsync("service-orders/ord-1/targets/ord-1-t001", """{ "note": "first", "version": 0 }"""))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stale = await client.PatchRawAsync("service-orders/ord-1/targets/ord-1-t001", """{ "note": "second", "version": 0 }""");

        var problem = await stale.ShouldBeProblemAsync(HttpStatusCode.Conflict, WorkflowProblemCodes.TargetConcurrentModification);
        problem.GetProperty("orderId").GetString().Should().Be("ord-1");
        Stored(host, "ord-1-t001").Note.Should().Be("first");
    }

    [Fact]
    public async Task PatchTarget_UnknownTargetAnotherOrdersTargetOrUnknownOrder_Returns404WithTheMatchingCode()
    {
        await using var host = await HostWithTargetsAsync();
        await host.SeedOrderAsync("ord-2");
        await host.SeedTargetsAsync("ord-2", 1);
        using var client = host.ClientFor("admin");
        const string body = """{ "note": "x", "version": 0 }""";

        var unknownTarget = await client.PatchRawAsync("service-orders/ord-1/targets/nope", body);
        var otherOrdersTarget = await client.PatchRawAsync("service-orders/ord-1/targets/ord-2-t001", body);
        var unknownOrder = await client.PatchRawAsync("service-orders/ord-missing/targets/ord-1-t001", body);

        await unknownTarget.ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.TargetNotFound);
        await otherOrdersTarget.ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.TargetNotFound);
        await unknownOrder.ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound);
        Stored(host, "ord-2-t001").Note.Should().BeNull();
    }

    [Theory]
    [InlineData("reader")]   // same organization, but neither creator, assignee nor supervisor
    [InlineData("outsider")] // another organization
    public async Task PatchTarget_CallerTheOrderRulesRefuse_Returns403AuthDenied_AndWritesNothing(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        await using var host = await HostWithTargetsAsync();
        using var client = host.ClientFor(token);

        var response = await client.PatchRawAsync("service-orders/ord-1/targets/ord-1-t001", """{ "note": "x", "version": 0 }""");

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied);
        problem.GetProperty("reason").GetString().Should().NotBeNullOrWhiteSpace();
        Stored(host, "ord-1-t001").Note.Should().BeNull();
    }

    [Fact]
    public async Task PatchTarget_TheOrdersCreator_IsAllowed_ThePrincipalComesFromTheTokenNotFromTheBody()
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        var creator = OrderTargetsApiHost.Identities["creator"];
        var admin = OrderTargetsApiHost.Identities["admin"];
        await host.SeedOrderAsync(createdBy: creator.UserId.ToString());
        await host.SeedTargetsAsync("ord-1", 1);
        using var asCreator = host.ClientFor("creator");
        using var asReader = host.ClientFor("reader");

        // The body claims to be the creator and an administrator; only the token counts.
        var forged = $$"""{ "note": "forged", "version": 0, "userId": "{{creator.UserId}}", "principal": { "userId": "{{admin.UserId}}", "roleNames": ["Administrator"] } }""";
        var byReader = await asReader.PatchRawAsync($"service-orders/ord-1/targets/ord-1-t001?userId={creator.UserId}", forged);
        var byCreator = await asCreator.PatchRawAsync("service-orders/ord-1/targets/ord-1-t001", """{ "note": "mine", "version": 0 }""");

        await byReader.ShouldBeProblemAsync(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied);
        byCreator.StatusCode.Should().Be(HttpStatusCode.NoContent);
        Stored(host, "ord-1-t001").Note.Should().Be("mine");
    }

    [Fact]
    public async Task PatchTarget_WithoutAToken_Returns401()
    {
        await using var host = await HostWithTargetsAsync();
        using var client = host.ClientFor(token: null);

        var response = await client.PatchRawAsync("service-orders/ord-1/targets/ord-1-t001", """{ "note": "x", "version": 0 }""");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── R8: POST /service-orders/{id}/targets/remove ──────────────────────────

    [Fact]
    public async Task RemoveTargets_RemovesTheListedTargetsOfThatOrder_AndReportsHowMany()
    {
        await using var host = await HostWithTargetsAsync(targets: 4);
        await host.SeedOrderAsync("ord-2");
        await host.SeedTargetsAsync("ord-2", 1);
        using var client = host.ClientFor("admin");

        var body = await (await client.PostJsonAsync(
            "service-orders/ord-1/targets/remove",
            new { targetIds = new[] { "ord-1-t001", "ord-1-t003", "unknown", "ord-2-t001" } })).ReadJsonAsync();

        body.GetProperty("removed").GetInt32().Should().Be(2);
        host.Store.Snapshot().Select(t => t.Id).Should().BeEquivalentTo("ord-1-t002", "ord-1-t004", "ord-2-t001");
    }

    [Fact]
    public async Task RemoveTargets_ZeroIdsMoreThanFiveHundredOrABlankId_Return400ValidationFailed()
    {
        await using var host = await HostWithTargetsAsync();
        using var client = host.ClientFor("admin");
        var tooMany = Enumerable.Range(1, 501).Select(i => $"t-{i}").ToArray();

        var none = await client.PostJsonAsync("service-orders/ord-1/targets/remove", new { targetIds = Array.Empty<string>() });
        var missing = await client.PostRawAsync("service-orders/ord-1/targets/remove", "{}");
        var over = await client.PostJsonAsync("service-orders/ord-1/targets/remove", new { targetIds = tooMany });
        var blank = await client.PostJsonAsync("service-orders/ord-1/targets/remove", new { targetIds = new[] { "" } });
        var malformed = await client.PostRawAsync("service-orders/ord-1/targets/remove", "{");
        var atTheLimit = await client.PostJsonAsync("service-orders/ord-1/targets/remove", new { targetIds = tooMany.Take(500).ToArray() });

        foreach (var response in new[] { none, missing, over, blank, malformed })
            await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, WorkflowProblemCodes.ValidationFailed);
        atTheLimit.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Store.Snapshot().Should().HaveCount(2);
    }

    [Fact]
    public async Task RemoveTargets_UnknownOrder_Returns404_AndRefusedCaller403_AndNoToken401_WithNothingRemoved()
    {
        await using var host = await HostWithTargetsAsync();
        var body = new { targetIds = new[] { "ord-1-t001" } };
        using var admin = host.ClientFor("admin");
        using var reader = host.ClientFor("reader");
        using var anonymous = host.ClientFor(token: null);

        var unknownOrder = await admin.PostJsonAsync("service-orders/ord-missing/targets/remove", body);
        var refused = await reader.PostJsonAsync("service-orders/ord-1/targets/remove", body);
        var unauthenticated = await anonymous.PostJsonAsync("service-orders/ord-1/targets/remove", body);

        await unknownOrder.ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound);
        await refused.ShouldBeProblemAsync(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied);
        unauthenticated.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Store.Snapshot().Should().HaveCount(2);
    }

    [Fact]
    public async Task EditRoutes_CrossOrganizationGrantee_IsAllowedExactlyTheActionItsGrantNames()
    {
        await using var host = await HostWithTargetsAsync();
        host.Grants.Active.Add(new()
        {
            GranteeOrganizationId = OrderTargetsApiHost.OrgB,
            ResourceOrganizationId = OrderTargetsApiHost.OrgA,
            AllowedActions = { "serviceorders:view" },
        });
        using var client = host.ClientFor("outsider");

        var viewOnly = await client.PostJsonAsync("service-orders/ord-1/targets/remove", new { targetIds = new[] { "ord-1-t001" } });
        host.Grants.Active[0].AllowedActions.Add("serviceorders:annotate");
        var withAnnotate = await client.PostJsonAsync("service-orders/ord-1/targets/remove", new { targetIds = new[] { "ord-1-t001" } });

        await viewOnly.ShouldBeProblemAsync(HttpStatusCode.Forbidden, WorkflowProblemCodes.Denied);
        (await withAnnotate.ReadJsonAsync()).GetProperty("removed").GetInt32().Should().Be(1);
    }
}
