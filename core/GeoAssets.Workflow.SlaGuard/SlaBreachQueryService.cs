using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.SlaGuard;

/// <summary>
/// Read-side projection (XD01-233) over whatever <see cref="SlaGuardEngine.AttachDeadline"/> has
/// already stamped onto live <see cref="IServiceOrder"/> attributes — no new persisted state,
/// derived entirely from the existing attributes-bag convention.
///
/// <paramref name="engine"/> is nullable because no host in this repo registers one with a real
/// regulatory matrix yet (see <see cref="SlaMatrixRow"/>'s own doc comment on why this engine
/// never ships with a hardcoded default matrix). Until a host does, every query reports
/// <see cref="SlaBreachSummary.NotConfigured"/> rather than silently returning an empty result
/// that would look identical to "checked and clean."
///
/// An order whose stamped (category, setting) pair isn't covered by <paramref name="engine"/>'s
/// configured matrix is skipped (can't be evaluated), not counted as a false breach or a false
/// clean — <see cref="SlaGuardEngine.ComputeDeadline"/> throws for exactly this case.
/// </summary>
public sealed class SlaBreachQueryService(
    IServiceOrderReader orders,
    SlaGuardEngine? engine,
    TimeProvider timeProvider) : ISlaBreachQueryService
{
    public async Task<SlaBreachSummary> GetLiveSummaryAsync(CancellationToken ct = default)
    {
        if (engine is null)
            return SlaBreachSummary.NotConfigured;

        var all = await orders.GetAllAsync(ct).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        var breaches = new List<SlaBreachEvent>();
        var byCategory = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var order in all)
        {
            if (!order.Attributes.TryGetValue(SlaGuardEngine.ServiceCategoryAttributeKey, out var category) ||
                !order.Attributes.TryGetValue(SlaGuardEngine.TerritorialSettingAttributeKey, out var setting))
                continue;

            var startedAt = new DateTimeOffset(DateTime.SpecifyKind(order.CreatedAt, DateTimeKind.Utc));

            SlaBreachEvent? breach;
            try
            {
                breach = engine.CheckBreach(order.Id, category, setting, startedAt, now);
            }
            catch (KeyNotFoundException)
            {
                continue;
            }

            if (breach is null)
                continue;

            breaches.Add(breach);
            byCategory[category] = byCategory.GetValueOrDefault(category) + 1;
        }

        return new SlaBreachSummary(IsEngineConfigured: true, breaches.Count, byCategory, breaches);
    }
}
