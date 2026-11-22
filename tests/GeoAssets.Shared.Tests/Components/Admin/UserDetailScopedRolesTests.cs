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

/// <summary>XD01-327: <see cref="UserDetail"/> lists the roles a user holds on other organizations, apart from their home roles.</summary>
public sealed class UserDetailScopedRolesTests : BunitContext
{
    private static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000327");
    private static readonly Guid Third = new("7e1d0000-0000-4000-8000-000000000327");

    private readonly FakeScopedRoleClient _client = new();

    public UserDetailScopedRolesTests()
    {
        Services.AddSingleton<IJsonStringLocalizer>(new KeyLocalizer());
        Services.AddSingleton<IUserRepository>(new UnusedUserRepository());
        Services.AddSingleton<IRoleRepository>(new FixedRoleRepository());
        Services.AddSingleton<IOrganizationRepository>(new FixedOrganizationRepository());
    }

    private static AppUser Tiago() => new()
    {
        Id = new Guid("bb000002-0000-4000-8000-000000000327"),
        Email = "tiago@bimbo.test",
        DisplayName = "Tiago",
        CreatedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    private IRenderedComponent<UserDetail> RenderDetail(bool withClient = true)
    {
        if (withClient) Services.AddSingleton<IScopedRoleAdministrationClient>(_client);
        return Render<UserDetail>(p => p.Add(c => c.User, Tiago()));
    }

    [Fact]
    public void ListsEachRoleOnAnotherOrganization_ByOrganization_UnderItsOwnHeading()
    {
        _client.UserAssignments.Add(FakeScopedRoleClient.Assignment(Coelba, "Tiago", "tiago@bimbo.test", "BIMBO", "Contractor C1"));
        _client.UserAssignments.Add(FakeScopedRoleClient.Assignment(Third, "Tiago", "tiago@bimbo.test", "BIMBO", "Auditor", organizationName: "THIRD"));

        var cut = RenderDetail();

        var section = cut.Find(".user-scoped-roles");
        section.QuerySelector("h6")!.TextContent.Should().Be("admin.contractorAccess.userRoles.title");
        cut.FindAll(".user-scoped-role").Select(li => li.TextContent).Should().Equal("COELBA — Contractor C1", "THIRD — Auditor");
        section.QuerySelectorAll("input[type=checkbox]").Should().BeEmpty("roles on other organizations are read-only here");
    }

    [Fact]
    public void NoRoleElsewhere_SaysNone()
    {
        var cut = RenderDetail();

        cut.Find(".user-scoped-roles-empty").TextContent.Should().Be("admin.contractorAccess.userRoles.empty");
    }

    [Fact]
    public void AFailedLoad_SaysSo()
    {
        _client.ReadFailure = new HttpRequestException("down");

        var cut = RenderDetail();

        cut.Find(".user-scoped-roles .attribute-errors").TextContent.Should().Be("admin.contractorAccess.userRoles.loadError");
    }

    [Fact]
    public void WithoutTheClient_TheSectionIsAbsent()
    {
        var cut = RenderDetail(withClient: false);

        cut.FindAll(".user-scoped-roles").Should().BeEmpty();
    }
}
