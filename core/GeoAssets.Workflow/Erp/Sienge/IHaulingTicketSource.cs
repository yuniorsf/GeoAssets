namespace GeoAssets.Workflow.Erp.Sienge;

/// <summary>Host-side source of dump-truck hauling tickets to sync to Sienge (XD01-254).</summary>
public interface IHaulingTicketSource
{
    Task<IReadOnlyList<HaulingTicketRecord>> GetHaulingTicketsSinceAsync(DateTime sinceUtc, CancellationToken ct = default);
}
