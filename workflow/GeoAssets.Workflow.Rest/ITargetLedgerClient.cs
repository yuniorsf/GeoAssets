using GeoAssets.Core.Models;
using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Rest;

/// <summary>
/// A remote vertical's port onto the kernel's global target ledger (XD01-379, ADR-001 §3.4; this client XD01-382): the
/// targets of the vertical's own orders are reserved, released and committed in the kernel, keyed by
/// <c>(vertical, orderId)</c>, so exclusivity holds across verticals. Every call names the organization it acts for in
/// its body (or query string) — never in a header — and authenticates as the vertical's service principal.
/// Failures come back as the exceptions the kernel's in-process services throw: a reservation another order's exclusive
/// hold blocks is <see cref="TargetLedgerConflictException"/>.
/// </summary>
public interface ITargetLedgerClient
{
    /// <summary>What reserving the selection would add; writes nothing.</summary>
    Task<TargetSelectionPreview> PreviewAsync(TargetLedgerReserveCommand command, CancellationToken ct);

    /// <summary>Reserves the selection for the order; safe to repeat (features it already holds are skipped).</summary>
    /// <exception cref="TargetLedgerConflictException">Another order holds a kept feature with blocking exclusivity.</exception>
    Task<TargetSelectionResult> ReserveAsync(TargetLedgerReserveCommand command, CancellationToken ct);

    /// <summary>
    /// Releases the order's open targets — those in <paramref name="targetIds"/>, or all when null. Idempotent: returns the
    /// targets this call released (empty when none was left), or null when the ledger holds no target of the order in
    /// <paramref name="organizationId"/>.
    /// </summary>
    Task<IReadOnlyList<TargetLedgerEventItem>?> ReleaseAsync(
        TargetLedgerKey key, Guid organizationId, IReadOnlyCollection<string>? targetIds, string? actor, CancellationToken ct);

    /// <summary>
    /// Marks the order's open, unfinished targets done — those in <paramref name="targetIds"/>, or all when null. Null and
    /// empty mean what they mean for <see cref="ReleaseAsync"/>.
    /// </summary>
    Task<IReadOnlyList<TargetLedgerEventItem>?> CommitAsync(
        TargetLedgerKey key, Guid organizationId, IReadOnlyCollection<string>? targetIds, string? actor, CancellationToken ct);

    /// <summary>
    /// One page (<paramref name="skip"/>, <paramref name="take"/> from 1 to 1000) of the order's targets, released ones
    /// included; an order the ledger holds no target of reads as an empty page.
    /// </summary>
    Task<PagedResult<TargetLedgerEntry>> GetUsagesAsync(TargetLedgerKey key, Guid organizationId, int skip, int take, CancellationToken ct);
}
