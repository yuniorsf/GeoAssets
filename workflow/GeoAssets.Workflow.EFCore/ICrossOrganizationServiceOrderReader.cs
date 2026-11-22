using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Persistence;

/// <summary>
/// The grant-aware cross-organization read path for a list (XD01-328): the orders other organizations own, fetched
/// explicitly by organization rather than by turning the tenant query filter or row-level security off for the context.
/// <see cref="EFServiceOrderRepository"/> implements it; a caller asks only for the organizations its caller's grants name
/// and decides each returned order with <see cref="Rules.ServiceOrderRules"/>.
/// </summary>
public interface ICrossOrganizationServiceOrderReader
{
    /// <summary>The orders owned by <paramref name="organizationIds"/>, the context's own organization and <see cref="Guid.Empty"/> excluded.</summary>
    Task<IReadOnlyList<IServiceOrder>> GetOfOtherOrganizationsAsync(IReadOnlyCollection<Guid> organizationIds, CancellationToken ct = default);
}
