using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.EFCore.Repositories;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GeoAssets.Server.Tests.OrganizationScopedRoles;

/// <summary>
/// The scoped-role administration routes (XD01-327) over the production identity stack: the real
/// <see cref="GeoAuthorizationService"/>, the AuthZ policy bridge and the EF repositories, on an in-memory SQLite identity
/// database with one request-scoped <see cref="GeoIdentityDbContext"/> per request. A request is made as one of the seeded
/// users (<see cref="UserHeader"/> = its external object id), whose identity-provider roles are its seeded home role.
/// <para>
/// COELBA has contracted BIMBO (an active grant, from COELBA to BIMBO, that requires a scoped role) and once contracted
/// THIRD (an expired grant). Each organization has an administrator (role <see cref="AdministratorRole"/>: <c>users:read</c>,
/// <c>users:edit</c>); BIMBO has two technicians and an inactive user; <see cref="PlatformAdmin"/> holds
/// <c>organizations:manage-all</c>.
/// </para>
/// </summary>
internal sealed class ScopedRolesHarness : IAsyncDisposable
{
    public const string UserHeader = "X-Test-User";

    public const string AdministratorRole = "Administrator";
    public const string PlatformAdministratorRole = "PlatformAdministrator";
    public const string ContractorRole = "Contractor C1";
    public const string ReaderRole = "UserReader";

    public static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000327");
    public static readonly Guid Bimbo = new("b1b00000-0000-4000-8000-000000000327");
    public static readonly Guid Third = new("7e1d0000-0000-4000-8000-000000000327");

    public static readonly Guid CoelbaAdmin = new("aa000001-0000-4000-8000-000000000327");
    public static readonly Guid CoelbaMember = new("aa000002-0000-4000-8000-000000000327");
    public static readonly Guid CoelbaReader = new("aa000003-0000-4000-8000-000000000327");
    public static readonly Guid BimboAdmin = new("bb000001-0000-4000-8000-000000000327");
    public static readonly Guid BimboTech = new("bb000002-0000-4000-8000-000000000327");
    public static readonly Guid BimboTech2 = new("bb000003-0000-4000-8000-000000000327");
    public static readonly Guid BimboInactive = new("bb000004-0000-4000-8000-000000000327");
    public static readonly Guid ThirdTech = new("cc000001-0000-4000-8000-000000000327");
    public static readonly Guid PlatformAdmin = new("dd000001-0000-4000-8000-000000000327");

    public static readonly Guid AdministratorRoleId = new("11000000-0000-4000-8000-000000000327");
    public static readonly Guid PlatformAdministratorRoleId = new("12000000-0000-4000-8000-000000000327");
    public static readonly Guid ContractorRoleId = new("13000000-0000-4000-8000-000000000327");
    public static readonly Guid ReaderRoleId = new("14000000-0000-4000-8000-000000000327");

    private static readonly Dictionary<Guid, string> HomeRoles = new()
    {
        [CoelbaAdmin] = AdministratorRole,
        [CoelbaReader] = ReaderRole,
        [BimboAdmin] = AdministratorRole,
        [PlatformAdmin] = PlatformAdministratorRole,
    };

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private IHost? _host;
    private bool _disposed;

