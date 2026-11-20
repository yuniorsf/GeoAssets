using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Services;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Server;

/// <summary>
/// The kernel's consumer of <see cref="IntegrationEventTypes.AssetMutationBatchRequestedV1"/> (XD01-383, ADR-001 Phase 7):
/// applies each batch to this host's catalog, once, and answers it with
/// <see cref="IntegrationEventTypes.AssetMutationBatchAppliedV1"/> or <see cref="IntegrationEventTypes.AssetMutationBatchRejectedV1"/>
/// on the kernel's outbox. Domain-neutral: it serves every vertical's batches alike, and routes the reply back by what the
/// batch says (<see cref="AssetMutationBatchRequestedEvent.SourceVerticalId"/>, the source order, the correlation id; the
/// trace continues through the outbox envelope).
///
/// <para><b>Transaction story.</b> The catalog (<c>GeoAssetsDbContext</c>) and the kernel's inbox/outbox
/// (<c>ServiceOrderDbContext</c>) are two transactions, so the protocol is made safe instead of atomic: (1) a message the
/// inbox already holds under <see cref="ConsumerName"/> is skipped; (2) the batch is applied through the catalog's applier,
/// which records it in the applied-batch store in the catalog's own transaction, so applying it again — after a crash, a
/// redelivery, an operator's retry — is a no-op that returns the original result; (3) the inbox record and the reply are
/// written in one unit of work. A crash between (2) and (3) leaves the message unrecorded: its redelivery finds the batch
/// applied and stages the reply then. The catalog changes exactly once; the reply is staged at least once, and the
/// vertical deduplicates it by batch.</para>
///
/// <para><b>Rejection vs retry.</b> What retrying cannot fix by itself — a feature not in the organization's catalog, an
/// invalid or unsupported mutation, a malformed batch (<see cref="ObservableAssetMutationBatchApplier.Outcome"/> =
/// <c>rejected</c>) — is answered with a rejection, and the message is consumed. Anything else (the database, a
/// cancellation, a batch addressed to another catalog instance) propagates: nothing is recorded or staged, and the
/// transport redelivers the batch.</para>
/// </summary>
public sealed partial class AssetMutationBatchApplyHandler : IIntegrationEventHandler<AssetMutationBatchRequestedEvent>
{
    /// <summary>The inbox consumer name the kernel records applied batches' messages under.</summary>
    public const string ConsumerName = "kernel.asset-mutation-apply";

    /// <summary>The configuration key of this consumer's broker overrides (<c>WorkflowKafkaConsumer:Consumers:AssetMutationApply</c>, …).</summary>
    public const string ConsumerKey = "AssetMutationApply";

    /// <summary>How long a processed message stays in the inbox.</summary>
    public static readonly TimeSpan InboxRetention = TimeSpan.FromDays(7);

    private readonly KernelAssetMutationApplier _applier;
    private readonly IIdempotentInbox _inbox;
    private readonly IOutboxWriter _outbox;
    private readonly IWorkflowUnitOfWork _unitOfWork;
    private readonly ITenantOrganizationScope _organizationScope;
    private readonly GeoAssetsActivitySource _tracer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AssetMutationBatchApplyHandler> _logger;

