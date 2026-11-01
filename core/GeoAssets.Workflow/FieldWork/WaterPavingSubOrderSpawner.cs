using System.Globalization;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Domain.Sanitation.Icerp;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// The parent→child order spawning rule (XD01-230) for a water/sewer repair transitioning to
/// <see cref="FieldWorkStatus.Inspected"/>: validates the crew's recorded trench footprint
/// against the device's georeferenced bounding polygon, and — only when that check passes —
/// both transitions the parent and builds its paving/ICERP restoration child order together, so
/// neither happens without the other (this call is the atomic unit; persisting both within one
/// database transaction is the caller/repository's job, same division of responsibility
/// <see cref="ThreeWayConflictReconciler"/> and <see cref="FieldWorkOrderTransitioner"/> already
/// use — pure domain logic here, infrastructure at the edges).
/// </summary>
public static class WaterPavingSubOrderSpawner
{
    /// <summary>Maximum allowed divergence between the recorded trench area and the georeferenced boundary's actual area.</summary>
    public const double MaxAreaDivergenceFraction = 0.15;

    /// <summary>
    /// Validates the geometry, then transitions <paramref name="parentOrder"/> to
    /// <see cref="FieldWorkStatus.Inspected"/> and returns the new child order — in that order,
    /// so a thrown <see cref="TrenchGeometryDivergenceException"/> leaves the parent order
    /// completely untouched (never partially committed).
    /// </summary>
    public static ServiceOrder TransitionToInspectedWithPavingSubOrder(
        ServiceOrder parentOrder,
        TrenchDimensions recordedTrench,
        GeoPolygon georeferencedBoundary,
        OrderType parentOrderType,
        OrderType pavingOrderType,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(parentOrder);
        ArgumentNullException.ThrowIfNull(recordedTrench);
        ArgumentNullException.ThrowIfNull(pavingOrderType);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var actualAreaSquareMeters = GeodeticAreaCalculator.AreaSquareMeters(georeferencedBoundary);
        if (!IsWithinDivergenceTolerance(recordedTrench.AreaSquareMeters, actualAreaSquareMeters))
            throw new TrenchGeometryDivergenceException(recordedTrench.AreaSquareMeters, actualAreaSquareMeters);

        var child = BuildChildOrder(parentOrder, recordedTrench, pavingOrderType, timeProvider);
        parentOrder.Transition(FieldWorkStatus.Inspected, timeProvider, parentOrderType);
        return child;
    }

    /// <summary>
    /// True when the recorded and actual areas are within <see cref="MaxAreaDivergenceFraction"/>
    /// of each other, relative to the actual (georeferenced) area. An actual area of zero (a
    /// degenerate or missing boundary) is never within tolerance — there is nothing real to
    /// validate the recorded measurement against.
    /// </summary>
    public static bool IsWithinDivergenceTolerance(double recordedAreaSquareMeters, double actualAreaSquareMeters)
    {
        if (actualAreaSquareMeters <= 0) return false;

        var divergence = Math.Abs(recordedAreaSquareMeters - actualAreaSquareMeters) / actualAreaSquareMeters;
        return divergence <= MaxAreaDivergenceFraction;
    }

    private static ServiceOrder BuildChildOrder(
        ServiceOrder parentOrder, TrenchDimensions recordedTrench, OrderType pavingOrderType, TimeProvider timeProvider)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["trenchLengthMeters"] = recordedTrench.LengthMeters.ToString(CultureInfo.InvariantCulture),
            ["trenchWidthMeters"] = recordedTrench.WidthMeters.ToString(CultureInfo.InvariantCulture),
            ["trenchDepthMeters"] = recordedTrench.DepthMeters.ToString(CultureInfo.InvariantCulture),
            ["trenchAreaSquareMeters"] = recordedTrench.AreaSquareMeters.ToString(CultureInfo.InvariantCulture),
            ["trenchVolumeCubicMeters"] = recordedTrench.VolumeCubicMeters.ToString(CultureInfo.InvariantCulture),
        };

        // XD01-252: the child inherits the parent's IScopeableResource.ScopeId (if any) so an
        // OrganizationGrant scoped to the parent's município/subset (XD01-250) also authorizes
        // the auto-spawned paving sub-order — without this, the child would silently fall
        // outside any scoped grant that covered the parent.
        if (((IScopeableResource)parentOrder).ScopeId is { } parentScopeId)
            attributes["scopeId"] = parentScopeId;

        return new ServiceOrder
        {
            Title = $"Paving restoration — {parentOrder.Title}",
            OrderTypeId = pavingOrderType.Id,
            ParentOrderId = parentOrder.Id,
            OrganizationId = parentOrder.OrganizationId,
            CreatedBy = parentOrder.CreatedBy,
            CreatedAt = now,
            Status = pavingOrderType.InitialStateKey ?? ServiceOrderStatus.Draft,
            Attributes = attributes,
        };
    }
}
