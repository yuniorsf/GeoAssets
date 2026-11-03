using FluentAssertions;
using GeoAssets.IpBilling.FaturamentoLedger;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;
using Xunit;

namespace GeoAssets.IpBilling.Tests.FaturamentoLedger;

public class FaturamentoManualEntryServiceTests
{
    private static readonly Guid MunicipioId = Guid.NewGuid();
    private const string Competencia = "2026-03";

    private sealed class FakeFaturamentoRepository : IFaturamentoRepository
    {
        private readonly Dictionary<(Guid, string), Faturamento> _ledger = [];
        public int SaveChangesCallCount { get; private set; }

        public Task<Faturamento?> GetByMunicipioAndCompetenciaAsync(Guid municipioId, string competencia, CancellationToken ct = default) =>
            Task.FromResult(_ledger.TryGetValue((municipioId, competencia), out var f) ? f : null);

        public Task<IReadOnlyList<Faturamento>> GetByMunicipioAsync(Guid municipioId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Faturamento>>(_ledger.Values.Where(f => f.MunicipioId == municipioId).ToList());

        public Task AddAsync(Faturamento faturamento, CancellationToken ct = default)
        {
            _ledger[(faturamento.MunicipioId, faturamento.Competencia)] = faturamento;
            return Task.CompletedTask;
        }

        public Task<Faturamento> PostOrderCalculationAsync(
            string serviceOrderId, Guid municipioId, string competencia, decimal kwhDeltaOrBaseline,
            FaturamentoPostingMode mode, DateTimeOffset postedAt, CancellationToken ct = default) =>
            throw new NotSupportedException("Not exercised by manual-entry/batch-load/recarga tests.");

        public Task<Faturamento> OverwriteManualEntryAsync(
            Guid municipioId, string competencia, decimal kwhFaturado, string enteredByReference,
            DateTimeOffset postedAt, CancellationToken ct = default)
        {
            var faturamento = new Faturamento
            {
                MunicipioId = municipioId,
                Competencia = competencia,
                KwhFaturado = kwhFaturado,
                CreatedAt = postedAt,
            };
            _ledger[(municipioId, competencia)] = faturamento;
            return Task.FromResult(faturamento);
        }

        public Task<IReadOnlyList<FaturamentoPosting>> GetPostingsByOrderAsync(string serviceOrderId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FaturamentoPosting>>([]);

        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.CompletedTask;
        }
    }

    private static FaturamentoManualEntryService CreateService(FakeFaturamentoRepository repo) =>
        new(repo, new FaturamentoLedgerService(repo), TimeProvider.System);

    [Fact]
    public async Task ManualEntryAsync_NewPair_Inserts()
    {
        var repo = new FakeFaturamentoRepository();
        var service = CreateService(repo);

        var result = await service.ManualEntryAsync(MunicipioId, Competencia, 500m, "operator-1");

        result.KwhFaturado.Should().Be(500m);
        repo.SaveChangesCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ManualEntryAsync_ExistingPair_ThrowsAlreadyPosted()
    {
        var repo = new FakeFaturamentoRepository();
        var service = CreateService(repo);
        await service.ManualEntryAsync(MunicipioId, Competencia, 500m, "operator-1");

        var act = () => service.ManualEntryAsync(MunicipioId, Competencia, 999m, "operator-2");

        await act.Should().ThrowAsync<FaturamentoAlreadyPostedException>();
    }

    [Fact]
    public async Task BatchLoadAsync_MixOfNewAndExistingPairs_InsertsNewAndSkipsExisting()
    {
        var repo = new FakeFaturamentoRepository();
        var service = CreateService(repo);
        var existingMunicipio = Guid.NewGuid();
        await service.ManualEntryAsync(existingMunicipio, Competencia, 100m, "seed");

        var newMunicipio = Guid.NewGuid();
        var result = await service.BatchLoadAsync(
            [
                new FaturamentoBatchItem(newMunicipio, Competencia, 200m),
                new FaturamentoBatchItem(existingMunicipio, Competencia, 999m),
            ],
            "batch-operator");

        result.Applied.Should().ContainSingle(f => f.MunicipioId == newMunicipio && f.KwhFaturado == 200m);
        result.Skipped.Should().ContainSingle(s => s.MunicipioId == existingMunicipio && s.Reason == "Faturamento já cadastrado");

        // The existing pair's value must be untouched by the skip.
        var untouched = await repo.GetByMunicipioAndCompetenciaAsync(existingMunicipio, Competencia);
        untouched!.KwhFaturado.Should().Be(100m);
    }

    [Fact]
    public async Task RecargaAsync_MixOfExistingAndMissingPairs_UpdatesExistingAndSkipsMissing()
    {
        var repo = new FakeFaturamentoRepository();
        var service = CreateService(repo);
        var existingMunicipio = Guid.NewGuid();
        await service.ManualEntryAsync(existingMunicipio, Competencia, 100m, "seed");

        var missingMunicipio = Guid.NewGuid();
        var result = await service.RecargaAsync(
            [
                new FaturamentoBatchItem(existingMunicipio, Competencia, 777m),
                new FaturamentoBatchItem(missingMunicipio, Competencia, 200m),
            ],
            "recarga-operator");

        result.Applied.Should().ContainSingle(f => f.MunicipioId == existingMunicipio && f.KwhFaturado == 777m);
        result.Skipped.Should().ContainSingle(s => s.MunicipioId == missingMunicipio && s.Reason == "Faturamento inexistente");

        // The missing pair must not have been created by the skip.
        var stillMissing = await repo.GetByMunicipioAndCompetenciaAsync(missingMunicipio, Competencia);
        stillMissing.Should().BeNull();
    }
}
