namespace GeoAssets.Shared.Services.OrderTargets;

/// <summary>Why the map selection cannot be planned as an order's targets (XD01-312).</summary>
public enum OrderTargetSelectionPlanProblem
{
    /// <summary>Nothing is selected.</summary>
    EmptySelection,

    /// <summary>Every selected feature is loaded and of an asset type the order type does not target.</summary>
    NoEligibleFeature,

    /// <summary>
    /// The selection is not one fence's result and has more ids than one identifier list may carry
    /// (<c>IdentifierListSelectionStrategy.MaxDistinctValues</c>).
    /// </summary>
    TooManyIdentifiers,
}
