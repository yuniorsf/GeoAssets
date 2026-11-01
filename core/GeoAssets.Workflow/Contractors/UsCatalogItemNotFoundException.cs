namespace GeoAssets.Workflow.Contractors;

/// <summary>
/// Thrown by <see cref="OrderRemunerationCalculator"/> when a BoQ line's item code has no
/// catalog version in effect for the given organization/instant (XD01-247) — silently skipping
/// the line would under-report the order's remuneration, so this fails loud instead.
/// </summary>
public sealed class UsCatalogItemNotFoundException : InvalidOperationException
{
    public Guid OrganizationId { get; }
    public string ItemCode { get; } = string.Empty;
    public DateTime AsOfUtc { get; }

    public UsCatalogItemNotFoundException(Guid organizationId, string itemCode, DateTime asOfUtc)
        : base($"No US catalog item '{itemCode}' is in effect for organization '{organizationId}' at {asOfUtc:O}.")
    {
        OrganizationId = organizationId;
        ItemCode = itemCode;
        AsOfUtc = asOfUtc;
    }

    public UsCatalogItemNotFoundException()
    {
    }

    public UsCatalogItemNotFoundException(string message) : base(message)
    {
    }

    public UsCatalogItemNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
