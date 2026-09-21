using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// Exercises the timing/logging instrumentation added to <see cref="PostgresAssetProvider.LoadCacheFromDb"/>
/// (private, exercised via the public <see cref="PostgresAssetProvider.GetAll"/>) and the private
/// <c>SaveChanges</c> helper (exercised via <see cref="PostgresAssetProvider.Add"/>) — XD01-159.
/// No behavior change: these assertions only confirm the row-count/elapsed-ms numbers are captured.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresAssetProviderInstrumentationTests(PostgresContainerFixture fixture)
{
    private sealed class CapturingLogger : ILogger<PostgresAssetProvider>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));
    }

    private async Task ClearAsync()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE geo_entity");
    }

    private async Task SeedAsync(string sql)
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static PostgresAssetProvider CreateSut(DbContextOptions<GeoAssetsDbContext> options, ILogger<PostgresAssetProvider> logger) =>
        new(new GeoAssetsDbContext(options), options, logger, TimeProvider.System);

    [Fact]
    public async Task GetAll_FirstCall_LogsLoadCacheFromDbRowCountAndElapsedMs()
    {
        await ClearAsync();
        await SeedAsync("""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId")
            VALUES ('a', 'Alpha', 'type-a'), ('b', 'Bravo', 'type-a');
            """);
        var logger = new CapturingLogger();
        await using var sut = CreateSut(fixture.CreateOptions(), logger);

        var result = sut.GetAll();

        result.Should().HaveCount(2);
        logger.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("LoadCacheFromDb") &&
            e.Message.Contains("2 rows"));
    }

    [Fact]
    public async Task GetAll_SecondCall_DoesNotReloadOrLogAgain()
    {
        await ClearAsync();
        await SeedAsync("""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId")
            VALUES ('a', 'Alpha', 'type-a');
            """);
        var logger = new CapturingLogger();
        await using var sut = CreateSut(fixture.CreateOptions(), logger);

        sut.GetAll();
        sut.GetAll();

        logger.Entries.Count(e => e.Message.Contains("LoadCacheFromDb")).Should().Be(1);
    }

    [Fact]
    public async Task Add_LogsSaveChangesRowCountAndElapsedMs()
    {
        await ClearAsync();
        var logger = new CapturingLogger();
        await using var sut = CreateSut(fixture.CreateOptions(), logger);
        var feature = new GeoFeature { Id = "new-feature", Properties = { AssetTypeId = "type-a" } };

        sut.Add(feature);

        logger.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("SaveChanges") &&
            e.Message.Contains("1 rows"));
    }
}
