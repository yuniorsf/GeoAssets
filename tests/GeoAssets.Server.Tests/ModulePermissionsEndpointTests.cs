using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.Modules;
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

/// <summary>XD01-377: <c>PUT /api/identity/verticals/{verticalId}/permissions</c> — only a service principal may publish a vertical's permissions.</summary>
public sealed class ModulePermissionsEndpointTests
{
    private const string RolesHeader = "X-Test-Roles";
    private const string Url = "/api/identity/verticals/widgets/permissions";

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RolesHeader, out var roles)) return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "caller") };
            claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => new Claim(ClaimTypes.Role, r)));
            var identity = new ClaimsIdentity(claims, "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private IHost? _host;

        public GeoIdentityDbContext Db { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;

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
                        // MapIdentityApi() maps every identity endpoint; their dependencies must be resolvable to build the route table.
                        AddNeverCalled<IGeoAuthorizationService>(services);
                        AddNeverCalled<IUserRepository>(services);
                        AddNeverCalled<IRoleRepository>(services);
                        AddNeverCalled<IPermissionRepository>(services);
                        AddNeverCalled<IPolicyRepository>(services);
                        AddNeverCalled<IOrganizationGrantRepository>(services);
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
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints => endpoints.MapIdentityApi());
                    });
                })
                .StartAsync();
            Client = _host.GetTestServer().CreateClient();
        }

        public static HttpRequestMessage Put(object? body, string? roles)
        {
            var request = new HttpRequestMessage(HttpMethod.Put, new Uri(Url, UriKind.Relative)) { Content = JsonContent.Create(body) };
            if (roles is not null) request.Headers.Add(RolesHeader, roles);
            return request;
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
    }

    private static void AddNeverCalled<TService>(IServiceCollection services)
        where TService : class =>
        services.AddSingleton(DispatchProxy.Create<TService, NeverCalledProxy>());

    private static readonly ModulePermissionSet ValidSet = new()
    {
        Permissions = [new("widgets:read", "widgets", "read", "permissions.widgets.read")],
        RoleBundles = [new(new Guid("10000000-0000-0000-0000-0000000000b1"), "WidgetReader", "Lee widgets", ["widgets:read"])],
    };

    [Fact]
    public async Task Put_Anonymous_Returns401()
    {
        await using var harness = new Harness();
        await harness.StartAsync();

        using var request = Harness.Put(ValidSet, roles: null);
        using var response = await harness.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await harness.Db.Permissions.AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Supervisor,Administrator")]
    [InlineData("SomeOtherRole")]
    public async Task Put_AuthenticatedWithoutTheServiceCallerAppRole_Returns403(string roles)
    {
        await using var harness = new Harness();
        await harness.StartAsync();

        using var request = Harness.Put(ValidSet, roles);
        using var response = await harness.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await harness.Db.Permissions.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Put_WithServiceCallerAppRole_AppliesTheSet_ThenReportsNoChangeForTheSameBody()
    {
        await using var harness = new Harness();
        await harness.StartAsync();

        using var first = Harness.Put(ValidSet, GeoAuthorizationHandler.ServiceCallerAppRole);
        using var firstResponse = await harness.Client.SendAsync(first);
        using var second = Harness.Put(ValidSet, GeoAuthorizationHandler.ServiceCallerAppRole);
        using var secondResponse = await harness.Client.SendAsync(second);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<Result>();
        firstBody.Should().Be(new Result("widgets", true));
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await secondResponse.Content.ReadFromJsonAsync<Result>()).Should().Be(new Result("widgets", false));
        (await harness.Db.Permissions.CountAsync(p => p.Code == "widgets:read")).Should().Be(1);
        (await harness.Db.Roles.CountAsync(r => r.Name == "WidgetReader")).Should().Be(1);
        (await harness.Db.RolePermissions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Put_InvalidSet_Returns400WithTheProblem()
    {
        await using var harness = new Harness();
        await harness.StartAsync();
        var invalid = new ModulePermissionSet { Permissions = [new("", "widgets", "read", "k")] };

        using var request = Harness.Put(invalid, GeoAuthorizationHandler.ServiceCallerAppRole);
        using var response = await harness.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("error").And.Contain("needs a code");
        (await harness.Db.Permissions.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Put_NullBody_Returns400()
    {
        await using var harness = new Harness();
        await harness.StartAsync();

        using var request = Harness.Put(null, GeoAuthorizationHandler.ServiceCallerAppRole);
        using var response = await harness.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed record Result(string VerticalId, bool Changed);
}
