using Bunit;
using FluentAssertions;
using GeoAssets.Core.Localization;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Shared.Components.Admin;
using GeoAssets.Shared.Tests.Components.ServiceOrders;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.Admin;

/// <summary>XD01-327: the "Contractor access" panel of the organization admin surface.</summary>
public sealed class ContractorAccessPanelTests : BunitContext
{
    private static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000327");
    private static readonly Guid Bimbo = new("b1b00000-0000-4000-8000-000000000327");

    private readonly FakeScopedRoleClient _client = new();

    public ContractorAccessPanelTests()
    {
        Services.AddSingleton<IJsonStringLocalizer>(new KeyLocalizer());
        Services.AddSingleton<IOrganizationRepository>(new FixedOrganizationRepository(
            new Organization { Id = Coelba, Name = "COELBA", CreatedAt = default },
            new Organization { Id = Bimbo, Name = "BIMBO", CreatedAt = default }));
        Services.AddSingleton<IRoleRepository>(new FixedRoleRepository(new AppRole { Name = "Contractor C1" }));
        Services.AddSingleton<IPermissionRepository>(new FixedPermissionRepository(
            new AppPermission { Code = "serviceorders:read", Resource = "serviceorders", Action = "read", Description = "permissions.serviceorders.read" },
            new AppPermission { Code = "serviceorders:complete", Resource = "serviceorders", Action = "complete", Description = "permissions.serviceorders.complete" }));
    }

    private IRenderedComponent<ContractorAccessPanel> RenderPanel(bool withClient = true)
    {
        if (withClient) Services.AddSingleton<IScopedRoleAdministrationClient>(_client);
        return Render<ContractorAccessPanel>(p => p.Add(c => c.OrganizationId, Coelba));
    }

