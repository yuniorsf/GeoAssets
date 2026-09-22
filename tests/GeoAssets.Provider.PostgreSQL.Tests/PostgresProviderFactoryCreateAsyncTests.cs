using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// Exercises <see cref="PostgresProviderFactory.CreateAsync"/>'s cache pre-warming (XD01-161):
/// the returned provider's first <see cref="PostgresAssetProvider.GetAll"/> must not fall through
/// to <see cref="PostgresAssetProvider.LoadCacheFromDb"/>'s blocking query — the whole point of
/// warming the cache asynchronously before any sync caller can reach it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresProviderFactoryCreateAsyncTests(PostgresContainerFixture fixture)
{
    private sealed class CapturingLogger : ILogger<PostgresAssetProvider>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        public CapturingLogger Logger { get; } = new();
        public ILogger CreateLogger(string categoryName) => Logger;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
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

    [Fact]
    public async Task CreateAsync_FirstGetAll_DoesNotTriggerLoadCacheFromDb()
    {
        await ClearAsync();
        await SeedAsync("""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId")
            VALUES ('a', 'Alpha', 'type-a'), ('b', 'Bravo', 'type-a');
            """);
        var loggerFactory = new CapturingLoggerFactory();
        var factory = new PostgresProviderFactory(loggerFactory, TimeProvider.System);

        await using var sut = (PostgresAssetProvider)await factory.CreateAsync(fixture.ConnectionString);
        var result = sut.GetAll();

        result.Should().HaveCount(2);
        loggerFactory.Logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("WarmCacheAsync") &&
            e.Message.Contains("2 rows"));
        loggerFactory.Logger.Entries.Should().NotContain(e => e.Message.Contains("LoadCacheFromDb"));
    }

    [Fact]
    public async Task CreateAsync_ReturnsProviderMarkedAsSyncProvider()
    {
        await ClearAsync();
        var loggerFactory = new CapturingLoggerFactory();
        var factory = new PostgresProviderFactory(loggerFactory, TimeProvider.System);

        await using var sut = (PostgresAssetProvider)await factory.CreateAsync(fixture.ConnectionString);

        sut.Should().BeAssignableTo<ISyncProvider>();
    }
}
