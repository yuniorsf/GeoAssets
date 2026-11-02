namespace GeoAssets.IpBilling.Levantamento;

/// <summary>
/// Thrown when a Levantamento spreadsheet upload contains malformed rows — carries every
/// offending row together (not just the first), mirroring
/// <c>ServiceOrderAttributeValidationException</c>'s "structured, not a generic failure" shape.
/// </summary>
public sealed class LevantamentoUploadValidationException : InvalidOperationException
{
    public IReadOnlyList<LevantamentoUploadRowError> Errors { get; }

    public LevantamentoUploadValidationException(IReadOnlyList<LevantamentoUploadRowError> errors)
        : base($"Levantamento upload contains {(errors ?? throw new ArgumentNullException(nameof(errors))).Count} invalid row(s).")
    {
        Errors = errors;
    }

    public LevantamentoUploadValidationException()
    {
        Errors = [];
    }

    public LevantamentoUploadValidationException(string message) : base(message)
    {
        Errors = [];
    }

    public LevantamentoUploadValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = [];
    }
}
