using FluentAssertions;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// <see cref="EFMirroredCrossOrganizationEntitlements"/> (XD01-328): a caller's grants are its organization's active,
/// unexpired mirrored grants, with every field the rule reads; its scoped roles are the mirror rows of its own
/// identity-provider id. Nothing else leaks in.
/// </summary>
public sealed class EFMirroredCrossOrganizationEntitlementsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Bimbo = new("b1b1b1b1-0000-4000-8000-000000000328");
    private static readonly Guid Coelba = new("c0c0c0c0-0000-4000-8000-000000000328");
    private static readonly Guid Third = new("d0d0d0d0-0000-4000-8000-000000000328");

    private static OrganizationGrantMirrorRecord Grant(Guid grantee, Guid resource, bool active = true, DateTime? expiresAt = null) => new()
    {
        GrantId = Guid.NewGuid(),
        GranteeOrganizationId = grantee,
        ResourceOrganizationId = resource,
        ResourceType = "ServiceOrder",
        ScopeId = "district-7",
        IsActive = active,
        ExpiresAt = expiresAt,
        AllowedActionsJson = """["serviceorders:view","serviceorders:execute"]""",
        RequiredRole = "Contractor C1",
        RequiresScopedRole = true,
        OrderTypeIdsJson = """["cut"]""",
        SyncedAtUtc = Now.UtcDateTime,
    };

    private static ScopedRoleAssignmentMirrorRecord Scoped(string oid, Guid organization, string roles, string codes) => new()
    {
        UserId = Guid.NewGuid(),
        ResourceOrganizationId = organization,
        ExternalObjectId = oid,
        RoleNamesJson = roles,
        PermissionCodesJson = codes,
        SyncedAtUtc = Now.UtcDateTime,
    };

    private static EFMirroredCrossOrganizationEntitlements Sut(SqliteFixture fixture) => new(fixture.NewContext(), new FakeTimeProvider(Now));

    [Fact]
    public async Task Grants_AreTheOrganizationsActiveUnexpiredOnes_WithEveryFieldTheRuleReads()
    {
        using var fixture = new SqliteFixture();
        var kept = Grant(Bimbo, Coelba, expiresAt: Now.UtcDateTime.AddDays(1));
        fixture.Context.OrganizationGrantMirror.AddRange(
            kept,
            Grant(Bimbo, Third, active: false),
            Grant(Bimbo, Third, expiresAt: Now.UtcDateTime.AddMinutes(-1)),
            Grant(Third, Coelba));
        await fixture.Context.SaveChangesAsync();

        var entitlements = await Sut(fixture).GetAsync(Bimbo, "oid-nobody");

        var grant = entitlements.Grants.Should().ContainSingle().Subject;
        grant.ResourceOrganizationId.Should().Be(Coelba.ToString());
        grant.ResourceType.Should().Be("ServiceOrder");
        grant.ScopeId.Should().Be("district-7");
        grant.AllowedActions.Should().Equal("serviceorders:view", "serviceorders:execute");
        grant.RequiredRole.Should().Be("Contractor C1");
        grant.RequiresScopedRole.Should().BeTrue();
        grant.OrderTypeIds.Should().Equal("cut");
        grant.ExpiresAt.Should().Be(kept.ExpiresAt);
        entitlements.ScopedRoleNames.Should().BeEmpty();
        entitlements.ScopedPermissionCodes.Should().BeEmpty();
    }

    [Fact]
    public async Task ScopedRoles_AreTheCallersOwn_PerOrganization()
    {
        using var fixture = new SqliteFixture();
        fixture.Context.ScopedRoleAssignmentMirror.AddRange(
            Scoped("oid-ada", Coelba, """["Contractor C1"]""", """["serviceorders:view","serviceorders:execute"]"""),
            Scoped("oid-ada", Third, """["Viewer"]""", """["serviceorders:view"]"""),
            Scoped("oid-bob", Coelba, """["Administrator"]""", """["serviceorders:approve"]"""));
        await fixture.Context.SaveChangesAsync();

        var entitlements = await Sut(fixture).GetAsync(Bimbo, "oid-ada");

        entitlements.Grants.Should().BeEmpty();
        entitlements.ScopedRoleNames.Keys.Should().BeEquivalentTo([Coelba, Third]);
        entitlements.ScopedRoleNames[Coelba].Should().Equal("Contractor C1");
        entitlements.ScopedPermissionCodes[Coelba].Should().Equal("serviceorders:view", "serviceorders:execute");
        entitlements.ScopedPermissionCodes[Third].Should().Equal("serviceorders:view");
    }

    [Fact]
    public async Task TwoRowsOfOneOrganizationUnderTheSameId_AreMerged()
    {
        using var fixture = new SqliteFixture();
        fixture.Context.ScopedRoleAssignmentMirror.AddRange(
            Scoped("oid-ada", Coelba, """["Contractor C1"]""", """["serviceorders:view"]"""),
            Scoped("oid-ada", Coelba, """["Contractor C1","Viewer"]""", """["serviceorders:view","serviceorders:execute"]"""));
        await fixture.Context.SaveChangesAsync();

        var entitlements = await Sut(fixture).GetAsync(Bimbo, "oid-ada");

        entitlements.ScopedRoleNames[Coelba].Should().BeEquivalentTo("Contractor C1", "Viewer");
        entitlements.ScopedPermissionCodes[Coelba].Should().BeEquivalentTo("serviceorders:view", "serviceorders:execute");
    }

    [Fact]
    public async Task NoOrganizationAndNoId_HoldNothing()
    {
        using var fixture = new SqliteFixture();
        fixture.Context.OrganizationGrantMirror.Add(Grant(Guid.Empty, Coelba));
        fixture.Context.ScopedRoleAssignmentMirror.Add(Scoped(string.Empty, Coelba, """["Contractor C1"]""", """["serviceorders:view"]"""));
        await fixture.Context.SaveChangesAsync();

        (await Sut(fixture).GetAsync(Guid.Empty, string.Empty)).Should().BeSameAs(MirroredEntitlements.None);
    }
}
