using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Selection.Strategies;
using GeoAssets.Workflow.Targets;
using Xunit;
using static GeoAssets.Workflow.Tests.Targets.SelectionServiceFixture;

namespace GeoAssets.Workflow.Tests.Targets;

/// <summary>
/// The guards of <see cref="OrderTargetSelectionService"/> (SEL-G1..G8) and the restrictions it
/// forces onto every selection (SEL-B1..B4). Each guard is run for preview and for apply.
/// </summary>
public sealed class OrderTargetSelectionServiceGuardTests
{
    private static Task Run(
        OrderTargetSelectionService sut, bool apply, TargetSelectionRequest request, WorkflowPrincipal principal,
        CancellationToken ct = default) =>
        apply ? sut.ApplyAsync(request, principal, ct) : sut.PreviewAsync(request, principal, ct);

    // ── SEL-G1 / SEL-G2 ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SEL_G1_NullRequestOrPrincipal_ThrowsArgumentNullException(bool apply)
    {
        using var fixture = new SelectionServiceFixture();

        await FluentActions.Invoking(() => Run(fixture.Sut, apply, null!, Admin)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => Run(fixture.Sut, apply, FenceRequest(), null!)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void SEL_G1_NullParameters_ThrowsArgumentNullException()
    {
        var act = () => new TargetSelectionRequest(OrderId, PolygonFenceSelectionStrategy.Id, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(false, "", "polygon-fence")]
    [InlineData(false, "   ", "polygon-fence")]
    [InlineData(false, "ord-1", "")]
    [InlineData(false, "ord-1", "  ")]
    [InlineData(true, "", "polygon-fence")]
    [InlineData(true, "ord-1", " ")]
    public async Task SEL_G2_BlankOrderIdOrStrategyId_ThrowsArgumentException(bool apply, string orderId, string strategyId)
    {
        using var fixture = new SelectionServiceFixture();
        await fixture.SeedOrderAsync();

        var act = () => Run(fixture.Sut, apply, new TargetSelectionRequest(orderId, strategyId, Fence()), Admin);

        await act.Should().ThrowAsync<ArgumentException>();
        fixture.Query.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(-1, 50)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 201)]
    public void SEL_G2_PagingOutOfRange_IsRejectedByTheRequestItself(int page, int pageSize)
    {
        var act = () => new TargetSelectionRequest(OrderId, PolygonFenceSelectionStrategy.Id, Fence(), page, pageSize);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SEL_G2_PagingOutOfRange_IsAlsoRejectedOnWith_AndTheBoundsAreAccepted()
    {
        var request = new TargetSelectionRequest(OrderId, PolygonFenceSelectionStrategy.Id, Fence(), page: 1, pageSize: 200);

        FluentActions.Invoking(() => request with { PageSize = 201 }).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => request with { Page = 0 }).Should().Throw<ArgumentOutOfRangeException>();
        (request with { PageSize = 1 }).PageSize.Should().Be(1);
    }

    // ── SEL-G3 / SEL-G4: before the target store is touched ───────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SEL_G3_UnknownOrder_ThrowsKeyNotFoundException_WithoutTouchingTheTargetStore(bool apply)
    {
        using var fixture = new SelectionServiceFixture();
        var store = new UntouchableTargetStore();

        var act = () => Run(fixture.Build(store, store), apply, FenceRequest("ord-missing"), Admin);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        store.Calls.Should().Be(0);
        fixture.Query.Calls.Should().Be(0);
    }

    [Fact]
    public async Task SEL_G4_Preview_PrincipalWithoutView_ThrowsOrderActionDenied_BeforeTheTargetStoreOrTheStrategy()
    {
        using var fixture = new SelectionServiceFixture(answer: Poles(3));
        await fixture.SeedOrderAsync();
        var store = new UntouchableTargetStore();

        var act = () => fixture.Build(store, store).PreviewAsync(FenceRequest(), Stranger, CancellationToken.None);

        var denied = (await act.Should().ThrowAsync<OrderActionDeniedException>()).Which;
        denied.Evaluation!.Allowed.Should().BeFalse();
        denied.Action.Should().Be(OrderActionType.View);
        denied.OrderId.Should().Be(OrderId);
        store.Calls.Should().Be(0);
        fixture.Query.Calls.Should().Be(0);
    }

    [Fact]
    public async Task SEL_G4_Apply_PrincipalWithViewButNotAnnotate_ThrowsOrderActionDenied_AndWritesNothing()
    {
        using var fixture = new SelectionServiceFixture(answer: Poles(3));
        var order = await fixture.SeedOrderAsync();
        var store = new UntouchableTargetStore();
        var sut = fixture.Build(store, store);

        var act = () => sut.ApplyAsync(FenceRequest(), Viewer, CancellationToken.None);

        (await act.Should().ThrowAsync<OrderActionDeniedException>()).Which.Action.Should().Be(OrderActionType.Annotate);
        store.Calls.Should().Be(0);
        order.SelectionSpec.Should().BeNull();
        order.ActionLog.Should().BeEmpty();
    }

    [Fact]
    public async Task SEL_G4_Preview_PrincipalWithViewOnly_IsAllowed()
    {
        using var fixture = new SelectionServiceFixture(answer: Poles(3));
        await fixture.SeedOrderAsync();

        var preview = await fixture.Sut.PreviewAsync(FenceRequest(), Viewer, CancellationToken.None);

        preview.Matched.Should().Be(3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SEL_G4_AnonymousPrincipal_IsDenied(bool apply)
    {
        using var fixture = new SelectionServiceFixture(answer: Poles(1));
        await fixture.SeedOrderAsync();

        var act = () => Run(fixture.Sut, apply, FenceRequest(), WorkflowPrincipal.Anonymous);

        await act.Should().ThrowAsync<OrderActionDeniedException>();
        fixture.Store.Snapshot().Should().BeEmpty();
    }

    // ── SEL-G5 .. SEL-G8 ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SEL_G5_OrderTypeNotRegistered_ThrowsKeyNotFoundException(bool apply)
    {
        using var fixture = new SelectionServiceFixture(registerOrderType: false);
        await fixture.SeedOrderAsync();

        var act = () => Run(fixture.Sut, apply, FenceRequest(), Admin);

        (await act.Should().ThrowAsync<KeyNotFoundException>()).Which.Message.Should().Contain(OrderTypeId);
        fixture.Query.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SEL_G6_OrderTypeWithoutTargetPolicy_ThrowsInvalidOperationException(bool apply)
    {
        using var fixture = new SelectionServiceFixture(registerOrderType: false);
        fixture.OrderTypes.Register(new OrderType { Id = OrderTypeId, DisplayName = "No targets" });
        await fixture.SeedOrderAsync();

        var act = () => Run(fixture.Sut, apply, FenceRequest(), Admin);

        await act.Should().ThrowExactlyAsync<InvalidOperationException>();
        fixture.Query.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SEL_G7_UnknownStrategy_ThrowsKeyNotFoundException_WithoutCallingAnyStrategy(bool apply)
    {
        using var fixture = new SelectionServiceFixture(answer: Poles(1));
        await fixture.SeedOrderAsync();

        var act = () => Run(fixture.Sut, apply, new TargetSelectionRequest(OrderId, "no-such-strategy", Fence()), Admin);

        (await act.Should().ThrowAsync<KeyNotFoundException>()).Which.Message.Should().Contain("no-such-strategy");
        fixture.Query.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(ServiceOrderStatus.Completed)]
    [InlineData(ServiceOrderStatus.Cancelled)]
    public async Task SEL_G8_Apply_OrderInATerminalState_ThrowsInvalidServiceOrderTransitionException_AndWritesNothing(string status)
    {
        using var fixture = new SelectionServiceFixture(answer: Poles(2));
        await fixture.SeedOrderAsync(status: status);

        var act = () => fixture.Sut.ApplyAsync(FenceRequest(), Admin, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>()).Which.From.Should().Be(status);
        fixture.Store.Snapshot().Should().BeEmpty();
        fixture.Query.Calls.Should().Be(0);
    }

    [Fact]
    public async Task SEL_G8_Preview_OrderInATerminalState_IsStillAllowed()
    {
        using var fixture = new SelectionServiceFixture(answer: Poles(2));
        await fixture.SeedOrderAsync(status: ServiceOrderStatus.Completed);

        var preview = await fixture.Sut.PreviewAsync(FenceRequest(), Admin, CancellationToken.None);

        preview.Matched.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StrategyRejectingItsParameters_PropagatesUnchanged_AndWritesNothing(bool apply)
    {
        using var fixture = new SelectionServiceFixture(answer: Poles(2));
        await fixture.SeedOrderAsync();
        var bowTie = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [PolygonFenceSelectionStrategy.RingsParameter] = new double[][][] { [[0, 0], [1, 1], [1, 0], [0, 1], [0, 0]] },
        };

        var act = () => Run(fixture.Sut, apply, FenceRequest(parameters: bowTie), Admin);

        (await act.Should().ThrowAsync<GeoAssets.Core.Services.InvalidSelectionFenceException>())
            .Which.Defect.Should().Be(GeoAssets.Core.Services.SelectionFenceDefect.SelfIntersecting);
        fixture.Store.Snapshot().Should().BeEmpty();
    }

    // ── SEL-B1: asset types ───────────────────────────────────────────────────

    [Fact]
    public async Task SEL_B1_NoAssetTypesRequested_ThePolicysTypesBecomeTheFilter()
    {
        using var fixture = new SelectionServiceFixture(Policy(assetTypeIds: ["pole", "tower"]), answer: Poles(2));
        await fixture.SeedOrderAsync();

        await fixture.Sut.PreviewAsync(FenceRequest(), Admin, CancellationToken.None);

        fixture.Query.Filter!.AssetTypeIds.Should().BeEquivalentTo("pole", "tower");
    }

    [Fact]
    public async Task SEL_B1_RequestedAssetTypes_AreIntersectedWithThePolicys_NeverWidened()
    {
        using var fixture = new SelectionServiceFixture(Policy(assetTypeIds: ["pole", "tower"]), answer: Poles(2));
        await fixture.SeedOrderAsync();
        var parameters = Fence();
        parameters[PolygonFenceSelectionStrategy.AssetTypeIdsParameter] = new[] { "tower", "manhole" };

        await fixture.Sut.PreviewAsync(FenceRequest(parameters: parameters), Admin, CancellationToken.None);

        fixture.Query.Filter!.AssetTypeIds.Should().Equal("tower");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SEL_B1_EmptyIntersection_MatchesNothing_WithoutCallingTheProvider(bool apply)
    {
        using var fixture = new SelectionServiceFixture(answer: Poles(4));
        var order = await fixture.SeedOrderAsync();
        var parameters = Fence();
        parameters[PolygonFenceSelectionStrategy.AssetTypeIdsParameter] = new[] { "manhole" };
        var request = FenceRequest(parameters: parameters);

        if (apply)
        {
            var result = await fixture.Sut.ApplyAsync(request, Admin, CancellationToken.None);
            result.Matched.Should().Be(0);
            result.Added.Should().Be(0);
            order.SelectionSpec!.ExecutedAt.Should().Be(Now.UtcDateTime);
        }
        else
        {
            var preview = await fixture.Sut.PreviewAsync(request, Admin, CancellationToken.None);
            preview.Matched.Should().Be(0);
            preview.FirstPage.Items.Should().BeEmpty();
        }

        fixture.Query.Calls.Should().Be(0);
        fixture.Store.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task SEL_B1_FeatureOfATypeOutsideThePolicy_ReturnedByASloppyStore_IsDropped()
    {
        using var fixture = new SelectionServiceFixture(answer: [Pole("p-1"), Pole("m-1", assetTypeId: "manhole")]);
        await fixture.SeedOrderAsync();

        var preview = await fixture.Sut.PreviewAsync(FenceRequest(), Admin, CancellationToken.None);

        preview.Matched.Should().Be(1);
        preview.FirstPage.Items.Should().ContainSingle().Which.FeatureId.Should().Be("p-1");
    }

    // ── SEL-B2: identifier-list attribute key ─────────────────────────────────

    [Fact]
    public async Task SEL_B2_IdentifierListWithoutAttributeKey_UsesThePolicysAliasAttribute()
    {
        using var fixture = new SelectionServiceFixture(answer: [Pole("p-1", "P-1")]);
        await fixture.SeedOrderAsync();

        await fixture.Sut.PreviewAsync(IdentifierRequest(attributeKey: null, "P-1"), Admin, CancellationToken.None);

        fixture.Query.AttributeKey.Should().Be(AliasKey);
        fixture.Query.Values.Should().Equal("P-1");
    }

    [Fact]
    public async Task SEL_B2_IdentifierListWithItsOwnAttributeKey_KeepsIt()
    {
        using var fixture = new SelectionServiceFixture(answer: [Pole("p-1", "P-1")]);
        await fixture.SeedOrderAsync();

        await fixture.Sut.PreviewAsync(IdentifierRequest(IdentifierListSelectionStrategy.FeatureIdKey, "p-1"), Admin, CancellationToken.None);

        fixture.Query.AttributeKey.Should().Be("id");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SEL_B2_NoAttributeKeyAndNoAliasAttributeOnThePolicy_ThrowsArgumentException(bool apply)
    {
        using var fixture = new SelectionServiceFixture(Policy(aliasAttributeKey: null), answer: [Pole("p-1")]);
        await fixture.SeedOrderAsync();

        var act = () => Run(fixture.Sut, apply, IdentifierRequest(attributeKey: null, "P-1"), Admin);

        await act.Should().ThrowAsync<ArgumentException>();
        fixture.Query.Calls.Should().Be(0);
    }

    // ── SEL-B3: organization ──────────────────────────────────────────────────

    [Fact]
    public async Task SEL_B3_TheOrdersOrganizationIsForcedIntoTheSelection_WhateverThePrincipalOrTheRequestSay()
    {
        using var fixture = new SelectionServiceFixture(answer: Poles(2));
        await fixture.SeedOrderAsync();
        // XD01-332: an Administrator of organization B no longer reaches an order of A by role
        // alone; this one does through a cross-organization grant, which is the legitimate
        // case the forcing below protects against.
        var outsider = new WorkflowPrincipal("admin-of-b", OrgB.ToString(), ["Administrator"], [], [])
        {
            OrgGrants = [new WorkflowOrgGrant(OrgA.ToString(), "ServiceOrder", ["serviceorders:view"], RequiredRole: null)],
        };
        var parameters = Fence();
        parameters["organizationId"] = OrgB.ToString();

        await fixture.Sut.PreviewAsync(FenceRequest(parameters: parameters), outsider, CancellationToken.None);

        fixture.Query.Filter!.OrganizationId.Should().Be(OrgA);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SEL_B3_FeaturesOfAnotherOrganization_ReturnedByASloppyStore_NeverBecomeTargets(bool apply)
    {
        using var fixture = new SelectionServiceFixture(answer: [Pole("a-1"), Pole("b-1", organizationId: OrgB), Pole("b-2", organizationId: OrgB)]);
        await fixture.SeedOrderAsync();

        if (apply)
        {
            (await fixture.Sut.ApplyAsync(FenceRequest(), Admin, CancellationToken.None)).Added.Should().Be(1);
            fixture.Store.Snapshot().Should().ContainSingle().Which.FeatureId.Should().Be("a-1");
        }
        else
        {
            var preview = await fixture.Sut.PreviewAsync(FenceRequest(), Admin, CancellationToken.None);
            preview.Matched.Should().Be(1);
            preview.FirstPage.Items.Should().OnlyContain(t => t.OrganizationId == OrgA);
        }
    }

    // ── SEL-B4: hard ceiling ──────────────────────────────────────────────────

    [Fact]
    public async Task SEL_B4_TheProviderIsAskedForOneMoreThanTheCeiling_ToDetectOverflow()
    {
        using var fixture = new SelectionServiceFixture(maxMatched: 5, answer: Poles(5));
        await fixture.SeedOrderAsync();

        var preview = await fixture.Sut.PreviewAsync(FenceRequest(), Admin, CancellationToken.None);

        fixture.Query.Filter!.MaxResults.Should().Be(6);
        preview.Matched.Should().Be(5, "exactly at the ceiling is accepted");
    }

    [Fact]
    public async Task SEL_B4_IdentifierList_IsBoundedByTheSameCeiling()
    {
        using var fixture = new SelectionServiceFixture(maxMatched: 5, answer: [Pole("p-1", "P-1")]);
        await fixture.SeedOrderAsync();

        await fixture.Sut.PreviewAsync(IdentifierRequest(attributeKey: null, "P-1"), Admin, CancellationToken.None);

        fixture.Query.Filter!.MaxResults.Should().Be(6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SEL_B4_MoreMatchesThanMaxMatched_ThrowsSelectionTooLargeException_AndWritesNothing(bool apply)
    {
        using var fixture = new SelectionServiceFixture(maxMatched: 5, answer: Poles(6));
        var order = await fixture.SeedOrderAsync();

        var act = () => Run(fixture.Sut, apply, FenceRequest(acceptTruncation: true), Admin);

        var tooLarge = (await act.Should().ThrowAsync<SelectionTooLargeException>()).Which;
        tooLarge.Ceiling.Should().Be(5);
        tooLarge.MatchedAtLeast.Should().Be(6);
        fixture.Store.Snapshot().Should().BeEmpty();
        order.ActionLog.Should().BeEmpty();
    }

    [Fact]
    public async Task SEL_B4_ASmallerMaxResultsFromTheCaller_IsHonoured_ALargerOneIsNot()
    {
        using var fixture = new SelectionServiceFixture(maxMatched: 5, answer: Poles(2));
        await fixture.SeedOrderAsync();
        var smaller = Fence();
        smaller[BoundedFeatureSelector.MaxResultsParameter] = 3;
        var larger = Fence();
        larger[BoundedFeatureSelector.MaxResultsParameter] = 1_000_000;

        await fixture.Sut.PreviewAsync(FenceRequest(parameters: smaller), Admin, CancellationToken.None);
        var forSmaller = fixture.Query.Filter!.MaxResults;
        await fixture.Sut.PreviewAsync(FenceRequest(parameters: larger), Admin, CancellationToken.None);
        var forLarger = fixture.Query.Filter!.MaxResults;

        forSmaller.Should().Be(3);
        forLarger.Should().Be(6);
    }

    [Fact]
    public void SEL_B4_Options_DefaultCeilingIsFiftyThousand_AndRejectsNonPositiveValues()
    {
        var options = new OrderTargetSelectionOptions();

        options.MaxMatched.Should().Be(50_000);
        options.MaxResolved.Should().Be(10_000);
        FluentActions.Invoking(() => options.MaxMatched = 0).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => options.MaxResolved = -1).Should().Throw<ArgumentOutOfRangeException>();
    }
}
