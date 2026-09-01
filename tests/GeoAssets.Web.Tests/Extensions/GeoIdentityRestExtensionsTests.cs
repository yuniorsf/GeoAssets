using FluentAssertions;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Web.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Web.Tests.Extensions;

/// <summary>
/// Proves <see cref="GeoIdentityRestExtensions.AddGeoIdentityRest"/> fails fast with a clear
/// exception when GeoAssetsServer:BaseUrl is missing, instead of silently resolving repositories
/// that would fail unhelpfully against an empty base address (XD01-132).
/// </summary>
public class GeoIdentityRestExtensionsTests
{
    private static IServiceProvider BuildProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddHttpClient("GeoAssetsServer");
        services.AddGeoIdentityRest();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddGeoIdentityRest_MissingGeoAssetsServerBaseUrl_ThrowsOnResolve()
    {
        var configuration = new ConfigurationBuilder().Build();
        var provider = BuildProvider(configuration);

        var act = () => provider.GetRequiredService<IUserRepository>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("GeoAssetsServer:BaseUrl is not configured.");
    }

    [Fact]
    public void AddGeoIdentityRest_ConfiguredGeoAssetsServerBaseUrl_ResolvesSuccessfully()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GeoAssetsServer:BaseUrl"] = "http://test"
            })
            .Build();
        var provider = BuildProvider(configuration);

        var repository = provider.GetRequiredService<IUserRepository>();

        repository.Should().NotBeNull();
    }
}
