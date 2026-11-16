using GeoAssets.Core.Models;
using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.FaturamentoLedger;
using GeoAssets.IpBilling.Levantamento;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;

namespace GeoAssets.IpBilling.Tests.Calculation;

/// <summary>Ledger + postings with the same dedup rule as <c>EFFaturamentoRepository</c>.</summary>
internal sealed class InMemoryFaturamentoRepository : IFaturamentoRepository
{
    private readonly Dictionary<(Guid MunicipioId, string Competencia), Faturamento> _ledger = [];

    public List<FaturamentoPosting> Postings { get; } = [];
    public int PostCalls { get; private set; }
    public int SaveCalls { get; private set; }

    public void DropLedgerRow(Guid municipioId, string competencia) => _ledger.Remove((municipioId, competencia));

    public Task<Faturamento?> GetByMunicipioAndCompetenciaAsync(Guid municipioId, string competencia, CancellationToken ct = default) =>
        Task.FromResult(_ledger.GetValueOrDefault((municipioId, competencia)));

    public Task<IReadOnlyList<Faturamento>> GetByMunicipioAsync(Guid municipioId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Faturamento>>(_ledger.Values.Where(f => f.MunicipioId == municipioId).ToList());

    public Task AddAsync(Faturamento faturamento, CancellationToken ct = default)
    {
        _ledger[(faturamento.MunicipioId, faturamento.Competencia)] = faturamento;
        return Task.CompletedTask;
    }

    public Task<Faturamento> PostOrderCalculationAsync(
        string serviceOrderId, Guid municipioId, string competencia, decimal kwhDeltaOrBaseline,
        FaturamentoPostingMode mode, DateTimeOffset postedAt, bool isProvisional = false, CancellationToken ct = default)
    {
        PostCalls++;
        ct.ThrowIfCancellationRequested();

        if (Postings.Any(p => string.Equals(p.ServiceOrderId, serviceOrderId, StringComparison.Ordinal) &&
                              string.Equals(p.Competencia, competencia, StringComparison.Ordinal)) &&
            _ledger.TryGetValue((municipioId, competencia), out var existing))
        {
            return Task.FromResult(existing);
        }

        var rounded = IpEnergyMath.Round(kwhDeltaOrBaseline);
        if (!_ledger.TryGetValue((municipioId, competencia), out var faturamento))
        {
            faturamento = new Faturamento { MunicipioId = municipioId, Competencia = competencia, KwhFaturado = rounded, CreatedAt = postedAt };
            _ledger[(municipioId, competencia)] = faturamento;
        }
        else
        {
            faturamento.KwhFaturado = mode == FaturamentoPostingMode.Accumulate
                ? IpEnergyMath.Round(faturamento.KwhFaturado + rounded)
                : rounded;
        }

        Postings.Add(new FaturamentoPosting
        {
            ServiceOrderId = serviceOrderId,
            MunicipioId = municipioId,
            Competencia = competencia,
            Mode = mode,
            DeltaOrBaselineKwh = rounded,
            ResultingKwh = faturamento.KwhFaturado,
            PostedAt = postedAt,
            IsProvisional = isProvisional,
        });

        return Task.FromResult(faturamento);
    }

    public Task<Faturamento> OverwriteManualEntryAsync(
        Guid municipioId, string competencia, decimal kwhFaturado, string enteredByReference,
        DateTimeOffset postedAt, CancellationToken ct = default) =>
        throw new NotSupportedException("Manual entries are not exercised by the inspection posting tests.");

    public Task<IReadOnlyList<FaturamentoPosting>> GetPostingsByOrderAsync(string serviceOrderId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FaturamentoPosting>>(
            Postings.Where(p => string.Equals(p.ServiceOrderId, serviceOrderId, StringComparison.Ordinal)).ToList());

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCalls++;
        return Task.CompletedTask;
    }
}
