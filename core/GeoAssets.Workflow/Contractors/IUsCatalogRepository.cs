namespace GeoAssets.Workflow.Contractors;

/// <summary>
/// Persistence abstraction for <see cref="UsCatalogItem"/>. Scoped to the caller's own tenant
/// the same way <see cref="IContractorUsLedgerRepository"/> is (XD01-202) — equality-only Row
/// Level Security on <c>OrganizationId</c>, no cross-contractor grant exception.
/// </summary>
public interface IUsCatalogRepository
{
    Task<UsCatalogItem?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// The catalog version of <paramref name="itemCode"/> in effect at <paramref name="asOfUtc"/>
    /// for <paramref name="organizationId"/> — null when no version was effective at that
    /// instant (before the item's first version, or after a version with no successor).
    /// </summary>
    Task<UsCatalogItem?> FindEffectiveAsync(Guid organizationId, string itemCode, DateTime asOfUtc, CancellationToken ct = default);

    /// <summary>Every version (all item codes, all effective date ranges) for one concessionaire/contract.</summary>
    Task<IReadOnlyList<UsCatalogItem>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default);

    Task AddAsync(UsCatalogItem item, CancellationToken ct = default);
    Task UpdateAsync(UsCatalogItem item, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
