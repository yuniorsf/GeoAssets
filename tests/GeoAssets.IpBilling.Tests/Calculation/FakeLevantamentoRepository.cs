using GeoAssets.Core.Models;
using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.FaturamentoLedger;
using GeoAssets.IpBilling.Levantamento;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;

namespace GeoAssets.IpBilling.Tests.Calculation;

/// <summary>Only answers "most recent validated Levantamento before this competência".</summary>
internal sealed class FakeLevantamentoRepository : ILevantamentoRepository
{
    private readonly List<LevantamentoHeader> _validated = [];

    public FakeLevantamentoRepository WithValidated(Guid municipioId, string competencia)
    {
        _validated.Add(new LevantamentoHeader
        {
            ServiceOrderId = $"lev-{competencia}",
            MunicipioId = municipioId,
            Competencia = competencia,
            IsValidated = true,
        });
        return this;
    }

    public Task<LevantamentoHeader?> GetMostRecentValidatedAsync(
        Guid municipioId, string beforeCompetencia, CancellationToken ct = default) =>
        Task.FromResult(_validated
            .Where(h => h.MunicipioId == municipioId &&
                        string.CompareOrdinal(h.Competencia, beforeCompetencia) < 0)
            .OrderByDescending(h => h.Competencia, StringComparer.Ordinal)
            .FirstOrDefault());

    public Task<LevantamentoHeader?> GetHeaderAsync(string serviceOrderId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task AddHeaderAsync(LevantamentoHeader header, CancellationToken ct = default) => throw new NotSupportedException();
    public Task MarkValidatedAsync(string serviceOrderId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<LevantamentoTransformerLine>> GetTransformerLinesAsync(string serviceOrderId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task ReplaceTransformerLinesAsync(string serviceOrderId, IReadOnlyList<LevantamentoTransformerLine> lines, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Levantamento24hLine>> Get24hLinesAsync(string serviceOrderId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task Replace24hLinesAsync(string serviceOrderId, IReadOnlyList<Levantamento24hLine> lines, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<LevantamentoHeader?> GetFirstValidatedAsync(Guid municipioId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlySet<string>> GetAllValidatedCodigosInstalacaoAsync(Guid municipioId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
}
