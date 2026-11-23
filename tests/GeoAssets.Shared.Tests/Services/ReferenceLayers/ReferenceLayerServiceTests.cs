using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Shared.Services.ReferenceLayers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.ReferenceLayers;

/// <summary>
/// XD01-331 — <see cref="ReferenceLayerService"/>: what is offered (and to whom nothing is), the per-organization toggle,
/// the debounced and superseded viewport loads, a mid-session <c>403</c>, and that reference features never reach the
/// user's own features. The map is a <see cref="RecordingMapInterop"/>, whose every feature member throws: a test that
/// completes proves the reference path rendered, removed or cleared none of the user's features.
/// </summary>
public sealed class ReferenceLayerServiceTests : IDisposable
{
    private const string Div = "geoassets-map";
    private const string Reference = ReferenceLayerOffer.Permission;

    private static readonly Guid Bimbo = new("b1b00000-0000-4000-8000-000000000331");
    private static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000331");
    private static readonly Guid Embasa = new("e0ba5a00-0000-4000-8000-000000000331");
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);

    private static readonly BoundingBox Salvador = new(-38.6, -13.05, -38.4, -12.8);
    private static readonly BoundingBox Feira = new(-39.1, -12.4, -38.8, -12.1);

    private static readonly CrossOrgEntitlementsDto Entitled = new(
    [
        new CrossOrgGrantEntitlementDto(Coelba, "COELBA", "GeoFeature", null, [], [Reference], null, false, null),
        new CrossOrgGrantEntitlementDto(Embasa, "EMBASA", null, null, [], [Reference], null, false, null),
    ], []);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly RecordingMapInterop _map = new() { ViewBounds = Salvador };
    private readonly FakeReferenceFeatureClient _client = new();
    private readonly FakeEntitlements _entitlements = new(Entitled);
    private readonly FakeAuthorization _authorization = new(Home(Reference));
    private readonly ReferenceLayerService _sut;

    public ReferenceLayerServiceTests() =>
        _sut = new ReferenceLayerService(_map, _client, _entitlements, _authorization, _time, NullLogger<ReferenceLayerService>.Instance);

    public void Dispose() => _sut.Dispose();

    private static AuthorizationContext Home(params string[] permissions) => new()
    {
        User = new AppUser { Id = Guid.NewGuid(), Email = "t@bimbo.example", DisplayName = "T", OrganizationId = Bimbo, CreatedAt = Now.UtcDateTime },
        Roles = [],
        Claims = [],
        Permissions = permissions,
    };

    private static string LayerOf(Guid organizationId) => ReferenceLayerService.LayerIdFor(organizationId);

    /// <summary>A settled view: the change, then the debounce interval elapsing.</summary>
    private Task ViewportAsync(BoundingBox bbox)
    {
        var settled = _sut.OnViewportChangedAsync(Div, bbox.MinLon, bbox.MinLat, bbox.MaxLon, bbox.MaxLat);
        _time.Advance(ReferenceLayerService.DebounceInterval);
        return settled;
    }

    private async Task ReadyWithCoelbaOnAsync()
    {
        await _sut.LoadOrganizationsAsync();
        await _sut.AttachAsync(Div);
        await _sut.SetEnabledAsync(Coelba, true);
    }

    // ── What is offered ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnEntitledUser_IsOfferedEachOrganization_AllOffByDefault()
    {
        var offered = await _sut.LoadOrganizationsAsync();

        offered.Should().Equal(new ReferenceOrganization(Coelba, "COELBA"), new ReferenceOrganization(Embasa, "EMBASA"));
        _sut.Organizations.Should().BeSameAs(offered);
        _sut.IsEnabled(Coelba).Should().BeFalse();
        _sut.IsEnabled(Embasa).Should().BeFalse();
    }

    [Fact]
    public async Task AUserWithNoEntitlement_IsOfferedNothing_AndNoRequestIsMade()
    {
        _entitlements.Current = CrossOrgEntitlementsDto.None;

        (await _sut.LoadOrganizationsAsync()).Should().BeEmpty();
        await _sut.AttachAsync(Div);
        await _sut.SetEnabledAsync(Coelba, true);
        await ViewportAsync(Salvador);

        _authorization.Reads.Should().Be(0, "without a candidate grant the home context is not even read");
        _client.Requests.Should().BeEmpty();
        _map.ReferenceSetCalls.Should().BeEmpty();
        _map.ViewBoundsCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task AGrantTheServerWouldNotHonour_OffersNothing()
    {
        _entitlements.Current = new CrossOrgEntitlementsDto(
            [new CrossOrgGrantEntitlementDto(Coelba, "COELBA", "Feature", null, [], [Reference], null, false, null)], []);

        (await _sut.LoadOrganizationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AFailedLookup_OffersNothing_WithoutThrowing()
    {
        _entitlements.FailWith = new HttpRequestException("kernel down");

        (await _sut.LoadOrganizationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public Task ACancelledLookup_Throws()
    {
        _entitlements.FailWith = new OperationCanceledException();

        return FluentActions.Awaiting(() => _sut.LoadOrganizationsAsync()).Should().ThrowAsync<OperationCanceledException>();
    }

    // ── The toggle ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SwitchingOn_LoadsTheLayerAtOnceForTheMapsCurrentView()
    {
        await ReadyWithCoelbaOnAsync();

        _sut.IsEnabled(Coelba).Should().BeTrue();
        _map.ViewBoundsCalls.Should().Equal(Div);
        _client.Requests.Should().ContainSingle().Which.Should().Match<(Guid OrganizationId, BoundingBox Bbox, CancellationToken Token)>(
            r => r.OrganizationId == Coelba && r.Bbox == Salvador);
        var drawn = _map.ReferenceSetCalls.Should().ContainSingle().Subject;
        drawn.DivId.Should().Be(Div);
        drawn.LayerId.Should().Be(LayerOf(Coelba)).And.Be($"reference-{Coelba:N}");
        drawn.Json.Should().Be(FakeReferenceFeatureClient.Body(Coelba, Salvador));
        drawn.Options.Should().Be(ReferenceLayerService.LayerOptions);
        drawn.Options.LabelMinZoom.Should().Be(16);
    }

    [Fact]
    public async Task SwitchingOnBeforeTheMapExists_LoadsWhenItIsAttached()
    {
        await _sut.LoadOrganizationsAsync();
        await _sut.SetEnabledAsync(Coelba, true);
        _client.Requests.Should().BeEmpty();

        await _sut.AttachAsync(Div);

        _map.ReferenceSetCalls.Should().ContainSingle().Which.LayerId.Should().Be(LayerOf(Coelba));
    }

    [Fact]
    public async Task SwitchingOnAnOrganizationThatIsNotOffered_DoesNothing()
    {
        await _sut.LoadOrganizationsAsync();
        await _sut.AttachAsync(Div);

        await _sut.SetEnabledAsync(Guid.NewGuid(), true);

        _client.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SwitchingOff_ClearsTheLayer_AndLaterViewsLoadNothingForIt()
    {
        await ReadyWithCoelbaOnAsync();

        await _sut.SetEnabledAsync(Coelba, false);
        await ViewportAsync(Feira);

        _sut.IsEnabled(Coelba).Should().BeFalse();
        _map.ReferenceClearCalls.Should().Equal((Div, LayerOf(Coelba)));
        _client.Requests.Should().ContainSingle("only the load made when it was switched on");
    }

    [Fact]
    public async Task SwitchingOffWhatIsOff_TouchesNothing()
    {
        await _sut.LoadOrganizationsAsync();
        await _sut.AttachAsync(Div);

        await _sut.SetEnabledAsync(Coelba, false);

        _map.ReferenceClearCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task TheToggleStateOutlivesTheMap_ForTheSession()
    {
        await ReadyWithCoelbaOnAsync();
        _sut.Detach(Div);
        _map.ViewBounds = Feira;

        await _sut.AttachAsync(Div);

        _sut.IsEnabled(Coelba).Should().BeTrue();
        _map.ReferenceSetCalls.Select(c => c.Json).Should().Equal(
            FakeReferenceFeatureClient.Body(Coelba, Salvador), FakeReferenceFeatureClient.Body(Coelba, Feira));
    }

    // ── Following the viewport ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PanningLoadsTheNewArea_ReplacingTheOldOne_OnlyAfterTheDebounce()
    {
        await ReadyWithCoelbaOnAsync();

        var settled = _sut.OnViewportChangedAsync(Div, Feira.MinLon, Feira.MinLat, Feira.MaxLon, Feira.MaxLat);
        _time.Advance(ReferenceLayerService.DebounceInterval - TimeSpan.FromMilliseconds(1));
        _client.Requests.Should().HaveCount(1, "the view has not settled yet");

        _time.Advance(TimeSpan.FromMilliseconds(1));
        await settled;

        _client.Requests.Should().HaveCount(2);
        _client.Requests[1].Bbox.Should().Be(Feira);
        _map.ReferenceSetCalls.Should().HaveCount(2);
        _map.ReferenceSetCalls[1].LayerId.Should().Be(LayerOf(Coelba), "the same layer is redrawn: the old area is dropped");
        _map.ReferenceSetCalls[1].Json.Should().Be(FakeReferenceFeatureClient.Body(Coelba, Feira));
    }

    [Fact]
    public async Task ASupersededViewportChange_IsNeverRequested()
    {
        await ReadyWithCoelbaOnAsync();

        var first = _sut.OnViewportChangedAsync(Div, Feira.MinLon, Feira.MinLat, Feira.MaxLon, Feira.MaxLat);
        _time.Advance(TimeSpan.FromMilliseconds(100));
        var second = _sut.OnViewportChangedAsync(Div, Salvador.MinLon, Salvador.MinLat, Salvador.MaxLon, Salvador.MaxLat);
        await first;
        _time.Advance(ReferenceLayerService.DebounceInterval);
        await second;

        _client.Requests.Select(r => r.Bbox).Should().Equal(Salvador, Salvador);
    }

    [Fact]
    public async Task ANewViewportCancelsTheLoadInFlight_AndItsLateAnswerIsNeverDrawn()
    {
        await ReadyWithCoelbaOnAsync();
        var pending = new TaskCompletionSource<ReferenceFeatureResponse>();
        _client.Respond = (_, _, _) => pending.Task;

        var feira = _sut.OnViewportChangedAsync(Div, Feira.MinLon, Feira.MinLat, Feira.MaxLon, Feira.MaxLat);
        _time.Advance(ReferenceLayerService.DebounceInterval);
        await _client.WhenRequestedAsync(2);
        var inFlight = _client.Requests[1];
        inFlight.Bbox.Should().Be(Feira);

        _client.Respond = (organizationId, bbox, _) => Task.FromResult(ReferenceFeatureResponse.Granted(FakeReferenceFeatureClient.Body(organizationId, bbox)));
        var salvador = _sut.OnViewportChangedAsync(Div, Salvador.MinLon, Salvador.MinLat, Salvador.MaxLon, Salvador.MaxLat);
        inFlight.Token.IsCancellationRequested.Should().BeTrue("a newer view supersedes the load at once");

        pending.SetResult(ReferenceFeatureResponse.Granted("""[{"id":"stale"}]"""));
        await feira;
        _time.Advance(ReferenceLayerService.DebounceInterval);
        await salvador;

        _map.ReferenceSetCalls.Select(c => c.Json).Should().NotContain("""[{"id":"stale"}]""")
            .And.EndWith(FakeReferenceFeatureClient.Body(Coelba, Salvador));
    }

    [Fact]
    public async Task ACancelledRequest_IsDroppedQuietly()
    {
        await ReadyWithCoelbaOnAsync();
        _client.Respond = (_, _, _) => Task.FromCanceled<ReferenceFeatureResponse>(new CancellationToken(canceled: true));

        var feira = _sut.OnViewportChangedAsync(Div, Feira.MinLon, Feira.MinLat, Feira.MaxLon, Feira.MaxLat);
        _time.Advance(ReferenceLayerService.DebounceInterval);

        await FluentActions.Awaiting(() => feira).Should().NotThrowAsync();
        _map.ReferenceSetCalls.Should().ContainSingle("only the first load was drawn");
    }

    [Fact]
    public async Task EachSwitchedOnOrganization_IsLoadedForTheNewView()
    {
        await ReadyWithCoelbaOnAsync();
        await _sut.SetEnabledAsync(Embasa, true);

        await ViewportAsync(Feira);

        _client.Requests.Skip(2).Select(r => r.OrganizationId).Should().BeEquivalentTo([Coelba, Embasa]);
        _map.ReferenceSetCalls.Skip(2).Select(c => c.LayerId).Should().BeEquivalentTo([LayerOf(Coelba), LayerOf(Embasa)]);
    }

    [Fact]
    public async Task AWrappedView_IsNormalizedBeforeItIsRequested()
    {
        await ReadyWithCoelbaOnAsync();

        var settled = _sut.OnViewportChangedAsync(Div, Salvador.MinLon + 360, -95, Salvador.MaxLon + 360, -12.8);
        _time.Advance(ReferenceLayerService.DebounceInterval);
        await settled;

        var asked = _client.Requests[^1].Bbox;
        asked.MinLon.Should().BeApproximately(Salvador.MinLon, 1e-9);
        asked.MaxLon.Should().BeApproximately(Salvador.MaxLon, 1e-9);
        asked.MinLat.Should().Be(-90);
    }

    [Fact]
    public async Task AViewThatCannotBeAskedFor_LoadsNothing()
    {
        await ReadyWithCoelbaOnAsync();

        await _sut.OnViewportChangedAsync(Div, double.NaN, 0, 1, 1);
        _time.Advance(ReferenceLayerService.DebounceInterval);

        _client.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task AViewportOfAnotherMap_IsIgnored()
    {
        await ReadyWithCoelbaOnAsync();

        await _sut.OnViewportChangedAsync("other-map", Feira.MinLon, Feira.MinLat, Feira.MaxLon, Feira.MaxLat);
        _time.Advance(ReferenceLayerService.DebounceInterval);

        _client.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task WithNothingSwitchedOn_AViewportChangeIsRemembered_AndUsedWhenALayerIsSwitchedOn()
    {
        await _sut.LoadOrganizationsAsync();
        await _sut.AttachAsync(Div);
        await _sut.OnViewportChangedAsync(Div, Feira.MinLon, Feira.MinLat, Feira.MaxLon, Feira.MaxLat);
        _client.Requests.Should().BeEmpty();

        await _sut.SetEnabledAsync(Coelba, true);

        _map.ViewBoundsCalls.Should().BeEmpty("the last settled view is already known");
        _client.Requests.Should().ContainSingle().Which.Bbox.Should().Be(Feira);
    }

    [Fact]
    public async Task Detaching_CancelsTheDebounceAndTheLoads_AndForgetsTheMap()
    {
        await ReadyWithCoelbaOnAsync();
        var pending = _sut.OnViewportChangedAsync(Div, Feira.MinLon, Feira.MinLat, Feira.MaxLon, Feira.MaxLat);

        _sut.Detach(Div);
        await pending;
        _time.Advance(ReferenceLayerService.DebounceInterval);
        await _sut.SetEnabledAsync(Embasa, true);

        _client.Requests.Should().ContainSingle("nothing is loaded for a map that is gone");
    }

    [Fact]
    public async Task DetachingAnotherMap_ChangesNothing()
    {
        await ReadyWithCoelbaOnAsync();

        _sut.Detach("other-map");
        await ViewportAsync(Feira);

        _client.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task AMapThatReportsNoView_LoadsNothing_UntilItsViewSettles()
    {
        _map.ViewBounds = null;
        await ReadyWithCoelbaOnAsync();
        _client.Requests.Should().BeEmpty();

        await ViewportAsync(Feira);

        _client.Requests.Should().ContainSingle().Which.Bbox.Should().Be(Feira);
    }

    // ── A revoked entitlement ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A403_ClearsTheLayer_AndWithdrawsTheToggle_Silently()
    {
        await ReadyWithCoelbaOnAsync();
        var changed = 0;
        _sut.Changed += (_, _) => changed++;
        _client.Respond = (_, _, _) => Task.FromResult(ReferenceFeatureResponse.Forbidden);

        await FluentActions.Awaiting(() => ViewportAsync(Feira)).Should().NotThrowAsync();

        _map.ReferenceClearCalls.Should().Equal((Div, LayerOf(Coelba)));
        _sut.IsEnabled(Coelba).Should().BeFalse();
        _sut.Organizations.Select(o => o.Id).Should().Equal(Embasa);
        changed.Should().Be(1);
        _map.ReferenceSetCalls.Should().ContainSingle("the refused answer is never drawn");
    }

    [Fact]
    public async Task AWithdrawnOrganization_StaysWithdrawnForTheSession_EvenWhileTheEntitlementsStillNameIt()
    {
        await _sut.LoadOrganizationsAsync();
        await _sut.AttachAsync(Div);
        _client.Respond = (_, _, _) => Task.FromResult(ReferenceFeatureResponse.Forbidden);
        await _sut.SetEnabledAsync(Coelba, true);

        var offered = await _sut.LoadOrganizationsAsync();
        await _sut.SetEnabledAsync(Coelba, true);

        offered.Select(o => o.Id).Should().Equal(Embasa);
        _client.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task AnOrganizationNoLongerEntitled_IsSwitchedOffAndCleared_WhenTheOfferIsReloaded()
    {
        await ReadyWithCoelbaOnAsync();
        _entitlements.Current = new CrossOrgEntitlementsDto([Entitled.Grants[1]], []);

        var offered = await _sut.LoadOrganizationsAsync();

        offered.Select(o => o.Id).Should().Equal(Embasa);
        _sut.IsEnabled(Coelba).Should().BeFalse();
        _map.ReferenceClearCalls.Should().Equal((Div, LayerOf(Coelba)));
    }

    // ── Failures that are not a refusal ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnyOtherFailure_LeavesTheLayerAndTheToggleAsTheyWere()
    {
        await ReadyWithCoelbaOnAsync();
        _client.Respond = (_, _, _) => Task.FromException<ReferenceFeatureResponse>(new HttpRequestException("503"));

        await FluentActions.Awaiting(() => ViewportAsync(Feira)).Should().NotThrowAsync();

        _sut.IsEnabled(Coelba).Should().BeTrue();
        _sut.Organizations.Should().HaveCount(2);
        _map.ReferenceClearCalls.Should().BeEmpty();
        _map.ReferenceSetCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task AFailedDraw_IsLogged_NotThrown()
    {
        await _sut.LoadOrganizationsAsync();
        await _sut.AttachAsync(Div);
        _map.FailReferenceSetWith = new JSException("leaflet");

        await FluentActions.Awaiting(() => _sut.SetEnabledAsync(Coelba, true)).Should().NotThrowAsync();
        _sut.IsEnabled(Coelba).Should().BeTrue();
    }

    // ── Never the user's own features ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReferenceFeatures_NeverReachTheAssetProvider_TheFeatureListOrAnExport()
    {
        var provider = new TestAssetProvider();
        var own = new GeoFeature { Id = "own-pole" };
        provider.Add(own);

        await ReadyWithCoelbaOnAsync();
        await _sut.SetEnabledAsync(Embasa, true);
        await ViewportAsync(Feira);

        // The whole flow ran against a map double whose feature members all throw, and drew four times…
        _map.ReferenceSetCalls.Should().HaveCount(4);
        // …while the provider — the feature list's, auto-save's, selection's, export's and the project session's source —
        // holds exactly what it held before.
        provider.GetAll().Should().ContainSingle().Which.Should().BeSameAs(own);
        typeof(ReferenceLayerService).GetConstructors().Single().GetParameters().Select(p => p.ParameterType)
            .Should().NotContain(t => typeof(IAssetProvider).IsAssignableFrom(t) || typeof(IStorageService).IsAssignableFrom(t),
                "the service has no way to reach the user's features or their storage");
    }

    [Fact]
    public async Task Dispose_CancelsWhatIsInFlight()
    {
        await ReadyWithCoelbaOnAsync();
        _client.Respond = (_, _, _) => new TaskCompletionSource<ReferenceFeatureResponse>().Task;
        _ = _sut.OnViewportChangedAsync(Div, Feira.MinLon, Feira.MinLat, Feira.MaxLon, Feira.MaxLat);
        _time.Advance(ReferenceLayerService.DebounceInterval);
        await _client.WhenRequestedAsync(2);

        _sut.Dispose();

        _client.Requests[1].Bbox.Should().Be(Feira);
        _client.Requests[1].Token.IsCancellationRequested.Should().BeTrue();
    }
}
