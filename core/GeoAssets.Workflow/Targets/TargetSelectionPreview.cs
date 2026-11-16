using GeoAssets.Core.Models;

namespace GeoAssets.Workflow.Targets;

/// <summary>
/// What a selection would do to an order, without having done it.
/// <c>Matched = AlreadyInOrder + Eligible</c>; of the eligible features,
/// <c>Eligible - TruncatedByLimit</c> would be added, and those are what
/// <see cref="FirstPage"/>, <see cref="WarningCount"/> and <see cref="BlockedCount"/> describe.
/// </summary>
public sealed record TargetSelectionPreview
{
    /// <summary>The order's type, for telemetry and for the caller's own display.</summary>
    public required string OrderTypeId { get; init; }

    /// <summary>Features the strategy matched, after the asset-type and organization restrictions.</summary>
    public required int Matched { get; init; }

    /// <summary>Matched features the order does not hold yet.</summary>
    public required int Eligible { get; init; }

    /// <summary>Matched features the order already holds; they are never added twice.</summary>
    public required int AlreadyInOrder { get; init; }

    /// <summary>
    /// How many eligible features do not fit under the order type's selection limit; 0 when
    /// everything fits or the type has no limit.
    /// </summary>
    public required int TruncatedByLimit { get; init; }

    /// <summary>Features that would be added with <see cref="OrderTargetConflictLevel.Warning"/>.</summary>
    public required int WarningCount { get; init; }

    /// <summary>
    /// Features that would be added with <see cref="OrderTargetConflictLevel.Blocked"/>. They are
    /// added (so they stay visible) and count toward the selection limit.
    /// </summary>
    public required int BlockedCount { get; init; }

    /// <summary>
    /// The requested page of the targets that would be added, ordered by feature id (ordinal);
    /// <see cref="PagedResult{T}.TotalCount"/> is how many would be added in all.
    /// </summary>
    public required PagedResult<ServiceOrderTarget> FirstPage { get; init; }

    /// <summary><c>identifier-list</c> only: requested identifiers that matched no feature.</summary>
    public IReadOnlyList<string> NotFoundValues { get; init; } = [];

    /// <summary><c>identifier-list</c> only: requested identifiers that matched more than one feature.</summary>
    public IReadOnlyList<string> AmbiguousValues { get; init; } = [];
}
