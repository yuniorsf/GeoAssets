using FluentAssertions;
using GeoAssets.Identity.Authentication;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GeoAssets.Identity.Tests.Authentication;

/// <summary>XD01-397: which API scope a request through the gateway is authorized for, by its path.</summary>
public sealed class ApiScopeRoutesTests
{
    private static readonly Uri Gateway = new("https://gw/");
    private const string Kernel = "api://kernel/access_as_user";
    private const string NeutralOperator = "api://no/access_as_user";

    private static ApiScopeRoutes Build(string? defaultScope = Kernel, params ApiScopeRoute[] routes) => new(Gateway, defaultScope, routes);

    private static ApiScopeRoutes WithNeutralOperator() => Build(Kernel, new ApiScopeRoute("/api/v/neutral-operator/", NeutralOperator));

    [Fact]
    public void ARelativeBaseUrl_IsRefused()
    {
        var act = () => new ApiScopeRoutes(new Uri("/api/", UriKind.Relative), Kernel);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ANullBaseUrl_IsRefused()
    {
        var act = () => new ApiScopeRoutes(null!, Kernel);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ABaseUrlWithoutATrailingSlash_GetsOne()
    {
        var routes = new ApiScopeRoutes(new Uri("https://gw/base"), Kernel);

        routes.BaseUrl.Should().Be(new Uri("https://gw/base/"));
    }

    [Fact]
    public void ABaseUrlWithATrailingSlash_IsKept()
    {
        new ApiScopeRoutes(new Uri("https://gw/base/"), Kernel).BaseUrl.Should().Be(new Uri("https://gw/base/"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankDefaultScope_IsNull(string? scope)
    {
        Build(scope).DefaultScope.Should().BeNull();
    }

    [Fact]
    public void TheDefaultScope_IsTrimmed()
    {
        Build($"  {Kernel} ").DefaultScope.Should().Be(Kernel);
    }

    [Fact]
    public void NoRoutes_MeansNone()
    {
        var routes = new ApiScopeRoutes(Gateway, Kernel);

        routes.Routes.Should().BeEmpty();
        routes.HasRouteScopes.Should().BeFalse();
    }

    [Fact]
    public void EntriesWithABlankPrefixOrScope_AreDropped()
    {
        var routes = Build(
            Kernel,
            new ApiScopeRoute("", NeutralOperator),
            new ApiScopeRoute("  ", NeutralOperator),
            new ApiScopeRoute("/api/v/a/", ""),
            new ApiScopeRoute("/api/v/b/", "   "),
            new ApiScopeRoute(null!, NeutralOperator),
            new ApiScopeRoute("/api/v/c/", null!));

        routes.Routes.Should().BeEmpty();
        routes.HasRouteScopes.Should().BeFalse();
    }

    [Theory]
    [InlineData("api/v/x", "/api/v/x/")]
    [InlineData("/api/v/x", "/api/v/x/")]
    [InlineData("api/v/x/", "/api/v/x/")]
    [InlineData("/api/v/x/", "/api/v/x/")]
    [InlineData("  /api/v/x/  ", "/api/v/x/")]
    public void APrefix_IsNormalisedToStartAndEndWithASlash(string prefix, string expected)
    {
        var routes = Build(Kernel, new ApiScopeRoute(prefix, $"  {NeutralOperator} "));

        routes.Routes.Should().ContainSingle().Which.Should().Be(new ApiScopeRoute(expected, NeutralOperator));
        routes.HasRouteScopes.Should().BeTrue();
    }

    [Fact]
    public void TheKeptRoutes_KeepTheirInputOrder()
    {
        var routes = Build(Kernel, new ApiScopeRoute("/b/", "s-b"), new ApiScopeRoute("/a/", "s-a"));

        routes.Routes.Select(r => r.PathPrefix).Should().Equal("/b/", "/a/");
    }

    [Fact]
    public void TwoEntriesWithTheSameNormalisedPrefix_AreRefused()
    {
        var act = () => Build(Kernel, new ApiScopeRoute("/api/v/x/", "s1"), new ApiScopeRoute("API/V/X", "s2"));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ADroppedEntry_DoesNotCountAsADuplicate()
    {
        var routes = Build(Kernel, new ApiScopeRoute("/api/v/x/", ""), new ApiScopeRoute("/api/v/x/", "s2"));

        routes.Routes.Should().ContainSingle().Which.Scope.Should().Be("s2");
    }

    [Fact]
    public void ARequestUnderARoute_GetsTheRoutesScope()
    {
        WithNeutralOperator().ScopeFor(new Uri("https://gw/api/v/neutral-operator/workflow/service-orders")).Should().Be(NeutralOperator);
    }

    [Fact]
    public void AKernelRequest_GetsTheDefaultScope()
    {
        WithNeutralOperator().ScopeFor(new Uri("https://gw/api/workflow/service-orders")).Should().Be(Kernel);
    }

    [Fact]
    public void TheExactPrefixWithoutItsTrailingSlash_Matches()
    {
        WithNeutralOperator().ScopeFor(new Uri("https://gw/api/v/neutral-operator")).Should().Be(NeutralOperator);
    }

    [Fact]
    public void ASiblingPathSharingOnlyTheTextPrefix_DoesNotMatch()
    {
        WithNeutralOperator().ScopeFor(new Uri("https://gw/api/v/neutral-operator-x/workflow")).Should().Be(Kernel);
    }

    [Fact]
    public void TheMatch_IgnoresCase()
    {
        WithNeutralOperator().ScopeFor(new Uri("https://GW/API/V/Neutral-Operator/x")).Should().Be(NeutralOperator);
    }

    [Fact]
    public void TheLongestMatchingPrefix_Wins()
    {
        var routes = Build(
            Kernel,
            new ApiScopeRoute("/api/v/", "s-short"),
            new ApiScopeRoute("/api/v/neutral-operator/", "s-long"),
            new ApiScopeRoute("/api/v/other/", "s-other"));

        routes.ScopeFor(new Uri("https://gw/api/v/neutral-operator/x")).Should().Be("s-long");
        routes.ScopeFor(new Uri("https://gw/api/v/neutral/x")).Should().Be("s-short");
    }

    [Fact]
    public void TheLongestPrefixWins_WhateverTheListOrder()
    {
        var routes = Build(
            Kernel,
            new ApiScopeRoute("/api/v/neutral-operator/", "s-long"),
            new ApiScopeRoute("/api/v/", "s-short"));

        routes.ScopeFor(new Uri("https://gw/api/v/neutral-operator/x")).Should().Be("s-long");
    }

    [Theory]
    [InlineData("https://other/api/v/neutral-operator/x")]
    [InlineData("https://gw:8443/api/v/neutral-operator/x")]
    [InlineData("http://gw/api/v/neutral-operator/x")]
    public void ARequestToAnotherSchemeHostOrPort_GetsTheDefaultScope(string uri)
    {
        WithNeutralOperator().ScopeFor(new Uri(uri)).Should().Be(Kernel);
    }

    [Fact]
    public void AGatewayUnderAPathBase_ResolvesTheRoutesRelativeToIt()
    {
        var routes = new ApiScopeRoutes(new Uri("https://gw/base/"), Kernel, [new ApiScopeRoute("/api/v/x/", NeutralOperator)]);

        routes.ScopeFor(new Uri("https://gw/base/api/v/x/y")).Should().Be(NeutralOperator);
        routes.ScopeFor(new Uri("https://gw/base/api/v/x")).Should().Be(NeutralOperator);
        routes.ScopeFor(new Uri("https://gw/api/v/x/y")).Should().Be(Kernel);
    }

    [Fact]
    public void ARelativeRequestUri_IsRefused()
    {
        var act = () => WithNeutralOperator().ScopeFor(new Uri("/api/v/neutral-operator/x", UriKind.Relative));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ANullRequestUri_IsRefused()
    {
        var act = () => WithNeutralOperator().ScopeFor(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithoutADefaultScope_AnUnmatchedRequest_GetsNull()
    {
        Build(null, new ApiScopeRoute("/api/v/x/", "s")).ScopeFor(new Uri("https://gw/api/other")).Should().BeNull();
    }

    [Fact]
    public void AllScopes_ListTheDefaultFirst_ThenEachDistinctRouteScope()
    {
        var routes = Build(
            Kernel,
            new ApiScopeRoute("/a/", "s-a"),
            new ApiScopeRoute("/b/", Kernel),
            new ApiScopeRoute("/c/", "s-a"),
            new ApiScopeRoute("/d/", "s-d"));

        routes.AllScopes.Should().Equal(Kernel, "s-a", "s-d");
        routes.AdditionalScopes.Should().Equal("s-a", "s-d");
    }

    [Fact]
    public void WithoutADefaultScope_AllScopes_AreTheRouteScopes()
    {
        var routes = Build(null, new ApiScopeRoute("/a/", "s-a"));

        routes.AllScopes.Should().Equal("s-a");
        routes.AdditionalScopes.Should().Equal("s-a");
    }

    [Fact]
    public void WithNothingConfigured_NoScopeIsListed()
    {
        var routes = Build(null);

        routes.AllScopes.Should().BeEmpty();
        routes.AdditionalScopes.Should().BeEmpty();
    }

    [Fact]
    public void WithOnlyADefault_AllScopesIsTheDefault_AndNothingIsAdditional()
    {
        var routes = Build();

        routes.AllScopes.Should().Equal(Kernel);
        routes.AdditionalScopes.Should().BeEmpty();
    }

    private static IConfiguration Section(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("GeoAssetsServer");

    [Fact]
    public void FromConfiguration_ReadsTheApiScope_AndTheRouteScopes()
    {
        var section = Section(new(StringComparer.Ordinal)
        {
            ["GeoAssetsServer:ApiScope"] = Kernel,
            ["GeoAssetsServer:RouteScopes:0:PathPrefix"] = "/api/v/neutral-operator/",
            ["GeoAssetsServer:RouteScopes:0:Scope"] = NeutralOperator,
            ["GeoAssetsServer:RouteScopes:1:PathPrefix"] = "/api/v/blank/",
            ["GeoAssetsServer:RouteScopes:1:Scope"] = "",
        });

        var routes = ApiScopeRoutes.FromConfiguration(section, Gateway);

        routes.DefaultScope.Should().Be(Kernel);
        routes.Routes.Should().ContainSingle().Which.Should().Be(new ApiScopeRoute("/api/v/neutral-operator/", NeutralOperator));
        routes.ScopeFor(new Uri("https://gw/api/v/neutral-operator/x")).Should().Be(NeutralOperator);
        routes.BaseUrl.Should().Be(Gateway);
    }

    [Fact]
    public void FromConfiguration_WithoutARouteScopesSection_IsDefaultOnly()
    {
        var routes = ApiScopeRoutes.FromConfiguration(Section(new(StringComparer.Ordinal) { ["GeoAssetsServer:ApiScope"] = Kernel }), Gateway);

        routes.DefaultScope.Should().Be(Kernel);
        routes.HasRouteScopes.Should().BeFalse();
    }

    [Fact]
    public void FromConfiguration_WithAnEmptySection_HasNoScopeAtAll()
    {
        var routes = ApiScopeRoutes.FromConfiguration(Section(new(StringComparer.Ordinal)), Gateway);

        routes.DefaultScope.Should().BeNull();
        routes.Routes.Should().BeEmpty();
    }

    [Fact]
    public void FromConfiguration_RefusesNullArguments()
    {
        var section = Section(new(StringComparer.Ordinal));

        var nullSection = () => ApiScopeRoutes.FromConfiguration(null!, Gateway);
        var nullBase = () => ApiScopeRoutes.FromConfiguration(section, null!);

        nullSection.Should().Throw<ArgumentNullException>();
        nullBase.Should().Throw<ArgumentNullException>();
    }
}
