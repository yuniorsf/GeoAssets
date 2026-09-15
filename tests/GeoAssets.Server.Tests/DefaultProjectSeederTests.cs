using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>Coverage of <see cref="DefaultProjectSeeder"/> (XD01-144).</summary>
public class DefaultProjectSeederTests
{
    private sealed class InMemoryProjectRepository : IProjectRepository
    {
        private readonly List<Project> _projects = [];
        public IReadOnlyList<Project> All => _projects;

        public event EventHandler<Project>? ProjectSaved;
        public event EventHandler<Guid>? ProjectDeleted;

        public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_projects.FirstOrDefault(p => p.Id == id));

        public Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Project>>([.. _projects]);

        public Task<IReadOnlyList<Project>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Project>>([.. _projects.Where(p => p.OrganizationId == organizationId)]);

        public Task<IReadOnlyList<Project>> GetForksOfAsync(Guid parentProjectId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Project>>([.. _projects.Where(p => p.ParentProjectId == parentProjectId)]);

        public Task AddAsync(Project project, CancellationToken ct = default)
        {
            _projects.Add(project);
            ProjectSaved?.Invoke(this, project);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Project project, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class InMemoryOrganizationRepository(params Organization[] seed) : IOrganizationRepository
    {
        private readonly List<Organization> _orgs = [.. seed];

        public Task<Organization?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_orgs.FirstOrDefault(o => o.Id == id));

        public Task<Organization?> GetBySlugAsync(string slug, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Organization>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Organization>>([.. _orgs]);

        public Task<IReadOnlyList<AppUser>> GetUsersAsync(Guid organizationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(Organization organization, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(Organization organization, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static Organization Org(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(), Name = "Acme", Slug = "acme", CreatedAt = DateTime.UtcNow, IsActive = true,
    };

    // ── EnsureDefaultProjectAsync ─────────────────────────────────────────────

    [Fact]
    public async Task EnsureDefaultProjectAsync_OrgHasNoProjects_CreatesGeneralProjectWithBuiltInAssetTypes()
    {
        var repo = new InMemoryProjectRepository();
        var orgId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

        await DefaultProjectSeeder.EnsureDefaultProjectAsync(repo, orgId, new FixedTimeProvider(now));

        var project = repo.All.Should().ContainSingle().Subject;
        project.Kind.Should().Be(ProjectKind.General);
        project.OrganizationId.Should().Be(orgId);
        project.Providers.Should().BeEmpty();
        project.AssetTypeScope!.VisibleAssetTypeIds.Should().BeEquivalentTo(
            [AssetType.Point.Id, AssetType.Line.Id, AssetType.Area.Id]);
        project.LayerScope!.Overrides.Should().BeEmpty();
        project.ViewState.Should().NotBeNull();
        project.CreatedAt.Should().Be(now.UtcDateTime);
        project.ParentProjectId.Should().BeNull();
    }

    [Fact]
    public async Task EnsureDefaultProjectAsync_OrgAlreadyHasGeneralProject_DoesNotCreateAnother()
    {
        // Idempotency — safe to call on every startup, and again right after this same call
        // for the same org (e.g. a retried request), without accumulating duplicate defaults.
        var repo = new InMemoryProjectRepository();
        var orgId = Guid.NewGuid();
        var timeProvider = new FixedTimeProvider(DateTimeOffset.UtcNow);

        await DefaultProjectSeeder.EnsureDefaultProjectAsync(repo, orgId, timeProvider);
        await DefaultProjectSeeder.EnsureDefaultProjectAsync(repo, orgId, timeProvider);

        repo.All.Should().ContainSingle();
    }

    [Fact]
    public async Task EnsureDefaultProjectAsync_OrgHasOnlyAUserProject_StillCreatesTheGeneralOne()
    {
        // A Kind == User fork must not be mistaken for the org's default General Project.
        var repo = new InMemoryProjectRepository();
        var orgId = Guid.NewGuid();
        await repo.AddAsync(new Project { Kind = ProjectKind.User, OrganizationId = orgId, ParentProjectId = Guid.NewGuid() });

        await DefaultProjectSeeder.EnsureDefaultProjectAsync(repo, orgId, new FixedTimeProvider(DateTimeOffset.UtcNow));

        repo.All.Should().HaveCount(2);
        repo.All.Should().ContainSingle(p => p.Kind == ProjectKind.General);
    }

    [Fact]
    public async Task EnsureDefaultProjectAsync_DoesNotCreateAnyUserProjectRow()
    {
        // Acceptance criterion: seeding/opening the default for the first time creates no
        // Kind == User row — only editing it does (copy-on-write, XD01-141), which this
        // seeder never triggers.
        var repo = new InMemoryProjectRepository();

        await DefaultProjectSeeder.EnsureDefaultProjectAsync(repo, Guid.NewGuid(), new FixedTimeProvider(DateTimeOffset.UtcNow));

        repo.All.Should().NotContain(p => p.Kind == ProjectKind.User);
    }

    // ── SeedDefaultProjectsAsync (via IServiceProvider) ──────────────────────

    [Fact]
    public async Task SeedDefaultProjectsAsync_MultipleOrganizations_SeedsOneProjectPerOrg()
    {
        var orgA = Org();
        var orgB = Org();
        var projectRepo = new InMemoryProjectRepository();
        var services = new ServiceCollection()
            .AddSingleton<IProjectRepository>(projectRepo)
            .AddSingleton<IOrganizationRepository>(new InMemoryOrganizationRepository(orgA, orgB))
            .AddSingleton<TimeProvider>(new FixedTimeProvider(DateTimeOffset.UtcNow))
            .BuildServiceProvider();

        await services.SeedDefaultProjectsAsync();

        projectRepo.All.Should().HaveCount(2);
        projectRepo.All.Should().Contain(p => p.OrganizationId == orgA.Id);
        projectRepo.All.Should().Contain(p => p.OrganizationId == orgB.Id);
    }

    [Fact]
    public async Task SeedDefaultProjectsAsync_CalledTwice_IsIdempotent()
    {
        var org = Org();
        var projectRepo = new InMemoryProjectRepository();
        var services = new ServiceCollection()
            .AddSingleton<IProjectRepository>(projectRepo)
            .AddSingleton<IOrganizationRepository>(new InMemoryOrganizationRepository(org))
            .AddSingleton<TimeProvider>(new FixedTimeProvider(DateTimeOffset.UtcNow))
            .BuildServiceProvider();

        await services.SeedDefaultProjectsAsync();
        await services.SeedDefaultProjectsAsync();

        projectRepo.All.Should().ContainSingle();
    }
}
