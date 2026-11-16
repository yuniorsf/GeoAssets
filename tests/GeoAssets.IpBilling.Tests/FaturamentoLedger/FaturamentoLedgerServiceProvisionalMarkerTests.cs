using FluentAssertions;
using GeoAssets.IpBilling.FaturamentoLedger;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Tests.Calculation;
using Xunit;

namespace GeoAssets.IpBilling.Tests.FaturamentoLedger;

/// <summary>
/// VAL-B9 (XD01-301): <see cref="FaturamentoLedgerService"/> forwards the provisional marker on
/// an inspection delta and never sets it on a Levantamento baseline.
/// </summary>
public class FaturamentoLedgerServiceProvisionalMarkerTests
{
    private static readonly Guid MunicipioId = Guid.NewGuid();
    private static readonly DateTimeOffset PostedAt = new(2027, 2, 3, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AccumulateInspectionDeltaAsync_VAL_B9_ForwardsIsProvisional(bool isProvisional)
    {
        var repo = new InMemoryFaturamentoRepository();
        var service = new FaturamentoLedgerService(repo);

        await service.AccumulateInspectionDeltaAsync("order-1", MunicipioId, "2027-01", 12.5m, PostedAt, isProvisional);

        var posting = repo.Postings.Should().ContainSingle().Which;
        posting.IsProvisional.Should().Be(isProvisional);
        posting.Mode.Should().Be(FaturamentoPostingMode.Accumulate);
    }

    [Fact]
    public async Task AccumulateInspectionDeltaAsync_VAL_B9_DefaultsToNotProvisional()
    {
        var repo = new InMemoryFaturamentoRepository();

        await new FaturamentoLedgerService(repo).AccumulateInspectionDeltaAsync("order-1", MunicipioId, "2027-01", 12.5m, PostedAt);

        repo.Postings.Should().ContainSingle().Which.IsProvisional.Should().BeFalse();
    }

    [Fact]
    public async Task SetLevantamentoBaselineAsync_VAL_B9_IsNeverProvisional()
    {
        var repo = new InMemoryFaturamentoRepository();

        await new FaturamentoLedgerService(repo).SetLevantamentoBaselineAsync("lev-1", MunicipioId, "2027-01", 900m, PostedAt);

        var posting = repo.Postings.Should().ContainSingle().Which;
        posting.IsProvisional.Should().BeFalse();
        posting.Mode.Should().Be(FaturamentoPostingMode.Set);
    }
}
