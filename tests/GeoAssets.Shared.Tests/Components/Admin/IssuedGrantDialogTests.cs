using Bunit;
using FluentAssertions;
using GeoAssets.Core.Localization;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Shared.Components.Admin;
using GeoAssets.Shared.Tests.Components.ServiceOrders;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.Admin;

/// <summary>XD01-411: the dialog an organization's administrator issues a grant to a contractor with, or edits one.</summary>
public sealed class IssuedGrantDialogTests : BunitContext
{
    private static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000411");
    private static readonly Guid Bimbo = new("b1b00000-0000-4000-8000-000000000411");
    private static readonly Guid Third = new("7e1d0000-0000-4000-8000-000000000411");

    private readonly FakeScopedRoleClient _client = new();
    private int _saved;
    private int _cancelled;

    public IssuedGrantDialogTests()
    {
        Services.AddSingleton<IJsonStringLocalizer>(new KeyLocalizer());
        Services.AddSingleton<IScopedRoleAdministrationClient>(_client);
        Services.AddSingleton<IOrganizationRepository>(new FixedOrganizationRepository(
            new Organization { Id = Coelba, Name = "COELBA", CreatedAt = default },
            new Organization { Id = Third, Name = "THIRD", CreatedAt = default },
            new Organization { Id = Bimbo, Name = "BIMBO", CreatedAt = default }));
        Services.AddSingleton<IPermissionRepository>(new FixedPermissionRepository(
            Permission("serviceorders:read"), Permission("serviceorders:complete"), Permission("features:read")));
        Services.AddSingleton<IRoleRepository>(new FixedRoleRepository(new AppRole { Name = "Contractor C1" }, new AppRole { Name = "Auditor" }));
    }

    private static AppPermission Permission(string code) => new()
    {
        Code = code, Resource = code.Split(':')[0], Action = code.Split(':')[1], Description = $"permissions.{code.Replace(':', '.')}",
    };

