using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.Modules;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Identity.EFCore.Tests;

/// <summary>XD01-377: the kernel seeder applies each module's contributed permissions and role bundles, idempotently and without ever deleting.</summary>
public sealed class ModulePermissionSeedingTests
{
    private static readonly Guid TenantRoleId = new("10000000-0000-0000-0000-000000000005");

    private sealed class TestContributor(
        string verticalId, IEnumerable<PermissionDefinition> permissions, IEnumerable<RoleBundleContribution> roleBundles)
        : IModulePermissionContributor
    {
        public string VerticalId { get; } = verticalId;
        public IEnumerable<PermissionDefinition> Permissions { get; } = permissions;
        public IEnumerable<RoleBundleContribution> RoleBundles { get; } = roleBundles;
    }

    private static readonly PermissionDefinition[] WidgetPermissions =
    [
        new("widgets:read", "widgets", "read", "permissions.widgets.read"),
        new("widgets:edit", "widgets", "edit", "permissions.widgets.edit"),
    ];

    private static TestContributor Widgets() => new(
        "widgets",
        WidgetPermissions,
        [new(new Guid("10000000-0000-0000-0000-0000000000a1"), "WidgetEditor", "Edita widgets", ["widgets:read", "widgets:edit", "serviceorders:read"])]);

    private static TestContributor IspTenant() => new(
        "neutral-operator-equivalent",
        [],
        [new(TenantRoleId, "IspTenant", "Inquilino ISP: solicitudes y disputas", ["serviceorders:read", "serviceorders:create"])]);

    private static async Task<(SqliteConnection Connection, GeoIdentityDbContext Db)> OpenAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new GeoIdentityDbContext(new DbContextOptionsBuilder<GeoIdentityDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        return (connection, db);
    }

    private static async Task<(int Permissions, int Roles, int RolePermissions)> CountsAsync(GeoIdentityDbContext db) =>
        (await db.Permissions.CountAsync(), await db.Roles.CountAsync(), await db.RolePermissions.CountAsync());

    private static Task<List<string>> GrantedCodesAsync(GeoIdentityDbContext db, Guid roleId) =>
        db.RolePermissions.Where(rp => rp.RoleId == roleId)
            .Join(db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Code)
            .ToListAsync();

    [Fact]
    public async Task SeedAsync_IspTenantBundleContributor_SeedsTheRoleWithExactlyServiceOrdersReadAndCreate()
    {
        var (connection, db) = await OpenAsync();
        using var _ = connection;
        using var __ = db;

        await GeoIdentitySeeder.SeedAsync(db, new FakeTimeProvider(), [IspTenant()]);

        (await db.Roles.AnyAsync(r => r.Id == TenantRoleId && r.Name == "IspTenant" && r.IsBuiltIn)).Should().BeTrue();
        (await GrantedCodesAsync(db, TenantRoleId)).Should().BeEquivalentTo(["serviceorders:read", "serviceorders:create"]);
    }

    [Fact]
    public async Task SeedAsync_WithoutContributors_SeedsNoIspTenantRole()
    {
        var (connection, db) = await OpenAsync();
        using var _ = connection;
        using var __ = db;

        await GeoIdentitySeeder.SeedAsync(db, new FakeTimeProvider());

        (await db.Roles.AnyAsync(r => r.Id == TenantRoleId || r.Name == "IspTenant")).Should().BeFalse();
    }

    [Fact]
    public async Task SeedAsync_ContributorWithPermissions_AddsThem()
    {
        var (connection, db) = await OpenAsync();
        using var _ = connection;
        using var __ = db;

        await GeoIdentitySeeder.SeedAsync(db, new FakeTimeProvider(), [Widgets()]);

        var widgets = await db.Permissions.Where(p => p.Resource == "widgets").ToListAsync();
        widgets.Should().HaveCount(2);
        widgets.Should().ContainSingle(p => p.Code == "widgets:edit" && p.Action == "edit" && p.Description == "permissions.widgets.edit");
    }

    [Fact]
    public async Task SeedAsync_ContributorWithNewRoleBundle_CreatesTheBuiltInRoleWithItsGrants()
    {
        var (connection, db) = await OpenAsync();
        using var _ = connection;
        using var __ = db;
        var contributor = Widgets();
        var bundle = contributor.RoleBundles.Single();

        await GeoIdentitySeeder.SeedAsync(db, new FakeTimeProvider(), [contributor]);

        var role = await db.Roles.SingleAsync(r => r.Id == bundle.RoleId);
        role.Name.Should().Be("WidgetEditor");
        role.Description.Should().Be("Edita widgets");
        role.IsBuiltIn.Should().BeTrue();
        (await GrantedCodesAsync(db, bundle.RoleId)).Should().BeEquivalentTo(["widgets:read", "widgets:edit", "serviceorders:read"]);
    }

    [Fact]
    public async Task SeedAsync_BundleOnExistingKernelRole_BackfillsOnlyTheMissingGrant()
    {
        var (connection, db) = await OpenAsync();
        using var _ = connection;
        using var __ = db;
        var contributor = new TestContributor("widgets", WidgetPermissions,
            [new(GeoIdentitySeeder.ReadOnlyRoleId, "ReadOnly", "ignored", ["widgets:read", "serviceorders:read"])]);
        await GeoIdentitySeeder.SeedAsync(db, new FakeTimeProvider());
        var before = await GrantedCodesAsync(db, GeoIdentitySeeder.ReadOnlyRoleId);
        var rolesBefore = await db.Roles.CountAsync();

        await GeoIdentitySeeder.SeedAsync(db, new FakeTimeProvider(), [contributor]);

        (await GrantedCodesAsync(db, GeoIdentitySeeder.ReadOnlyRoleId)).Should().BeEquivalentTo([.. before, "widgets:read"]);
        (await db.Roles.CountAsync()).Should().Be(rolesBefore);
        (await db.Roles.SingleAsync(r => r.Id == GeoIdentitySeeder.ReadOnlyRoleId)).Description.Should().NotBe("ignored");
    }

