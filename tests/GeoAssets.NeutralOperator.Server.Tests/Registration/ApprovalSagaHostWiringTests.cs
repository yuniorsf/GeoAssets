using FluentAssertions;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.NeutralOperator.Modules.Occupancy;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using GeoAssets.Workflow.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Registration;

/// <summary>
/// The approval saga on the NeutralOperator host (XD01-383): it consumes the kernel's answers and sweeps for timeouts, but it
/// does not host the kernel's own consumer of the asset mutation batches — that stays with the kernel — and with no broker it says
/// at startup that its approvals will wait.
/// </summary>
public sealed class ApprovalSagaHostWiringTests
{
    [Fact]
    public async Task TheHost_ConsumesTheKernelsAppliedAndRejectedAnswers_ButNotTheBatchesThemselves()
    {
        await using var host = await NeutralOperatorHost.StartAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        provider.GetService<IIntegrationEventHandler<AssetBatchAppliedEvent>>().Should().BeOfType<OccupancyApplyAppliedHandler>();
        provider.GetService<IIntegrationEventHandler<AssetMutationBatchRejectedEvent>>().Should().BeOfType<OccupancyApplyRejectedHandler>();
        provider.GetService<IIntegrationEventHandler<AssetMutationBatchRequestedEvent>>().Should().BeNull("applying the batches is the kernel's consumer, not a vertical's");
        provider.GetServices<InProcessIntegrationRoute>().Select(route => route.MessageType).Should().BeEquivalentTo(
            [IntegrationEventTypes.AssetMutationBatchAppliedV1, IntegrationEventTypes.AssetMutationBatchRejectedV1]);
        provider.GetService<IOccupancyApplySagaService>().Should().NotBeNull();
        provider.GetService<IOccupancyApplySagaMonitor>().Should().NotBeNull();
    }

    [Fact]
    public async Task TheHost_RunsTheTimeoutSweeper_WithTheDocumentedDefaults()
    {
        await using var host = await NeutralOperatorHost.StartAsync();

        host.Services.GetServices<IHostedService>().OfType<OccupancyApplySagaSweeper>().Should().ContainSingle();
        var options = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<OccupancyApplySagaOptions>>().Value;
        options.ApplyTimeout.Should().Be(TimeSpan.FromMinutes(15));
        options.SweepInterval.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task TheHost_WithNoBroker_WarnsAtStartup_ThatApprovalsWaitForTheKernelsApply()
    {
        var logs = new WarningCapture();
        await using var host = await NeutralOperatorHost.StartAsync(configureServices: services => services.AddLogging(builder => builder.AddProvider(logs)));

        logs.Messages.Should().Contain(message => message.Contains("occupancy approvals wait for the kernel's asset-mutation apply", StringComparison.Ordinal));
    }

    private sealed class WarningCapture : ILoggerProvider
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages) return [.. _messages];
            }
        }

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(WarningCapture owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel < LogLevel.Warning) return;
                lock (owner._messages) owner._messages.Add(formatter(state, exception));
            }
        }
    }
}
