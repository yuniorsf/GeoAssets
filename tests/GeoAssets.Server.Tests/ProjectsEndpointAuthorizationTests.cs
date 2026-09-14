using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// Proves <see cref="IProjectService"/>'s copy-on-write fork-on-edit (XD01-141) is actually
/// wired into the real <c>ProjectsRestApiExtensions</c> endpoints through the full ASP.NET
/// Core pipeline — in particular the deliberate route-level deviation documented on
/// <see cref="ProjectsRestApiExtensions"/> (manage-* scope endpoints route-require only
/// <c>projects:read</c>, not the specific manage-* code, so the fallback in
/// <see cref="ProjectService"/> — unit-tested in isolation by <see cref="ProjectServiceTests"/>
/// — actually gets a chance to run instead of being 403'd before the endpoint body executes).
/// </summary>
public class ProjectsEndpointAuthorizationTests
{
    private sealed class FakeGeoAuthorizationService(Guid callerId, HashSet<string> permissions) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => Task.FromResult(permissions.Contains(permissionCode));
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default)
            => Task.FromResult(new AuthorizationContext
            {
                User = new AppUser
                {
                    Id = callerId, Email = "caller@example.com", DisplayName = "Caller",
                    CreatedAt = DateTime.UtcNow, OrganizationId = null,
                },
                Roles       = [],
                Claims      = [],
                Permissions = [.. permissions],
            });
    }

    private sealed class NeverCalledOrganizationGrantRepository : IOrganizationGrantRepository
    {
        public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OrganizationGrant>>([]);
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(Guid granteeOrganizationId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OrganizationGrant>>([]);
        public Task AddAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Minimal in-memory <see cref="IProjectRepository"/> — enough for these
    /// endpoint-wiring tests, not a general-purpose test double.</summary>
    private sealed class InMemoryProjectRepository : IProjectRepository
    {
        private readonly List<Project> _projects;
        public InMemoryProjectRepository(IEnumerable<Project> seed) => _projects = [.. seed];

        public event EventHandler<Project>? ProjectSaved;
        public event EventHandler<Guid>? ProjectDeleted;

        public Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_projects.FirstOrDefault(p => p.Id == id));

        public Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Project>>([.. _projects]);

        public Task<IReadOnlyList<Project>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Project>>([.. _projects.Where(p => p.OrganizationId == organizationId)]);

        public Task<IReadOnlyList<Project>> GetForksOfAsync(Guid parentProjectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Project>>([.. _projects.Where(p => p.ParentProjectId == parentProjectId)]);

        public Task AddAsync(Project project, CancellationToken ct = default)
        {
            _projects.Add(project);
            ProjectSaved?.Invoke(this, project);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Project project, CancellationToken ct = default)
        {
            var index = _projects.FindIndex(p => p.Id == project.Id);
            if (index < 0) throw new KeyNotFoundException($"Project '{project.Id}' not found.");
            _projects[index] = project;
            ProjectSaved?.Invoke(this, project);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            _projects.RemoveAll(p => p.Id == id);
            ProjectDeleted?.Invoke(this, id);
            return Task.CompletedTask;
        }
    }

    private static async Task<TestServer> BuildServerAsync(Guid callerId, HashSet<string> permissions, Project seed)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("Test", _ => { });
                    services.AddAuthorization();
                    services.AddGeoAuthorizationPolicyBridge();
                    services.AddSingleton(TimeProvider.System);
                    services.AddSingleton<IGeoAuthorizationService>(new FakeGeoAuthorizationService(callerId, permissions));
                    services.AddSingleton<IOrganizationGrantRepository>(new NeverCalledOrganizationGrantRepository());
                    var repo = new InMemoryProjectRepository([seed]);
                    services.AddSingleton<IProjectRepository>(repo);
                    services.AddSingleton<IProjectReader>(repo);
                    services.AddSingleton<IProjectWriter>(repo);
                    services.AddSingleton<IProjectService, ProjectService>();
                });
                webHost.Configure(app =>
                {
                    app.Use(async (ctx, next) =>
                    {
                        if (ctx.Request.Headers.ContainsKey("X-Test-Authenticated"))
                            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                                [new Claim(ClaimTypes.NameIdentifier, "test-user")], "TestScheme"));
                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapProjectsApi());
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    private static HttpClient AuthenticatedClient(TestServer server)
    {
        var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "1");
        return client;
    }

    private static Project GeneralProject() => new()
    {
        Id             = Guid.NewGuid(),
        Kind           = ProjectKind.General,
        OrganizationId = Guid.Empty, // unowned sentinel — org-boundary check always passes
        Name           = "Shared Project",
        Description    = "desc",
    };

    // ── projects:read gates every route, including manage-* ones ─────────────

    [Fact]
    public async Task PutProviders_CallerLacksEvenRead_Returns403BeforeReachingTheHandler()
    {
        var project = GeneralProject();
        using var server = await BuildServerAsync(Guid.NewGuid(), permissions: [], project);
        using var client = AuthenticatedClient(server);

        var response = await client.PutAsJsonAsync($"/api/projects/{project.Id}/providers", new List<ProjectProviderEntry>());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── The copy-on-write deviation itself ────────────────────────────────────

    [Fact]
    public async Task PutProviders_CallerHasReadOnly_RedirectsOntoAForkInsteadOf403()
    {
        // This is the behavior that requires the manage-* endpoints to route-require only
        // projects:read (see ProjectsRestApiExtensions's own doc comment) — fails without
        // that design: requiring projects:manage-providers at the route level would 403 here
        // before this handler, and the copy-on-write fallback would never run.
        var project = GeneralProject();
        using var server = await BuildServerAsync(Guid.NewGuid(), permissions: ["projects:read"], project);
        using var client = AuthenticatedClient(server);

        var response = await client.PutAsJsonAsync($"/api/projects/{project.Id}/providers", new List<ProjectProviderEntry>());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var returned = await response.Content.ReadFromJsonAsync<Project>();
        returned!.Kind.Should().Be(ProjectKind.User);
        returned.ParentProjectId.Should().Be(project.Id);
    }

    [Fact]
    public async Task PutProviders_CallerHasManagePermission_MutatesTheGeneralProjectDirectly()
    {
        var project = GeneralProject();
        using var server = await BuildServerAsync(Guid.NewGuid(), permissions: ["projects:read", "projects:manage-providers"], project);
        using var client = AuthenticatedClient(server);

        var response = await client.PutAsJsonAsync($"/api/projects/{project.Id}/providers", new List<ProjectProviderEntry>());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var returned = await response.Content.ReadFromJsonAsync<Project>();
        returned!.Id.Should().Be(project.Id);
        returned.Kind.Should().Be(ProjectKind.General);
    }

    // ── Rename/Delete: no copy-on-write, route-requires the exact code ────────

    [Fact]
    public async Task PutName_CallerHasReadOnly_Returns403NotAFork()
    {
        var project = GeneralProject();
        using var server = await BuildServerAsync(Guid.NewGuid(), permissions: ["projects:read"], project);
        using var client = AuthenticatedClient(server);

        var response = await client.PutAsJsonAsync($"/api/projects/{project.Id}/name",
            new { Name = "New Name", Description = "New Desc" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteProject_CallerHasReadOnly_Returns403NotAFork()
    {
        var project = GeneralProject();
        using var server = await BuildServerAsync(Guid.NewGuid(), permissions: ["projects:read"], project);
        using var client = AuthenticatedClient(server);

        var response = await client.DeleteAsync($"/api/projects/{project.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteProject_CallerHasDeletePermission_Returns204()
    {
        var project = GeneralProject();
        using var server = await BuildServerAsync(Guid.NewGuid(), permissions: ["projects:read", "projects:delete"], project);
        using var client = AuthenticatedClient(server);

        var response = await client.DeleteAsync($"/api/projects/{project.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Not found ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetProject_UnknownId_Returns404()
    {
        var project = GeneralProject();
        using var server = await BuildServerAsync(Guid.NewGuid(), permissions: ["projects:read"], project);
        using var client = AuthenticatedClient(server);

        var response = await client.GetAsync($"/api/projects/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
