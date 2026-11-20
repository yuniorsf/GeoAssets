using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.IpBilling.GrantSync;
using Microsoft.Extensions.Options;

namespace GeoAssets.IpBilling.Server;

/// <summary>
/// Sends the município grant changes saves queued (<see cref="IMunicipioGrantSyncQueue"/>, XD01-395) to the kernel through
/// <see cref="MunicipioProvisioningService"/>. Every <see cref="MunicipioGrantSyncOptions.PollInterval"/> (at once again
/// after a full batch) it claims due requests from <see cref="IMunicipioGrantSyncStore"/> — oldest first, never two of one
/// município at a time — and sends each:
/// <list type="bullet">
///   <item>sent → completed, counted <c>synced</c>;</item>
///   <item>failed → its attempt counted, the bounded error kept (no token or secret) and the next attempt scheduled after
///   <see cref="Backoff"/>, counted <c>retry</c> and logged as a warning; after
///   <see cref="MunicipioGrantSyncOptions.MaxAttempts"/> failed attempts it is marked Failed instead — kept for operators,
///   counted <c>failed</c> and logged as an error. A later save of the município queues a new request behind it.</item>
/// </list>
/// The kernel's grant upsert and revoke are idempotent (one active grant per key, XD01-394), so a crash between the kernel
/// call and the completion only sends the same change again once the lease expires. One scope per batch.
/// </summary>
internal sealed partial class MunicipioGrantSyncDispatcher : BackgroundService
{
    private const int MaxFailureLength = 1024;

    private readonly IServiceScopeFactory _scopes;
    private readonly MunicipioGrantSyncOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly GeoAssetsActivitySource _tracer;
    private readonly MunicipioGrantSyncMetrics _metrics;
    private readonly ILogger<MunicipioGrantSyncDispatcher> _logger;
    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public MunicipioGrantSyncDispatcher(
        IServiceScopeFactory scopes,
        IOptions<MunicipioGrantSyncOptions> options,
        TimeProvider timeProvider,
        GeoAssetsActivitySource tracer,
        MunicipioGrantSyncMetrics metrics,
        ILogger<MunicipioGrantSyncDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(tracer);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);

        _scopes = scopes;
        _options = options.Value;
        _timeProvider = timeProvider;
        _tracer = tracer;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>
    /// The delay before the attempt after <paramref name="failedAttempts"/> failed ones:
    /// <see cref="MunicipioGrantSyncOptions.InitialBackoff"/> doubled per failed attempt, capped at
    /// <see cref="MunicipioGrantSyncOptions.MaxBackoff"/>, then jittered to 50–100 % of it so that requests failed together
    /// do not retry in step.
    /// </summary>
    public static TimeSpan Backoff(MunicipioGrantSyncOptions options, int failedAttempts)
    {
        ArgumentNullException.ThrowIfNull(options);

        var exponent = Math.Min(Math.Max(failedAttempts - 1, 0), 30);
        var capped = Math.Min(
            options.InitialBackoff.TotalMilliseconds * Math.Pow(2, exponent),
            options.MaxBackoff.TotalMilliseconds);
        var jitter = 0.5 + (RandomNumberGenerator.GetInt32(0, 501) / 1000.0);
        return TimeSpan.FromMilliseconds(capped * jitter);
    }

