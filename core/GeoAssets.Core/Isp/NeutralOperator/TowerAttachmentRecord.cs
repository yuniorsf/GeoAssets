namespace GeoAssets.Core.Isp.NeutralOperator;

/// <summary>
/// One recorded piece of equipment mounted on a wireless tower (XD01-266): an elevation
/// bracket, antenna radome, microwave dish, or power-metering point. Unlike
/// <see cref="PoleAttachmentSlot"/>'s fixed 1-6 slot index, a tower has no slot-count
/// ceiling — <see cref="TowerAttachmentCatalog"/> tracks an open list keyed by
/// <see cref="RecordId"/> instead of a bounded index.
/// </summary>
public sealed class TowerAttachmentRecord
{
    /// <summary>Caller-assigned stable identifier for this record (e.g. an inventory tag).</summary>
    public required string RecordId { get; init; }

    public required TowerAttachmentKind Kind { get; init; }

    /// <summary>Mounting height above ground/base, in meters. Required for
    /// <see cref="TowerAttachmentKind.Bracket"/>; optional for other kinds.</summary>
    public decimal? ElevationMeters { get; set; }

    /// <summary>Inventory identifier (serial/model) for <see cref="TowerAttachmentKind.Radome"/>
    /// or <see cref="TowerAttachmentKind.Dish"/> equipment.</summary>
    public string? InventoryIdentifier { get; set; }

    /// <summary>Metered power draw, in watts. Required for <see cref="TowerAttachmentKind.PowerMeter"/>.</summary>
    public decimal? MeteredPowerDrawWatts { get; set; }

    /// <summary>CNPJ of the attaching operator, when known — mirrors <see cref="PoleAttachmentSlot.OperatorCnpj"/>.</summary>
    public string? OperatorCnpj { get; set; }

    /// <summary>The attached equipment/cable <c>GeoFeature.Id</c>, when this record corresponds
    /// to a feature in the asset graph.</summary>
    public string? AttachedFeatureId { get; set; }
}
