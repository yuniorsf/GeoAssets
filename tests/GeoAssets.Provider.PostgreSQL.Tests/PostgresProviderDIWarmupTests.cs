using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// Exercises <see cref="PostgresServiceExtensions.AddWarmedPostgresAssetProviderAsync"/> — the
/// exact method <c>GeoAssets.Server/Program.cs</c> calls to pre-warm its <c>IAssetProvider</c>
/// singleton before <c>app.Run()</c> (XD01-173), so <c>PostgresAssetProvider</c>'s
/// <see cref="ISyncProvider"/> marker (XD01-161) is actually honest for that deployment. Calls
/// the real registration method against a plain <see cref="ServiceCollection"/>, not a
/// reimplementation of its logic — would fail if the method silently fell back to the
/// synchronous, unwarmed <c>PostgresProviderFactory.Create</c>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresProviderDIWarmupTests(PostgresContainerFixture fixture)
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

    [Fact]
    public async Task AddWarmedPostgresAssetProviderAsync_RegistersProvider_AlreadyWarmedBeforeFirstGetAll()
    {
        await ClearAsync();
        var loggerFactory = new CapturingLoggerFactory();

        // Mirrors Program.cs's own registration order exactly.
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        services.AddSingleton(TimeProvider.System);
        services.AddGeoAssetsPostgres();
        await services.AddWarmedPostgresAssetProviderAsync(fixture.ConnectionString);

        await using var provider = services.BuildServiceProvider();
        var assetProvider = provider.GetRequiredService<IAssetProvider>();
        var result = assetProvider.GetAll();

        result.Should().BeEmpty(); // table was truncated above — just proving no exception/blocking path
        loggerFactory.Logger.Entries.Should().Contain(e => e.Message.Contains("WarmCacheAsync"));
        loggerFactory.Logger.Entries.Should().NotContain(e => e.Message.Contains("LoadCacheFromDb"));
    }

    [Fact]
    public async Task AddWarmedPostgresAssetProviderAsync_RegistersProvider_WrappedInValidatingAssetProvider()
    {
        await ClearAsync();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(new CapturingLoggerFactory());
        services.AddSingleton(TimeProvider.System);
        services.AddGeoAssetsPostgres();
        await services.AddWarmedPostgresAssetProviderAsync(fixture.ConnectionString);

        await using var provider = services.BuildServiceProvider();
        var assetProvider = provider.GetRequiredService<IAssetProvider>();

        // Same validation wrapper Program.cs applied directly before this ticket.
        assetProvider.Should().BeOfType<GeoAssets.Core.Providers.ValidatingAssetProvider>();
    }
}
