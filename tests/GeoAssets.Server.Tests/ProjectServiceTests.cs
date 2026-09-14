using System.Security.Claims;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// Unit-level coverage of <see cref="ProjectService"/> (XD01-141) — the copy-on-write
/// fork-on-edit logic and the ownership bypass, both new behavior this ticket introduces on
/// top of the already-covered <see cref="OrgResourceAuthorizationHandler"/> (see
/// <see cref="OrgResourceAuthorizationHandlerTests"/> for that piece in isolation).
/// </summary>
public class ProjectServiceTests
{
    // ── Fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeAuthorizationService(Func<string, bool> permits) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
        {
            var requirement = requirements.OfType<OrgResourceRequirement>().Single();
            return Task.FromResult(permits(requirement.PermissionCode)
                ? AuthorizationResult.Success()
                : AuthorizationResult.Failed());
        }

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => throw new NotSupportedException();
    }

    private sealed class FakeGeoAuthorizationService(Guid callerId) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default)
            => Task.FromResult(new AuthorizationContext
            {
                User = new AppUser
                {
                    Id          = callerId,
                    Email       = "caller@example.com",
                    DisplayName = "Caller",
                    CreatedAt   = DateTime.UtcNow,
                },
                Roles       = [],
                Claims      = [],
                Permissions = [],
            });
    }

    /// <summary>In-memory <see cref="IProjectReader"/>/<see cref="IProjectWriter"/> exposing
    /// only the members <see cref="ProjectService"/> actually calls (<see cref="AddAsync"/>,
    /// <see cref="GetForksOfAsync"/>) — every other member throws, so a test fails loudly if
    /// the service starts depending on something new.</summary>
    private sealed class FakeProjectStore : IProjectReader, IProjectWriter
    {
        private readonly List<Project> _projects = [];
        public IReadOnlyList<Project> Added => _projects;

        public event EventHandler<Project>? ProjectSaved;
        public event EventHandler<Guid>? ProjectDeleted;

        public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Project>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Project>> GetForksOfAsync(Guid parentProjectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Project>>([.. _projects.Where(p => p.ParentProjectId == parentProjectId)]);

        public Task AddAsync(Project project, CancellationToken ct = default)
        {
            _projects.Add(project);
            ProjectSaved?.Invoke(this, project);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Project project, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // The reader and writer must be the *same* backing store (so a fork just added is visible
    // to a later GetForksOfAsync).
    private static (ProjectService Sut, FakeProjectStore Store) BuildSut(
        Guid callerId, Func<string, bool>? permits = null, DateTimeOffset? now = null)
    {
        var store = new FakeProjectStore();
        var sut = new ProjectService(
            new FakeAuthorizationService(permits ?? (_ => false)),
            new FakeGeoAuthorizationService(callerId),
            store,
            store,
            new FixedTimeProvider(now ?? DateTimeOffset.UtcNow));
        return (sut, store);
    }

    private static Project GeneralProject(Guid orgId) => new()
    {
        Id             = Guid.NewGuid(),
        Kind           = ProjectKind.General,
        OrganizationId = orgId,
        Name           = "Shared Project",
        Description    = "desc",
    };

    private static ClaimsPrincipal Caller() => new(new ClaimsIdentity(authenticationType: "TestAuth"));

    // ── Direct permission on a General Project ───────────────────────────────

    [Fact]
    public async Task ResolveMutationTargetAsync_CallerHoldsPermission_ReturnsSameProject()
    {
        var (sut, _) = BuildSut(Guid.NewGuid(), permits: code => code == "projects:manage-providers");
        var project = GeneralProject(Guid.NewGuid());

        var target = await sut.ResolveMutationTargetAsync(Caller(), project, "projects:manage-providers");

        target.Should().BeSameAs(project);
    }

    // ── Copy-on-write fork-on-edit ────────────────────────────────────────────

    [Fact]
    public async Task ResolveMutationTargetAsync_ManagePermissionDeniedButCanRead_CreatesFork()
    {
        // Fails without the fix: a naive "permission denied -> null" implementation would
        // return null here instead of a fork.
        var callerId = Guid.NewGuid();
        var (sut, store) = BuildSut(callerId, permits: code => code == "projects:read");
        var project = GeneralProject(Guid.NewGuid());

        var target = await sut.ResolveMutationTargetAsync(Caller(), project, "projects:manage-providers");

        target.Should().NotBeNull();
        target!.Kind.Should().Be(ProjectKind.User);
        target.ParentProjectId.Should().Be(project.Id);
        target.CreatedByUserId.Should().Be(callerId);
        store.Added.Should().ContainSingle().Which.Should().BeSameAs(target);
    }

    [Fact]
    public async Task ResolveMutationTargetAsync_Fork_CopiesNameDescriptionAndOrganization()
    {
        var callerId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var (sut, _) = BuildSut(callerId, permits: code => code == "projects:read");
        var project = GeneralProject(orgId);
        project.Name = "Field Ops";
        project.Description = "Shared field assets";

        var target = await sut.ResolveMutationTargetAsync(Caller(), project, "projects:manage-layers");

        target!.Name.Should().Be("Field Ops");
        target.Description.Should().Be("Shared field assets");
        target.OrganizationId.Should().Be(orgId);
    }

    [Fact]
    public async Task ResolveMutationTargetAsync_ExistingForkForCaller_ReusesItInsteadOfCreatingAnother()
    {
        var callerId = Guid.NewGuid();
        var (sut, store) = BuildSut(callerId, permits: code => code == "projects:read");
        var project = GeneralProject(Guid.NewGuid());

        var first = await sut.ResolveMutationTargetAsync(Caller(), project, "projects:manage-providers");
        var second = await sut.ResolveMutationTargetAsync(Caller(), project, "projects:manage-view");

        second.Should().BeSameAs(first);
        store.Added.Should().ContainSingle();
    }

    [Fact]
    public async Task ResolveMutationTargetAsync_ManagePermissionDeniedAndCannotRead_ReturnsNull()
    {
        var (sut, store) = BuildSut(Guid.NewGuid(), permits: _ => false);
        var project = GeneralProject(Guid.NewGuid());

        var target = await sut.ResolveMutationTargetAsync(Caller(), project, "projects:manage-providers");

        target.Should().BeNull();
        store.Added.Should().BeEmpty();
    }

    // ── Rename/Delete: no copy-on-write fallback ─────────────────────────────

    [Theory]
    [InlineData("projects:rename")]
    [InlineData("projects:delete")]
    public async Task ResolveMutationTargetAsync_NonManageCodeDenied_ReturnsNullEvenWithRead(string permissionCode)
    {
        // projects:rename/projects:delete have no "fork instead" fallback — a caller who can
        // read but not rename/delete a General Project gets a hard 403, not a redirected fork.
        var (sut, store) = BuildSut(Guid.NewGuid(), permits: code => code == "projects:read");
        var project = GeneralProject(Guid.NewGuid());

        var target = await sut.ResolveMutationTargetAsync(Caller(), project, permissionCode);

        target.Should().BeNull();
        store.Added.Should().BeEmpty();
    }

    // ── Ownership bypass on User Projects ────────────────────────────────────

    [Fact]
    public async Task ResolveMutationTargetAsync_OwnerOfUserProject_BypassesPermissionCheckEntirely()
    {
        var ownerId = Guid.NewGuid();
        var (sut, _) = BuildSut(ownerId, permits: _ => false); // holds no projects:* permission at all
        var fork = new Project
        {
            Id              = Guid.NewGuid(),
            Kind            = ProjectKind.User,
            CreatedByUserId = ownerId,
            ParentProjectId = Guid.NewGuid(),
        };

        var target = await sut.ResolveMutationTargetAsync(Caller(), fork, "projects:manage-providers");

        target.Should().BeSameAs(fork);
    }

    [Fact]
    public async Task ResolveMutationTargetAsync_NonOwnerOfUserProjectWithoutPermission_ReturnsNull()
    {
        // One level only, no chains: a User Project that isn't the caller's own never
        // triggers a further fork — it's a straight permission check.
        var (sut, store) = BuildSut(Guid.NewGuid(), permits: _ => false);
        var othersFork = new Project
        {
            Id              = Guid.NewGuid(),
            Kind            = ProjectKind.User,
            CreatedByUserId = Guid.NewGuid(),
            ParentProjectId = Guid.NewGuid(),
        };

        var target = await sut.ResolveMutationTargetAsync(Caller(), othersFork, "projects:manage-providers");

        target.Should().BeNull();
        store.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveMutationTargetAsync_NonOwnerOfUserProjectWithPermission_ReturnsSameProject()
    {
        var (sut, _) = BuildSut(Guid.NewGuid(), permits: code => code == "projects:manage-providers");
        var othersFork = new Project
        {
            Id              = Guid.NewGuid(),
            Kind            = ProjectKind.User,
            CreatedByUserId = Guid.NewGuid(),
            ParentProjectId = Guid.NewGuid(),
        };

        var target = await sut.ResolveMutationTargetAsync(Caller(), othersFork, "projects:manage-providers");

        target.Should().BeSameAs(othersFork);
    }

    // ── Fork timestamps ───────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveMutationTargetAsync_Fork_SetsCreatedAtAndUpdatedAtFromTimeProvider()
    {
        var now = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        var (sut, _) = BuildSut(Guid.NewGuid(), permits: code => code == "projects:read", now: now);
        var project = GeneralProject(Guid.NewGuid());

        var target = await sut.ResolveMutationTargetAsync(Caller(), project, "projects:manage-view");

        target!.CreatedAt.Should().Be(now.UtcDateTime);
        target.UpdatedAt.Should().Be(now.UtcDateTime);
    }
}
