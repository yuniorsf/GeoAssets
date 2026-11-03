namespace GeoAssets.IpBilling.Oficio;

/// <summary>
/// Persistence abstraction for an Ofício's header/line graph. Mirrors
/// <c>Fiscalizacao.IFiscalizacaoRepository</c>'s shape, plus the número-keyed lookup/clear
/// operations the duplicate-upload guard and supersede path need (XD01-185).
/// </summary>
public interface IOficioRepository
{
    Task<OficioHeader?> GetHeaderAsync(string serviceOrderId, CancellationToken ct = default);

    /// <summary>
    /// Looks up whichever header currently claims <paramref name="numeroPrefeitura"/> within
    /// <paramref name="municipioId"/>, if any — a header whose claim was released by
    /// <see cref="ClearNumeroPrefeituraAsync"/> has <c>NumeroPrefeitura == null</c> and will
    /// never match here.
    /// </summary>
    Task<OficioHeader?> GetHeaderByNumeroAsync(
        Guid municipioId, string numeroPrefeitura, CancellationToken ct = default);

    Task AddHeaderAsync(OficioHeader header, CancellationToken ct = default);

    Task MarkValidatedAsync(string serviceOrderId, CancellationToken ct = default);

    /// <summary>Releases <paramref name="serviceOrderId"/>'s claim on its current
    /// <see cref="OficioHeader.NumeroPrefeitura"/> by setting it to <c>null</c>, freeing that
    /// número for a corrective re-upload on a different order. A no-op if the header has no
    /// current número.</summary>
    Task ClearNumeroPrefeituraAsync(string serviceOrderId, CancellationToken ct = default);

    Task<IReadOnlyList<OficioLine>> GetLinesAsync(
        string serviceOrderId, CancellationToken ct = default);

    /// <summary>Recalculation is idempotent per order — this deletes any previously-persisted
    /// lines for <paramref name="serviceOrderId"/> and inserts <paramref name="lines"/> in
    /// their place.</summary>
    Task ReplaceLinesAsync(
        string serviceOrderId, IReadOnlyList<OficioLine> lines, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
