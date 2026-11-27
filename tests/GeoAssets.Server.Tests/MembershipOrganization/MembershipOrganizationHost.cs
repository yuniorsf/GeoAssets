using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Server.Tests.Routing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Server.Tests.MembershipOrganization;

/// <summary>
/// The real kernel host (postgres-spatial) on the shared Testcontainers database, signed in by
/// <see cref="MembershipAuthenticationHandler"/> and answering identity records from <see cref="MembershipAuthorizationService"/>:
/// one with <c>GeoAssets:Tenancy:OrganizationFromMembership</c> on, one with it off (XD01-471).
/// </summary>
internal static class MembershipOrganizationHost
{
    public const string CollectionName = "MembershipOrganizationHost";

    private static readonly SemaphoreSlim StartLock = new(1, 1);
    private static MembershipOrganizationHostContext? _enabled;
    private static MembershipOrganizationHostContext? _disabled;
    private static readonly List<WebApplicationFactory<HttpTenantContext>> Factories = [];

    public static Task<MembershipOrganizationHostContext> GetEnabledAsync() => GetAsync(enabled: true);

    public static Task<MembershipOrganizationHostContext> GetDisabledAsync() => GetAsync(enabled: false);

    private static async Task<MembershipOrganizationHostContext> GetAsync(bool enabled)
    {
        await StartLock.WaitAsync();
        try
        {
            if ((enabled ? _enabled : _disabled) is { } existing)
            {
                return existing;
            }

            _ = await ServerHostFixture.GetServicesAsync();
            var baseFactory = await ServerHostFixture.CreateUnstartedFactoryAsync("postgres-spatial");
            var factory = baseFactory.WithWebHostBuilder(host =>
            {
                host.UseSetting(CallerMembershipOrganizationExtensions.EnabledKey, enabled ? "true" : "false");
                host.ConfigureTestServices(services =>
                {
                    services.AddScoped<IGeoAuthorizationService, MembershipAuthorizationService>();
                    services.AddAuthentication(options =>
                        {
                            options.DefaultScheme = MembershipAuthenticationHandler.SchemeName;
                            options.DefaultAuthenticateScheme = MembershipAuthenticationHandler.SchemeName;
                            options.DefaultChallengeScheme = MembershipAuthenticationHandler.SchemeName;
                            options.DefaultForbidScheme = MembershipAuthenticationHandler.SchemeName;
                        })
                        .AddScheme<AuthenticationSchemeOptions, MembershipAuthenticationHandler>(MembershipAuthenticationHandler.SchemeName, _ => { });
                });
            });
            Factories.Add(factory);

            var connectionString = factory.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:GeoAssets"]!;
            var context = new MembershipOrganizationHostContext(factory.CreateClient(), connectionString);
            if (enabled) _enabled = context; else _disabled = context;
            return context;
        }
        finally
        {
            StartLock.Release();
        }
    }
}
