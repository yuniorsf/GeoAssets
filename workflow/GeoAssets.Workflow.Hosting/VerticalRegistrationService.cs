using System.Security.Cryptography;
using GeoAssets.Identity.Authorization.Modules;
using GeoAssets.Workflow.Rest;
using Microsoft.Extensions.Options;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// The bootstrap registration of a vertical host with the kernel (XD01-382, ADR-001 Part 1.2; generic since XD01-385), as
/// the <c>GeoAssets.ServiceCaller</c> service principal, for the vertical <see cref="VerticalRegistrationSource"/> names:
/// <list type="number">
///   <item><c>PUT /api/registry/verticals/{verticalId}/order-types</c> — the source's order-type and feature-action
///   descriptors (XD01-369), when it has any: every order type of the vertical with its target policy and terminal
///   statuses (what the global target ledger needs);</item>
///   <item><c>PUT /api/identity/verticals/{verticalId}/permissions</c> — the permissions and role bundles of the vertical's
///   <see cref="IModulePermissionContributor"/>s (XD01-377).</item>
/// </list>
/// Both are idempotent upserts. A failed attempt is retried with exponential backoff and jitter on
/// <see cref="TimeProvider"/> (<see cref="VerticalRegistrationOptions"/>) until it succeeds; afterwards it is republished every
/// refresh interval. <see cref="VerticalRegistrationState"/> reports where it stands to the health checks.
/// </summary>
public sealed partial class VerticalRegistrationService(
    IServiceScopeFactory scopes,
    VerticalRegistrationSource source,
    IEnumerable<IModulePermissionContributor> contributors,
    VerticalRegistrationState state,
    IOptions<VerticalRegistrationOptions> options,
    TimeProvider timeProvider,
    ILogger<VerticalRegistrationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                await PublishAsync(stoppingToken).ConfigureAwait(false);
                var wasRegistered = state.Current.Status == VerticalRegistrationStatus.Registered;
                state.Registered(timeProvider.GetUtcNow());
                if (!wasRegistered) LogRegistered(logger, source.VerticalId);
                failures = 0;
                delay = settings.RefreshInterval;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                failures++;
                state.Failed(ex.Message);
                delay = Backoff(settings, failures);
                LogFailed(logger, source.VerticalId, failures, delay, ex);
            }

            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    /// <summary>
    /// The delay before attempt <paramref name="failures"/> + 1: <see cref="VerticalRegistrationOptions.InitialDelay"/> doubled per
    /// consecutive failure, capped at <see cref="VerticalRegistrationOptions.MaxDelay"/>, then jittered to 50–100 % of it so
    /// that hosts restarted together do not retry in step.
    /// </summary>
    public static TimeSpan Backoff(VerticalRegistrationOptions settings, int failures)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var exponent = Math.Min(Math.Max(failures - 1, 0), 30);
        var capped = Math.Min(settings.InitialDelay.TotalMilliseconds * Math.Pow(2, exponent), settings.MaxDelay.TotalMilliseconds);
        var jitter = 0.5 + (RandomNumberGenerator.GetInt32(0, 501) / 1000.0);
        return TimeSpan.FromMilliseconds(capped * jitter);
    }

    private async Task PublishAsync(CancellationToken ct)
    {
        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var client = scope.ServiceProvider.GetRequiredService<IVerticalRegistrationClient>();

            if (source.OrderTypes is { } orderTypes)
            {
                await client.PublishOrderTypesAsync(source.VerticalId, orderTypes(scope.ServiceProvider), ct).ConfigureAwait(false);
            }

            var own = contributors
                .Where(c => string.Equals(c.VerticalId, source.VerticalId, StringComparison.Ordinal))
                .ToList();
            var permissions = new ModulePermissionSet
            {
                Permissions = [.. own.SelectMany(c => c.Permissions)],
                RoleBundles = [.. own.SelectMany(c => c.RoleBundles)],
            };
            await client.PublishPermissionsAsync(source.VerticalId, permissions, ct).ConfigureAwait(false);
        }
    }

    [LoggerMessage(EventId = 3821, Level = LogLevel.Information, Message = "Vertical '{VerticalId}' is registered with the kernel.")]
    private static partial void LogRegistered(ILogger logger, string verticalId);

    [LoggerMessage(
        EventId = 3822,
        Level = LogLevel.Warning,
        Message = "Registering vertical '{VerticalId}' with the kernel failed ({Failures} in a row); retrying in {Delay}.")]
    private static partial void LogFailed(ILogger logger, string verticalId, int failures, TimeSpan delay, Exception exception);
}