    private static OrganizationGrantDto Grant(bool requiresScopedRole, params string[] orderTypes) =>
        new(Guid.NewGuid(), Bimbo, Coelba, "ServiceOrder", null, ["serviceorders:read", "serviceorders:complete"], null, null, "seed",
            new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), true)
        {
            RequiresScopedRole = requiresScopedRole,
            OrderTypeIds = orderTypes,
        };

    [Fact]
    public void WithoutTheClient_RendersNothing()
    {
        var cut = RenderPanel(withClient: false);

        cut.Markup.Trim().Should().BeEmpty();
    }

    [Fact]
    public void Grants_ShowTheContractor_TheOrderTypes_AndWhetherARoleHereIsRequired()
    {
        _client.IssuedGrants.Add(Grant(requiresScopedRole: true, "cut", "reconnect"));
        _client.IssuedGrants.Add(Grant(requiresScopedRole: false));

        var cut = RenderPanel();

        var rows = cut.FindAll("table.contractor-grants tbody tr");
        rows.Should().HaveCount(2);
        rows[0].QuerySelector("td")!.TextContent.Should().Be("BIMBO");
        rows[0].QuerySelector(".contractor-grant-order-types")!.TextContent.Should().Be("cut, reconnect");
        rows[0].QuerySelector(".contractor-grant-scoped .badge-builtin")!.TextContent.Should().Be("admin.contractorAccess.yes");
        rows[1].QuerySelector(".contractor-grant-order-types")!.TextContent.Should().Be("admin.contractorAccess.all");
        rows[1].QuerySelector(".contractor-grant-scoped")!.TextContent.Trim().Should().Be("admin.contractorAccess.no");
        rows[1].TextContent.Should().Contain("admin.contractorAccess.never");
    }

    [Fact]
    public void NoGrantsAndNoAssignments_SayEach()
    {
        var cut = RenderPanel();

        cut.Find(".contractor-grants-empty").TextContent.Should().Be("admin.contractorAccess.grantsEmpty");
        cut.Find(".contractor-assignments-empty").TextContent.Should().Be("admin.contractorAccess.assignmentsEmpty");
    }

    [Fact]
    public void Assignments_ShowTheUser_TheirOrganization_TheRole_AndWhoAssignedIt()
    {
        _client.Assignments.Add(FakeScopedRoleClient.Assignment(Coelba, "Tiago", "tiago@bimbo.test", "BIMBO", "Contractor C1"));

        var cut = RenderPanel();

        var cells = cut.FindAll("table.contractor-assignments tbody tr td");
        cells[0].TextContent.Should().Contain("Tiago").And.Contain("tiago@bimbo.test");
        cells[1].TextContent.Should().Be("BIMBO");
        cells[2].TextContent.Should().Be("Contractor C1");
        cells[3].TextContent.Should().Be("admin@coelba.test");
    }

    [Fact]
    public void Revoke_AsksForConfirmation_ThenRevokesTheAssignment_AndRefreshesTheList()
    {
        var assignment = FakeScopedRoleClient.Assignment(Coelba, "Tiago", "tiago@bimbo.test", "BIMBO", "Contractor C1");
        _client.Assignments.Add(assignment);
        var cut = RenderPanel();

        cut.Find("button.contractor-revoke").Click();
        _client.Revoked.Should().BeEmpty("nothing is revoked before the confirmation");
        cut.Find(".dialog-box p").TextContent.Should().Be("admin.contractorAccess.revokeConfirm");
        cut.Find(".dialog-box button.btn-danger").Click();

        cut.WaitForAssertion(() => _client.Revoked.Should().Equal((Coelba, assignment.Id)));
        cut.WaitForAssertion(() => cut.FindAll("table.contractor-assignments").Should().BeEmpty());
    }

    [Fact]
    public void AFailedRevoke_KeepsTheAssignment_AndSaysSo()
    {
        _client.Assignments.Add(FakeScopedRoleClient.Assignment(Coelba, "Tiago", "tiago@bimbo.test", "BIMBO", "Contractor C1"));
        _client.RevokeFailure = new ScopedRoleAdministrationException(500, null, "boom");
        var cut = RenderPanel();

        cut.Find("button.contractor-revoke").Click();
        cut.Find(".dialog-box button.btn-danger").Click();

        cut.WaitForAssertion(() => cut.Find(".attribute-errors").TextContent.Should().Be("admin.contractorAccess.revokeError"));
        cut.FindAll("table.contractor-assignments tbody tr").Should().ContainSingle();
    }

    [Fact]
    public void AServerRefusal403_ShowsTheForbiddenMessage_AndNoAssignButton()
    {
        _client.ReadFailure = new ScopedRoleAdministrationException(403, ScopedRoleAssignmentProblems.Forbidden, "no");

        var cut = RenderPanel();

        cut.Find(".empty-state").TextContent.Should().Be("admin.contractorAccess.forbidden");
        cut.FindAll("button.btn-primary").Should().BeEmpty();
        cut.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public void AnyOtherLoadFailure_ShowsTheLoadError()
    {
        _client.ReadFailure = new HttpRequestException("down");

        var cut = RenderPanel();

        cut.Find(".attribute-errors").TextContent.Should().Be("admin.contractorAccess.loadError");
    }

    [Fact]
    public void Assigning_ThroughTheDialog_AddsTheAssignmentToTheList()
    {
        var tiago = Guid.NewGuid();
        _client.ContractedUsers.Add(new ContractedUserDto(tiago, "Tiago", "tiago@bimbo.test", Bimbo, "BIMBO"));
        var cut = RenderPanel();

        cut.Find(".section-header-row button.btn-primary").Click();
        cut.Find("#scoped-role-user").Change(tiago.ToString());
        cut.Find("#scoped-role-role").Change(cut.Find("#scoped-role-role option:last-child").GetAttribute("value"));
        cut.Find("button.contractor-assign-submit").Click();

        cut.WaitForAssertion(() => cut.FindAll("table.contractor-assignments tbody tr").Should().ContainSingle());
        cut.FindAll(".dialog-box").Should().BeEmpty("the dialog closes once assigned");
    }

    // ── XD01-411: issuing, editing and revoking grants ───────────────────────

    private static OrganizationGrantDto AdministratorGrant() => Grant(requiresScopedRole: false) with { Source = OrganizationGrantSources.Administrator };

    [Fact]
    public void AGrantAnIntegrationProvisioned_IsMarkedSo_AndOffersNoEditOrRevoke()
    {
        _client.IssuedGrants.Add(Grant(requiresScopedRole: true, "cut"));

        var cut = RenderPanel();

        var row = cut.Find("table.contractor-grants tbody tr");
        row.QuerySelector(".contractor-grant-source .badge")!.TextContent.Should().Be("admin.contractorAccess.source.service");
        row.QuerySelector(".contractor-grant-source .badge")!.GetAttribute("title").Should().Be("admin.contractorAccess.source.serviceHint");
        row.QuerySelectorAll("button").Should().BeEmpty();
    }

    [Fact]
    public void AGrantAnAdministratorIssued_OffersEditAndRevoke()
    {
        _client.IssuedGrants.Add(AdministratorGrant());

        var cut = RenderPanel();

        var row = cut.Find("table.contractor-grants tbody tr");
        row.QuerySelector(".contractor-grant-source")!.TextContent.Trim().Should().Be("admin.contractorAccess.source.administrator");
        row.QuerySelector("button.contractor-grant-edit").Should().NotBeNull();
        row.QuerySelector("button.contractor-grant-revoke").Should().NotBeNull();
    }

    [Fact]
    public void Issuing_ThroughTheDialog_AddsTheGrantToTheList()
    {
        var cut = RenderPanel();

        cut.Find("button.contractor-issue-grant").Click();
        cut.Find("#issued-grant-grantee").Change(Bimbo.ToString());
        cut.Find("#issued-grant-action-serviceorders-read").Change(true);
        cut.Find("button.issued-grant-submit").Click();

        cut.WaitForAssertion(() => cut.FindAll("table.contractor-grants tbody tr").Should().ContainSingle());
        _client.Issued.Single().OrganizationId.Should().Be(Coelba);
        cut.FindAll(".dialog-box").Should().BeEmpty("the dialog closes once issued");
    }

    [Fact]
    public void Editing_ThroughTheDialog_UpdatesTheGrantInTheList()
    {
        var grant = AdministratorGrant();
        _client.IssuedGrants.Add(grant);
        var cut = RenderPanel();

        cut.Find("button.contractor-grant-edit").Click();
        cut.Find("#issued-grant-action-serviceorders-read").Change(false);
        cut.Find("button.issued-grant-submit").Click();

        cut.WaitForAssertion(() => _client.Updated.Should().ContainSingle());
        _client.Updated.Single().GrantId.Should().Be(grant.Id);
        cut.WaitForAssertion(() => cut.Find("table.contractor-grants tbody tr").TextContent.Should().Contain("serviceorders:complete")
            .And.NotContain("serviceorders:read"));
        cut.FindAll(".dialog-box").Should().BeEmpty();
    }

    [Fact]
    public void RevokingAGrant_AsksForConfirmationNamingTheContractor_ThenRevokesIt()
    {
        Services.AddSingleton<IJsonStringLocalizer>(new ArgumentsLocalizer());
        var grant = AdministratorGrant();
        _client.IssuedGrants.Add(grant);
        var cut = RenderPanel();

        cut.Find("button.contractor-grant-revoke").Click();
        cut.Find(".dialog-box").TextContent.Should().Contain("admin.contractorAccess.revokeGrantConfirm(BIMBO)");
        cut.Find(".dialog-box button.btn-danger").Click();

        cut.WaitForAssertion(() => _client.RevokedGrants.Should().Equal((Coelba, grant.Id)));
        cut.WaitForAssertion(() => cut.Find(".contractor-grants-empty").Should().NotBeNull());
    }

    [Theory]
    [InlineData(IssuedGrantProblems.ServiceOwned, "admin.contractorAccess.grantForm.error.serviceOwned")]
    [InlineData(null, "admin.contractorAccess.revokeGrantError")]
    public void AFailedGrantRevoke_KeepsTheGrant_AndSaysWhy(string? code, string expectedKey)
    {
        _client.IssuedGrants.Add(AdministratorGrant());
        _client.GrantRevokeFailure = new ScopedRoleAdministrationException(409, code, "refused");
        var cut = RenderPanel();

        cut.Find("button.contractor-grant-revoke").Click();
        cut.Find(".dialog-box button.btn-danger").Click();

        cut.WaitForAssertion(() => cut.Find(".contractor-grant-error").TextContent.Should().Be(expectedKey));
        cut.FindAll("table.contractor-grants tbody tr").Should().ContainSingle();
    }

    /// <summary>Answers a key with itself, and a formatted key with its arguments too, so a test can see which were used.</summary>
    private sealed class ArgumentsLocalizer : IJsonStringLocalizer
    {
        public string this[string key] => key;
        public string GetString(string key, params object[] args) => $"{key}({string.Join(", ", args)})";
        public event EventHandler LocalizationChanged { add { } remove { } }
    }
}
