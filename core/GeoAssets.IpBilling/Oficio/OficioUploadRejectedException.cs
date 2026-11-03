namespace GeoAssets.IpBilling.Oficio;

/// <summary>
/// Thrown by <c>OficioRestClient</c> (XD01-187) when an upload is rejected with structured
/// row-level errors (HTTP 400). Mirrors <c>Fiscalizacao.FiscalizacaoUploadRejectedException</c>'s
/// shape (XD01-184).
/// </summary>
public sealed class OficioUploadRejectedException : Exception
{
    public IReadOnlyList<OficioUploadRowError> RowErrors { get; } = [];

    public OficioUploadRejectedException(string message, IReadOnlyList<OficioUploadRowError> rowErrors)
        : base(message)
    {
        RowErrors = rowErrors;
    }

    public OficioUploadRejectedException()
    {
    }

    public OficioUploadRejectedException(string message) : base(message)
    {
    }

    public OficioUploadRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
