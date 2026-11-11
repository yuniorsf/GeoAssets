using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Isp.NeutralOperator;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Shared.Services.FieldWork;

/// <summary>
/// XD01-260's portable, testable half of the mobile pole census workflow — builds the single-pole
/// <see cref="PoleCensusOrderTypes.PoleCensusInspectionId"/> order and encodes a completed
/// <see cref="PoleCensusSubmission"/> onto its <see cref="ServiceOrder.Attributes"/>, validating
/// against <see cref="PoleSharingComplianceChecklistSchema"/> before the MAUI host queues it
/// locally (AC: "validates... before local queuing") — same device-I/O-free split
/// <c>FtthDropInstallChecklistService</c> already established for the FTTH checklist (XD01-277):
/// this type does no GPS/camera/SQLite work itself.
/// </summary>
public static class PoleCensusInspectionService
{
    private static readonly OrderType PoleCensusOrderType = PoleCensusOrderTypes.BuildPoleCensusInspection();

    public static ServiceOrder CreateOrder(GeoFeature pole, string technicianId, Guid organizationId, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(pole);
        ArgumentException.ThrowIfNullOrWhiteSpace(technicianId);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        return new ServiceOrder
        {
            Title = $"Pole census — {pole.Properties.Name}",
            OrderTypeId = PoleCensusOrderTypes.PoleCensusInspectionId,
            Status = FieldWorkWorkflowGraph.InitialStateKey,
            OrganizationId = organizationId,
            CreatedBy = technicianId,
            CreatedAt = now,
            Features = [pole],
        };
    }

    /// <summary>
    /// Encodes <paramref name="submission"/> onto <paramref name="order"/>'s attributes and
    /// validates the result against <see cref="PoleSharingComplianceChecklistSchema"/>. Leaves
    /// <paramref name="order"/> unmodified on failure.
    /// </summary>
    public static PoleCensusChecklistStepResult RecordSubmission(ServiceOrder order, PoleCensusSubmission submission, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var candidate = new Dictionary<string, string>(order.Attributes, StringComparer.Ordinal)
        {
            [PoleSharingComplianceChecklistSchema.StructuralConditionKey] = submission.StructuralCondition,
            [PoleSharingComplianceChecklistSchema.Band500MmCompliantKey] = submission.Band500MmCompliant ? "true" : "false",
            [PoleSharingComplianceChecklistSchema.ClearanceMeasurementsKey] = JsonSerializer.Serialize(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["groundToLowestCableMeters"] = submission.GroundToLowestCableMeters,
                ["powerToCommunicationBandMeters"] = submission.PowerToCommunicationBandMeters,
            }),
            [PoleSharingComplianceChecklistSchema.OccupantsKey] = JsonSerializer.Serialize(submission.Occupants.Select(occupant =>
            {
                var entry = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["slotIndex"] = occupant.SlotIndex,
                    ["status"] = occupant.Status,
                };
                if (occupant.OperatorCnpj is not null)
                    entry["operatorCnpj"] = occupant.OperatorCnpj;
                return entry;
            })),
            [PoleSharingComplianceChecklistSchema.UnauthorizedCableDetectedKey] = submission.UnauthorizedCableDetected ? "true" : "false",
        };

        var errors = ServiceOrderAttributeValidator.Validate(PoleCensusOrderType, candidate);
        if (errors.Count > 0)
            return PoleCensusChecklistStepResult.Failure(errors[0]);

        foreach (var (key, value) in candidate)
            order.Attributes[key] = value;

        // Advances through FieldWorkWorkflowGraph's structurally-legal edges directly via
        // ServiceOrder.Transition, not FieldWorkOrderTransitioner — bypassing its
        // IFieldWorkTransitionGuard checks (CanArriveAsync/CanStartWorkAsync/CanCompleteAsync)
        // deliberately, the same way FtthDropInstallChecklistService's own custom graph never
        // invokes that guard chain either. The GPS/proximity precondition those guards would
        // otherwise check is already enforced on-device by PoleCensusProximityGate before this
        // method is ever called; the server-side sync/reconciliation point is this offline-first
        // flow's authoritative check, not a second guard re-run here.
        order.Transition(FieldWorkStatus.Pending, timeProvider, PoleCensusOrderType);
        order.Transition(FieldWorkStatus.Scheduled, timeProvider, PoleCensusOrderType);
        order.Transition(FieldWorkStatus.Dispatched, timeProvider, PoleCensusOrderType);
        order.Transition(FieldWorkStatus.EnRoute, timeProvider, PoleCensusOrderType);
        order.Transition(FieldWorkStatus.OnSite, timeProvider, PoleCensusOrderType);
        order.Transition(FieldWorkStatus.InProgress, timeProvider, PoleCensusOrderType);
        order.Transition(FieldWorkStatus.Completed, timeProvider, PoleCensusOrderType);

        return PoleCensusChecklistStepResult.Success();
    }
}
