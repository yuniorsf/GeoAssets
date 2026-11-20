using System.Net;
using FluentAssertions;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Http;

/// <summary>
/// The selection preview (XD01-382, AC2): the order and its authorization are this host's, the selection itself is the kernel's
/// ledger's — the preview forwards to its reserve route with <c>preview: true</c>, the organization in the body, and writes nothing.
/// </summary>
public sealed class TargetSelectionPreviewTests
{
    private const string Order = "no-preview-1";
    private static readonly HttpMethod Post = HttpMethod.Post;

    [Fact]
    public async Task Preview_ForwardsToTheLedgersReserveRouteAsAPreview_WithTheOrganizationInTheBody_AndNoOrganizationHeader()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var owner = host.ClientFor("owner-admin");
        await owner.CreateOrderAsync(Order);

        var body = await (await owner.PreviewAsync(Order, "pole-1", "pole-2")).ReadJsonAsync();

        body.GetProperty("matched").GetInt32().Should().Be(2);
        body.GetProperty("eligible").GetInt32().Should().Be(2);
        body.GetProperty("firstPage").GetProperty("items").EnumerateArray().Select(t => t.GetProperty("featureId").GetString())
            .Should().BeEquivalentTo("pole-1", "pole-2");
        var reserve = host.Kernel.To(Post, "/reserve").Should().ContainSingle().Subject;
        reserve.Path.Should().Be($"/api/targets/ledger/neutral-operator/{Order}/reserve");
        reserve.Json.GetProperty("preview").GetBoolean().Should().BeTrue();
        reserve.Json.GetProperty("organizationId").GetGuid().Should().Be(TestIdentities.OwnerOrganization);
        reserve.Json.GetProperty("orderTypeId").GetString().Should().Be(NeutralOperatorApi.PoleWork);
        reserve.Json.GetProperty("strategyId").GetString().Should().Be(NeutralOperatorApi.Strategy);
        reserve.Headers.Keys.Should().NotContain(
            name => name.Contains("organization", StringComparison.OrdinalIgnoreCase) || name.Contains("tenant", StringComparison.OrdinalIgnoreCase),
            "the organization is named in the body of a ledger call, never in a header");
        reserve.Authorization.Should().Be($"Bearer {FakeServiceAuthTokenProvider.Token}", "the ledger is called as the host's service principal, not as the user");
    }

    [Fact]
    public async Task Preview_WritesNothing_NeitherInTheHostNorInTheLedger()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var owner = host.ClientFor("owner-admin");
        await owner.CreateOrderAsync(Order);

        await (await owner.PreviewAsync(Order, "pole-1")).ReadJsonAsync();

        (await DatabaseProbe.ValueAsync(host.ConnectionString, """SELECT count(*)::text AS "Value" FROM "ServiceOrderTargets" """)).Should().Be("0");
        host.Kernel.LedgerOf(Order).Should().BeEmpty();
        host.Kernel.To(HttpMethod.Get, "/usages").Should().BeEmpty("a preview mirrors nothing");
    }

    [Fact]
    public async Task Preview_ForAnUnknownOrder_Is404_AndNeverReachesTheLedger()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        using var owner = host.ClientFor("owner-admin");

        await (await owner.PreviewAsync("no-such-order", "pole-1")).ShouldBeProblemAsync(HttpStatusCode.NotFound, WorkflowProblemCodes.OrderNotFound);

        host.Kernel.To(Post, "/ledger/").Should().BeEmpty();
    }

    [Fact]
    public async Task Preview_WhenTheLedgerIsDown_Is503LedgerUnavailable()
    {
        await using var host = await NeutralOperatorHost.StartAsync(kernel => kernel.ReserveFault = KernelFault.ServerError);
        using var owner = host.ClientFor("owner-admin");
        await owner.CreateOrderAsync(Order);

        await (await owner.PreviewAsync(Order, "pole-1")).ShouldBeProblemAsync(HttpStatusCode.ServiceUnavailable, WorkflowProblemCodes.TargetLedgerUnavailable);
    }
}
