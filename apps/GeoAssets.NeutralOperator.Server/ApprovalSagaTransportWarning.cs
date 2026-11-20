using GeoAssets.Workflow.Hosting;

namespace GeoAssets.NeutralOperator.Server;

/// <summary>
/// Says at startup that this host's approval saga cannot complete (XD01-383): with no broker, the outbox relay runs in
/// process, and in process this host reaches only itself — never the remote kernel that applies the approvals' asset
/// mutation batches. Those batches then stay pending in the outbox (none is lost; they are published once a broker is
/// configured), the approvals stay "awaiting apply", and the saga timeout flags them. The saga needs a broker on this host;
/// there is no REST fallback, because only the kernel's consumer tells a rejection from a retryable failure.
/// </summary>
internal sealed partial class ApprovalSagaTransportWarning(ILogger<ApprovalSagaTransportWarning> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        LogNoBroker(logger, WorkflowMessagingHostExtensions.OutboxTransportKey);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "No broker configured ({TransportKey} = None): occupancy approvals wait for the kernel's asset-mutation apply, which this host can reach only through a broker. Their batches stay pending in the outbox until one is configured, and the saga timeout flags them.")]
    private static partial void LogNoBroker(ILogger logger, string transportKey);
}