    public HttpClient Client { get; private set; } = null!;

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero));

    public async Task StartAsync()
    {
        await _connection.OpenAsync();
        await using (var db = NewContext())
        {
            await db.Database.EnsureCreatedAsync();
            Seed(db);
            await db.SaveChangesAsync();
        }

        _host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddHttpContextAccessor();
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("Test", _ => { });
                    services.AddAuthorization();
                    services.AddGeoAuthorizationPolicyBridge();
                    services.AddSingleton<TimeProvider>(Time);
                    services.AddDbContext<GeoIdentityDbContext>(options => options.UseSqlite(_connection));
                    services.AddScoped<IUserRepository, EFUserRepository>();
                    services.AddScoped<IRoleRepository, EFRoleRepository>();
                    services.AddScoped<IOrganizationRepository, EFOrganizationRepository>();
                    services.AddScoped<IOrganizationGrantRepository, EFOrganizationGrantRepository>();
                    services.AddScoped<IUserClaimRepository, EFUserClaimRepository>();
                    services.AddScoped<IPolicyRepository, EFPolicyRepository>();
                    services.AddScoped<ICurrentUserAccessor, HeaderCurrentUserAccessor>();
                    services.AddScoped<IGeoAuthorizationService, GeoAuthorizationService>();
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapOrganizationScopedRolesApi());
                });
            })
            .StartAsync();
        Client = _host.GetTestServer().CreateClient();
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, Guid? asUser, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative));
        if (body is not null) request.Content = JsonContent.Create(body);
        if (asUser is { } user) request.Headers.Add(UserHeader, ExternalId(user));
        return await Client.SendAsync(request);
    }

    public Task<HttpResponseMessage> AssignAsync(Guid organizationId, Guid userId, Guid roleId, Guid? asUser) =>
        SendAsync(HttpMethod.Post, $"/api/identity/organizations/{organizationId}/scoped-roles", asUser, new { userId, roleId });

    /// <summary>The authorization context <paramref name="userId"/>'s next request would be evaluated with.</summary>
    public async Task<AuthorizationContext> ContextOfNextRequestAsync(Guid userId)
    {
        using var scope = _host!.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var http = new DefaultHttpContext();
        http.Request.Headers[UserHeader] = ExternalId(userId);
        accessor.HttpContext = http;
        try
        {
            return await scope.ServiceProvider.GetRequiredService<IGeoAuthorizationService>().GetAuthorizationContextAsync();
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    public async Task<OrganizationGrant> AddGrantAsync(Guid grantee, Guid resource, DateTime? expiresAt = null, bool isActive = true)
    {
        await using var db = NewContext();
        var grant = Grant(grantee, resource, expiresAt, isActive);
        db.OrganizationGrants.Add(grant);
        await db.SaveChangesAsync();
        return grant;
    }

    public async Task<IReadOnlyList<UserRole>> ScopedRowsAsync()
    {
        await using var db = NewContext();
        return await db.UserRoles.AsNoTracking().Where(ur => ur.OrganizationId != null).ToListAsync();
    }

    public async Task SeedScopedRoleAsync(Guid userId, Guid roleId, Guid organizationId, string assignedBy = "seed")
    {
        await using var db = NewContext();
        db.UserRoles.Add(new UserRole
        {
            UserId = userId, RoleId = roleId, OrganizationId = organizationId, AssignedBy = assignedBy,
            AssignedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();
    }

    public static string ExternalId(Guid userId) => $"oid-{userId}";

    private GeoIdentityDbContext NewContext() =>
        new(new DbContextOptionsBuilder<GeoIdentityDbContext>().UseSqlite(_connection).Options);

    private void Seed(GeoIdentityDbContext db)
    {
        var created = Time.GetUtcNow().UtcDateTime.AddDays(-30);
        db.Organizations.AddRange(
            new Organization { Id = Coelba, Name = "COELBA", Slug = "coelba", CreatedAt = created },
            new Organization { Id = Bimbo, Name = "BIMBO", Slug = "bimbo", CreatedAt = created },
            new Organization { Id = Third, Name = "THIRD", Slug = "third", CreatedAt = created });

        var usersRead = Permission("users:read");
        var usersEdit = Permission("users:edit");
        var manageAll = Permission("organizations:manage-all");
        var ordersRead = Permission("serviceorders:read");
        db.Permissions.AddRange(usersRead, usersEdit, manageAll, ordersRead);

        db.Roles.AddRange(
            Role(AdministratorRoleId, AdministratorRole, usersRead, usersEdit),
            Role(PlatformAdministratorRoleId, PlatformAdministratorRole, usersRead, usersEdit, manageAll),
            Role(ContractorRoleId, ContractorRole, ordersRead),
            Role(ReaderRoleId, ReaderRole, usersRead));

        db.Users.AddRange(
            User(CoelbaAdmin, "coelba.admin@coelba.test", "Ana (COELBA admin)", Coelba, created),
            User(CoelbaMember, "carlos@coelba.test", "Carlos (COELBA)", Coelba, created),
            User(CoelbaReader, "rita@coelba.test", "Rita (COELBA reader)", Coelba, created),
            User(BimboAdmin, "bruno.admin@bimbo.test", "Bruno (BIMBO admin)", Bimbo, created),
            User(BimboTech, "tiago@bimbo.test", "Tiago (BIMBO)", Bimbo, created),
            User(BimboTech2, "beatriz@bimbo.test", "Beatriz (BIMBO)", Bimbo, created),
            User(BimboInactive, "ines@bimbo.test", "Inês (BIMBO, inactive)", Bimbo, created, isActive: false),
            User(ThirdTech, "teo@third.test", "Teo (THIRD)", Third, created),
            User(PlatformAdmin, "root@platform.test", "Paula (platform)", organizationId: null, created));

        var scoped = Grant(Bimbo, Coelba, expiresAt: null, isActive: true);
        scoped.ResourceType = "ServiceOrder";
        scoped.RequiresScopedRole = true;
        scoped.OrderTypeIds.Add("cut");
        db.OrganizationGrants.AddRange(
            scoped,
            Grant(Third, Coelba, expiresAt: Time.GetUtcNow().UtcDateTime.AddDays(-1), isActive: true));
    }

    private OrganizationGrant Grant(Guid grantee, Guid resource, DateTime? expiresAt, bool isActive) => new()
    {
        GranteeOrganizationId = grantee,
        ResourceOrganizationId = resource,
        AllowedActions = ["serviceorders:read"],
        ExpiresAt = expiresAt,
        GrantedBy = "seed",
        GrantedAt = Time.GetUtcNow().UtcDateTime.AddDays(-10),
        IsActive = isActive,
    };

    private static AppPermission Permission(string code) =>
        new() { Code = code, Resource = code.Split(':')[0], Action = code.Split(':')[1], Description = code };

    private static AppRole Role(Guid id, string name, params AppPermission[] permissions)
    {
        var role = new AppRole { Id = id, Name = name };
        role.RolePermissions.AddRange(permissions.Select(p => new RolePermission { RoleId = id, Permission = p }));
        return role;
    }

    private static AppUser User(Guid id, string email, string name, Guid? organizationId, DateTime created, bool isActive = true) => new()
    {
        Id = id,
        ExternalObjectId = ExternalId(id),
        Email = email,
        DisplayName = name,
        OrganizationId = organizationId,
        IsActive = isActive,
        CreatedAt = created,
    };

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        Client?.Dispose();
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await _connection.DisposeAsync();
    }

    /// <summary>The caller named by <see cref="UserHeader"/>, with their seeded home role as the identity provider's roles claim.</summary>
    private sealed class HeaderCurrentUserAccessor(IHttpContextAccessor accessor) : ICurrentUserAccessor
    {
        public CurrentUser? GetCurrentUser()
        {
            var externalId = accessor.HttpContext?.Request.Headers[UserHeader].ToString();
            if (string.IsNullOrEmpty(externalId)) return null;

            var userId = Guid.Parse(externalId["oid-".Length..]);
            IReadOnlyList<string> roles = HomeRoles.TryGetValue(userId, out var role) ? [role] : [];
            return new CurrentUser(externalId, $"{userId}@test", userId.ToString(), roles);
        }
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var user)) return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }
}
