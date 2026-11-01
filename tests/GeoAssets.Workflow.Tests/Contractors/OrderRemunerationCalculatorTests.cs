using FluentAssertions;
using GeoAssets.Workflow.Contractors;
using Xunit;

namespace GeoAssets.Workflow.Tests.Contractors;

/// <summary>
/// XD01-247's AC: "Calculation matches the doc's worked example (US-EL-014 × 1 → 185.50 US →
/// R$714.18 at the given rate) exactly", multi-item aggregation, and a pluggable
/// per-organization catalog lookup so CEMIG/CPFL rates never collide.
/// </summary>
public class OrderRemunerationCalculatorTests
{
    private static readonly Guid Cemig = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Cpfl = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime AsOfUtc = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private static UsCatalogItem Item(
        Guid organizationId, string itemCode, decimal baseUsFactor, decimal regionalMultiplier, decimal monetaryConversionRate) => new()
        {
            OrganizationId = organizationId,
            ItemCode = itemCode,
            BaseUsFactor = baseUsFactor,
            RegionalMultiplier = regionalMultiplier,
            MonetaryConversionRate = monetaryConversionRate,
            EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

    private sealed class FakeUsCatalogRepository : IUsCatalogRepository
    {
        private readonly List<UsCatalogItem> _items = [];

        public void Seed(UsCatalogItem item) => _items.Add(item);

        public Task<UsCatalogItem?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_items.Find(i => i.Id == id));

        public Task<UsCatalogItem?> FindEffectiveAsync(Guid organizationId, string itemCode, DateTime asOfUtc, CancellationToken ct = default) =>
            Task.FromResult(_items.Find(i =>
                i.OrganizationId == organizationId
                && string.Equals(i.ItemCode, itemCode, StringComparison.Ordinal)
                && i.EffectiveFrom <= asOfUtc
                && (i.EffectiveTo is null || i.EffectiveTo > asOfUtc)));

        public Task<IReadOnlyList<UsCatalogItem>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<UsCatalogItem>>(_items.FindAll(i => i.OrganizationId == organizationId));

        public Task AddAsync(UsCatalogItem item, CancellationToken ct = default)
        {
            _items.Add(item);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(UsCatalogItem item, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            _items.RemoveAll(i => i.Id == id);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    // ── Doc's worked example ─────────────────────────────────────────────────

    [Fact]
    public async Task CalculateAsync_DocWorkedExample_MatchesExactly()
    {
        var repo = new FakeUsCatalogRepository();
        repo.Seed(Item(Cemig, "US-EL-014", baseUsFactor: 185.50m, regionalMultiplier: 1m, monetaryConversionRate: 3.85m));
        var calculator = new OrderRemunerationCalculator(repo);

        var result = await calculator.CalculateAsync(Cemig, [("US-EL-014", 1m)], AsOfUtc);

        result.TotalUs.Should().Be(185.50m);
        result.TotalBrl.Should().Be(714.18m);
    }

    // ── Multi-item aggregation ────────────────────────────────────────────────

    [Fact]
    public async Task CalculateAsync_MultiItemOrder_SumsPerItemAndAggregateTotalsCorrectly()
    {
        var repo = new FakeUsCatalogRepository();
        repo.Seed(Item(Cemig, "US-EL-014", baseUsFactor: 185.50m, regionalMultiplier: 1m, monetaryConversionRate: 3.85m)); // pole replacement
        repo.Seed(Item(Cemig, "US-EL-022", baseUsFactor: 42.00m, regionalMultiplier: 1.1m, monetaryConversionRate: 3.85m)); // conductor run
        var calculator = new OrderRemunerationCalculator(repo);

        var result = await calculator.CalculateAsync(Cemig, [("US-EL-014", 2m), ("US-EL-022", 3m)], AsOfUtc);

        var poleLineUs = 2m * 185.50m * 1m;
        var conductorLineUs = 3m * 42.00m * 1.1m;
        var expectedTotalUs = poleLineUs + conductorLineUs;

        result.TotalUs.Should().Be(expectedTotalUs);
        result.TotalBrl.Should().Be(Math.Round(expectedTotalUs * 3.85m, 2, MidpointRounding.AwayFromZero));
        result.Lines.Should().HaveCount(2);
        result.Lines[0].LineUs.Should().Be(poleLineUs);
        result.Lines[1].LineUs.Should().Be(conductorLineUs);
    }

    // ── Pluggable per-organization lookup (no CEMIG/CPFL collision) ──────────

    [Fact]
    public async Task CalculateAsync_SameItemCodeUnderTwoOrganizations_UsesEachOwnFactorAndRate()
    {
        var repo = new FakeUsCatalogRepository();
        repo.Seed(Item(Cemig, "US-EL-014", baseUsFactor: 185.50m, regionalMultiplier: 1m, monetaryConversionRate: 3.85m));
        repo.Seed(Item(Cpfl, "US-EL-014", baseUsFactor: 200.00m, regionalMultiplier: 1m, monetaryConversionRate: 4.00m));
        var calculator = new OrderRemunerationCalculator(repo);

        var cemigResult = await calculator.CalculateAsync(Cemig, [("US-EL-014", 1m)], AsOfUtc);
        var cpflResult = await calculator.CalculateAsync(Cpfl, [("US-EL-014", 1m)], AsOfUtc);

        cemigResult.TotalUs.Should().Be(185.50m);
        cpflResult.TotalUs.Should().Be(200.00m);
        cemigResult.TotalBrl.Should().NotBe(cpflResult.TotalBrl);
    }

    // ── Missing catalog item ─────────────────────────────────────────────────

    [Fact]
    public Task CalculateAsync_ItemCodeNotInCatalog_Throws()
    {
        var repo = new FakeUsCatalogRepository();
        var calculator = new OrderRemunerationCalculator(repo);

        var act = async () => await calculator.CalculateAsync(Cemig, [("NO-SUCH-ITEM", 1m)], AsOfUtc);

        return act.Should().ThrowAsync<UsCatalogItemNotFoundException>();
    }

    [Fact]
    public Task CalculateAsync_ItemVersionNotYetEffective_Throws()
    {
        var repo = new FakeUsCatalogRepository();
        repo.Seed(Item(Cemig, "US-EL-014", baseUsFactor: 185.50m, regionalMultiplier: 1m, monetaryConversionRate: 3.85m));
        var calculator = new OrderRemunerationCalculator(repo);

        var act = async () => await calculator.CalculateAsync(Cemig, [("US-EL-014", 1m)], new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        return act.Should().ThrowAsync<UsCatalogItemNotFoundException>();
    }

    // ── Ambiguous rate guard ──────────────────────────────────────────────────

    [Fact]
    public Task CalculateAsync_LinesResolveToDifferentRates_Throws()
    {
        var repo = new FakeUsCatalogRepository();
        repo.Seed(Item(Cemig, "US-EL-014", baseUsFactor: 185.50m, regionalMultiplier: 1m, monetaryConversionRate: 3.85m));
        repo.Seed(Item(Cemig, "US-EL-022", baseUsFactor: 42.00m, regionalMultiplier: 1m, monetaryConversionRate: 4.10m));
        var calculator = new OrderRemunerationCalculator(repo);

        var act = async () => await calculator.CalculateAsync(Cemig, [("US-EL-014", 1m), ("US-EL-022", 1m)], AsOfUtc);

        return act.Should().ThrowAsync<AmbiguousFinancialConversionRateException>();
    }

    // ── Empty order ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CalculateAsync_NoLines_ReturnsZeroTotals()
    {
        var repo = new FakeUsCatalogRepository();
        var calculator = new OrderRemunerationCalculator(repo);

        var result = await calculator.CalculateAsync(Cemig, [], AsOfUtc);

        result.TotalUs.Should().Be(0);
        result.TotalBrl.Should().Be(0);
        result.Lines.Should().BeEmpty();
    }

    [Fact]
    public Task CalculateAsync_NullLines_Throws()
    {
        var repo = new FakeUsCatalogRepository();
        var calculator = new OrderRemunerationCalculator(repo);

        var act = async () => await calculator.CalculateAsync(Cemig, null!, AsOfUtc);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }
}
