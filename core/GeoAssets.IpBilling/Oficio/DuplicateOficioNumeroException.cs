namespace GeoAssets.IpBilling.Oficio;

/// <summary>
/// Thrown by <see cref="OficioCalculationService"/> when a new Ofício upload's
/// <c>(MunicipioId, NumeroPrefeitura)</c> combination already belongs to a different, still-active
/// <c>ServiceOrder</c> — rejecting by default rather than silently overwriting a financial
/// record, per this ticket's own description (XD01-185). The legitimate correction path is
/// <see cref="OficioCalculationService.SupersedeAsync"/>, which releases the number from the
/// existing order first.
/// </summary>
public sealed class DuplicateOficioNumeroException : InvalidOperationException
{
    public Guid MunicipioId { get; }
    public string NumeroPrefeitura { get; } = string.Empty;
    public string ExistingServiceOrderId { get; } = string.Empty;

    public DuplicateOficioNumeroException(Guid municipioId, string numeroPrefeitura, string existingServiceOrderId)
        : base($"Município '{municipioId}' already has an active Ofício número '{numeroPrefeitura}' on order '{existingServiceOrderId}'. Supersede it first to reassign the número.")
    {
        MunicipioId = municipioId;
        NumeroPrefeitura = numeroPrefeitura;
        ExistingServiceOrderId = existingServiceOrderId;
    }

    public DuplicateOficioNumeroException()
    {
    }

    public DuplicateOficioNumeroException(string message) : base(message)
    {
    }

    public DuplicateOficioNumeroException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
