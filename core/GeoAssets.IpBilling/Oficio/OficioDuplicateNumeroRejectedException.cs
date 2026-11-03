namespace GeoAssets.IpBilling.Oficio;

/// <summary>
/// Client-side counterpart (XD01-187) of the server's <see cref="DuplicateOficioNumeroException"/>
/// — thrown by <c>OficioRestClient</c> when an upload is rejected with HTTP 409 Conflict because
/// the <c>(MunicipioId, NumeroPrefeitura)</c> combination already belongs to a different, still
/// -active order. The <c>GeoAssets.Web</c> client can't reference the server-side exception type
/// directly (different host assembly), so this carries the same structured fields across the
/// REST boundary. The legitimate correction path is <c>IOficioRestClient.SupersedeAsync</c> on
/// <see cref="ExistingServiceOrderId"/>.
/// </summary>
public sealed class OficioDuplicateNumeroRejectedException : Exception
{
    public Guid MunicipioId { get; }
    public string NumeroPrefeitura { get; } = string.Empty;
    public string ExistingServiceOrderId { get; } = string.Empty;

    public OficioDuplicateNumeroRejectedException(
        string message, Guid municipioId, string numeroPrefeitura, string existingServiceOrderId)
        : base(message)
    {
        MunicipioId = municipioId;
        NumeroPrefeitura = numeroPrefeitura;
        ExistingServiceOrderId = existingServiceOrderId;
    }

    public OficioDuplicateNumeroRejectedException()
    {
    }

    public OficioDuplicateNumeroRejectedException(string message) : base(message)
    {
    }

    public OficioDuplicateNumeroRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
