using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
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
using Microsoft.Extensions.Time.Testing;

namespace GeoAssets.Server.Tests.OrganizationGrants;

/// <summary>
/// The kernel's generic organization-grant routes (XD01-385) over the real <see cref="OrganizationGrantService"/> and the real
/// <see cref="EFOrganizationGrantRepository"/> (so the grant-sync outbox is the production one) on an in-memory SQLite identity
/// database, on a <see cref="FakeTimeProvider"/>. A request is authenticated by <see cref="RolesHeader"/> (its roles) — none, anonymous.
/// </summary>
internal sealed class OrganizationGrantsHarness : IAsyncDisposable
{
    public const string RolesHeader = "X-Test-Roles";
    public const string OrganizationHeader = "X-Test-Org";
    public const string ServiceCaller = "GeoAssets.ServiceCaller";

    public static readonly Guid Grantee = new("a0a0a0a0-0000-4000-8000-000000000385");
    public static readonly Guid Resource = new("b0b0b0b0-0000-4000-8000-000000000385");
    public static readonly Guid Other = new("c0c0c0c0-0000-4000-8000-000000000385");

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private IHost? _host;

    public GeoIdentityDbContext Db { get; private set; } = null!;

    public HttpClient Client { get; private set; } = null!;

    /// <summary>Wraps the real repository of each request scope (XD01-394: to force save conflicts); set before <see cref="StartAsync"/>.</summary>
    public Func<IOrganizationGrantRepository, IOrganizationGrantRepository>? DecorateRepository { get; set; }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));

    public async Task StartAsync()
    {
        await _connection.OpenAsync();
        Db = new GeoIdentityDbContext(new DbContextOptionsBuilder<GeoIdentityDbContext>().UseSqlite(_connection).Options);
        await Db.Database.EnsureCreatedAsync();

        _host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("Test", _ => { });
                    services.AddAuthorization();
                    services.AddSingleton(Db);
                    services.AddSingleton<TimeProvider>(Time);
                    services.AddScoped<IOrganizationGrantRepository>(sp =>
                    {
                        var repository = new EFOrganizationGrantRepository(sp.GetRequiredService<GeoIdentityDbContext>(), sp.GetRequiredService<TimeProvider>());
                        return DecorateRepository?.Invoke(repository) ?? repository;
                    });
                    services.AddScoped<OrganizationGrantService>();
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapOrganizationGrantsApi());
                });
            })
            .StartAsync();
        Client = _host.GetTestServer().CreateClient();
    }

    public static HttpRequestMessage Request(HttpMethod method, string url, string? roles, object? body = null)
    {
        var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative));
        if (body is not null) request.Content = JsonContent.Create(body);
        if (roles is not null) request.Headers.Add(RolesHeader, roles);
        return request;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? roles, object? body = null)
    {
        using var request = Request(method, url, roles, body);
        return await Client.SendAsync(request);
    }

    public Task<HttpResponseMessage> UpsertAsync(object body, string? roles = ServiceCaller) => SendAsync(HttpMethod.Put, "/api/identity/organization-grants", roles, body);

    public Task<HttpResponseMessage> RevokeAsync(object body, string? roles = ServiceCaller) => SendAsync(HttpMethod.Post, "/api/identity/organization-grants/revoke", roles, body);

    public Task<HttpResponseMessage> ListAsync(Guid grantee, Guid resource, string? query = null, string? roles = ServiceCaller) =>
        SendAsync(HttpMethod.Get, $"/api/identity/organization-grants?granteeOrganizationId={grantee}&resourceOrganizationId={resource}{query}", roles);

    public static object UpsertBody(
        Guid? grantee = null, Guid? resource = null, string? resourceType = null, string? scopeId = "3550308",
        string[]? actions = null, string? requiredRole = null, DateTime? expiresAt = null, string grantedBy = "admin@test") => new
        {
            key = new { granteeOrganizationId = grantee ?? Grantee, resourceOrganizationId = resource ?? Resource, resourceType, scopeId },
            allowedActions = actions ?? ["ip-billing:read"],
            requiredRole,
            expiresAt,
            grantedBy,
        };

    public static object KeyBody(Guid? grantee = null, Guid? resource = null, string? resourceType = null, string? scopeId = "3550308") =>
        new { granteeOrganizationId = grantee ?? Grantee, resourceOrganizationId = resource ?? Resource, resourceType, scopeId };

    public async Task<IReadOnlyList<OrganizationGrantSyncEntry>> OutboxAsync()
    {
        using var scope = _host!.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IOrganizationGrantRepository>().GetSyncBatchAsync(0, 1000);
    }

    public async Task<OrganizationGrant> SeedAsync(
        Guid? grantee = null, Guid? resource = null, string? resourceType = null, string? scopeId = "3550308",
        bool isActive = true, DateTime? expiresAt = null, DateTime? grantedAt = null, string[]? actions = null)
    {
        var grant = new OrganizationGrant
        {
            GranteeOrganizationId = grantee ?? Grantee,
            ResourceOrganizationId = resource ?? Resource,
            ResourceType = resourceType,
            ScopeId = scopeId,
            AllowedActions = [.. actions ?? ["ip-billing:read"]],
            ExpiresAt = expiresAt,
            GrantedBy = "seed",
            GrantedAt = grantedAt ?? Time.GetUtcNow().UtcDateTime,
            IsActive = isActive,
        };
        Db.OrganizationGrants.Add(grant);
        await Db.SaveChangesAsync();
        return grant;
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        if (Db is not null) await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RolesHeader, out var roles)) return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "caller") };
            claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => new Claim(ClaimTypes.Role, r)));
            if (Request.Headers.TryGetValue(OrganizationHeader, out var org)) claims.Add(new Claim("org", org.ToString()));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), "Test")));
        }
    }
}
