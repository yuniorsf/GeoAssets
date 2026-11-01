using GeoAssets.Core.Interfaces;

namespace GeoAssets.Workflow.Contractors;

/// <summary>
/// One itemized Unidade de Serviço (US) catalog entry, versioned per concessionaire (XD01-241):
/// CEMIG, Neoenergia, CPFL, Sanepar each maintain their own divergent item codes/factors for the
/// same physical task, so <see cref="ItemCode"/> is only unique within one
/// <see cref="OrganizationId"/> (the concessionaire/contract this catalog version belongs to),
/// never globally — CEMIG's "US-EL-014" and a CPFL item that happens to reuse the same code are
/// two unrelated, independently versioned entries.
///
/// <see cref="EffectiveFrom"/>/<see cref="EffectiveTo"/> let the same <see cref="ItemCode"/>
/// have multiple historical versions over time (a factor revision) without losing the old
/// value for ledger entries claimed under it — <see cref="EffectiveTo"/> null means "currently
/// in effect".
/// </summary>
public sealed class UsCatalogItem : IOrgOwnedResource
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The concessionaire/contract this catalog version belongs to. See <see cref="IOrgOwnedResource"/>.</summary>
    public Guid OrganizationId { get; init; } = Guid.Empty;

    public string ItemCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    /// <summary>The item's base US factor — "FactorUS" in the TotalUS formula (<see cref="UsTotalCalculator"/>).</summary>
    public decimal BaseUsFactor { get; set; }

    /// <summary>Multiplies <see cref="BaseUsFactor"/> for this concessionaire's region/territory.</summary>
    public decimal RegionalMultiplier { get; set; } = 1m;

    /// <summary>Monetary value of one US under this catalog version, for converting a TotalUS figure to currency.</summary>
    public decimal MonetaryConversionRate { get; set; }

    public DateTime EffectiveFrom { get; init; }

    /// <summary>Null means this version is currently in effect.</summary>
    public DateTime? EffectiveTo { get; set; }
}