    /// <summary>One batch: claims the due requests and sends each; returns how many were claimed.</summary>
    public async Task<int> DispatchOnceAsync(CancellationToken ct)
    {
        var scope = _scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IMunicipioGrantSyncStore>();
            var provisioning = scope.ServiceProvider.GetRequiredService<MunicipioProvisioningService>();

            var claimed = await store
                .ClaimDueAsync(_options.BatchSize, _options.LeaseDuration, _workerId, ct)
                .ConfigureAwait(false);

            foreach (var item in claimed)
            {
                ct.ThrowIfCancellationRequested();
                await DispatchAsync(store, provisioning, item, ct).ConfigureAwait(false);
            }

            return claimed.Count;
        }
    }

    /// <summary>
    /// Sends one change: a revoke revokes its key; a sync revokes the old key first when the IBGE code or the Distribuidora
    /// moved under the link, then upserts the new grant (revoking the previous Prefeitura's when it changed).
    /// </summary>
    internal static async Task ApplyAsync(
        MunicipioProvisioningService provisioning, MunicipioGrantChange change, string requestedBy, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provisioning);
        ArgumentNullException.ThrowIfNull(change);

        if (change.Kind == MunicipioGrantSyncKind.Revoke)
        {
            await provisioning
                .RevokeAsync(change.IbgeCode, change.DistribuidoraOrganizationId, change.PrefeituraOrganizationId, ct)
                .ConfigureAwait(false);
            return;
        }

        var previousPrefeitura = change.PreviousPrefeituraOrganizationId;
        if (change.PreviousIbgeCode is { } previousIbgeCode)
        {
            await provisioning
                .RevokeAsync(
                    previousIbgeCode,
                    change.PreviousDistribuidoraOrganizationId ?? change.DistribuidoraOrganizationId,
                    previousPrefeitura ?? change.PrefeituraOrganizationId,
                    ct)
                .ConfigureAwait(false);
            previousPrefeitura = null;
        }

        await provisioning
            .SyncGrantAsync(
                change.IbgeCode,
                change.DistribuidoraOrganizationId,
                change.PrefeituraOrganizationId,
                previousPrefeitura,
                requestedBy,
                ct)
            .ConfigureAwait(false);
    }

    /// <summary>The bounded text kept for a failed attempt: the exception's type and message, any bearer token redacted.</summary>
    internal static string DescribeFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var message = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : $"{exception.GetType().Name}: {BearerToken().Replace(exception.Message, "Bearer [redacted]")}";
        return message.Length > MaxFailureLength ? message[..MaxFailureLength] : message;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;

        var consecutiveLoopFailures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = _options.PollInterval;
            try
            {
                var claimed = await DispatchOnceAsync(stoppingToken).ConfigureAwait(false);
                consecutiveLoopFailures = 0;
                if (claimed >= _options.BatchSize) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveLoopFailures++;
                delay = Backoff(_options, consecutiveLoopFailures);
                LogLoopFailed(_logger, delay.TotalSeconds, ex);
            }

            try
            {
                await Task.Delay(delay, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task DispatchAsync(
        IMunicipioGrantSyncStore store, MunicipioProvisioningService provisioning, MunicipioGrantSyncWorkItem item, CancellationToken ct)
    {
        using var activity = _tracer.StartActivity("IpBilling.MunicipioGrantSync");
        activity?.SetTag("ipbilling.municipio.id", item.Change.MunicipioId.ToString("D"));
        activity?.SetTag("ipbilling.grant_sync.kind", item.Change.Kind.ToString());
        activity?.SetTag("ipbilling.grant_sync.attempt", item.Attempts + 1);

        try
        {
            await ApplyAsync(provisioning, item.Change, item.RequestedBy, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            GeoAssetsActivitySource.RecordException(activity, ex);
            await RecordFailureAsync(store, item, ex, ct).ConfigureAwait(false);
            return;
        }

        if (await store.MarkCompletedAsync(item.Id, _workerId, ct).ConfigureAwait(false))
        {
            _metrics.Record(MunicipioGrantSyncMetrics.Synced);
            activity?.SetTag("ipbilling.grant_sync.outcome", MunicipioGrantSyncMetrics.Synced);
        }
        else
        {
            LogLeaseLost(_logger, item.Id, item.Change.MunicipioId);
        }
    }

    private async Task RecordFailureAsync(
        IMunicipioGrantSyncStore store, MunicipioGrantSyncWorkItem item, Exception exception, CancellationToken ct)
    {
        var failure = DescribeFailure(exception);
        var attempt = item.Attempts + 1;
        var change = item.Change;

        if (attempt >= _options.MaxAttempts)
        {
            if (await store.MarkFailedAsync(item.Id, _workerId, failure, ct).ConfigureAwait(false))
            {
                _metrics.Record(MunicipioGrantSyncMetrics.Failed);
                Activity.Current?.SetTag("ipbilling.grant_sync.outcome", MunicipioGrantSyncMetrics.Failed);
                LogGaveUp(_logger, item.Id, change.MunicipioId, change.IbgeCode, change.Kind, attempt, exception);
            }
            else
            {
                LogLeaseLost(_logger, item.Id, change.MunicipioId);
            }

            return;
        }

        var delay = Backoff(_options, attempt);
        var nextAttemptAt = _timeProvider.GetUtcNow() + delay;
        if (await store.ScheduleRetryAsync(item.Id, _workerId, failure, nextAttemptAt, ct).ConfigureAwait(false))
        {
            _metrics.Record(MunicipioGrantSyncMetrics.Retry);
            Activity.Current?.SetTag("ipbilling.grant_sync.outcome", MunicipioGrantSyncMetrics.Retry);
            LogRetrying(
                _logger, item.Id, change.MunicipioId, change.IbgeCode, change.Kind, attempt, _options.MaxAttempts,
                delay.TotalSeconds, exception);
        }
        else
        {
            LogLeaseLost(_logger, item.Id, change.MunicipioId);
        }
    }

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BearerToken();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Grant sync request {RequestId} ({Kind}) of município {MunicipioId} (IBGE {IbgeCode}) failed on attempt {Attempt} of {MaxAttempts}; retrying in {RetryDelaySeconds:F0} s.")]
    private static partial void LogRetrying(
        ILogger logger, long requestId, Guid municipioId, string ibgeCode, MunicipioGrantSyncKind kind, int attempt, int maxAttempts,
        double retryDelaySeconds, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Grant sync request {RequestId} ({Kind}) of município {MunicipioId} (IBGE {IbgeCode}) failed {Attempts} times and was marked Failed; repair it with POST /api/ip-billing/municipio-grants/sync or save the município again.")]
    private static partial void LogGaveUp(
        ILogger logger, long requestId, Guid municipioId, string ibgeCode, MunicipioGrantSyncKind kind, int attempts, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Grant sync request {RequestId} of município {MunicipioId} was taken over by another worker after its lease expired; its outcome here was not recorded.")]
    private static partial void LogLeaseLost(ILogger logger, long requestId, Guid municipioId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The município grant sync dispatcher failed to process a batch; retrying in {RetryDelaySeconds:F0} s.")]
    private static partial void LogLoopFailed(ILogger logger, double retryDelaySeconds, Exception exception);
}
