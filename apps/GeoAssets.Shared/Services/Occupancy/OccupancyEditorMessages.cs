using GeoAssets.NeutralOperator.Domain;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.Shared.Services.Occupancy;

/// <summary>
/// Which localization key <c>OccupancyChangeEditor</c> shows for what (XD01-315): statuses,
/// classes, change kinds, the warnings of a staged set and the failures of the staging service.
/// Kept apart from the component so the key set is testable against the three language files.
/// </summary>
public static class OccupancyEditorMessages
{
    public static string StatusKey(PoleAttachmentOccupantStatus status) => $"occupancy.status.{status}";

    public static string ClassKey(OccupancyClass occupancyClass) => $"occupancy.class.{occupancyClass}";

    public static string ChangeKindKey(OccupancyChangeKind kind) => $"occupancy.change.{kind}";

    /// <summary>The key for a warning code of <see cref="TargetOccupancyView.Warnings"/>; a generic one for a code this client does not know.</summary>
    public static string WarningKey(string code) => code switch
    {
        OccupancyProblemCodes.StaleBaseline => "occupancy.warning.staleBaseline",
        OccupancyProblemCodes.EntryNotFound => "occupancy.warning.entryNotFound",
        OccupancyProblemCodes.InvalidChange => "occupancy.warning.invalidChange",
        _ => "occupancy.warning.other",
    };

    /// <summary>The key for a failure of the staging service, most specific first.</summary>
    public static string ErrorKey(Exception exception) => exception switch
    {
        OrderActionDeniedException => "occupancy.error.denied",
        OrderTargetNotFoundException => "occupancy.error.targetNotFound",
        OccupancyEntryNotFoundException => "occupancy.error.entryNotFound",
        KeyNotFoundException => "occupancy.error.orderNotFound",
        TargetIsNotAPoleException => "occupancy.error.notAPole",
        OccupancyTargetReleasedException => "occupancy.error.targetReleased",
        InvalidServiceOrderTransitionException => "occupancy.error.orderClosed",
        InvalidOccupancyChangeException => "occupancy.error.invalid",
        _ => "occupancy.error.unexpected",
    };

    /// <summary>Every key the two methods above and <see cref="OccupancyChangeDraft.TryPreview"/> can return.</summary>
    public static IReadOnlyList<string> MessageKeys { get; } =
    [
        "occupancy.warning.staleBaseline", "occupancy.warning.entryNotFound", "occupancy.warning.invalidChange", "occupancy.warning.other",
        "occupancy.error.denied", "occupancy.error.targetNotFound", "occupancy.error.entryNotFound", "occupancy.error.orderNotFound",
        "occupancy.error.notAPole", "occupancy.error.targetReleased", "occupancy.error.orderClosed", "occupancy.error.invalid",
        "occupancy.error.unexpected", "occupancy.error.transition", "occupancy.error.outOfRange", "occupancy.error.slotOccupied",
    ];
}
