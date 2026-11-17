using FluentAssertions;
using Xunit;

namespace GeoAssets.Tools.SharedUseImport.Tests;

public sealed class GuardTests
{
    [Fact]
    public Task ReadMappingAsync_NullStream_Throws()
    {
        var act = () => SharedUseImportJson.ReadMappingAsync(null!, CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void PostgresOccupancyStore_Open_NullArguments_Throw()
    {
        var options = new SharedUseImportOptions("su.csv", "map.json", "out.json", SharedUseTestData.Owner, DryRun: false, "Host=unused");

        var nullOptions = () => PostgresOccupancyStore.Open(null!, TimeProvider.System);
        var nullClock = () => PostgresOccupancyStore.Open(options, null!);

        nullOptions.Should().Throw<ArgumentNullException>().WithParameterName("options");
        nullClock.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
    }

    [Fact]
    public async Task PostgresOccupancyStore_Open_DoesNotConnectUntilTheStoreIsUsed()
    {
        var options = new SharedUseImportOptions("su.csv", "map.json", "out.json", SharedUseTestData.Owner, DryRun: false, "Host=unused");

        var session = PostgresOccupancyStore.Open(options, TimeProvider.System);
        await using (session.ConfigureAwait(false))
        {
            session.Reader.Should().BeSameAs(session.Writer);
        }
    }
}
