using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Shared.Components.Layout;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.Layout;

public class MainLayoutTests
{
    private static readonly CurrentUser SampleUser = new(
        ExternalObjectId: "oid-123",
        Email: "user@example.com",
        DisplayName: "Sample User",
        ExternalRoles: []);

    private sealed class StubUserRepository(AppUser? userToReturn) : IUserRepository
    {
        public Task<AppUser?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<AppUser?> GetByExternalObjectIdAsync(string oid, CancellationToken ct = default) =>
            Task.FromResult(userToReturn);

        public Task<AppUser?> GetByEmailAsync(string email, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<AppUser>> GetAllAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<AppUser>> GetByRoleAsync(string roleName, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<AppUser>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<AppRole>> GetRolesAsync(Guid userId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<AppPermission>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task AddAsync(AppUser user, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(AppUser user, CancellationToken ct = default) => throw new NotImplementedException();

        public Task AssignRoleAsync(Guid userId, Guid roleId, string? assignedBy = null, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task RemoveRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class StubOrganizationRepository(Organization? organizationToReturn) : IOrganizationRepository
    {
        public Task<Organization?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(organizationToReturn);

        public Task<Organization?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<Organization>> GetAllAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<AppUser>> GetUsersAsync(Guid organizationId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task AddAsync(Organization organization, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(Organization organization, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }

    [Fact]
    public async Task ResolveOrganizationNameAsync_NoOrganizationRepository_ReturnsNull()
    {
        var userRepo = new StubUserRepository(userToReturn: null);

        var result = await MainLayout.ResolveOrganizationNameAsync(SampleUser, organizationRepo: null, userRepo);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveOrganizationNameAsync_NoCurrentUser_ReturnsNull()
    {
        var userRepo = new StubUserRepository(userToReturn: null);
        var orgRepo  = new StubOrganizationRepository(organizationToReturn: null);

        var result = await MainLayout.ResolveOrganizationNameAsync(currentUser: null, orgRepo, userRepo);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveOrganizationNameAsync_UserNotProvisioned_ReturnsNull()
    {
        var userRepo = new StubUserRepository(userToReturn: null);
        var orgRepo  = new StubOrganizationRepository(organizationToReturn: null);

        var result = await MainLayout.ResolveOrganizationNameAsync(SampleUser, orgRepo, userRepo);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveOrganizationNameAsync_UserHasNoOrganization_ReturnsNull()
    {
        var appUser = new AppUser
        {
            ExternalObjectId = SampleUser.ExternalObjectId,
            OrganizationId   = null,
            CreatedAt        = DateTime.UtcNow,
        };
        var userRepo = new StubUserRepository(appUser);
        var orgRepo  = new StubOrganizationRepository(organizationToReturn: null);

        var result = await MainLayout.ResolveOrganizationNameAsync(SampleUser, orgRepo, userRepo);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveOrganizationNameAsync_OrganizationDeleted_ReturnsNull()
    {
        var orgId = Guid.NewGuid();
        var appUser = new AppUser
        {
            ExternalObjectId = SampleUser.ExternalObjectId,
            OrganizationId   = orgId,
            CreatedAt        = DateTime.UtcNow,
        };
        var userRepo = new StubUserRepository(appUser);
        var orgRepo  = new StubOrganizationRepository(organizationToReturn: null);

        var result = await MainLayout.ResolveOrganizationNameAsync(SampleUser, orgRepo, userRepo);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveOrganizationNameAsync_UserHasOrganization_ReturnsOrganizationName()
    {
        var orgId = Guid.NewGuid();
        var appUser = new AppUser
        {
            ExternalObjectId = SampleUser.ExternalObjectId,
            OrganizationId   = orgId,
            CreatedAt        = DateTime.UtcNow,
        };
        var organization = new Organization
        {
            Id        = orgId,
            Name      = "Empresa Eléctrica del Norte",
            CreatedAt = DateTime.UtcNow,
        };
        var userRepo = new StubUserRepository(appUser);
        var orgRepo  = new StubOrganizationRepository(organization);

        var result = await MainLayout.ResolveOrganizationNameAsync(SampleUser, orgRepo, userRepo);

        result.Should().Be("Empresa Eléctrica del Norte");
    }

    [Fact]
    public void SelectDefaultProject_NoProjects_ReturnsNull()
    {
        var result = MainLayout.SelectDefaultProject([]);

        result.Should().BeNull();
    }

    [Fact]
    public void SelectDefaultProject_OnlyUserProjects_ReturnsNull()
    {
        var projects = new[]
        {
            new Project { Id = Guid.NewGuid(), Kind = ProjectKind.User, CreatedAt = DateTime.UtcNow },
        };

        var result = MainLayout.SelectDefaultProject(projects);

        result.Should().BeNull();
    }

    [Fact]
    public void SelectDefaultProject_SingleGeneralProject_ReturnsIt()
    {
        var general = new Project { Id = Guid.NewGuid(), Kind = ProjectKind.General, CreatedAt = DateTime.UtcNow };
        var projects = new[]
        {
            new Project { Id = Guid.NewGuid(), Kind = ProjectKind.User, CreatedAt = DateTime.UtcNow },
            general,
        };

        var result = MainLayout.SelectDefaultProject(projects);

        result.Should().Be(general);
    }

    [Fact]
    public void SelectDefaultProject_MultipleGeneralProjects_ReturnsEarliestCreated()
    {
        var older = new Project
        {
            Id = Guid.NewGuid(), Kind = ProjectKind.General, CreatedAt = new DateTime(2024, 1, 1),
        };
        var newer = new Project
        {
            Id = Guid.NewGuid(), Kind = ProjectKind.General, CreatedAt = new DateTime(2025, 1, 1),
        };

        var result = MainLayout.SelectDefaultProject([newer, older]);

        result.Should().Be(older);
    }
}
