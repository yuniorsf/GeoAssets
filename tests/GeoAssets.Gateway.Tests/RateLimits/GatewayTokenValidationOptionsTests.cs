using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GeoAssets.Gateway.Tests.RateLimits;

/// <summary>XD01-398: reading <see cref="GatewayTokenValidationOptions"/> — when validation is configured, and the bounded timings.</summary>
public sealed class GatewayTokenValidationOptionsTests
{
    private const string Derived = "https://contoso.ciamlogin.com/tenant-1/v2.0/.well-known/openid-configuration";

    private static GatewayTokenValidationOptions Read(params (string Key, string? Value)[] settings) =>
        GatewayTokenValidationOptions.Read(
            new ConfigurationBuilder().AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value))).Build());

    [Theory]
    [InlineData("https://contoso.ciamlogin.com/")]
    [InlineData("https://contoso.ciamlogin.com")]
    public void Read_InstanceAndTenantId_DerivesTheMetadataAddress(string instance)
    {
        var options = Read(("AzureAdCiam:Instance", instance), ("AzureAdCiam:TenantId", "tenant-1"));

        options.IsConfigured.Should().BeTrue();
        options.MetadataAddress.Should().Be(Derived);
    }

    [Fact]
    public void Read_AMetadataAddress_WinsOverInstanceAndTenantId()
    {
        var options = Read(
            ("AzureAdCiam:MetadataAddress", "http://ciam.test/x/.well-known/openid-configuration"),
            ("AzureAdCiam:Instance", "https://contoso.ciamlogin.com/"),
            ("AzureAdCiam:TenantId", "tenant-1"));

        options.IsConfigured.Should().BeTrue();
        options.MetadataAddress.Should().Be("http://ciam.test/x/.well-known/openid-configuration");
    }

    [Fact]
    public void Read_Nothing_IsNotConfigured()
    {
        var options = Read();

        options.IsConfigured.Should().BeFalse();
    }

    [Theory]
    [InlineData("https://contoso.ciamlogin.com/", "")]
    [InlineData("", "tenant-1")]
    [InlineData("  ", "  ")]
    public void Read_OnlyHalfOfInstanceAndTenantId_IsNotConfigured(string instance, string tenantId)
    {
        var options = Read(("AzureAdCiam:Instance", instance), ("AzureAdCiam:TenantId", tenantId), ("AzureAdCiam:MetadataAddress", " "));

        options.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void Read_NoTimings_UsesTheDefaults()
    {
        var options = Read();

        options.ClockSkew.Should().Be(GatewayTokenValidationOptions.DefaultClockSkew).And.Be(TimeSpan.FromMinutes(5));
        options.MetadataFailureBackoff.Should().Be(GatewayTokenValidationOptions.DefaultMetadataFailureBackoff).And.Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Read_ConfiguredTimings_AreReadInSeconds()
    {
        var options = Read(
            ("RateLimiting:TokenValidation:ClockSkewSeconds", "60"),
            ("RateLimiting:TokenValidation:MetadataFailureBackoffSeconds", "7"));

        options.ClockSkew.Should().Be(TimeSpan.FromSeconds(60));
        options.MetadataFailureBackoff.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Theory]
    [InlineData("RateLimiting:TokenValidation:ClockSkewSeconds", "0")]
    [InlineData("RateLimiting:TokenValidation:ClockSkewSeconds", "-5")]
    [InlineData("RateLimiting:TokenValidation:ClockSkewSeconds", "abc")]
    [InlineData("RateLimiting:TokenValidation:MetadataFailureBackoffSeconds", "0")]
    [InlineData("RateLimiting:TokenValidation:MetadataFailureBackoffSeconds", "-1")]
    [InlineData("RateLimiting:TokenValidation:MetadataFailureBackoffSeconds", "1.5x")]
    public void Read_ANonPositiveOrGarbageTiming_ThrowsNamingTheKey(string key, string value)
    {
        var act = () => Read((key, value));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{key}*");
    }
}
