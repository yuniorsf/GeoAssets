using GeoAssets.Workflow.Orders;

namespace GeoAssets.Shared.Services.Offline;

/// <summary>
/// Local, encrypted-at-rest persistence for <see cref="ServiceOrder"/> (XD01-243) — the
/// device-side source of truth a field crew works against while offline. Concrete
/// implementations (e.g. SQLCipher on MAUI) live outside this project, which stays
/// TFM-agnostic and testable.
///
/// <see cref="GetBaseSnapshotAsync"/>/<see cref="SaveBaseSnapshotAsync"/> hold the copy as last
/// known from the server — the "base" snapshot
/// <see cref="GeoAssets.Workflow.FieldWork.ThreeWayConflictReconciler.Reconcile"/> needs, distinct
/// from the device's own in-progress edits returned by <see cref="GetAsync"/>.
/// </summary>
public interface ILocalServiceOrderStore
{
    Task<ServiceOrder?> GetAsync(string serviceOrderId, CancellationToken ct = default);

    Task<IReadOnlyList<ServiceOrder>> GetAllAsync(CancellationToken ct = default);

    Task UpsertAsync(ServiceOrder order, CancellationToken ct = default);

    Task<ServiceOrder?> GetBaseSnapshotAsync(string serviceOrderId, CancellationToken ct = default);

    Task SaveBaseSnapshotAsync(ServiceOrder order, CancellationToken ct = default);
}
