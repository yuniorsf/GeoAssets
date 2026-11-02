namespace GeoAssets.IpBilling.Levantamento;

/// <summary>
/// Thrown by <see cref="ILevantamentoRestClient.UploadAsync"/> when the server rejects an
/// upload — either structured row-level errors (<see cref="RowErrors"/> non-empty, matching
/// <see cref="LevantamentoWorkbookRowParser"/>'s shape) or a generic message (e.g. order not in
/// <c>Carregado</c>).
/// </summary>
public sealed class LevantamentoUploadRejectedException : InvalidOperationException
{
    public IReadOnlyList<LevantamentoUploadRowError> RowErrors { get; }

    public LevantamentoUploadRejectedException(string message, IReadOnlyList<LevantamentoUploadRowError> rowErrors)
        : base(message)
    {
        RowErrors = rowErrors;
    }

    public LevantamentoUploadRejectedException()
    {
        RowErrors = [];
    }

    public LevantamentoUploadRejectedException(string message) : base(message)
    {
        RowErrors = [];
    }

    public LevantamentoUploadRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
        RowErrors = [];
    }
}
