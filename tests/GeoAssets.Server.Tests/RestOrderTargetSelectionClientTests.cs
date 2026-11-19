using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rest;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// <see cref="RestOrderTargetSelectionService"/> and <see cref="RestMapSelectionClient"/> against
/// the real endpoints in a TestServer (XD01-308): the same results, and — boundary-error parity —
/// the same exception types the in-process <see cref="OrderTargetSelectionService"/> throws.
/// </summary>
public sealed class RestOrderTargetSelectionClientTests
{
    /// <summary>What the port requires a caller to pass; the wire never carries it.</summary>
    private static readonly WorkflowPrincipal ClaimedAdministrator =
        new("claimed-admin", OrderTargetsApiHost.OrgA.ToString(), ["Administrator"], [], ["serviceorders:annotate"]);

    private static Dictionary<string, object> Fence(double[][][]? rings = null) => new(StringComparer.Ordinal)
    {
        ["rings"] = rings ?? OrderTargetsApiHost.FenceRings(),
    };

    private static TargetSelectionRequest FenceRequest(
        string orderId = "ord-1", bool acceptTruncation = false, double[][][]? rings = null, int pageSize = 50) =>
        new(orderId, "polygon-fence", Fence(rings), pageSize: pageSize, acceptTruncation: acceptTruncation);

    private static async Task<(OrderTargetsApiHost Host, HttpClient Http, RestOrderTargetSelectionService Sut)> StartAsync(
        string token = "admin",
        string orderTypeId = OrderTargetsApiHost.PoleSurvey,
        string status = ServiceOrderStatus.Draft,
        Action<OrderTargetSelectionOptions>? configure = null)
    {
        var host = await OrderTargetsApiHost.StartAsync(configure);
        host.SeedAssets();
        await host.SeedOrderAsync(orderTypeId: orderTypeId, status: status);
        var http = host.ClientFor(token);
        return (host, http, new RestOrderTargetSelectionService(http));
    }

