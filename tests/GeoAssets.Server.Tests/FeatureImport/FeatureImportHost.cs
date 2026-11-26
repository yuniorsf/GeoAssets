using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Server.Tests.Routing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Server.Tests.FeatureImport;

internal static class FeatureImportHost
{
    public const string CollectionName = "FeatureImportHost";

    private static readonly SemaphoreSlim StartLock = new(1, 1);
    private static FeatureImportHostContext? _context;
    private static WebApplicationFactory<HttpTenantContext>? _factory;

    public static async Task<FeatureImportHostContext> GetAsync()
    {
        await StartLock.WaitAsync();
        try
        {
            if (_context is null)
            {
                _ = await ServerHostFixture.GetServicesAsync();
                var baseFactory = await ServerHostFixture.CreateUnstartedFactoryAsync("postgres-spatial");
                _factory = baseFactory.WithWebHostBuilder(host => host.ConfigureTestServices(services =>
                {
                    services.AddScoped<IGeoAuthorizationService, KernelOnlyPermittingAuthorizationService>();
                    services.AddAuthentication(options =>
                        {
                            options.DefaultScheme = HeaderRolesAuthenticationHandler.SchemeName;
                            options.DefaultAuthenticateScheme = HeaderRolesAuthenticationHandler.SchemeName;
                            options.DefaultChallengeScheme = HeaderRolesAuthenticationHandler.SchemeName;
                            options.DefaultForbidScheme = HeaderRolesAuthenticationHandler.SchemeName;
                        })
                        .AddScheme<AuthenticationSchemeOptions, HeaderRolesAuthenticationHandler>(HeaderRolesAuthenticationHandler.SchemeName, _ => { });
                }));
                var connectionString = _factory.Services.GetRequiredService<IConfiguration>()["ConnectionStrings:GeoAssets"]!;
                _context = new FeatureImportHostContext(_factory.CreateClient(), connectionString);
            }

            return _context;
        }
        finally
        {
            StartLock.Release();
        }
    }
}
