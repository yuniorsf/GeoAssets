using GeoAssets.Core.Interfaces;

namespace GeoAssets.Workflow.Contractors;

/// <summary>
/// One itemized Unidade de Serviço (US) line item a contractor claims against a
/// ServiceOrder, evidenced by one or more geotagged photos (<see cref="PhotoProof"/>).
/// The core subdomain the "Glosa Crisis" archetype plan depends on (XD01-202).
/// </summary>
public sealed class ContractorUsLedgerEntry : IOrgOwnedResource
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The claiming contractor's own tenant. See <see cref="IOrgOwnedResource"/>.</summary>
    public Guid OrganizationId { get; init; } = Guid.Empty;

    /// <summary>The ServiceOrder this line item is claimed against, if any.</summary>
    public string? ServiceOrderId { get; init; }

    public string UsCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitValue { get; set; }
    public decimal TotalValue => Quantity * UnitValue;

    public List<LedgerPhotoProof> PhotoProof { get; init; } = [];

    public DateTime CreatedAt { get; init; } = DateTime.MinValue;
    public DateTime? UpdatedAt { get; set; }
}