    [Fact]
    public async Task PreviewAsync_ReturnsThePreviewTheHostComputed_IncludingThePagedTargets()
    {
        var (host, http, sut) = await StartAsync();
        await using var _ = host;
        using var __ = http;

        var preview = await sut.PreviewAsync(FenceRequest(pageSize: 2), ClaimedAdministrator, CancellationToken.None);

        preview.Should().BeEquivalentTo(new
        {
            OrderTypeId = OrderTargetsApiHost.PoleSurvey, Matched = 3, Eligible = 3, AlreadyInOrder = 0, TruncatedByLimit = 0, WarningCount = 0, BlockedCount = 0,
        });
        preview.FirstPage.TotalCount.Should().Be(3);
        preview.FirstPage.Items.Select(t => (t.FeatureId, t.Alias, t.OrderId, t.Status))
            .Should().Equal(("a-pole-1", "P-100", "ord-1", OrderTargetStatus.Pending), ("a-pole-2", "P-200", "ord-1", OrderTargetStatus.Pending));
        preview.FirstPage.Items[0].Should().BeEquivalentTo(new { Longitude = 1.0, Latitude = 1.0, OrganizationId = OrderTargetsApiHost.OrgA });
        host.Store.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_AddsTheTargets_AttributedToTheTokensUser_NotToThePrincipalArgument()
    {
        var (host, http, sut) = await StartAsync();
        await using var _ = host;
        using var __ = http;

        var result = await sut.ApplyAsync(FenceRequest(), ClaimedAdministrator, CancellationToken.None);
        var again = await sut.ApplyAsync(FenceRequest(), ClaimedAdministrator, CancellationToken.None);

        result.Should().BeEquivalentTo(new { Added = 3, Matched = 3, Eligible = 3, AlreadyInOrder = 0 });
        again.Should().BeEquivalentTo(new { Added = 0, AlreadyInOrder = 3 });
        host.Store.Snapshot().Select(t => t.AddedBy).Distinct(StringComparer.Ordinal)
            .Should().Equal(OrderTargetsApiHost.Identities["admin"].UserId.ToString());
    }

    [Fact]
    public async Task IdentifierList_RoundTripsTheNotFoundAndAmbiguousValues()
    {
        var (host, http, sut) = await StartAsync();
        await using var _ = host;
        using var __ = http;
        host.Assets.Add(OrderTargetsApiHost.Asset("a-pole-dup", 5, 5, OrderTargetsApiHost.OrgA, plate: "P-200"));
        var request = new TargetSelectionRequest(
            "ord-1", "identifier-list", new Dictionary<string, object>(StringComparer.Ordinal) { ["values"] = new[] { "P-100", "P-200", "P-404" } });

        var preview = await sut.PreviewAsync(request, ClaimedAdministrator, CancellationToken.None);

        preview.NotFoundValues.Should().Equal("P-404");
        preview.AmbiguousValues.Should().Equal("P-200");
    }

    [Fact]
    public async Task APrincipalArgumentClaimingMoreThanTheTokenHolds_ChangesNothing_TheHostDeniesTheTokensUser()
    {
        var (host, http, sut) = await StartAsync(token: "reader");
        await using var _ = host;
        using var __ = http;

        var preview = () => sut.PreviewAsync(FenceRequest(), ClaimedAdministrator, CancellationToken.None);
        var apply = () => sut.ApplyAsync(FenceRequest(), ClaimedAdministrator, CancellationToken.None);

        var deniedView = (await preview.Should().ThrowAsync<OrderActionDeniedException>()).Which;
        deniedView.Action.Should().Be(OrderAction.View);
        deniedView.OrderId.Should().Be("ord-1");
        deniedView.Evaluation!.Reason.Should().Be("No rule granted this action.");
        (await apply.Should().ThrowAsync<OrderActionDeniedException>()).Which.Action.Should().Be(OrderAction.Annotate);
        host.Store.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownOrder_UnknownStrategy_AndUnregisteredOrderType_ThrowKeyNotFoundException()
    {
        var (host, http, sut) = await StartAsync();
        await using var _ = host;
        using var __ = http;
        await host.SeedOrderAsync("ord-ghost", "ghost-type");
        var unknownStrategy = new TargetSelectionRequest("ord-1", "no-such-strategy", Fence());

        await FluentActions.Invoking(() => sut.PreviewAsync(FenceRequest("ord-missing"), ClaimedAdministrator, CancellationToken.None))
            .Should().ThrowAsync<KeyNotFoundException>();
        await FluentActions.Invoking(() => sut.ApplyAsync(unknownStrategy, ClaimedAdministrator, CancellationToken.None))
            .Should().ThrowAsync<KeyNotFoundException>();
        await FluentActions.Invoking(() => sut.PreviewAsync(FenceRequest("ord-ghost"), ClaimedAdministrator, CancellationToken.None))
            .Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task InvalidFence_ThrowsInvalidSelectionFenceException_WithTheSameDefect_AndBadParametersArgumentException()
    {
        var (host, http, sut) = await StartAsync();
        await using var _ = host;
        using var __ = http;
        double[][][] bowTie = [[[0, 0], [10, 10], [10, 0], [0, 10], [0, 0]]];
        var missingRings = new TargetSelectionRequest("ord-1", "polygon-fence", new Dictionary<string, object>(StringComparer.Ordinal));

        var invalidFence = () => sut.ApplyAsync(FenceRequest(rings: bowTie), ClaimedAdministrator, CancellationToken.None);
        var badParameters = () => sut.PreviewAsync(missingRings, ClaimedAdministrator, CancellationToken.None);

        (await invalidFence.Should().ThrowAsync<InvalidSelectionFenceException>()).Which.Defect.Should().Be(SelectionFenceDefect.SelfIntersecting);
        await badParameters.Should().ThrowAsync<ArgumentException>();
        host.Store.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task OverTheSelectionLimit_ThrowsSelectionLimitExceededException_WithTheSameNumbers_UnlessTruncationIsAccepted()
    {
        var (host, http, sut) = await StartAsync(orderTypeId: OrderTargetsApiHost.LimitedSurvey);
        await using var _ = host;
        using var __ = http;

        var rejected = () => sut.ApplyAsync(FenceRequest(), ClaimedAdministrator, CancellationToken.None);
        (await rejected.Should().ThrowAsync<SelectionLimitExceededException>())
            .Which.Should().BeEquivalentTo(new { Limit = 2, Current = 0, Requested = 3 });
        host.Store.Snapshot().Should().BeEmpty();

        var truncated = await sut.ApplyAsync(FenceRequest(acceptTruncation: true), ClaimedAdministrator, CancellationToken.None);
        truncated.Should().BeEquivalentTo(new { Added = 2, TruncatedByLimit = 1 });
    }

    [Fact]
    public async Task PastTheHostCeiling_ThrowsSelectionTooLargeException_WithTheSameNumbers()
    {
        var (host, http, sut) = await StartAsync(configure: o => o.MaxMatched = 2);
        await using var _ = host;
        using var __ = http;

        var act = () => sut.PreviewAsync(FenceRequest(), ClaimedAdministrator, CancellationToken.None);

        (await act.Should().ThrowAsync<SelectionTooLargeException>()).Which.Should().BeEquivalentTo(new { Ceiling = 2, MatchedAtLeast = 3 });
    }

    [Fact]
    public async Task TerminalOrder_ThrowsInvalidServiceOrderTransitionException_AndATypeWithoutPolicyInvalidOperationException()
    {
        var (host, http, sut) = await StartAsync(status: ServiceOrderStatus.Completed);
        await using var _ = host;
        using var __ = http;
        await host.SeedOrderAsync("ord-plain", "inspection");

        var terminal = () => sut.ApplyAsync(FenceRequest(), ClaimedAdministrator, CancellationToken.None);
        var noPolicy = () => sut.PreviewAsync(FenceRequest("ord-plain"), ClaimedAdministrator, CancellationToken.None);

        (await terminal.Should().ThrowAsync<InvalidServiceOrderTransitionException>()).Which.From.Should().Be(ServiceOrderStatus.Completed);
        await noPolicy.Should().ThrowExactlyAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ClientSideGuards_FireBeforeAnyRequest_LikeTheInProcessService()
    {
        var (host, http, sut) = await StartAsync();
        await using var _ = host;
        using var __ = http;

        await FluentActions.Invoking(() => sut.PreviewAsync(null!, ClaimedAdministrator, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => sut.ApplyAsync(FenceRequest(), null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => sut.PreviewAsync(FenceRequest(" "), ClaimedAdministrator, CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        FluentActions.Invoking(() => FenceRequest(pageSize: 201)).Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── RestMapSelectionClient ────────────────────────────────────────────────

    [Fact]
    public async Task MapSelectionClient_ResolveAsync_ReturnsTheCallersOrganizationsFeatureIds()
    {
        var (host, http, _) = await StartAsync(token: "outsider");
        await using var __ = host;
        using var ___ = http;
        var sut = new RestMapSelectionClient(http);

        var ids = await sut.ResolveAsync("polygon-fence", Fence(), CancellationToken.None);

        ids.Should().Equal("b-pole-1", "b-pole-2");
    }

    [Fact]
    public async Task MapSelectionClient_ResolveAsync_TranslatesFailuresToTheSameExceptionTypes()
    {
        var (host, http, _) = await StartAsync(configure: o => o.MaxResolved = 2);
        await using var __ = host;
        using var ___ = http;
        var sut = new RestMapSelectionClient(http);
        double[][][] unclosed = [[[0, 0], [10, 0], [10, 10], [0, 10]]];

        await FluentActions.Invoking(() => sut.ResolveAsync("polygon-fence", Fence(), CancellationToken.None))
            .Should().ThrowAsync<SelectionTooLargeException>();
        (await FluentActions.Invoking(() => sut.ResolveAsync("polygon-fence", Fence(unclosed), CancellationToken.None))
            .Should().ThrowAsync<InvalidSelectionFenceException>()).Which.Defect.Should().Be(SelectionFenceDefect.NotClosed);
        await FluentActions.Invoking(() => sut.ResolveAsync("no-such-strategy", Fence(), CancellationToken.None))
            .Should().ThrowAsync<KeyNotFoundException>();
        await FluentActions.Invoking(() => sut.ResolveAsync(" ", Fence(), CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Invoking(() => sut.ResolveAsync("polygon-fence", null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ACallerWithoutThePermission_GetsAnHttpRequestException_NotAGuessedDomainException()
    {
        var (host, http, sut) = await StartAsync(token: "no-permission");
        await using var _ = host;
        using var __ = http;

        var preview = () => sut.PreviewAsync(FenceRequest(), ClaimedAdministrator, CancellationToken.None);
        var resolve = () => new RestMapSelectionClient(http).ResolveAsync("polygon-fence", Fence(), CancellationToken.None);

        (await preview.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
        (await resolve.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }
}