    private static OrganizationGrantDto Existing(DateTime? expiresAt = null) =>
        new(Guid.NewGuid(), Bimbo, Coelba, "ServiceOrder", "2927408", ["serviceorders:read"], "Contractor C1", expiresAt, "admin@coelba.test",
            new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), true)
        {
            RequiresScopedRole = true,
            OrderTypeIds = ["cut"],
            Source = OrganizationGrantSources.Administrator,
        };

    private IRenderedComponent<IssuedGrantDialog> RenderDialog(OrganizationGrantDto? grant = null, bool visible = true) =>
        Render<IssuedGrantDialog>(p => p
            .Add(c => c.Visible, visible)
            .Add(c => c.OrganizationId, Coelba)
            .Add(c => c.Grant, grant)
            .Add(c => c.OnSaved, EventCallback.Factory.Create(this, () => _saved++))
            .Add(c => c.OnCancel, EventCallback.Factory.Create(this, () => _cancelled++)));

    private static void ChooseBimboAndRead(IRenderedComponent<IssuedGrantDialog> cut)
    {
        cut.Find("#issued-grant-grantee").Change(Bimbo.ToString());
        cut.Find("#issued-grant-action-serviceorders-read").Change(true);
    }

    [Fact]
    public void Hidden_RendersNothing()
    {
        RenderDialog(visible: false).Markup.Trim().Should().BeEmpty();
    }

    [Fact]
    public void Issuing_OffersEveryOtherOrganizationByName_TheResourceTypes_AndThePermissions()
    {
        var cut = RenderDialog();

        cut.Find("h6").TextContent.Should().Be("admin.contractorAccess.grantForm.issueTitle");
        cut.FindAll("#issued-grant-grantee option").Select(o => o.TextContent).Should().Equal(
            "admin.contractorAccess.grantForm.chooseGrantee", "BIMBO", "THIRD");
        cut.FindAll("#issued-grant-resource-type option").Select(o => o.TextContent).Should().Equal(
            "admin.contractorAccess.grantForm.resourceAll",
            "admin.contractorAccess.grantForm.resource.ServiceOrder",
            "admin.contractorAccess.grantForm.resource.GeoFeature");
        cut.FindAll(".issued-grant-actions input[type=checkbox]").Select(i => i.GetAttribute("value")).Should().Equal(
            "features:read", "serviceorders:complete", "serviceorders:read");
        cut.Find(".issued-grant-actions").TextContent.Should().Contain("permissions.serviceorders.read");
    }

    [Fact]
    public void IssueStaysDisabled_UntilAContractorAndAnActionAreChosen()
    {
        var cut = RenderDialog();

        cut.Find("button.issued-grant-submit").HasAttribute("disabled").Should().BeTrue();
        cut.Find("#issued-grant-grantee").Change(Bimbo.ToString());
        cut.Find("button.issued-grant-submit").HasAttribute("disabled").Should().BeTrue();
        cut.Find("#issued-grant-action-serviceorders-read").Change(true);
        cut.Find("button.issued-grant-submit").HasAttribute("disabled").Should().BeFalse();
        cut.Find("#issued-grant-action-serviceorders-read").Change(false);
        cut.Find("button.issued-grant-submit").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Issue_SendsEveryField_ForTheOrganization_AndRaisesOnSaved()
    {
        var cut = RenderDialog();
        ChooseBimboAndRead(cut);
        cut.Find("#issued-grant-resource-type").Change("ServiceOrder");
        cut.Find("#issued-grant-scope").Change("  2927408 ");
        cut.Find("#issued-grant-order-types").Change("cut, reconnect, , Cut");
        cut.Find("#issued-grant-requires-scoped-role").Change(true);
        cut.Find("#issued-grant-required-role").Change("Contractor C1");
        cut.Find("#issued-grant-expires").Change("2026-12-31");

        cut.Find("button.issued-grant-submit").Click();

        cut.WaitForAssertion(() => _saved.Should().Be(1));
        var (organizationId, request) = _client.Issued.Single();
        organizationId.Should().Be(Coelba);
        request.GranteeOrganizationId.Should().Be(Bimbo);
        request.ResourceType.Should().Be("ServiceOrder");
        request.ScopeId.Should().Be("2927408");
        request.Rules.AllowedActions.Should().Equal("serviceorders:read");
        request.Rules.OrderTypeIds.Should().Equal("cut", "reconnect");
        request.Rules.RequiresScopedRole.Should().BeTrue();
        request.Rules.RequiredRole.Should().Be("Contractor C1");
        request.Rules.ExpiresAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
        request.Rules.ExpiresAt.Value.ToLocalTime().Should().Be(new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Local));
    }

    [Fact]
    public void LeftEmpty_TheGrantCoversEveryResourceEveryScope_AnyRole_AndNeverExpires()
    {
        var cut = RenderDialog();
        ChooseBimboAndRead(cut);

        cut.Find("button.issued-grant-submit").Click();

        cut.WaitForAssertion(() => _client.Issued.Should().ContainSingle());
        var request = _client.Issued.Single().Request;
        request.ResourceType.Should().BeNull();
        request.ScopeId.Should().BeNull();
        request.Rules.RequiredRole.Should().BeNull();
        request.Rules.RequiresScopedRole.Should().BeFalse();
        request.Rules.OrderTypeIds.Should().BeEmpty();
        request.Rules.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public void OrderTypes_AreOfferedOnlyForServiceOrders_AndAreNotSentForAnotherResourceType()
    {
        var cut = RenderDialog();
        ChooseBimboAndRead(cut);

        cut.FindAll("#issued-grant-order-types").Should().BeEmpty();
        cut.Find("#issued-grant-resource-type").Change("ServiceOrder");
        cut.Find("#issued-grant-order-types").Change("cut");
        cut.Find("#issued-grant-resource-type").Change("GeoFeature");
        cut.FindAll("#issued-grant-order-types").Should().BeEmpty();

        cut.Find("button.issued-grant-submit").Click();

        cut.WaitForAssertion(() => _client.Issued.Should().ContainSingle());
        _client.Issued.Single().Request.Rules.OrderTypeIds.Should().BeEmpty();
    }

    [Fact]
    public void Editing_ShowsTheGrant_WithItsKeyFixed()
    {
        var cut = RenderDialog(Existing());

        cut.Find("h6").TextContent.Should().Be("admin.contractorAccess.grantForm.editTitle");
        cut.Find("#issued-grant-grantee").GetAttribute("value").Should().Be("BIMBO");
        cut.Find("#issued-grant-grantee").HasAttribute("disabled").Should().BeTrue();
        cut.Find("#issued-grant-resource-type").HasAttribute("disabled").Should().BeTrue();
        cut.Find("#issued-grant-scope").HasAttribute("disabled").Should().BeTrue();
        cut.Find("#issued-grant-scope").GetAttribute("value").Should().Be("2927408");
        cut.Find("#issued-grant-action-serviceorders-read").HasAttribute("checked").Should().BeTrue();
        cut.Find("#issued-grant-action-features-read").HasAttribute("checked").Should().BeFalse();
        cut.Find("#issued-grant-order-types").GetAttribute("value").Should().Be("cut");
        cut.Find("#issued-grant-requires-scoped-role").HasAttribute("checked").Should().BeTrue();
        cut.Find("button.issued-grant-submit").TextContent.Trim().Should().Be("admin.contractorAccess.grantForm.save");
    }

    [Fact]
    public void Save_SendsTheNewRules_ForTheGrant_AndKeepsAnUntouchedExpiryExactly()
    {
        var expiresAt = new DateTime(2027, 1, 15, 2, 59, 59, DateTimeKind.Utc);
        var grant = Existing(expiresAt);
        _client.IssuedGrants.Add(grant);
        var cut = RenderDialog(grant);

        cut.Find("#issued-grant-action-serviceorders-complete").Change(true);
        cut.Find("#issued-grant-requires-scoped-role").Change(false);
        cut.Find("button.issued-grant-submit").Click();

        cut.WaitForAssertion(() => _saved.Should().Be(1));
        var (organizationId, grantId, rules) = _client.Updated.Single();
        organizationId.Should().Be(Coelba);
        grantId.Should().Be(grant.Id);
        rules.AllowedActions.Should().Equal("serviceorders:complete", "serviceorders:read");
        rules.RequiresScopedRole.Should().BeFalse();
        rules.OrderTypeIds.Should().Equal("cut");
        rules.RequiredRole.Should().Be("Contractor C1");
        rules.ExpiresAt.Should().Be(expiresAt);
        _client.Issued.Should().BeEmpty();
    }

    [Fact]
    public void Editing_StillOffersAnActionTheGrantHoldsThatIsNoLongerAPermission()
    {
        var grant = Existing() with { AllowedActions = ["serviceorders:read", "legacy:thing"] };

        var cut = RenderDialog(grant);

        cut.Find("#issued-grant-action-legacy-thing").HasAttribute("checked").Should().BeTrue();
    }

    [Theory]
    [InlineData(409, IssuedGrantProblems.Exists, "admin.contractorAccess.grantForm.error.exists")]
    [InlineData(409, IssuedGrantProblems.ServiceOwned, "admin.contractorAccess.grantForm.error.serviceOwned")]
    [InlineData(409, IssuedGrantProblems.Conflict, "admin.contractorAccess.grantForm.error.conflict")]
    [InlineData(400, IssuedGrantProblems.Invalid, "admin.contractorAccess.grantForm.error.invalid")]
    [InlineData(404, IssuedGrantProblems.NotFound, "admin.contractorAccess.grantForm.error.notFound")]
    [InlineData(403, ScopedRoleAssignmentProblems.Forbidden, "admin.contractorAccess.grantForm.error.forbidden")]
    [InlineData(403, null, "admin.contractorAccess.grantForm.error.forbidden")]
    [InlineData(500, null, "admin.contractorAccess.grantForm.error.generic")]
    public void ARefusal_StaysInTheDialog_ExplainedByItsCode(int status, string? code, string expectedKey)
    {
        _client.GrantWriteFailure = new ScopedRoleAdministrationException(status, code, "refused");
        var cut = RenderDialog();
        ChooseBimboAndRead(cut);

        cut.Find("button.issued-grant-submit").Click();

        cut.WaitForAssertion(() => cut.Find(".attribute-errors").TextContent.Should().Be(expectedKey));
        _saved.Should().Be(0);
        cut.FindAll(".dialog-box").Should().ContainSingle("the dialog stays open");
    }

    [Fact]
    public void ANetworkFailure_IsTheGenericError()
    {
        _client.GrantWriteFailure = new HttpRequestException("down");
        var cut = RenderDialog();
        ChooseBimboAndRead(cut);

        cut.Find("button.issued-grant-submit").Click();

        cut.WaitForAssertion(() => cut.Find(".attribute-errors").TextContent.Should().Be("admin.contractorAccess.grantForm.error.generic"));
    }

    [Fact]
    public void WithNoOtherOrganization_SaysSo_AndOffersOnlyCancel()
    {
        Services.AddSingleton<IOrganizationRepository>(new FixedOrganizationRepository(
            new Organization { Id = Coelba, Name = "COELBA", CreatedAt = default }));

        var cut = RenderDialog();

        cut.Find(".issued-grant-no-grantees").TextContent.Should().Be("admin.contractorAccess.grantForm.noGrantees");
        cut.FindAll("select").Should().BeEmpty();
        cut.Find(".dialog-actions button").Click();
        _cancelled.Should().Be(1);
    }

    [Fact]
    public void Reopening_ForgetsThePreviousAttempt()
    {
        _client.GrantWriteFailure = new HttpRequestException("down");
        var cut = RenderDialog();
        ChooseBimboAndRead(cut);
        cut.Find("button.issued-grant-submit").Click();
        cut.WaitForAssertion(() => cut.FindAll(".attribute-errors").Should().ContainSingle());

        cut.Render(p => p.Add(c => c.Visible, false));
        cut.Render(p => p.Add(c => c.Visible, true));

        cut.FindAll(".attribute-errors").Should().BeEmpty();
        cut.Find("#issued-grant-action-serviceorders-read").HasAttribute("checked").Should().BeFalse();
        cut.Find("button.issued-grant-submit").HasAttribute("disabled").Should().BeTrue();
    }
}
