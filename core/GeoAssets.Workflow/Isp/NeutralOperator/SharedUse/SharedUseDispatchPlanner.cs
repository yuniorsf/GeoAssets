using System.Security.Cryptography;
using System.Text;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;

/// <summary>
/// Decides what dispatching a shared-use order does (XD01-316): one child order per occupant
/// organization that has work on the order's poles. Pure — it reads no store and no clock but the
/// one handed in, so the same request always gives the same plan.
///
/// <para><b>Who gets a child.</b> An organization with at least one occupancy entry in status
/// <see cref="PoleAttachmentOccupantStatus.AuthorizedContracted"/> on a pole of the order that no
/// other open order blocks. An entry beyond the six regulated slots counts exactly like one on a
/// slot. Every other entry on those poles is reported in
/// <see cref="SharedUseDispatchPlan.SkippedEntries"/> with its reason, and a pole left with
/// nobody to notify in <see cref="SharedUseDispatchPlan.UnnotifiablePoleIds"/>; neither is
/// dropped silently and neither stops the dispatch.</para>
///
/// <para><b>What a child is.</b> Its id is <see cref="ChildOrderId"/> — a function of the parent
/// and the occupant alone, so planning the same parent again names the same children. It is owned
/// by the parent's organization, has the parent as <see cref="IServiceOrder.ParentOrderId"/>,
/// starts in the child type's initial state, and carries one dispatch to the occupant
/// organization. Its targets are the occupant's poles with the activities the parent assigned.</para>
/// </summary>
public static class SharedUseDispatchPlanner
{
    /// <summary>Attribute of a child order holding the organization it was created for, as canonical GUID text.</summary>
    public const string PartyOrganizationAttribute = "fanOutPartyOrganizationId";

    private const string ScopeAttribute = "scopeId";

