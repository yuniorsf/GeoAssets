namespace GeoAssets.Workflow.Targets;

/// <summary>
/// What <see cref="IOrderTargetSelectionService.ApplyAsync"/> did. The counts have the meaning
/// they have on <see cref="TargetSelectionPreview"/>; <see cref="Added"/> is how many targets
/// the store actually inserted, which is lower than <c>Eligible - TruncatedByLimit</c> only when
/// another caller added some of the same features concurrently.
/// </summary>
public sealed record TargetSelectionResult
{
    public required string OrderTypeId { get; init; }

    /// <summary>Targets inserted by this call; 0 when an identical selection was already applied.</summary>
    public required int Added { get; init; }

    public required int Matched { get; init; }

    public required int Eligible { get; init; }

    public required int AlreadyInOrder { get; init; }

    /// <summary>Eligible features left out because the request accepted truncation; otherwise 0.</summary>
    public required int TruncatedByLimit { get; init; }

    public required int WarningCount { get; init; }

    public required int BlockedCount { get; init; }

    public IReadOnlyList<string> NotFoundValues { get; init; } = [];

    public IReadOnlyList<string> AmbiguousValues { get; init; } = [];
}
