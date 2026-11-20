using System.Collections.Concurrent;
using System.Net;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.EFCore.Repositories;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using System.Reflection;
using GeoAssets.Core.Interfaces;

namespace GeoAssets.Server.Tests.IpBillingBridge;

/// <summary>
/// The kernel as a vertical host reaches it, with the kernel's own endpoint code: <c>MapIdentityApi</c> (the caller's
/// <c>/api/identity/me</c>, the permission registration PUT) and <c>MapOrganizationGrantsApi</c> over the real
/// <see cref="EFOrganizationGrantRepository"/> and <see cref="OrganizationGrantService"/> on an in-memory SQLite identity
/// database. Authentication is <see cref="BridgeTokenAuthenticationHandler"/> (kernel audience), every endpoint without its own
/// policy requires an authenticated caller — as the real kernel's fallback policy does. Records every request.
/// </summary>
internal sealed class StubKernel : IAsyncDisposable
{
    private readonly string _connectionString = $"Data Source=file:bridge-kernel-{Guid.NewGuid():N}?mode=memory&cache=shared";
    private SqliteConnection? _keeper;
    private readonly ConcurrentQueue<KernelRequest> _requests = new();
    private IHost? _host;

    /// <summary>A context of the test's own, over the kernel's database — read it for assertions only.</summary>
    public GeoIdentityDbContext Db { get; private set; } = null!;

    /// <summary>A new context over the kernel's database for one read; dispose it.</summary>
    public GeoIdentityDbContext NewReadContext() => NewContext();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));

    /// <summary>Answers the registration PUTs 503 while true (the kernel is up but not ready).</summary>
    public bool RegistrationFails { get; set; }

    public IReadOnlyList<KernelRequest> Requests => [.. _requests];

    public IReadOnlyList<KernelRequest> To(HttpMethod method, string pathContains) =>
        [.. Requests.Where(r => r.Method == method && r.Path.Contains(pathContains, StringComparison.Ordinal))];

    /// <summary>The kernel in process: an <see cref="HttpMessageHandler"/> a vertical host's HTTP clients send through.</summary>
    public HttpMessageHandler CreateHandler() => _host!.GetTestServer().CreateHandler();

    public async Task StartAsync()
    {
        // A shared-cache in-memory database, so each request's scope has a connection of its own (the registration loop and a test's
        // request run concurrently); the keeper connection holds the database alive.
        _keeper = new SqliteConnection(_connectionString);
        await _keeper.OpenAsync();
        Db = NewContext();
        await Db.Database.EnsureCreatedAsync();

        _host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddHttpContextAccessor();
                    services.AddAuthentication(BridgeTokenAuthenticationHandler.SchemeName)
                        .AddScheme<BridgeTokenAuthenticationOptions, BridgeTokenAuthenticationHandler>(
                            BridgeTokenAuthenticationHandler.SchemeName, o => o.KernelOnly = true);
                    services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
                    services.AddScoped(_ => NewContext());
                    services.AddSingleton<TimeProvider>(Time);
                    services.AddScoped<IGeoAuthorizationService, ClaimsAuthorizationService>();
                    services.AddScoped<IOrganizationGrantRepository>(sp =>
                        new EFOrganizationGrantRepository(sp.GetRequiredService<GeoIdentityDbContext>(), sp.GetRequiredService<TimeProvider>()));
                    services.AddScoped<OrganizationGrantService>();
                    // MapIdentityApi maps every identity endpoint; their other dependencies must resolve to build the route table.
                    AddNeverCalled<IUserRepository>(services);
                    AddNeverCalled<IRoleRepository>(services);
                    AddNeverCalled<IPermissionRepository>(services);
                    AddNeverCalled<IPolicyRepository>(services);
                    AddNeverCalled<IRoleAssignmentProvider>(services);
                    AddNeverCalled<IPendingInvitationRepository>(services);
                    AddNeverCalled<IUserInvitationProvider>(services);
                    AddNeverCalled<IInvitationEmailSender>(services);
                    AddNeverCalled<IUserClaimRepository>(services);
                    AddNeverCalled<IOrganizationRepository>(services);
                    AddNeverCalled<IGroupRepository>(services);
                    AddNeverCalled<IProjectRepository>(services);
                });
                webHost.Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        _requests.Enqueue(new KernelRequest(new HttpMethod(context.Request.Method), context.Request.Path.Value ?? string.Empty, context.Request.Headers.Authorization.ToString() is { Length: > 0 } a ? a : null));
                        if (RegistrationFails && HttpMethods.IsPut(context.Request.Method) && context.Request.Path.StartsWithSegments("/api/identity/verticals", StringComparison.Ordinal))
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                            return;
                        }

                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapIdentityApi();
                        endpoints.MapOrganizationGrantsApi();
                    });
                });
            })
            .StartAsync();
    }

    public async Task<IReadOnlyList<OrganizationGrant>> ActiveGrantsAsync(Guid grantee, Guid resource)
    {
        using var scope = _host!.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IOrganizationGrantRepository>().GetActiveGrantsAsync(grantee, resource);
    }

    public async Task<IReadOnlyList<OrganizationGrantSyncEntry>> OutboxAsync()
    {
        using var scope = _host!.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IOrganizationGrantRepository>().GetSyncBatchAsync(0, 1000);
    }

    private GeoIdentityDbContext NewContext() =>
        new(new DbContextOptionsBuilder<GeoIdentityDbContext>().UseSqlite(_connectionString).Options);

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        if (Db is not null) await Db.DisposeAsync();
        if (_keeper is not null) await _keeper.DisposeAsync();
    }

    private static void AddNeverCalled<TService>(IServiceCollection services)
        where TService : class =>
        services.AddSingleton(DispatchProxy.Create<TService, NeverCalledProxy>());

    /// <summary>The kernel's answer for "who is the caller": the roles and permissions of the request's validated token.</summary>
    private sealed class ClaimsAuthorizationService(IHttpContextAccessor accessor) : IGeoAuthorizationService
    {
        private System.Security.Claims.ClaimsPrincipal User => accessor.HttpContext!.User;

        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => Task.FromResult(User.IsInRole(roleName));

        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) =>
            Task.FromResult(User.HasClaim(BridgeTokenAuthenticationHandler.PermissionClaim, permissionCode));

        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) =>
            Task.FromResult(new AuthorizationContext
            {
                User = new AppUser { Id = Guid.NewGuid(), Email = "caller@example.test", DisplayName = "Caller", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                Roles = [.. User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value)],
                Claims = [],
                Permissions = [.. User.FindAll(BridgeTokenAuthenticationHandler.PermissionClaim).Select(c => c.Value)],
            });
    }
}
