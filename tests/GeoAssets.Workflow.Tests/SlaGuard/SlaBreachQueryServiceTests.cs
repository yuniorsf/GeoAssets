using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.SlaGuard;
using GeoAssets.Workflow.Tests.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.SlaGuard;

public sealed class SlaBreachQueryServiceTests
{
    private static readonly SlaMatrixRow[] TestMatrix =
    [
        new("EmergencyRestoration", "Urban", TimeSpan.FromHours(4)),
        new("UrgentReconnection", "Rural", TimeSpan.FromHours(24)),
    ];

    private static ServiceOrder OrderWithSla(string id, string category, string setting, DateTime createdAtUtc)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SlaGuardEngine.ServiceCategoryAttributeKey] = category,
            [SlaGuardEngine.TerritorialSettingAttributeKey] = setting,
        };

        return new ServiceOrder { Id = id, CreatedAt = createdAtUtc, Attributes = attributes };
    }

    [Fact]
    public async Task GetLiveSummaryAsync_WithNoEngineConfigured_ReturnsNotConfigured()
    {
        var repo = new FakeServiceOrderRepository();
        var sut = new SlaBreachQueryService(repo, engine: null, TimeProvider.System);

        var summary = await sut.GetLiveSummaryAsync();

        Assert.Equal(SlaBreachSummary.NotConfigured, summary);
        Assert.False(summary.IsEngineConfigured);
    }

    [Fact]
    public async Task GetLiveSummaryAsync_CountsOnlyOrdersPastTheirDeadline()
    {
        var now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var repo = new FakeServiceOrderRepository();

        // Breached: created 5h ago, 4h max duration for EmergencyRestoration/Urban.
        await repo.AddAsync(OrderWithSla("o1", "EmergencyRestoration", "Urban", now.AddHours(-5).UtcDateTime));
        // Not breached: created 1h ago, same category/setting.
        await repo.AddAsync(OrderWithSla("o2", "EmergencyRestoration", "Urban", now.AddHours(-1).UtcDateTime));
        // Breached: created 25h ago, 24h max duration for UrgentReconnection/Rural.
        await repo.AddAsync(OrderWithSla("o3", "UrgentReconnection", "Rural", now.AddHours(-25).UtcDateTime));
        // No SLA attributes at all — must be skipped silently, not counted either way.
        await repo.AddAsync(new ServiceOrder { Id = "o4", CreatedAt = now.AddHours(-100).UtcDateTime });

        var engine = new SlaGuardEngine(TestMatrix);
        var sut = new SlaBreachQueryService(repo, engine, clock);

        var summary = await sut.GetLiveSummaryAsync();

        Assert.True(summary.IsEngineConfigured);
        Assert.Equal(2, summary.TotalBreaches);
        Assert.Equal(1, summary.BreachesByServiceCategory["EmergencyRestoration"]);
        Assert.Equal(1, summary.BreachesByServiceCategory["UrgentReconnection"]);
        Assert.DoesNotContain("o2", summary.Breaches.Select(b => b.OrderId));
    }

    [Fact]
    public async Task GetLiveSummaryAsync_SkipsOrdersWithAnUnconfiguredMatrixCombination()
    {
        var now = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var repo = new FakeServiceOrderRepository();

        // Stamped category/setting combination has no matching SlaMatrixRow.
        await repo.AddAsync(OrderWithSla("o1", "SurgeInspection", "Urban", now.AddHours(-1000).UtcDateTime));

        var engine = new SlaGuardEngine(TestMatrix);
        var sut = new SlaBreachQueryService(repo, engine, clock);

        var summary = await sut.GetLiveSummaryAsync();

        Assert.True(summary.IsEngineConfigured);
        Assert.Equal(0, summary.TotalBreaches);
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
