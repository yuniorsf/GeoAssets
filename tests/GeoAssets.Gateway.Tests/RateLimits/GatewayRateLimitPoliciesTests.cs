using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GeoAssets.Gateway.Tests.RateLimits;

/// <summary>XD01-393: which rate-limit policy a path falls under.</summary>
public sealed class GatewayRateLimitPoliciesTests
{
    [Theory]
    [InlineData("/wms")]
    [InlineData("/WMS")]
    [InlineData("/Wfs")]
    [InlineData("/wms/")]
    [InlineData("/wms/x")]
    [InlineData("/wfs/layer/1")]
    [InlineData("/tiles/style.json")]
    [InlineData("/tiles/assets/0123abcd-0-0.0/8/94/146.mvt")]
    [InlineData("/tiles/sources/basemap/b45e00010a1b2c3d/8/94/146.mvt")]
    [InlineData("/tiles/sources/ortho/0e7a00010a1b2c3d/12/1515/2335.png")]
    [InlineData("/TILES/assets/v/12/1/2.png")]
    public void Classify_AWmsWfsOrTilesPath_IsTiles(string path)
    {
        GatewayRateLimitPolicies.Classify(new PathString(path)).Should().Be(GatewayRateLimitPolicy.Tiles);
    }

    [Theory]
    [InlineData("/wmsx")]
    [InlineData("/wfsx")]
    [InlineData("/api/wms")]
    [InlineData("/api/geoassets/wfs")]
    [InlineData("/tilesx/style.json")]
    [InlineData("/api/tiles/style.json")]
    [InlineData("/")]
    [InlineData("")]
    [InlineData("/api/v/neutral-operator/workflow/service-orders")]
    [InlineData("/healthz")]
    public void Classify_AnyOtherPath_IsApi(string path)
    {
        GatewayRateLimitPolicies.Classify(new PathString(path)).Should().Be(GatewayRateLimitPolicy.Api);
    }

    [Fact]
    public void Classify_TheQueryStringIsNotPartOfThePath_SoAWfsRequestIsTiles()
    {
        var request = new DefaultHttpContext().Request;
        request.Path = "/wfs";
        request.QueryString = new QueryString("?service=WFS&x=/api");

        GatewayRateLimitPolicies.Classify(request.Path).Should().Be(GatewayRateLimitPolicy.Tiles);
    }

    [Fact]
    public void ToName_NamesEachPolicyInLowerCase()
    {
        GatewayRateLimitPolicy.Api.ToName().Should().Be("api");
        GatewayRateLimitPolicy.Tiles.ToName().Should().Be("tiles");
    }

    [Fact]
    public void ToName_AnUnknownPolicy_Throws()
    {
        var act = () => ((GatewayRateLimitPolicy)99).ToName();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
