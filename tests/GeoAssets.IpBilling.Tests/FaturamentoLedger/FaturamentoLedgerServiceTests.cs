using FluentAssertions;
using GeoAssets.IpBilling.FaturamentoLedger;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;
using Xunit;

namespace GeoAssets.IpBilling.Tests.FaturamentoLedger;

public class FaturamentoLedgerServiceTests
{
    private static readonly Guid MunicipioId = Guid.NewGuid();
    private const string Competencia = "2026-03";
    private static readonly DateTimeOffset PostedAt = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeFaturamentoRepository : IFaturamentoRepository
    {
        private readonly Dictionary<(Guid, string), GeoAssets.IpBilling.Models.Faturamento> _ledger = [];
        private readonly HashSet<(string ServiceOrderId, string Competencia)> _posted = [];

        public Task<GeoAssets.IpBilling.Models.Faturamento?> GetByMunicipioAndCompetenciaAsync(Guid municipioId, string competencia, CancellationToken ct = default) =>
            Task.FromResult(_ledger.TryGetValue((municipioId, competencia), out var f) ? f : null);

        public Task<IReadOnlyList<GeoAssets.IpBilling.Models.Faturamento>> GetByMunicipioAsync(Guid municipioId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeoAssets.IpBilling.Models.Faturamento>>(
                _ledger.Values.Where(f => f.MunicipioId == municipioId).ToList());

        public Task AddAsync(GeoAssets.IpBilling.Models.Faturamento faturamento, CancellationToken ct = default)
        {
            _ledger[(faturamento.MunicipioId, faturamento.Competencia)] = faturamento;
            return Task.CompletedTask;
        }

        public Task<GeoAssets.IpBilling.Models.Faturamento> PostOrderCalculationAsync(
            string serviceOrderId, Guid municipioId, string competencia, decimal kwhDeltaOrBaseline,
            FaturamentoPostingMode mode, DateTimeOffset postedAt, CancellationToken ct = default)
        {
            var key = (serviceOrderId, competencia);
            if (_posted.Contains(key) && _ledger.TryGetValue((municipioId, competencia), out var existing))
            {
                return Task.FromResult(existing);
            }

            if (!_ledger.TryGetValue((municipioId, competencia), out var faturamento))
            {
                faturamento = new GeoAssets.IpBilling.Models.Faturamento
                {
                    MunicipioId = municipioId,
                    Competencia = competencia,
                    KwhFaturado = kwhDeltaOrBaseline,
                    CreatedAt = postedAt,
                };
                _ledger[(municipioId, competencia)] = faturamento;
            }
            else
            {
                faturamento.KwhFaturado = mode == FaturamentoPostingMode.Accumulate
                    ? faturamento.KwhFaturado + kwhDeltaOrBaseline
                    : kwhDeltaOrBaseline;
            }

            _posted.Add(key);
            return Task.FromResult(faturamento);
        }

        public Task<GeoAssets.IpBilling.Models.Faturamento> OverwriteManualEntryAsync(
            Guid municipioId, string competencia, decimal kwhFaturado, string enteredByReference,
            DateTimeOffset postedAt, CancellationToken ct = default)
        {
            if (!_ledger.TryGetValue((municipioId, competencia), out var faturamento))
            {
                faturamento = new GeoAssets.IpBilling.Models.Faturamento
                {
                    MunicipioId = municipioId,
                    Competencia = competencia,
                    KwhFaturado = kwhFaturado,
                    CreatedAt = postedAt,
                };
                _ledger[(municipioId, competencia)] = faturamento;
            }
            else
            {
                faturamento.KwhFaturado = kwhFaturado;
            }

            return Task.FromResult(faturamento);
        }

        public Task<IReadOnlyList<FaturamentoPosting>> GetPostingsByOrderAsync(string serviceOrderId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FaturamentoPosting>>([]);

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task SetLevantamentoBaselineAsync_CalledTwiceWithDifferentOrders_SecondReplacesTheFirst()
    {
        var repo = new FakeFaturamentoRepository();
        var service = new FaturamentoLedgerService(repo);

        var first = await service.SetLevantamentoBaselineAsync("order-lev-jan", MunicipioId, Competencia, 1000m, PostedAt);
        first.KwhFaturado.Should().Be(1000m);

        // A second, DIFFERENT Levantamento validation for the same competência (e.g. a
        // corrected re-survey) must REPLACE the baseline, not add to it.
        var second = await service.SetLevantamentoBaselineAsync("order-lev-feb-correction", MunicipioId, Competencia, 750m, PostedAt.AddDays(1));
        second.KwhFaturado.Should().Be(750m, "a second Levantamento validation SETs (replaces) the baseline, it never accumulates");
    }

    [Fact]
    public async Task AccumulateInspectionDeltaAsync_CalledTwiceWithDifferentOrders_SecondAddsToTheFirst()
    {
        var repo = new FakeFaturamentoRepository();
        var service = new FaturamentoLedgerService(repo);

        await service.SetLevantamentoBaselineAsync("order-lev", MunicipioId, Competencia, 1000m, PostedAt);

        var afterFirstDelta = await service.AccumulateInspectionDeltaAsync("order-fisc-1", MunicipioId, Competencia, 100m, PostedAt.AddHours(1));
        afterFirstDelta.KwhFaturado.Should().Be(1100m);

        // A second, DIFFERENT Fiscalização/Ofício validation for the same competência must ADD
        // to the running total, not replace it.
        var afterSecondDelta = await service.AccumulateInspectionDeltaAsync("order-ofic-1", MunicipioId, Competencia, 50m, PostedAt.AddHours(2));
        afterSecondDelta.KwhFaturado.Should().Be(1150m, "a second Fiscalização/Ofício validation ACCUMULATES (adds to) the running total, it never replaces it");
    }

    [Fact]
    public async Task AccumulateInspectionDeltaAsync_RetryOfSameOrder_IsIdempotentAndDoesNotDoublePost()
    {
        var repo = new FakeFaturamentoRepository();
        var service = new FaturamentoLedgerService(repo);

        await service.SetLevantamentoBaselineAsync("order-lev", MunicipioId, Competencia, 1000m, PostedAt);

        var firstCall = await service.AccumulateInspectionDeltaAsync("order-fisc-1", MunicipioId, Competencia, 100m, PostedAt.AddHours(1));
        var retrySameOrder = await service.AccumulateInspectionDeltaAsync("order-fisc-1", MunicipioId, Competencia, 100m, PostedAt.AddHours(2));

        retrySameOrder.KwhFaturado.Should().Be(firstCall.KwhFaturado, "retrying the same order's accumulate posting must not double-count it");
    }

    [Fact]
    public async Task OverwriteManualEntryAsync_IgnoresExistingBaselineAndAccumulatedValue()
    {
        var repo = new FakeFaturamentoRepository();
        var service = new FaturamentoLedgerService(repo);

        await service.SetLevantamentoBaselineAsync("order-lev", MunicipioId, Competencia, 1000m, PostedAt);
        await service.AccumulateInspectionDeltaAsync("order-fisc-1", MunicipioId, Competencia, 200m, PostedAt.AddHours(1));

        var overwritten = await service.OverwriteManualEntryAsync(
            MunicipioId, Competencia, 42.5m, "manual-correction-ref-1", PostedAt.AddDays(1));

        overwritten.KwhFaturado.Should().Be(42.5m, "a manual entry overwrites the external ground-truth figure outright, ignoring whatever was there before");
    }
}
