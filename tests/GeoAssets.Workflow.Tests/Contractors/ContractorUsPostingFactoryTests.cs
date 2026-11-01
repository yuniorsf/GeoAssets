using FluentAssertions;
using GeoAssets.Workflow.Contractors;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests.Contractors;

/// <summary>
/// XD01-242's AC: a posting is an immutable, append-only snapshot of a <see cref="UsCatalogItem"/>'s
/// figures at posting time, with <see cref="ContractorUsPosting.TotalUs"/>/<see cref="ContractorUsPosting.TotalBrl"/>
/// computed from that snapshot.
/// </summary>
public class ContractorUsPostingFactoryTests
{
    private static readonly Guid ContractorOrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static UsCatalogItem CatalogItem(decimal baseUsFactor = 2.5m, decimal regionalMultiplier = 1.2m, decimal monetaryConversionRate = 85.00m) => new()
    {
        OrganizationId = ContractorOrganizationId,
        ItemCode = "US-EL-014",
        Description = "Troca de poste",
        Unit = "un",
        BaseUsFactor = baseUsFactor,
        RegionalMultiplier = regionalMultiplier,
        MonetaryConversionRate = monetaryConversionRate,
        EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void Post_SnapshotsUnitUsAndMonetaryRateFromTheCatalogItem()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
        var catalogItem = CatalogItem();

        var posting = ContractorUsPostingFactory.Post("order-1", ContractorOrganizationId, catalogItem, quantity: 10m, timeProvider);

        posting.ServiceOrderId.Should().Be("order-1");
        posting.ContractorOrganizationId.Should().Be(ContractorOrganizationId);
        posting.ItemCode.Should().Be("US-EL-014");
        posting.Quantity.Should().Be(10m);
        posting.UnitUs.Should().Be(2.5m * 1.2m);
        posting.MonetaryRate.Should().Be(85.00m);
        posting.PostedAtUtc.Should().Be(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Post_TotalUs_IsQuantityTimesUnitUs()
    {
        var catalogItem = CatalogItem(baseUsFactor: 2.5m, regionalMultiplier: 1.2m);

        var posting = ContractorUsPostingFactory.Post("order-1", ContractorOrganizationId, catalogItem, quantity: 10m, new FakeTimeProvider());

        posting.TotalUs.Should().Be(10m * 2.5m * 1.2m);
    }

    [Fact]
    public void Post_TotalBrl_IsTotalUsTimesMonetaryRate()
    {
        var catalogItem = CatalogItem(baseUsFactor: 2.5m, regionalMultiplier: 1.2m, monetaryConversionRate: 85.00m);

        var posting = ContractorUsPostingFactory.Post("order-1", ContractorOrganizationId, catalogItem, quantity: 10m, new FakeTimeProvider());

        posting.TotalBrl.Should().Be(10m * 2.5m * 1.2m * 85.00m);
    }

    [Fact]
    public void Post_AssignsAFreshIdOnEveryCall()
    {
        var catalogItem = CatalogItem();
        var timeProvider = new FakeTimeProvider();

        var first = ContractorUsPostingFactory.Post("order-1", ContractorOrganizationId, catalogItem, quantity: 1m, timeProvider);
        var second = ContractorUsPostingFactory.Post("order-1", ContractorOrganizationId, catalogItem, quantity: 1m, timeProvider);

        first.Id.Should().NotBe(second.Id);
    }

    [Fact]
    public void Post_NullCatalogItem_Throws()
    {
        var act = () => ContractorUsPostingFactory.Post("order-1", ContractorOrganizationId, null!, quantity: 1m, new FakeTimeProvider());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Post_NullTimeProvider_Throws()
    {
        var act = () => ContractorUsPostingFactory.Post("order-1", ContractorOrganizationId, CatalogItem(), quantity: 1m, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Post_BlankServiceOrderId_Throws()
    {
        var act = () => ContractorUsPostingFactory.Post(" ", ContractorOrganizationId, CatalogItem(), quantity: 1m, new FakeTimeProvider());

        act.Should().Throw<ArgumentException>();
    }
}
