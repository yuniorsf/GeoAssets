using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.EFCore.Repositories;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// XD01-328: <c>GET /api/identity/scoped-role-assignments/sync</c> — the feed a vertical host mirrors the roles users hold on
/// other organizations from. It requires <c>users:read</c> and serves <see cref="EFScopedRoleAssignmentSyncFeed"/>'s entries,
/// a revocation as a tombstone.
/// </summary>
public sealed class ScopedRoleAssignmentSyncEndpointTests : IAsyncLifetime, IDisposable
{
    private const string RolesHeader = "X-Test-Roles";
    private const string UsersReader = "UsersReader";
    private static readonly DateTime Now = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private IHost _host = null!;
    private HttpClient _client = null!;
    private Guid _userId;
    private Guid _roleId;
    private Guid _coelba;

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RolesHeader, out var roles)) return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "caller") };
            claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => new Claim(ClaimTypes.Role, r)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), "Test")));
        }
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using (var db = NewContext())
        {
            await db.Database.EnsureCreatedAsync();
            var bimbo = new Organization { Name = "bimbo", Slug = "bimbo", CreatedAt = Now };
            var coelba = new Organization { Name = "coelba", Slug = "coelba", CreatedAt = Now };
            var user = new AppUser { ExternalObjectId = "oid-ada", Email = "ada@bimbo", DisplayName = "Ada", OrganizationId = bimbo.Id, CreatedAt = Now };
            var role = new AppRole { Name = "Contractor C1" };
            db.AddRange(bimbo, coelba, user, role);
            await db.SaveChangesAsync();
            (_userId, _roleId, _coelba) = (user.Id, role.Id, coelba.Id);
        }

        _host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("Test", _ => { });
                    services.AddAuthorization(options => options.AddPolicy("users:read", policy => policy.RequireRole(UsersReader)));
                    services.AddScoped(_ => NewContext());
                    services.AddScoped<IScopedRoleAssignmentSyncFeed, EFScopedRoleAssignmentSyncFeed>();
                    foreach (var type in new[]
                    {
                        typeof(IGeoAuthorizationService), typeof(IUserRepository), typeof(IRoleRepository), typeof(IPermissionRepository),
                        typeof(IPolicyRepository), typeof(IOrganizationGrantRepository), typeof(IRoleAssignmentProvider),
                        typeof(IPendingInvitationRepository), typeof(IUserInvitationProvider), typeof(IInvitationEmailSender),
                        typeof(IUserClaimRepository), typeof(IOrganizationRepository), typeof(IGroupRepository), typeof(IProjectRepository),
                    })
                    {
                        services.AddSingleton(type, NeverCalled(type));
                    }
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapIdentityApi());
                });
            })
            .StartAsync();
        _client = _host.GetTestServer().CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    public void Dispose() => _connection.Dispose();

    private static object NeverCalled(Type type) =>
        typeof(DispatchProxy).GetMethod(nameof(DispatchProxy.Create), 2, [])!
            .MakeGenericMethod(type, typeof(NeverCalledProxy))
            .Invoke(null, null)!;

    private GeoIdentityDbContext NewContext() =>
        new(new DbContextOptionsBuilder<GeoIdentityDbContext>().UseSqlite(_connection).Options);

    private async Task<HttpResponseMessage> GetAsync(string query, string? roles)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/identity/scoped-role-assignments/sync{query}", UriKind.Relative));
        if (roles is not null) request.Headers.Add(RolesHeader, roles);
        return await _client.SendAsync(request);
    }

    private async Task SetAssignmentAsync(bool assigned)
    {
        await using var db = NewContext();
        var users = new EFUserRepository(db, TimeProvider.System);
        if (assigned) await users.AssignRoleOnOrganizationAsync(_userId, _roleId, _coelba);
        else await users.RemoveRoleOnOrganizationAsync(_userId, _roleId, _coelba);
        await users.SaveChangesAsync();
    }

    [Fact]
    public async Task Anonymous_Returns401()
    {
        using var response = await GetAsync("?after=0&take=10", roles: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WithoutUsersRead_Returns403()
    {
        using var response = await GetAsync("?after=0&take=10", roles: "Technician");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WithUsersRead_ServesTheFeed_ARevocationAsATombstone()
    {
        await SetAssignmentAsync(assigned: true);

        using (var response = await GetAsync("?after=0&take=10", UsersReader))
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var entry = (await response.Content.ReadFromJsonAsync<List<ScopedRoleAssignmentSyncEntry>>())!.Should().ContainSingle().Subject;
            entry.UserId.Should().Be(_userId);
            entry.ExternalObjectId.Should().Be("oid-ada");
            entry.ResourceOrganizationId.Should().Be(_coelba);
            entry.RoleNames.Should().Equal("Contractor C1");
            entry.IsActive.Should().BeTrue();
        }

        await SetAssignmentAsync(assigned: false);

        using var after = await GetAsync("?after=1&take=10", UsersReader);
        (await after.Content.ReadFromJsonAsync<List<ScopedRoleAssignmentSyncEntry>>())!.Should().ContainSingle()
            .Which.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task ANonPositiveTake_ServesTheDefaultBatch()
    {
        await SetAssignmentAsync(assigned: true);

        using var response = await GetAsync("?after=0&take=0", UsersReader);

        (await response.Content.ReadFromJsonAsync<List<ScopedRoleAssignmentSyncEntry>>()).Should().ContainSingle();
    }
}
