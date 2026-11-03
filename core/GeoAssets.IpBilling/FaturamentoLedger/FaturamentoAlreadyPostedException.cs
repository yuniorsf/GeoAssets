namespace GeoAssets.IpBilling.FaturamentoLedger;

/// <summary>
/// Thrown by <see cref="FaturamentoManualEntryService.ManualEntryAsync"/> when the
/// (<see cref="MunicipioId"/>, <see cref="Competencia"/>) pair already has a
/// <c>Faturamento</c> row — manual single-record entry is insert-only and rejects by default
/// (XD01-189), mirroring the same "don't silently overwrite a financial record" posture
/// <c>DuplicateOficioNumeroException</c> (XD01-185) established for Ofício. Batch load and
/// recarga do not throw this for an existing/missing pair respectively — they skip and report
/// it instead, since those are bulk operations where one bad row shouldn't abort the rest.
/// </summary>
public sealed class FaturamentoAlreadyPostedException : InvalidOperationException
{
    public Guid MunicipioId { get; }
    public string Competencia { get; } = string.Empty;

    public FaturamentoAlreadyPostedException(Guid municipioId, string competencia)
        : base($"Faturamento já cadastrado para município '{municipioId}', competência '{competencia}'.")
    {
        MunicipioId = municipioId;
        Competencia = competencia;
    }

    public FaturamentoAlreadyPostedException()
    {
    }

    public FaturamentoAlreadyPostedException(string message) : base(message)
    {
    }

    public FaturamentoAlreadyPostedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
