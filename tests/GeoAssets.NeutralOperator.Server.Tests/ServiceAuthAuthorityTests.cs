using FluentAssertions;
using GeoAssets.Identity.Authentication;
using GeoAssets.NeutralOperator.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests;

/// <summary>
/// XD01-452: <c>GeoAssets.NeutralOperator.Server</c> asks its <c>GeoAssets.ServiceCaller</c> token from the CIAM authority (instance + tenant id), whose
/// issuer the kernel accepts, never from MSAL's <c>WithTenantId</c> default, <c>login.microsoftonline.com</c>.
/// </summary>
public sealed class ServiceAuthAuthorityTests
{
    private const string TenantId = "94bb6627-6a6f-4219-b6d2-ce9ca5e82215";

    private static readonly KeyValuePair<string, string?>[] EnabledCredentials =
    [
        new("ServiceAuth:Enabled", "true"),
        new("ServiceAuth:TenantId", TenantId),
        new("ServiceAuth:ClientId", "8a5c1a3e-1f6e-4f3c-9d2b-0c5e7b6a4d21"),
        new("ServiceAuth:ClientSecret", "not-a-real-secret"),
        new("ServiceAuth:Scope", "api://geoassets-server/.default"),
    ];

    private static IServiceCollection Register(params KeyValuePair<string, string?>[] settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddServiceAuthTokenProvider(configuration);
    }

    private static EntraCiamServiceAuthTokenProvider Provider(IServiceCollection services) =>
        services.Should().ContainSingle(d => d.ServiceType == typeof(IServiceAuthTokenProvider))
            .Which.ImplementationInstance.Should().BeOfType<EntraCiamServiceAuthTokenProvider>().Subject;

    [Fact]
    public void TheAuthority_DefaultsToTheAzureAdCiamInstance()
    {
        var services = Register([.. EnabledCredentials, new("AzureAdCiam:Instance", "https://contoso.ciamlogin.com/")]);

        var authority = Provider(services).Authority;

        authority.AbsoluteUri.Should().Be($"https://contoso.ciamlogin.com/{TenantId}");
        authority.Host.Should().NotBe("login.microsoftonline.com");
    }

    [Fact]
    public void TheServiceAuthInstance_WinsOverTheAzureAdCiamInstance()
    {
        var services = Register(
        [
            .. EnabledCredentials,
            new("ServiceAuth:Instance", "https://kernel-tenant.ciamlogin.com/"),
            new("AzureAdCiam:Instance", "https://contoso.ciamlogin.com/"),
        ]);

        Provider(services).Authority.AbsoluteUri.Should().Be($"https://kernel-tenant.ciamlogin.com/{TenantId}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://contoso.ciamlogin.com/")]
    [InlineData("contoso.ciamlogin.com")]
    public void EnabledWithoutAUsableInstance_FailsTheStartup(string? instance)
    {
        var act = () => Register([.. EnabledCredentials, new("AzureAdCiam:Instance", instance)]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ServiceAuth:Instance*");
    }

    [Fact]
    public void Disabled_RegistersNoTokenProvider()
    {
        var services = Register(new KeyValuePair<string, string?>("AzureAdCiam:Instance", "https://contoso.ciamlogin.com/"));

        services.Should().NotContain(d => d.ServiceType == typeof(IServiceAuthTokenProvider));
    }
}
