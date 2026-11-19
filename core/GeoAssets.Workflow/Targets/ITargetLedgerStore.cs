using GeoAssets.Core.Models;

namespace GeoAssets.Workflow.Targets;

/// <summary>
/// The global target ledger's store (XD01-379, ADR-001 §3.4): the targets of every vertical's orders, keyed by
/// <see cref="TargetLedgerKey"/>. The in-process target routes keep using <see cref="IServiceOrderTargetReader"/> and
/// <see cref="IServiceOrderTargetWriter"/>, which address <see cref="TargetLedger.KernelVerticalId"/> orders only.
///
/// Rules common to all members: a null argument throws <see cref="ArgumentNullException"/>; a cancelled token surfaces
/// as <see cref="OperationCanceledException"/>; authorization is the caller's job. Writes return exactly the targets
/// they changed, so a caller can report them (an integration event) in the same unit of work; repeating a write changes
/// nothing more and returns empty.
/// </summary>
public interface ITargetLedgerStore
{
    /// <summary>
    /// Every open (not released) usage of <paramref name="featureIds"/> in every vertical and every organization, except
    /// the targets of <paramref name="excluding"/> itself — conflict detection is global by design. One usage per
    /// (feature, holding order); empty <paramref name="featureIds"/> returns empty without querying.
    /// </summary>
    Task<IReadOnlyList<TargetLedgerUsage>> FindOpenUsagesAsync(
        IReadOnlyCollection<string> featureIds, TargetLedgerKey excluding, CancellationToken ct);

    /// <summary>
    /// Serializes reservations of <paramref name="featureIds"/> until the current unit of work's transaction ends, so the
    /// conflict check a reservation repeats after taking the locks sees every reservation committed before it — two
    /// verticals reserving the same feature at the same moment cannot both pass. Locks are taken in one stable order so
    /// concurrent reservations of overlapping sets cannot deadlock. A store without concurrent writers may do nothing.
    /// </summary>
    Task LockFeaturesAsync(IReadOnlyCollection<string> featureIds, CancellationToken ct);

    /// <summary>The organization whose targets <paramref name="key"/> holds, or null when it holds none.</summary>
    Task<Guid?> FindOrganizationAsync(TargetLedgerKey key, CancellationToken ct);

    /// <summary>
    /// One page of <paramref name="key"/>'s targets in <paramref name="organizationId"/> — released ones included —
    /// ordered by feature id then target id (ordinal). Another organization's targets of the same key are never
    /// returned; an order with none in that organization reads as an empty page with a total of 0.
    /// </summary>
    Task<PagedResult<TargetLedgerEntry>> GetPageAsync(
        TargetLedgerKey key, Guid organizationId, int skip, int take, CancellationToken ct);

    /// <summary>
    /// Inserts <paramref name="targets"/> (all of <paramref name="order"/>, under its organization), skipping any feature
    /// the order already holds, and returns the ones really inserted. The order type and its exclusivity are stored
    /// with each target, so a later global search reports them without the order.
    /// </summary>
    /// <exception cref="ArgumentException">A target names another vertical, order or organization than <paramref name="order"/>.</exception>
    Task<IReadOnlyList<ServiceOrderTarget>> ReserveAsync(
        TargetLedgerOrder order, IReadOnlyCollection<ServiceOrderTarget> targets, CancellationToken ct);

    /// <summary>
    /// Releases the open targets of <paramref name="key"/> at <paramref name="releasedAt"/> — those whose id is in
    /// <paramref name="targetIds"/>, or all of them when it is null — and returns the ones released. A null
    /// <paramref name="organizationId"/> releases whatever organization they belong to (the event consumer's case:
    /// the vertical reported its own order); otherwise only that organization's.
    /// </summary>
    Task<IReadOnlyList<ServiceOrderTarget>> ReleaseAsync(
        TargetLedgerKey key, Guid? organizationId, IReadOnlyCollection<string>? targetIds, DateTimeOffset releasedAt, CancellationToken ct);

    /// <summary>
    /// Marks the open, not yet finished targets of <paramref name="key"/> in <paramref name="organizationId"/>
    /// (<see cref="OrderTargetStatus.Pending"/> or <see cref="OrderTargetStatus.InProgress"/>) as
    /// <see cref="OrderTargetStatus.Done"/> — those whose id is in <paramref name="targetIds"/>, or all of them when it
    /// is null — and returns the ones changed. A committed target keeps holding its feature until it is released.
    /// </summary>
    Task<IReadOnlyList<ServiceOrderTarget>> CommitAsync(
        TargetLedgerKey key, Guid organizationId, IReadOnlyCollection<string>? targetIds, CancellationToken ct);
}
