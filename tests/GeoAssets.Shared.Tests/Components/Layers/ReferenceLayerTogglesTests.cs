using Bunit;
using FluentAssertions;
using GeoAssets.Core.Localization;
using GeoAssets.Core.Rendering;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Shared.Components.Layers;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services.ReferenceLayers;
using GeoAssets.Shared.Tests.Components.ServiceOrders;
using GeoAssets.Shared.Tests.Services;
using GeoAssets.Shared.Tests.Services.ReferenceLayers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.Layers;

/// <summary>
/// XD01-331 — <see cref="ReferenceLayerToggles"/>, the "Reference: COELBA" switches in the layers panel: one per offered
/// organization, off by default, none (and no request) for a user with no entitlement, the state kept across panel
/// re-renders, and a switch removed without an error when the server refuses the organization mid-session.
/// </summary>
public sealed class ReferenceLayerTogglesTests : BunitContext
{
    private const string Div = "geoassets-map";
    private const string Reference = ReferenceLayerOffer.Permission;

    private static readonly Guid Bimbo = new("b1b00000-0000-4000-8000-000000000331");
    private static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000331");
    private static readonly Guid Embasa = new("e0ba5a00-0000-4000-8000-000000000331");

    private static readonly CrossOrgEntitlementsDto Entitled = new(
    [
        new CrossOrgGrantEntitlementDto(Coelba, "COELBA", "GeoFeature", null, [], [Reference], null, false, null),
        new CrossOrgGrantEntitlementDto(Embasa, "EMBASA", "GeoFeature", null, [], [Reference], null, false, null),
    ], []);

    private readonly RecordingMapInterop _map = new() { ViewBounds = new BoundingBox(-38.6, -13.05, -38.4, -12.8) };
    private readonly FakeReferenceFeatureClient _client = new();

    private ReferenceLayerService Arrange(CrossOrgEntitlementsDto entitlements)
    {
        Services.AddSingleton<IJsonStringLocalizer>(new KeyLocalizer());
        Services.AddSingleton<IMapInterop>(_map);
        Services.AddSingleton<IReferenceFeatureClient>(_client);
        Services.AddSingleton<ICrossOrgEntitlementsService>(new FakeEntitlements(entitlements));
        Services.AddSingleton<IGeoAuthorizationService>(new FakeAuthorization(new AuthorizationContext
        {
            User = new AppUser { Id = Guid.NewGuid(), Email = "t@bimbo.example", DisplayName = "T", OrganizationId = Bimbo, CreatedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc) },
            Roles = [],
            Claims = [],
            Permissions = [Reference],
        }));
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        Services.AddSingleton<ILogger<ReferenceLayerService>>(NullLogger<ReferenceLayerService>.Instance);
        Services.AddSingleton<ReferenceLayerService>();
        return Services.GetRequiredService<ReferenceLayerService>();
    }

    [Fact]
    public void AnEntitledUser_SeesOneSwitchPerOrganization_AllOff()
    {
        Arrange(Entitled);

        var toggles = Render<ReferenceLayerToggles>();

        toggles.WaitForAssertion(() => toggles.FindAll("input[role=switch]").Should().HaveCount(2));
        toggles.FindAll("input[role=switch]").Should().OnlyContain(i => !i.HasAttribute("checked"));
        toggles.FindAll("label.form-check-label").Select(l => l.GetAttribute("for"))
            .Should().Equal($"reference-layer-{Coelba:N}", $"reference-layer-{Embasa:N}");
        toggles.Find(".reference-layers-title").TextContent.Should().Be("map.layers.reference.title");
        toggles.Markup.Should().Contain("map.layers.reference.toggle").And.Contain("map.layers.reference.help");
    }

    [Fact]
    public void AUserWithNoEntitlement_SeesNoSwitch_AndNoRequestIsMade()
    {
        Arrange(CrossOrgEntitlementsDto.None);

        var toggles = Render<ReferenceLayerToggles>();

        toggles.Markup.Trim().Should().BeEmpty();
        _client.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SwitchingOn_DrawsTheLayer_AndTheStateSurvivesTheNextRender()
    {
        var service = Arrange(Entitled);
        await service.AttachAsync(Div);
        var toggles = Render<ReferenceLayerToggles>();
        toggles.WaitForAssertion(() => toggles.FindAll("input[role=switch]").Should().HaveCount(2));

        await toggles.Find($"#reference-layer-{Coelba:N}").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = true });

        service.IsEnabled(Coelba).Should().BeTrue();
        _map.ReferenceSetCalls.Should().ContainSingle().Which.LayerId.Should().Be(ReferenceLayerService.LayerIdFor(Coelba));

        var reopened = Render<ReferenceLayerToggles>();
        reopened.WaitForAssertion(() => reopened.Find($"#reference-layer-{Coelba:N}").HasAttribute("checked").Should().BeTrue());
        reopened.Find($"#reference-layer-{Embasa:N}").HasAttribute("checked").Should().BeFalse();
    }

    [Fact]
    public async Task SwitchingOff_ClearsTheLayer()
    {
        var service = Arrange(Entitled);
        await service.AttachAsync(Div);
        var toggles = Render<ReferenceLayerToggles>();
        toggles.WaitForAssertion(() => toggles.FindAll("input[role=switch]").Should().HaveCount(2));
        await toggles.Find($"#reference-layer-{Coelba:N}").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = true });

        await toggles.Find($"#reference-layer-{Coelba:N}").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = false });

        service.IsEnabled(Coelba).Should().BeFalse();
        _map.ReferenceClearCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task A403MidSession_RemovesThatSwitch_WithoutAnError()
    {
        var service = Arrange(Entitled);
        await service.AttachAsync(Div);
        var toggles = Render<ReferenceLayerToggles>();
        toggles.WaitForAssertion(() => toggles.FindAll("input[role=switch]").Should().HaveCount(2));
        _client.Respond = (_, _, _) => Task.FromResult(ReferenceFeatureResponse.Forbidden);

        await toggles.Find($"#reference-layer-{Coelba:N}").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = true });

        toggles.WaitForAssertion(() => toggles.FindAll("input[role=switch]").Should().ContainSingle()
            .Which.Id.Should().Be($"reference-layer-{Embasa:N}"));
        toggles.Markup.Should().NotContain("alert").And.NotContain("error");
        _map.ReferenceClearCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task TheLastWithdrawnOrganization_RemovesTheWholeSection()
    {
        var service = Arrange(new CrossOrgEntitlementsDto([Entitled.Grants[0]], []));
        await service.AttachAsync(Div);
        var toggles = Render<ReferenceLayerToggles>();
        toggles.WaitForAssertion(() => toggles.FindAll("input[role=switch]").Should().ContainSingle());
        _client.Respond = (_, _, _) => Task.FromResult(ReferenceFeatureResponse.Forbidden);

        await toggles.Find($"#reference-layer-{Coelba:N}").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = true });

        toggles.WaitForAssertion(() => toggles.Markup.Trim().Should().BeEmpty());
    }
}
