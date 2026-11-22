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

/// <summary>XD01-327: the dialog that gives a contracted user a role on the organization.</summary>
public sealed class AssignScopedRoleDialogTests : BunitContext
{
    private static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000327");
    private static readonly Guid Bimbo = new("b1b00000-0000-4000-8000-000000000327");
    private static readonly Guid Tiago = new("bb000002-0000-4000-8000-000000000327");
    private static readonly Guid ContractorRoleId = new("13000000-0000-4000-8000-000000000327");

    private readonly FakeScopedRoleClient _client = new();
    private int _assigned;
    private int _cancelled;

    public AssignScopedRoleDialogTests()
    {
        Services.AddSingleton<IJsonStringLocalizer>(new KeyLocalizer());
        Services.AddSingleton<IScopedRoleAdministrationClient>(_client);
        Services.AddSingleton<IRoleRepository>(new FixedRoleRepository(
            new AppRole { Id = ContractorRoleId, Name = "Contractor C1" },
            new AppRole { Name = "Auditor" }));
    }

    private IRenderedComponent<AssignScopedRoleDialog> RenderDialog(bool visible = true) =>
        Render<AssignScopedRoleDialog>(p => p
            .Add(c => c.Visible, visible)
            .Add(c => c.OrganizationId, Coelba)
            .Add(c => c.OnAssigned, EventCallback.Factory.Create(this, () => _assigned++))
            .Add(c => c.OnCancel, EventCallback.Factory.Create(this, () => _cancelled++)));

    private void AddTiago() => _client.ContractedUsers.Add(new ContractedUserDto(Tiago, "Tiago", "tiago@bimbo.test", Bimbo, "BIMBO"));

    private static void Choose(IRenderedComponent<AssignScopedRoleDialog> cut)
    {
        cut.Find("#scoped-role-user").Change(Tiago.ToString());
        cut.Find("#scoped-role-role").Change(ContractorRoleId.ToString());
    }

    [Fact]
    public void Hidden_RendersNothing()
    {
        RenderDialog(visible: false).Markup.Trim().Should().BeEmpty();
    }

    [Fact]
    public void ListsTheContractedUsersWithTheirOrganization_AndTheRolesByName()
    {
        AddTiago();

        var cut = RenderDialog();

        cut.FindAll("#scoped-role-user option").Select(o => o.TextContent).Should().Equal(
            "admin.contractorAccess.chooseUser", "Tiago (tiago@bimbo.test) — BIMBO");
        cut.FindAll("#scoped-role-role option").Select(o => o.TextContent).Should().Equal(
            "admin.contractorAccess.chooseRole", "Auditor", "Contractor C1");
    }

    [Fact]
    public void AssignStaysDisabled_UntilBothAUserAndARoleAreChosen()
    {
        AddTiago();
        var cut = RenderDialog();

        cut.Find("button.contractor-assign-submit").HasAttribute("disabled").Should().BeTrue();
        cut.Find("#scoped-role-user").Change(Tiago.ToString());
        cut.Find("button.contractor-assign-submit").HasAttribute("disabled").Should().BeTrue();
        cut.Find("#scoped-role-role").Change(ContractorRoleId.ToString());
        cut.Find("button.contractor-assign-submit").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Assign_SendsTheChosenUserAndRole_ForTheOrganization_AndRaisesOnAssigned()
    {
        AddTiago();
        var cut = RenderDialog();
        Choose(cut);

        cut.Find("button.contractor-assign-submit").Click();

        cut.WaitForAssertion(() => _assigned.Should().Be(1));
        _client.Assigned.Should().Equal((Coelba, Tiago, ContractorRoleId));
    }

    [Theory]
    [InlineData(ScopedRoleAssignmentProblems.NotContracted, "admin.contractorAccess.error.notContracted")]
    [InlineData(ScopedRoleAssignmentProblems.SelfAssignment, "admin.contractorAccess.error.selfAssignment")]
    [InlineData(ScopedRoleAssignmentProblems.Invalid, "admin.contractorAccess.error.invalid")]
    [InlineData(ScopedRoleAssignmentProblems.NotFound, "admin.contractorAccess.error.notFound")]
    [InlineData(ScopedRoleAssignmentProblems.Forbidden, "admin.contractorAccess.forbidden")]
    [InlineData(null, "admin.contractorAccess.error.generic")]
    public void ARefusal_StaysInTheDialog_ExplainedByItsCode(string? code, string expectedKey)
    {
        AddTiago();
        _client.AssignFailure = new ScopedRoleAdministrationException(409, code, "refused");
        var cut = RenderDialog();
        Choose(cut);

        cut.Find("button.contractor-assign-submit").Click();

        cut.WaitForAssertion(() => cut.Find(".attribute-errors").TextContent.Should().Be(expectedKey));
        _assigned.Should().Be(0);
        cut.FindAll(".dialog-box").Should().ContainSingle("the dialog stays open");
    }

    [Fact]
    public void ANetworkFailure_IsTheGenericError()
    {
        AddTiago();
        _client.AssignFailure = new HttpRequestException("down");
        var cut = RenderDialog();
        Choose(cut);

        cut.Find("button.contractor-assign-submit").Click();

        cut.WaitForAssertion(() => cut.Find(".attribute-errors").TextContent.Should().Be("admin.contractorAccess.error.generic"));
    }

    [Fact]
    public void WithNoContractedUser_SaysSo_AndOffersOnlyCancel()
    {
        var cut = RenderDialog();

        cut.Find(".contractor-no-users").TextContent.Should().Be("admin.contractorAccess.noContractedUsers");
        cut.FindAll("select").Should().BeEmpty();
        cut.Find(".dialog-actions button").Click();
        _cancelled.Should().Be(1);
    }

    [Fact]
    public void AFailedLoad_ShowsItsError_InsteadOfTheNoUsersMessage()
    {
        _client.ReadFailure = new ScopedRoleAdministrationException(403, ScopedRoleAssignmentProblems.Forbidden, "no");

        var cut = RenderDialog();

        cut.Find(".attribute-errors").TextContent.Should().Be("admin.contractorAccess.forbidden");
        cut.FindAll(".contractor-no-users").Should().BeEmpty();
    }

    [Fact]
    public void Reopening_ClearsThePreviousChoiceAndError_AndReloadsTheUsers()
    {
        AddTiago();
        _client.AssignFailure = new ScopedRoleAdministrationException(409, ScopedRoleAssignmentProblems.NotContracted, "refused");
        var cut = RenderDialog();
        Choose(cut);
        cut.Find("button.contractor-assign-submit").Click();
        cut.WaitForAssertion(() => cut.FindAll(".attribute-errors").Should().ContainSingle());

        _client.ContractedUsers.Add(new ContractedUserDto(Guid.NewGuid(), "Beatriz", "beatriz@bimbo.test", Bimbo, "BIMBO"));
        cut.Render(p => p.Add(c => c.Visible, false));
        cut.Render(p => p.Add(c => c.Visible, true));

        cut.FindAll(".attribute-errors").Should().BeEmpty();
        cut.FindAll("#scoped-role-user option").Should().HaveCount(3);
        cut.Find("button.contractor-assign-submit").HasAttribute("disabled").Should().BeTrue();
    }
}