    public AssetMutationBatchApplyHandler(
        KernelAssetMutationApplier applier,
        IIdempotentInbox inbox,
        IOutboxWriter outbox,
        IWorkflowUnitOfWork unitOfWork,
        ITenantOrganizationScope organizationScope,
        GeoAssetsActivitySource tracer,
        TimeProvider timeProvider,
        ILogger<AssetMutationBatchApplyHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(organizationScope);
        ArgumentNullException.ThrowIfNull(tracer);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _applier = applier;
        _inbox = inbox;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _organizationScope = organizationScope;
        _tracer = tracer;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task HandleAsync(AssetMutationBatchRequestedEvent message, IntegrationMessageContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);

        using var span = _tracer.StartSagaActivity("AssetMutationApply", message.BatchId, message.SourceOrderId, message.OrganizationId.ToString("D"));
        span?.SetTag("catalog.instance.id", message.CatalogInstanceId);

        // Misaddressed: another catalog instance's batch is not this host's to answer, neither applied nor rejected.
        if (!string.Equals(message.CatalogInstanceId, _applier.CatalogInstanceId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Asset mutation batch '{message.BatchId}' is addressed to catalog instance '{message.CatalogInstanceId}'; this host is '{_applier.CatalogInstanceId}'.");
        }

        if (await _inbox.HasProcessedAsync(context.MessageId, ConsumerName, ct).ConfigureAwait(false))
        {
            span?.SetTag("messaging.idempotent.duplicate", true);
            return;
        }

        if (message.OrganizationId != Guid.Empty)
            _organizationScope.Enter(message.OrganizationId);

        var (reply, messageType) = await ApplyAsync(message, ct).ConfigureAwait(false);
        span?.SetTag("saga.outcome", reply is AssetBatchAppliedEvent ? "applied" : "rejected");

        await _unitOfWork.ExecuteAsync(
            async token =>
            {
                if (!await _inbox.TryRecordProcessedAsync(context.MessageId, ConsumerName, context.MessageType, InboxRetention, token).ConfigureAwait(false))
                    return false;

                await _outbox.EnqueueAsync(
                    reply,
                    messageType,
                    partitionKey: message.SourceOrderId ?? message.BatchId.ToString("D"),
                    catalogInstanceId: message.CatalogInstanceId,
                    correlationId: message.CorrelationId,
                    causationId: context.MessageId.ToString("D"),
                    ct: token).ConfigureAwait(false);
                return true;
            },
            ct).ConfigureAwait(false);
    }

    /// <summary>The batch applied — or, for a business failure, its rejection — and the message type to answer with.</summary>
    private async Task<(object Reply, string MessageType)> ApplyAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct)
    {
        try
        {
            var applied = await _applier.Applier.ApplyAsync(batch, ct).ConfigureAwait(false);

            // The applied-batch store keeps the original result, which predates the routing fields: route it as the batch says.
            return (applied with
            {
                CorrelationId = applied.CorrelationId ?? batch.CorrelationId,
                SourceVerticalId = applied.SourceVerticalId ?? batch.SourceVerticalId,
            }, IntegrationEventTypes.AssetMutationBatchAppliedV1);
        }
        catch (Exception ex) when (Rejection(batch, ex) is { } rejected)
        {
            LogRejected(_logger, batch.BatchId, batch.SourceVerticalId, rejected.ReasonCode, ex);
            return (rejected, IntegrationEventTypes.AssetMutationBatchRejectedV1);
        }
    }

    /// <summary>The rejection for <paramref name="exception"/>, or null when it is not a business failure (then the batch is retried).</summary>
    private AssetMutationBatchRejectedEvent? Rejection(AssetMutationBatchRequestedEvent batch, Exception exception)
    {
        if (!string.Equals(ObservableAssetMutationBatchApplier.Outcome(exception), ObservableAssetMutationBatchApplier.OutcomeRejected, StringComparison.Ordinal))
            return null;

        var (code, conflicting) = exception switch
        {
            AssetMutationFeatureNotFoundException missing => (AssetMutationBatchRejectionCodes.FeatureNotFound, (IReadOnlyList<string>)[missing.FeatureId]),
            KeyNotFoundException => (AssetMutationBatchRejectionCodes.FeatureNotFound, []),
            GeoFeatureAttributeValidationException or GeoFeatureGeometryMismatchException => (AssetMutationBatchRejectionCodes.ValidationFailed, []),
            NotSupportedException => (AssetMutationBatchRejectionCodes.Unsupported, []),
            _ => (AssetMutationBatchRejectionCodes.InvalidBatch, []),
        };

        var reason = string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;
        return AssetMutationBatchRejectedEvent.For(batch, code, reason, conflicting, _timeProvider);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Asset mutation batch {BatchId} from vertical {SourceVerticalId} rejected ({ReasonCode}); nothing was applied.")]
    private static partial void LogRejected(ILogger logger, Guid batchId, string? sourceVerticalId, string reasonCode, Exception exception);
}
