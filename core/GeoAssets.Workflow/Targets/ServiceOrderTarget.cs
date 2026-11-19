namespace GeoAssets.Workflow.Targets;

/// <summary>
/// One feature a service order works on, with the per-target work state (activities, status,
/// note) that the order's flat feature list cannot carry. Only orders whose
/// <see cref="Orders.OrderType.TargetPolicy"/> is set have targets.
///
/// Immutable: <see cref="WithActivities"/> and <see cref="WithStatus"/> return a changed copy,
/// which becomes durable only through <see cref="IServiceOrderTargetWriter.UpdateAsync"/>.
/// <see cref="Id"/>, <see cref="OrderId"/> and <see cref="FeatureId"/> can never be blank —
/// assigning a blank value throws <see cref="ArgumentException"/>, also through a
/// <c>with</c> expression.
/// </summary>
public sealed record ServiceOrderTarget
{
    /// <summary>Identifier of this target, unique across all orders.</summary>
    public required string Id
    {
        get;
        init => field = RequireText(value, "Id");
    }

    /// <summary>The order this target belongs to, within <see cref="VerticalId"/>.</summary>
    public required string OrderId
    {
        get;
        init => field = RequireText(value, "OrderId");
    }

    /// <summary>
    /// The vertical that owns <see cref="OrderId"/> (XD01-379): <see cref="TargetLedger.KernelVerticalId"/> — the
    /// default — for an in-process order, a remote vertical's id for a target reserved through the ledger API.
    /// </summary>
    public string VerticalId
    {
        get;
        init => field = RequireText(value, "VerticalId");
    } = TargetLedger.KernelVerticalId;

    /// <summary>The targeted feature. An order holds a feature at most once.</summary>
    public required string FeatureId
    {
        get;
        init => field = RequireText(value, "FeatureId");
    }

    /// <summary>Asset type of the feature when it was added.</summary>
    public required string AssetTypeId { get; init; }

    /// <summary>
    /// The feature's value for the policy's <see cref="OrderTargetPolicy.AliasAttributeKey"/>
    /// when it was added; null when the policy has no alias key or the feature has no such value.
    /// </summary>
    public string? Alias { get; init; }

    /// <summary>
    /// Ids of the <see cref="OrderTargetPolicy.Activities"/> to carry out on this target.
    /// Change through <see cref="WithActivities"/>, which checks them against the catalog.
    /// </summary>
    public IReadOnlyList<string> ActivityIds { get; init; } = [];

    /// <summary>
    /// One of the <see cref="OrderTargetStatus"/> keys. Change through <see cref="WithStatus"/>,
    /// which enforces the legal moves.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// The conflict level last evaluated for this target — a snapshot, not a live value;
    /// operations that depend on it (e.g. dispatch) re-evaluate.
    /// </summary>
    public OrderTargetConflictLevel ConflictLevel { get; init; }

    public string? Note { get; init; }

    /// <summary>
    /// Longitude (RFC 7946, SRID 4326) of the feature's centroid when it was added; null when
    /// the feature had no geometry. Feeds <see cref="IServiceOrderTargetReader.GetExtentAsync"/>.
    /// </summary>
    public double? Longitude { get; init; }

    /// <summary>Latitude counterpart of <see cref="Longitude"/>.</summary>
    public double? Latitude { get; init; }

    /// <summary>UTC instant the target was added, read from the caller's <see cref="TimeProvider"/>.</summary>
    public required DateTime AddedAt { get; init; }

    /// <summary>User id of whoever added the target.</summary>
    public required string AddedBy { get; init; }

    /// <summary>The owning order's organization.</summary>
    public required Guid OrganizationId { get; init; }

    /// <summary>
    /// Optimistic-concurrency token, managed by the store: 0 for a target that was never
    /// updated, incremented by one on every successful
    /// <see cref="IServiceOrderTargetWriter.UpdateAsync"/>. Callers hand back the value they
    /// read and never compute one themselves.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Returns a copy whose <see cref="ActivityIds"/> are <paramref name="activityIds"/>,
    /// checked against <paramref name="catalog"/> (the order type's
    /// <see cref="OrderTargetPolicy.Activities"/>). Ids match case-insensitively, as
    /// <see cref="OrderTargetPolicy.Validate"/> compares them, and are stored in the catalog's
    /// spelling; repeated ids collapse to their first occurrence, keeping the given order. An
    /// empty <paramref name="activityIds"/> clears the activities.
    /// </summary>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="ArgumentException">An id is not in <paramref name="catalog"/>.</exception>
    public ServiceOrderTarget WithActivities(IReadOnlyCollection<string> activityIds, IReadOnlyCollection<OrderTargetActivity> catalog)
    {
        ArgumentNullException.ThrowIfNull(activityIds);
        ArgumentNullException.ThrowIfNull(catalog);

        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var activity in catalog)
            known.TryAdd(activity.Id, activity.Id);

        var resolved = new List<string>(activityIds.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var activityId in activityIds)
        {
            if (activityId is null || !known.TryGetValue(activityId, out var catalogId))
            {
                throw new ArgumentException(
                    $"Activity '{activityId}' is not one of the order type's target activities.",
                    nameof(activityIds));
            }

            if (seen.Add(catalogId)) resolved.Add(catalogId);
        }

        return this with { ActivityIds = resolved };
    }

    /// <summary>
    /// Returns a copy in status <paramref name="next"/>, or this same instance when
    /// <paramref name="next"/> is already the current status (a no-op). Legal moves are those
    /// of <see cref="OrderTargetStatus.CanTransition"/>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="next"/> is null or blank.</exception>
    /// <exception cref="InvalidOperationException">
    /// The move from the current status to <paramref name="next"/> is not allowed, including
    /// when <paramref name="next"/> is not a defined status.
    /// </exception>
    public ServiceOrderTarget WithStatus(string next)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(next);

        if (string.Equals(Status, next, StringComparison.Ordinal)) return this;

        if (!OrderTargetStatus.CanTransition(Status, next))
            throw new InvalidOperationException($"Target '{Id}' cannot move from status '{Status}' to '{next}'.");

        return this with { Status = next };
    }

    private static string RequireText(string value, string memberName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"ServiceOrderTarget.{memberName} must not be blank.", nameof(value));

        return value;
    }
}
