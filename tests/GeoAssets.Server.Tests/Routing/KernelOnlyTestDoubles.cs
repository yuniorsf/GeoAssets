using GeoAssets.Core.Interfaces;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Server.Tests.Routing;

/// <summary>
/// What the kernel-only host (no module enabled, XD01-359) needs from a test: the asset store a module would otherwise
/// provide, and a signed-in, fully permitted caller in place of the bearer-token scheme.
/// </summary>
internal static class KernelOnlyTestDoubles
{
    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAssetProvider>(new TestAssetProvider());
        services.AddScoped<IGeoAuthorizationService, KernelOnlyPermittingAuthorizationService>();
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = KernelOnlyAuthenticationHandler.SchemeName;
                options.DefaultAuthenticateScheme = KernelOnlyAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = KernelOnlyAuthenticationHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, KernelOnlyAuthenticationHandler>(KernelOnlyAuthenticationHandler.SchemeName, _ => { });
    }
}
