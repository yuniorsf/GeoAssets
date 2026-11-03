namespace GeoAssets.IpBilling.Fiscalizacao;

/// <summary>
/// Thrown by <see cref="IFiscalizacaoRestClient.UploadAsync"/> when the server rejects an
/// upload — either structured row-level errors (<see cref="RowErrors"/> non-empty, matching
/// <see cref="FiscalizacaoWorkbookRowParser"/>'s shape) or a generic message (e.g. order not in
/// <c>Carregado</c>). Mirrors <c>Levantamento.LevantamentoUploadRejectedException</c>'s shape.
/// </summary>
public sealed class FiscalizacaoUploadRejectedException : InvalidOperationException
{
    public IReadOnlyList<FiscalizacaoUploadRowError> RowErrors { get; }

    public FiscalizacaoUploadRejectedException(string message, IReadOnlyList<FiscalizacaoUploadRowError> rowErrors)
        : base(message)
    {
        RowErrors = rowErrors;
    }

    public FiscalizacaoUploadRejectedException()
    {
        RowErrors = [];
    }

    public FiscalizacaoUploadRejectedException(string message) : base(message)
    {
        RowErrors = [];
    }

    public FiscalizacaoUploadRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
        RowErrors = [];
    }
}
