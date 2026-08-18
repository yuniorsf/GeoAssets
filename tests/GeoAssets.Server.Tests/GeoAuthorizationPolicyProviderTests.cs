using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.Server.Tests;

public class GeoAuthorizationPolicyProviderTests
{
    private static GeoAuthorizationPolicyProvider Sut(Action<AuthorizationOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddAuthorization(configure ?? (_ => { }));
        var provider = services.BuildServiceProvider();
        return new GeoAuthorizationPolicyProvider(provider.GetRequiredService<IOptions<AuthorizationOptions>>());
    }

    [Fact]
    public async Task GetPolicyAsync_AnyName_ReturnsPolicyWithMatchingGeoPolicyRequirement()
    {
        var sut = Sut();

        var policy = await sut.GetPolicyAsync("CanEditFeatures");

        policy.Should().NotBeNull();
        policy!.Requirements.Should().ContainSingle(r => r is GeoPolicyRequirement)
            .Which.As<GeoPolicyRequirement>().PolicyName.Should().Be("CanEditFeatures");
    }

    [Fact]
    public async Task GetPolicyAsync_AlsoRequiresAuthenticatedUser()
    {
        var sut = Sut();

        var policy = await sut.GetPolicyAsync("CanEditFeatures");

        policy!.Requirements.Should().ContainItemsAssignableTo<DenyAnonymousAuthorizationRequirement>();
    }

    [Fact]
    public async Task GetFallbackPolicyAsync_DelegatesToConfiguredFallbackPolicy()
    {
        var sut = Sut(options => options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build());

        var fallback = await sut.GetFallbackPolicyAsync();

        fallback.Should().NotBeNull();
        fallback!.Requirements.Should().ContainItemsAssignableTo<DenyAnonymousAuthorizationRequirement>();
    }

    [Fact]
    public async Task GetDefaultPolicyAsync_DoesNotThrow()
    {
        var sut = Sut();

        var act = () => sut.GetDefaultPolicyAsync();

        await act.Should().NotThrowAsync();
    }
}