    /// <summary>
    /// The id of the child order of <paramref name="parentOrderId"/> addressed to
    /// <paramref name="partyOrganizationId"/>: 36 characters of GUID text derived from the two,
    /// the same on every call.
    /// </summary>
    public static string ChildOrderId(string parentOrderId, Guid partyOrganizationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentOrderId);
        if (partyOrganizationId == Guid.Empty)
            throw new ArgumentException("The party organization must not be empty.", nameof(partyOrganizationId));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{parentOrderId}|{partyOrganizationId:N}"));
        return new Guid(hash.AsSpan(0, 16)).ToString();
    }

    /// <summary>
    /// The status the parent has once dispatched from <paramref name="status"/>. A type with its
    /// own workflow graph follows the transition its graph tags with
    /// <see cref="OrderAction.Dispatch"/>; a type without one moves a draft to pending and
    /// leaves a pending order where it is, so an order can be dispatched again after poles were added.
    /// </summary>
    /// <exception cref="InvalidServiceOrderTransitionException">No dispatch is possible from <paramref name="status"/>.</exception>
    public static string StatusAfterDispatch(OrderType orderType, string status)
    {
        ArgumentNullException.ThrowIfNull(orderType);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        if (orderType.States.Count > 0)
        {
            var transition = orderType.Transitions.FirstOrDefault(t =>
                string.Equals(t.FromStateKey, status, StringComparison.Ordinal) && t.TriggerAction == OrderAction.Dispatch);
            return transition?.ToStateKey ?? throw new InvalidServiceOrderTransitionException(status, status);
        }

        return status switch
        {
            ServiceOrderStatus.Draft or ServiceOrderStatus.Pending => ServiceOrderStatus.Pending,
            _ => throw new InvalidServiceOrderTransitionException(status, status),
        };
    }

    /// <summary>Plans the fan-out of <see cref="SharedUseDispatchPlanRequest.Parent"/>.</summary>
    /// <exception cref="InvalidOperationException">The parent type declares no fan-out, or the child type is not the one it names.</exception>
    /// <exception cref="InvalidServiceOrderTransitionException">The parent cannot be dispatched from its status.</exception>
    public static SharedUseDispatchPlan Plan(SharedUseDispatchPlanRequest request, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DispatchedBy, nameof(request));
        EnsureFanOut(request);

        var statusAfter = StatusAfterDispatch(request.ParentType, request.Parent.Status);
        var plannedAt = timeProvider.GetUtcNow();
        var now = plannedAt.UtcDateTime;

        var targets = request.Targets.OrderBy(t => t.FeatureId, StringComparer.Ordinal).ThenBy(t => t.Id, StringComparer.Ordinal).ToList();
        var open = targets.Where(t => !IsBlocked(request, t)).ToList();
        var withoutActivity = open.Where(t => t.ActivityIds.Count == 0).Select(t => t.Id).Order(StringComparer.Ordinal).ToList();

        var occupants = Occupants.Of(request, open);
        var children = occupants.ByOrganization
            .OrderBy(pair => pair.Key.ToString("D"), StringComparer.Ordinal)
            .Select(pair => Child(request, pair.Key, pair.Value, now))
            .ToList();

        return new SharedUseDispatchPlan
        {
            ParentOrderId = request.Parent.Id,
            ParentStatusAfterDispatch = statusAfter,
            PlannedAt = plannedAt,
            TargetCount = targets.Count,
            BlockedTargetCount = targets.Count - open.Count,
            Children = children,
            SkippedEntries = occupants.Skipped,
            UnnotifiablePoleIds = occupants.UnnotifiablePoleIds,
            BlockReason = BlockReason(targets.Count, open.Count, withoutActivity.Count, children.Count),
            TargetIdsWithoutActivity = withoutActivity,
        };
    }

    private static void EnsureFanOut(SharedUseDispatchPlanRequest request)
    {
        var fanOut = request.ParentType.TargetPolicy?.FanOut
            ?? throw new InvalidOperationException(
                $"Order type '{request.ParentType.Id}' declares no fan-out, so its orders are not dispatched to occupants.");

        if (!string.Equals(fanOut.ChildOrderTypeId, request.ChildType.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Order type '{request.ParentType.Id}' fans out into '{fanOut.ChildOrderTypeId}', not '{request.ChildType.Id}'.");
        }
    }

    private static bool IsBlocked(SharedUseDispatchPlanRequest request, ServiceOrderTarget target) =>
        request.ConflictLevels.GetValueOrDefault(target.FeatureId) == OrderTargetConflictLevel.Blocked;

    private static SharedUseDispatchBlockReason? BlockReason(int targets, int open, int withoutActivity, int children)
    {
        if (targets == 0) return SharedUseDispatchBlockReason.NoTargets;
        if (withoutActivity > 0) return SharedUseDispatchBlockReason.TargetsWithoutActivity;
        if (open == 0) return SharedUseDispatchBlockReason.AllTargetsBlocked;
        if (children == 0) return SharedUseDispatchBlockReason.NoEligibleOccupants;

        return null;
    }

    private static SharedUsePlannedChild Child(
        SharedUseDispatchPlanRequest request, Guid organizationId, List<ServiceOrderTarget> poles, DateTime now)
    {
        var parent = request.Parent;
        var childId = ChildOrderId(parent.Id, organizationId);

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PartyOrganizationAttribute] = organizationId.ToString("D"),
        };
        if ((parent as IScopeableResource)?.ScopeId is { } scopeId)
            attributes[ScopeAttribute] = scopeId;

        var order = new ServiceOrder
        {
            Id = childId,
            Title = parent.Title,
            Description = parent.Description,
            OrderTypeId = request.ChildType.Id,
            Status = request.ChildType.InitialStateKey ?? ServiceOrderStatus.Draft,
            Priority = parent.Priority,
            CreatedBy = request.DispatchedBy,
            OrganizationId = parent.OrganizationId,
            CreatedAt = now,
            ScheduledAt = parent.ScheduledAt,
            ParentOrderId = parent.Id,
            Attributes = attributes,
            Dispatches =
            [
                new OrderDispatch(organizationId.ToString("D"), DispatchTargetType.Organization, request.DispatchedBy, now)
                {
                    ActorKind = request.ActorKind,
                },
            ],
        };

        return new SharedUsePlannedChild
        {
            OccupantOrganizationId = organizationId,
            ChildOrderId = childId,
            AlreadyDispatched = request.ExistingChildOrderIds.Contains(childId, StringComparer.Ordinal),
            Order = order,
            Targets = [.. poles.Select(pole => ChildTarget(request, pole, childId, now))],
        };
    }

    private static ServiceOrderTarget ChildTarget(
        SharedUseDispatchPlanRequest request, ServiceOrderTarget pole, string childId, DateTime now) => new()
        {
            Id = OrderTargetSelectionService.TargetId(childId, pole.FeatureId),
            OrderId = childId,
            FeatureId = pole.FeatureId,
            AssetTypeId = pole.AssetTypeId,
            Alias = pole.Alias,
            ActivityIds = [.. pole.ActivityIds],
            Status = OrderTargetStatus.Pending,
            ConflictLevel = request.ConflictLevels.GetValueOrDefault(pole.FeatureId),
            Longitude = pole.Longitude,
            Latitude = pole.Latitude,
            AddedAt = now,
            AddedBy = request.DispatchedBy,
            OrganizationId = request.Parent.OrganizationId,
        };

    /// <summary>The occupancy of the poles that are not blocked, sorted into who is notified and who is not.</summary>
    private sealed class Occupants
    {
        public Dictionary<Guid, List<ServiceOrderTarget>> ByOrganization { get; } = [];

        public List<SharedUseSkippedEntry> Skipped { get; } = [];

        public List<string> UnnotifiablePoleIds { get; } = [];

        public static Occupants Of(SharedUseDispatchPlanRequest request, List<ServiceOrderTarget> openTargets)
        {
            var occupancies = new Dictionary<string, PoleOccupancy>(StringComparer.Ordinal);
            foreach (var occupancy in request.Occupancies)
                occupancies[occupancy.PoleFeatureId] = occupancy;

            var result = new Occupants();
            foreach (var target in openTargets)
            {
                var entries = occupancies.TryGetValue(target.FeatureId, out var onFile) ? onFile.Entries : [];
                if (!result.Sort(request.Parent.OrganizationId, target, entries))
                    result.UnnotifiablePoleIds.Add(target.FeatureId);
            }

            return result;
        }

        /// <summary>Returns whether the pole has at least one occupant to notify.</summary>
        private bool Sort(Guid poleOwnerOrganizationId, ServiceOrderTarget target, IReadOnlyList<PoleOccupancyEntry> entries)
        {
            var notified = new HashSet<Guid>();
            foreach (var entry in entries.OrderBy(e => e.Id.ToString("D"), StringComparer.Ordinal))
            {
                if (SkipReason(entry, poleOwnerOrganizationId) is { } reason)
                {
                    Skipped.Add(new SharedUseSkippedEntry(target.FeatureId, entry.Id, entry.SlotIndex, entry.Status, reason));
                    continue;
                }

                // An occupant with several contracted entries on one pole gets that pole once.
                var organizationId = entry.OccupantOrganizationId!.Value;
                if (!notified.Add(organizationId)) continue;

                if (!ByOrganization.TryGetValue(organizationId, out var poles))
                    ByOrganization[organizationId] = poles = [];
                poles.Add(target);
            }

            return notified.Count > 0;
        }

        private static SharedUseSkipReason? SkipReason(PoleOccupancyEntry entry, Guid poleOwnerOrganizationId)
        {
            if (entry.Status != PoleAttachmentOccupantStatus.AuthorizedContracted) return SharedUseSkipReason.NoActiveContract;
            if (entry.OccupantOrganizationId is not { } organizationId) return SharedUseSkipReason.UnknownOrganization;
            if (organizationId == poleOwnerOrganizationId) return SharedUseSkipReason.PoleOwner;

            return null;
        }
    }
}
