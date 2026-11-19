namespace GeoAssets.Workflow.SlaGuard;

/// <summary>Read-only query surface a dashboard host calls (XD01-233) — no write paths.</summary>
public interface ISlaBreachQueryService
{
    Task<SlaBreachSummary> GetLiveSummaryAsync(CancellationToken ct = default);
}
