using GeoAssets.Workflow.Targets;

namespace GeoAssets.Shared.Tests.Services.OrderTargets;

/// <summary>Targets and test doubles for the per-target work tests (XD01-313).</summary>
internal static class TargetWorkFakes
{
    public static ServiceOrderTarget Target(
        string featureId,
        string status = OrderTargetStatus.Pending,
        OrderTargetConflictLevel conflict = OrderTargetConflictLevel.None,
        string orderId = "o1",
        IReadOnlyList<string>? activityIds = null,
        string? note = null,
        int version = 0) => new()
    {
        Id = "t-" + featureId,
        OrderId = orderId,
        FeatureId = featureId,
        AssetTypeId = "pole",
        Alias = "P-" + featureId,
        Status = status,
        ConflictLevel = conflict,
        ActivityIds = activityIds ?? [],
        Note = note,
        AddedAt = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc),
        AddedBy = "u1",
        OrganizationId = Guid.Empty,
        Version = version,
    };

    public static IReadOnlyList<OrderTargetActivity> Catalog { get; } =
    [
        new("relocate-attachment", "Relocate attachment"),
        new("adjust-attachment", "Adjust attachment"),
        new("remove-attachment", "Remove attachment"),
    ];
}
