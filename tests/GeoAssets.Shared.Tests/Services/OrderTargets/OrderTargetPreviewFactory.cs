using GeoAssets.Core.Models;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Shared.Tests.Services.OrderTargets;

/// <summary>Builds previews and results the way the selection service reports them.</summary>
internal static class OrderTargetPreviewFactory
{
    public static TargetSelectionPreview Preview(
        int matched = 5,
        int alreadyInOrder = 0,
        int truncated = 0,
        int warnings = 0,
        int blocked = 0,
        string[]? notFound = null,
        int page = 1) => new()
    {
        OrderTypeId = "poles",
        Matched = matched,
        Eligible = matched - alreadyInOrder,
        AlreadyInOrder = alreadyInOrder,
        TruncatedByLimit = truncated,
        WarningCount = warnings,
        BlockedCount = blocked,
        FirstPage = new PagedResult<ServiceOrderTarget>
        {
            Items = [Target($"f{page}")],
            TotalCount = matched - alreadyInOrder - truncated,
        },
        NotFoundValues = notFound ?? [],
    };

    public static TargetSelectionResult Result(int added) => new()
    {
        OrderTypeId = "poles",
        Added = added,
        Matched = added,
        Eligible = added,
        AlreadyInOrder = 0,
        TruncatedByLimit = 0,
        WarningCount = 0,
        BlockedCount = 0,
    };

    public static ServiceOrderTarget Target(string featureId) => new()
    {
        Id = "t-" + featureId,
        OrderId = "o1",
        FeatureId = featureId,
        AssetTypeId = "pole",
        Status = OrderTargetStatus.Pending,
        AddedAt = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc),
        AddedBy = "u1",
        OrganizationId = Guid.Empty,
    };
}
