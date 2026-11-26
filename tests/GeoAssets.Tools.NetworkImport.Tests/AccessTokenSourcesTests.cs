using FluentAssertions;
using Xunit;

namespace GeoAssets.Tools.NetworkImport.Tests;

public sealed class AccessTokenSourcesTests
{
    private static Dictionary<string, string?> ClientCredentials(string? instance = "https://contoso.ciamlogin.com/") => new(StringComparer.Ordinal)
    {
        [AccessTokenSources.InstanceVariable] = instance,
        [AccessTokenSources.TenantIdVariable] = "00000000-0000-0000-0000-0000000000aa",
        [AccessTokenSources.ClientIdVariable] = "00000000-0000-0000-0000-0000000000bb",
        [AccessTokenSources.ClientSecretVariable] = "not-a-real-secret",
        [AccessTokenSources.ScopeVariable] = "api://00000000-0000-0000-0000-0000000000cc/.default",
    };

    private static (IAccessTokenSource? Source, string? Error) Resolve(Dictionary<string, string?> environment) =>
        AccessTokenSources.FromEnvironment(name => environment.TryGetValue(name, out var value) ? value : null);

    [Fact]
    public void FromEnvironment_StaticToken_WinsOverClientCredentials()
    {
        var environment = ClientCredentials();
        environment[AccessTokenSources.TokenVariable] = "  a-bearer-token  ";

        var (source, error) = Resolve(environment);

        error.Should().BeNull();
        source.Should().BeOfType<StaticAccessTokenSource>();
    }

    [Theory]
    [InlineData("https://contoso.ciamlogin.com/")]
    [InlineData("https://contoso.ciamlogin.com")]
    [InlineData(" https://contoso.ciamlogin.com/ignored/path ")]
    public void FromEnvironment_CompleteClientCredentials_BuildsTheClientCredentialsSource(string instance)
    {
        var (source, error) = Resolve(ClientCredentials(instance));

        error.Should().BeNull();
        source.Should().BeOfType<ClientCredentialsAccessTokenSource>();
    }

    [Theory]
    [InlineData(AccessTokenSources.InstanceVariable)]
    [InlineData(AccessTokenSources.TenantIdVariable)]
    [InlineData(AccessTokenSources.ClientIdVariable)]
    [InlineData(AccessTokenSources.ClientSecretVariable)]
    [InlineData(AccessTokenSources.ScopeVariable)]
    public void FromEnvironment_AnyClientCredentialMissing_NamesEveryVariable(string missing)
    {
        var environment = ClientCredentials();
        environment[missing] = " ";

        var (source, error) = Resolve(environment);

        source.Should().BeNull();
        error.Should().Contain(AccessTokenSources.TokenVariable)
            .And.Contain(AccessTokenSources.InstanceVariable)
            .And.Contain(AccessTokenSources.TenantIdVariable)
            .And.Contain(AccessTokenSources.ClientIdVariable)
            .And.Contain(AccessTokenSources.ClientSecretVariable)
            .And.Contain(AccessTokenSources.ScopeVariable);
    }

    [Theory]
    [InlineData("http://contoso.ciamlogin.com/")]
    [InlineData("contoso.ciamlogin.com")]
    [InlineData("not a url")]
    public void FromEnvironment_InstanceNotAnAbsoluteHttpsUrl_IsRefused(string instance)
    {
        var (source, error) = Resolve(ClientCredentials(instance));

        source.Should().BeNull();
        error.Should().Contain(AccessTokenSources.InstanceVariable).And.Contain("https");
    }
}
