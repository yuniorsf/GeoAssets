using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Erp.Totvs;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Real implementation of <see cref="IFieldWorkTransitionGuard.CanStartWorkAsync"/> for XD01-253:
/// blocks APR submission (<see cref="FieldWorkStatus.OnSite"/> → <see cref="FieldWorkStatus.InProgress"/>)
/// when <see cref="IServiceOrder.AssignedTo"/>'s last nightly Totvs Protheus compliance sync
/// shows an expired NR-10/NR-35/ASO certification or expired equipment calibration (see
/// <see cref="TechnicianComplianceRecord.IsCompliantAt"/>).
///
/// An order with no assignee, or an assignee this repository has never synced (not "synced and
/// found non-compliant" — genuinely never pulled), fails open (returns true) — the same
/// "no schema = no gate" convention <see cref="SafetyChecklistTransitionGuard"/> already uses:
/// this guard only blocks on an affirmatively-known expiry, never on missing data. Compose with
/// <see cref="CompositeFieldWorkTransitionGuard"/> alongside the other guards.
/// </summary>
public sealed class TechnicianComplianceTransitionGuard(
    ITechnicianComplianceRepository complianceRepository,
    TimeProvider timeProvider) : IFieldWorkTransitionGuard
{
    public Task<bool> CanArriveAsync(IServiceOrder order, GeoPoint devicePoint, CancellationToken ct = default) => Task.FromResult(true);

    public async Task<bool> CanStartWorkAsync(IServiceOrder order, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (string.IsNullOrWhiteSpace(order.AssignedTo)) return true;

        var record = await complianceRepository.GetAsync(order.AssignedTo, ct).ConfigureAwait(false);
        if (record is null) return true;

        return record.IsCompliantAt(timeProvider.GetUtcNow().UtcDateTime);
    }

    public Task<bool> CanCompleteAsync(IServiceOrder order, CancellationToken ct = default) => Task.FromResult(true);
}
