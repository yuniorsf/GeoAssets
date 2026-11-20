using FluentAssertions;
using GeoAssets.NeutralOperator.Modules;
using GeoAssets.NeutralOperator.Modules.Occupancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>The approval saga's timeout settings refuse values that would disable or spin the sweeper (XD01-383).</summary>
public sealed class OccupancyApplySagaOptionsValidationTests
{
    [Theory]
    [InlineData("Saga:ApplyTimeout", "00:00:00")]
    [InlineData("Saga:ApplyTimeout", "-00:01:00")]
    [InlineData("Saga:SweepInterval", "00:00:00")]
    [InlineData("Saga:SweepBatchSize", "0")]
    [InlineData("Saga:MaxPagesPerSweep", "0")]
    public void Options_WithANonPositiveSetting_AreRejected(string key, string value)
    {
        using var provider = Build((key, value));

        var act = () => provider.GetRequiredService<IOptions<OccupancyApplySagaOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*ApplyTimeout and SweepInterval must be positive*");
    }

    [Fact]
    public void Options_WithDefaults_AreAccepted()
    {
        using var provider = Build();

        var options = provider.GetRequiredService<IOptions<OccupancyApplySagaOptions>>().Value;

        options.ApplyTimeout.Should().Be(TimeSpan.FromMinutes(15));
        options.SweepInterval.Should().Be(TimeSpan.FromMinutes(1));
    }

    private static ServiceProvider Build(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddServerOccupancyCommit(configuration);
        return services.BuildServiceProvider();
    }
}
