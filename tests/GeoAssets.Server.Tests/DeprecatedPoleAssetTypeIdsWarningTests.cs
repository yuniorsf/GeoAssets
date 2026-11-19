using FluentAssertions;
using GeoAssets.Server.Modules.NeutralOperator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// The deprecated <c>Workflow:Occupancy:PoleAssetTypeIds</c> setting (XD01-368): honoured, but it only exists in a
/// host's configuration on purpose, so the host warns once at startup and registers the warning only then.
/// </summary>
public sealed class DeprecatedPoleAssetTypeIdsWarningTests
{
    private static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    private static ServiceProvider Provider(IConfiguration configuration, CapturedLogs logs)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddServerOrderOccupancyStaging(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void PoleAssetTypeIds_NothingConfigured_IsEmpty() =>
        OrderOccupancyHostExtensions.PoleAssetTypeIds(Configuration()).Should().BeEmpty();

    [Fact]
    public void PoleAssetTypeIds_Configured_AreTrimmed_BlanksSkipped() =>
        OrderOccupancyHostExtensions.PoleAssetTypeIds(Configuration(
                ($"{OrderOccupancyHostExtensions.PoleAssetTypeIdsKey}:0", " wooden-pole "),
                ($"{OrderOccupancyHostExtensions.PoleAssetTypeIdsKey}:1", " "),
                ($"{OrderOccupancyHostExtensions.PoleAssetTypeIdsKey}:2", "concrete-pole")))
            .Should().Equal("wooden-pole", "concrete-pole");

    [Fact]
    public void AddServerOrderOccupancyStaging_NoIdsConfigured_RegistersNoWarning()
    {
        using var provider = Provider(Configuration(), new CapturedLogs());

        provider.GetServices<IHostedService>().OfType<DeprecatedPoleAssetTypeIdsWarning>().Should().BeEmpty();
    }

    [Fact]
    public void AddServerOrderOccupancyStaging_OnlyBlankIdsConfigured_RegistersNoWarning()
    {
        using var provider = Provider(Configuration(($"{OrderOccupancyHostExtensions.PoleAssetTypeIdsKey}:0", " ")), new CapturedLogs());

        provider.GetServices<IHostedService>().OfType<DeprecatedPoleAssetTypeIdsWarning>().Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_IdsConfigured_LogsWarning3680ListingThem()
    {
        var logs = new CapturedLogs();
        using var provider = Provider(
            Configuration(
                ($"{OrderOccupancyHostExtensions.PoleAssetTypeIdsKey}:0", "wooden-pole"),
                ($"{OrderOccupancyHostExtensions.PoleAssetTypeIdsKey}:1", "concrete-pole")),
            logs);
        var warning = provider.GetServices<IHostedService>().OfType<DeprecatedPoleAssetTypeIdsWarning>().Should().ContainSingle().Subject;

        await warning.StartAsync(CancellationToken.None);
        await warning.StopAsync(CancellationToken.None);

        var entry = logs.Entries.Should().ContainSingle(e => e.EventId.Id == 3680).Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("wooden-pole, concrete-pole").And.Contain(OrderOccupancyHostExtensions.PoleAssetTypeIdsKey);
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        private readonly List<(EventId EventId, LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(EventId EventId, LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_entries) return [.. _entries];
            }
        }

        public ILogger CreateLogger(string categoryName) => new Capture(this);

        public void Dispose()
        {
        }

        private sealed class Capture(CapturedLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner._entries) owner._entries.Add((eventId, logLevel, formatter(state, exception)));
            }
        }
    }
}
