using System.Text.Json;
using FluentAssertions;
using GeoAssets.Tools.GatewayLoadProfile;
using Xunit;

namespace GeoAssets.Gateway.Tests.RateLimits;

/// <summary>XD01-393: the shipped rate-limit defaults are what the load-profile model derives, so neither can change without the other.</summary>
public sealed class LoadProfileModelDefaultsTests
{
    private static JsonElement RateLimiting()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "GeoAssets.Gateway.appsettings.json");
        var text = File.Exists(path) ? File.ReadAllText(path) : File.ReadAllText(FindInRepository("apps", "GeoAssets.Gateway", "appsettings.json"));
        return JsonDocument.Parse(text).RootElement.GetProperty("RateLimiting").Clone();
    }

    private static string FindInRepository(params string[] relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. relative]);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("The gateway's appsettings.json was not found above " + AppContext.BaseDirectory);
    }

    private static int Setting(string policy, string key) => RateLimiting().GetProperty(policy).GetProperty(key).GetInt32();

    [Fact]
    public void Model_TheDefaultTilesBucket_IsTheShippedAppsettingsTiles()
    {
        var model = new LoadProfileModel();

        model.RecommendedTileTokenLimit.Should().Be(Setting("Tiles", "TokenLimit"));
        model.RecommendedTileTokensPerSecond.Should().Be(Setting("Tiles", "TokensPerPeriod"));
        Setting("Tiles", "ReplenishmentPeriodSeconds").Should().Be(1, "the model's figures are per second");
    }

    [Fact]
    public void Model_TheDefaultApiBucket_IsTheShippedAppsettingsApi()
    {
        var model = new LoadProfileModel();

        model.RecommendedApiTokenLimit.Should().Be(Setting("Api", "TokenLimit"));
        model.RecommendedApiTokensPerSecond.Should().Be(Setting("Api", "TokensPerPeriod"));
        Setting("Api", "ReplenishmentPeriodSeconds").Should().Be(1);
    }

    [Fact]
    public void Model_TheDefaults_AreTheCodesFallbackDefaultsToo()
    {
        var model = new LoadProfileModel();

        GatewayRateLimiting.DefaultApi.Should().Be(new RateLimitBucketOptions(model.RecommendedApiTokenLimit, model.RecommendedApiTokensPerSecond, 1));
        GatewayRateLimiting.DefaultTiles.Should().Be(new RateLimitBucketOptions(model.RecommendedTileTokenLimit, model.RecommendedTileTokensPerSecond, 1));
    }

    [Fact]
    public void Model_TheProfileFigures_AreTheDocumentedOnes()
    {
        var model = new LoadProfileModel();

        model.TilesPerRepaint.Should().Be(40);
        model.TilesPerAction.Should().BeApproximately(60, 0.001);
        model.TileBurst.Should().Be(400);
        model.UploadBurst.Should().Be(220);
    }

    [Fact]
    public void Model_ADoubledPhotoSync_RaisesTheApiBurst()
    {
        var bigger = new LoadProfileModel(Photos: 200);

        bigger.RecommendedApiTokenLimit.Should().BeGreaterThan(new LoadProfileModel().RecommendedApiTokenLimit);
    }

    [Fact]
    public void Describe_PrintsTheRecommendedBuckets()
    {
        var text = new LoadProfileModel().Describe();

        text.Should().Contain("RateLimiting:Tiles  TokenLimit 1000").And.Contain("RateLimiting:Api    TokenLimit 500");
    }
}