    [Fact]
    public async Task SeedAsync_BundleWithUnknownPermissionCode_SkipsItAndGrantsTheRest()
    {
        var (connection, db) = await OpenAsync();
        using var _ = connection;
        using var __ = db;
        var roleId = Guid.NewGuid();
        var contributor = new TestContributor("widgets", [], [new(roleId, "Partial", "d", ["serviceorders:read", "nope:nothing"])]);

        await GeoIdentitySeeder.SeedAsync(db, new FakeTimeProvider(), [contributor]);

        (await GrantedCodesAsync(db, roleId)).Should().BeEquivalentTo(["serviceorders:read"]);
        (await db.Permissions.AnyAsync(p => p.Code == "nope:nothing")).Should().BeFalse();
    }

    [Fact]
    public async Task SeedAsync_SameContributorsTwice_IsANoOp()
    {
        var (connection, db) = await OpenAsync();
        using var _ = connection;
        using var __ = db;
        var time = new FakeTimeProvider();
        var contributors = new[] { Widgets(), IspTenant() };
        await GeoIdentitySeeder.SeedAsync(db, time, contributors);
        var first = await CountsAsync(db);

        await GeoIdentitySeeder.SeedAsync(db, time, contributors);

        (await CountsAsync(db)).Should().Be(first);
        var set = new ModulePermissionSet { Permissions = [.. contributors[0].Permissions], RoleBundles = [.. contributors[0].RoleBundles] };
        (await GeoIdentitySeeder.ApplyModulePermissionsAsync(db, set)).Should().BeFalse();
        (await CountsAsync(db)).Should().Be(first);
    }

    [Fact]
    public async Task SeedAsync_ContributorRemoved_DeletesNothing()
    {
        var (connection, db) = await OpenAsync();
        using var _ = connection;
        using var __ = db;
        var time = new FakeTimeProvider();
        var contributors = new[] { Widgets(), IspTenant() };
        await GeoIdentitySeeder.SeedAsync(db, time, contributors);
        var with = await CountsAsync(db);

        await GeoIdentitySeeder.SeedAsync(db, time);

        (await CountsAsync(db)).Should().Be(with);
        (await db.Permissions.CountAsync(p => p.Resource == "widgets")).Should().Be(2);
        (await GrantedCodesAsync(db, contributors[0].RoleBundles.Single().RoleId)).Should().HaveCount(3);
        (await GrantedCodesAsync(db, TenantRoleId)).Should().BeEquivalentTo(["serviceorders:read", "serviceorders:create"]);
    }

    [Fact]
    public async Task SeedAsync_InvalidContributor_ThrowsNamingTheVertical()
    {
        var (connection, db) = await OpenAsync();
        using var _ = connection;
        using var __ = db;
        var invalid = new TestContributor("broken-vertical", [new("", "r", "a", "k")], []);

        var thrown = await FluentActions.Awaiting(() => GeoIdentitySeeder.SeedAsync(db, new FakeTimeProvider(), [invalid]))
            .Should().ThrowAsync<InvalidOperationException>();

        thrown.Which.Message.Should().Contain("broken-vertical");
    }

    [Fact]
    public async Task ApplyModulePermissionsAsync_InvalidSet_ThrowsArgumentException()
    {
        var (connection, db) = await OpenAsync();
        using var _ = connection;
        using var __ = db;
        var set = new ModulePermissionSet { RoleBundles = [new(Guid.Empty, "X", "d", [])] };

        await FluentActions.Awaiting(() => GeoIdentitySeeder.ApplyModulePermissionsAsync(db, set))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ApplyModulePermissionsAsync_NewSet_ReturnsTrueAndPersists()
    {
        var (connection, db) = await OpenAsync();
        using var _ = connection;
        using var __ = db;
        await GeoIdentitySeeder.SeedAsync(db, new FakeTimeProvider());
        var contributor = Widgets();
        var set = new ModulePermissionSet { Permissions = [.. contributor.Permissions], RoleBundles = [.. contributor.RoleBundles] };

        (await GeoIdentitySeeder.ApplyModulePermissionsAsync(db, set)).Should().BeTrue();

        (await db.Permissions.CountAsync(p => p.Resource == "widgets")).Should().Be(2);
        (await GrantedCodesAsync(db, contributor.RoleBundles.Single().RoleId)).Should().HaveCount(3);
    }

    [Fact]
    public void GeoIdentitySeeder_ContainsNoVerticalStrings()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GeoAssets.sln")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the repository root holds GeoAssets.sln");
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "apps", "GeoAssets.Identity.EFCore", "GeoIdentitySeeder.cs"));

        foreach (var token in new[] { "ip-billing", "ipBilling", "IspTenant", "NeutralOperator", "Municipio", "occupancy", "shared-use" })
            source.Should().NotContainEquivalentOf(token, "the kernel seeder names no vertical (XD01-377)");
    }
}
