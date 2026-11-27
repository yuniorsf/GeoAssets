using FluentAssertions;
using GeoAssets.Core.Plugins;
using GeoAssets.Core.Rendering;
using GeoAssets.Server.Tiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GeoAssets.Server.Tests.Tiles;

/// <summary>
/// XD01-459 — <see cref="KeyedTileSourceReport"/>: once started, it logs what each keyed tile source serves, a source that
/// can't describe itself as a warning (the host keeps running), and every native library claimed with its owner.
/// </summary>
public sealed class KeyedTileSourceReportTests
{
    [Fact]
    public async Task ItLogsEachSource_AWarningForABrokenOne_AndTheClaimedNativeLibraries()
    {
        var library = "report-test-" + Guid.NewGuid().ToString("N");
        NativeLibraryOwnership.Claim(library, typeof(KeyedTileSourceReportTests).Assembly);
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IVectorTileSource>("basemap", new DescribedTestSource("basemap", new TileSourceDescription("b45e", ["roads", "water"], 0, 14)));
        services.AddKeyedSingleton<IRasterTileSource>("ortho", new DescribedTestSource("ortho", new TileSourceDescription("0e7a", ["ortho"], 0, 19)) { Broken = true });
        services.AddSingleton(KeyedTileSources.From(services));
        using var provider = services.BuildServiceProvider();
        var logs = new CapturedLogs();
        using var report = new KeyedTileSourceReport(provider, new Logger<KeyedTileSourceReport>(new LoggerFactory([logs])));

        await report.StartAsync(CancellationToken.None);
        await report.ExecuteTask!;

        logs.Entries.Should().Contain(e => e.Level == LogLevel.Information
            && e.Message.Contains("'basemap'", StringComparison.Ordinal) && e.Message.Contains("b45e", StringComparison.Ordinal)
            && e.Message.Contains("roads,water", StringComparison.Ordinal) && e.Message.Contains("0-14", StringComparison.Ordinal));
        logs.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("'ortho'", StringComparison.Ordinal));
        logs.Entries.Should().Contain(e => e.Level == LogLevel.Information
            && e.Message.Contains(library, StringComparison.Ordinal) && e.Message.Contains("GeoAssets.Server.Tests", StringComparison.Ordinal));
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
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
                lock (owner._entries) owner._entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
