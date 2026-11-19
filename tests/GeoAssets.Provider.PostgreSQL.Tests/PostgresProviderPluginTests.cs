using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>The PostgreSQL provider as an <see cref="IProviderPlugin"/> (XD01-371): settings, connection string lookup, warm-up.</summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresProviderPluginTests(PostgresContainerFixture fixture) : IDisposable
{
    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
    }

    private sealed class CapturingLogger(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            messages.Add(formatter(state, exception));
    }

    private readonly CapturingLoggerFactory _loggers = new();

    public void Dispose() => _loggers.Dispose();

    private PostgresProviderPlugin Plugin() => new(new PostgresProviderFactory(_loggers, TimeProvider.System));

    private async Task SeedTwoRowsAsync()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE geo_entity CASCADE");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId")
            VALUES ('a', 'Alpha', 'type-a'), ('b', 'Bravo', 'type-a');
            """);
    }

    private static ServiceProvider Services(params (string Name, string Value)[] connectionStrings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(connectionStrings.Select(c => new KeyValuePair<string, string?>($"ConnectionStrings:{c.Name}", c.Value)))
            .Build();
        return new ServiceCollection().AddSingleton<IConfiguration>(configuration).BuildServiceProvider();
    }

    private static ProviderConfig Settings(params (string Key, string Value)[] values) =>
        new(values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal));

    [Fact]
    public void Id_IsPostgres()
    {
        Plugin().Id.Should().Be("postgres").And.Be(PostgresProviderPlugin.PluginId);
    }

    [Fact]
    public async Task CreateAsync_WithConnectionStringName_ReturnsAMigratedWarmedProvider()
    {
        await SeedTwoRowsAsync();
        await using var services = Services(("Custom", fixture.ConnectionString));

        await using var provider = (PostgresAssetProvider)await Plugin()
            .CreateAsync(Settings(("connectionStringName", "Custom")), services);

        provider.GetAll().Should().HaveCount(2);
        _loggers.Messages.Should().Contain(m => m.Contains("WarmCacheAsync", StringComparison.Ordinal));
        _loggers.Messages.Should().NotContain(m => m.Contains("LoadCacheFromDb", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_WithInlineConnectionString_UsesIt()
    {
        await SeedTwoRowsAsync();
        await using var services = Services();

        await using var provider = (PostgresAssetProvider)await Plugin()
            .CreateAsync(Settings(("ConnectionString", fixture.ConnectionString), ("connectionStringName", "Missing")), services);

        provider.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateAsync_DefaultsToTheGeoAssetsConnectionString()
    {
        await SeedTwoRowsAsync();
        await using var services = Services(("GeoAssets", fixture.ConnectionString));

        await using var provider = (PostgresAssetProvider)await Plugin().CreateAsync(Settings(), services);

        provider.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateAsync_MissingConnectionString_Throws()
    {
        await using var services = Services();

        var act = () => Plugin().CreateAsync(Settings(("connectionStringName", "Nowhere")), services);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Nowhere*");
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("1")]
    [InlineData("yes please")]
    public async Task CreateAsync_InvalidWarmUp_Throws(string warmUp)
    {
        await using var services = Services(("GeoAssets", fixture.ConnectionString));

        var act = () => Plugin().CreateAsync(Settings(("warmUp", warmUp)), services);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*warmUp*");
    }

    [Fact]
    public async Task CreateAsync_WarmUpFalse_ReturnsAProviderThatStillReads()
    {
        await SeedTwoRowsAsync();
        await using var services = Services(("GeoAssets", fixture.ConnectionString));

        await using var provider = (PostgresAssetProvider)await Plugin().CreateAsync(Settings(("warmUp", "false")), services);
        var all = provider.GetAll();

        all.Should().HaveCount(2);
        _loggers.Messages.Should().NotContain(m => m.Contains("WarmCacheAsync", StringComparison.Ordinal));
        _loggers.Messages.Should().Contain(m => m.Contains("LoadCacheFromDb", StringComparison.Ordinal));
    }

    [Fact]
    public void AddGeoAssetsPostgres_Twice_RegistersThePluginOnce()
    {
        var services = new ServiceCollection();

        services.AddGeoAssetsPostgres();
        services.AddGeoAssetsPostgres();

        services.Where(d => d.ServiceType == typeof(IProviderPlugin)).Should().ContainSingle()
            .Which.ImplementationType.Should().Be<PostgresProviderPlugin>();
    }
}
