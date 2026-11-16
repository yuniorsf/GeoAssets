using FluentAssertions;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.TestKit;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// Runs the shared <see cref="PoleOccupancyStoreContractTests"/> against
/// <see cref="EFPoleOccupancyRepository"/> on SQLite. See
/// <see cref="PostgresPoleOccupancyRepositoryContractTests"/> for the same suite on PostgreSQL.
/// </summary>
public sealed class EFPoleOccupancyRepositoryContractTests : PoleOccupancyStoreContractTests
{
    private static readonly Guid Owner = PoleOccupancyEntryBuilder.DefaultPoleOwnerOrganizationId;

    protected override Task<PoleOccupancyStoreContractHarness> CreateHarnessAsync()
    {
        var scope = new DisposableGroup();
        var fixture = scope.Add(new SqliteFixture(new FakeTenantContext(Owner)));
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));
        var store = new EFPoleOccupancyRepository(fixture.Context, clock);

        return Task.FromResult(new PoleOccupancyStoreContractHarness(
            store,
            store,
            Owner,
            clock,
            organizationId =>
            {
                var view = new EFPoleOccupancyRepository(scope.Add(fixture.NewContext(new FakeTenantContext(organizationId))), clock);
                return new PoleOccupancyStoreView(view, view);
            },
            scope));
    }

    /// <summary>
    /// Pins what this class exists to prove — the suite runs against the EF repository itself,
    /// through both of its ports — and, being declared directly on the class, keeps it public
    /// and discoverable by xUnit (see <see cref="TestClassDiscoverabilityTests"/>).
    /// </summary>
    [Fact]
    public async Task CreateHarnessAsync_ExposesOneEfRepositoryThroughBothPorts()
    {
        await using var harness = await CreateHarnessAsync();

        harness.Reader.Should().BeOfType<EFPoleOccupancyRepository>();
        harness.Writer.Should().BeSameAs(harness.Reader);
        harness.AsOrganization(OtherOrganizationId).Reader.Should().BeOfType<EFPoleOccupancyRepository>().And.NotBeSameAs(harness.Reader);
    }
}
