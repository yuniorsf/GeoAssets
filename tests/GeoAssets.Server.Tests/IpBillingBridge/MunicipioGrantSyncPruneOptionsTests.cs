using FluentAssertions;
using GeoAssets.IpBilling.Server;
using Xunit;

namespace GeoAssets.Server.Tests.IpBillingBridge;

/// <summary>XD01-400: the retention and prune settings of <see cref="MunicipioGrantSyncOptions"/> — defaults and bounds.</summary>
public sealed class MunicipioGrantSyncPruneOptionsTests
{
    [Fact]
    public void Defaults_AreTheDocumentedOnes_AndValid()
    {
        var options = new MunicipioGrantSyncOptions();

        options.CompletedRetention.Should().Be(TimeSpan.FromDays(30));
        options.FailedRetention.Should().BeNull("failed requests are kept unless an operator sets a retention");
        options.PruneBatchSize.Should().Be(500);
        options.PruneInterval.Should().Be(TimeSpan.FromHours(1));
        MunicipioGrantSyncOptions.IsValid(options).Should().BeTrue();
    }

    [Theory]
    [InlineData("zero CompletedRetention")]
    [InlineData("negative CompletedRetention")]
    [InlineData("zero FailedRetention")]
    [InlineData("negative FailedRetention")]
    [InlineData("zero PruneBatchSize")]
    [InlineData("negative PruneBatchSize")]
    [InlineData("zero PruneInterval")]
    [InlineData("negative PruneInterval")]
    public void Invalid_AreRejected(string reason)
    {
        var options = reason switch
        {
            "zero CompletedRetention" => new MunicipioGrantSyncOptions { CompletedRetention = TimeSpan.Zero },
            "negative CompletedRetention" => new MunicipioGrantSyncOptions { CompletedRetention = TimeSpan.FromDays(-1) },
            "zero FailedRetention" => new MunicipioGrantSyncOptions { FailedRetention = TimeSpan.Zero },
            "negative FailedRetention" => new MunicipioGrantSyncOptions { FailedRetention = TimeSpan.FromDays(-1) },
            "zero PruneBatchSize" => new MunicipioGrantSyncOptions { PruneBatchSize = 0 },
            "negative PruneBatchSize" => new MunicipioGrantSyncOptions { PruneBatchSize = -5 },
            "zero PruneInterval" => new MunicipioGrantSyncOptions { PruneInterval = TimeSpan.Zero },
            "negative PruneInterval" => new MunicipioGrantSyncOptions { PruneInterval = TimeSpan.FromMinutes(-1) },
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
        };

        MunicipioGrantSyncOptions.IsValid(options).Should().BeFalse(reason);
    }

    [Fact]
    public void BoundaryValues_AreValid()
    {
        MunicipioGrantSyncOptions.IsValid(new MunicipioGrantSyncOptions
        {
            CompletedRetention = TimeSpan.FromTicks(1),
            FailedRetention = TimeSpan.FromTicks(1),
            PruneBatchSize = 1,
            PruneInterval = TimeSpan.FromTicks(1),
        }).Should().BeTrue();
    }

    [Fact]
    public void ValidationMessage_NamesThePruneSettings()
    {
        MunicipioGrantSyncOptions.ValidationMessage.Should()
            .Contain("CompletedRetention").And.Contain("FailedRetention").And.Contain("PruneBatchSize").And.Contain("PruneInterval");
    }
}
