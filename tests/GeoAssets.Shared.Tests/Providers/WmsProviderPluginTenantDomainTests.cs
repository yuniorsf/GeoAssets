using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Provider.WMS;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Shared.Tests.Providers;

/// <summary>
/// XD01-473 — the <c>wms</c> plugin's optional tenant domain: the distributor silo the gateway routes the layer's tiles to
/// (their <c>X-Tenant-Domain</c> header). Blank means none, as before; anything but a host name is refused, since it
/// travels as a request header.
/// </summary>
public sealed class WmsProviderPluginTenantDomainTests
{
    private static readonly IServiceProvider Services = new ServiceCollection().BuildServiceProvider();

    private static async Task<IWmsProvider> CreateAsync(string url, string? tenantDomain)
    {
        var config = new ProviderConfig(new Dictionary<string, string>(StringComparer.Ordinal) { ["url"] = url });
        if (tenantDomain is not null) config.Set("tenantDomain", tenantDomain);
        return (IWmsProvider)await new WmsProviderPlugin().CreateAsync(config, Services);
    }

    [Fact]
    public void ConfigFields_OfferAnOptionalTenantDomain()
    {
        var field = new WmsProviderPlugin().ConfigFields.Should().ContainSingle(f => f.Key == "tenantDomain").Subject;

        field.Required.Should().BeFalse();
        field.Placeholder.Should().Be("coelba.local");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NoTenantDomain_TheLayerHasNone_AsBefore(string? tenantDomain)
    {
        var wms = await CreateAsync("http://localhost:8090", tenantDomain);

        wms.WmsTenantDomain.Should().BeNull();
        wms.WmsBaseUrl.Should().Be("http://localhost:8090/wms");
    }

    [Theory]
    [InlineData("coelba.local", "coelba.local")]
    [InlineData("  cemar.local ", "cemar.local")]
    [InlineData("silo-1.example.com", "silo-1.example.com")]
    [InlineData("localhost", "localhost")]
    public async Task AHostName_IsTheLayersTenantDomain(string tenantDomain, string expected)
    {
        var wms = await CreateAsync("http://localhost:8090", tenantDomain);

        wms.WmsTenantDomain.Should().Be(expected);
    }

    [Theory]
    [InlineData("coelba.local\r\nX-Injected: 1")]
    [InlineData("coelba local")]
    [InlineData("-coelba.local")]
    [InlineData("coelba-.local")]
    [InlineData("coelba..local")]
    [InlineData("coelba.local/wms")]
    [InlineData("coelba.local:8080")]
    [InlineData("coelbá.local")]
    public async Task AnythingButAHostName_IsRefused(string tenantDomain)
    {
        var act = () => CreateAsync("http://localhost:8090", tenantDomain);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*not a host name*");
    }

    [Fact]
    public Task ALabelLongerThan63Characters_IsRefused()
    {
        var act = () => CreateAsync("http://localhost:8090", new string('a', 64) + ".local");

        return act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void AnIWmsProviderThatDoesNotSayHasNoTenantDomain()
    {
        IWmsProvider wms = new Services.Rendering.FakeWmsLayerProvider();

        wms.WmsTenantDomain.Should().BeNull();
    }
}
