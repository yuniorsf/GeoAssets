namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// Thrown by <see cref="CleanupDriveBatchingService.CompleteCutOrderAsync"/> when the
/// post-removal photo's HMAC-SHA256 evidence digest (XD01-261) is missing or fails
/// verification — the XD01-264 AC "a cut order cannot be closed without a sealed post-removal
/// photo", enforced via <c>FieldPhotoEvidenceIngestionVerifier.RequiresRejection</c>.
/// </summary>
public sealed class CutOrderMissingSealedEvidenceException : InvalidOperationException
{
    public string ServiceOrderId { get; } = string.Empty;

    public CutOrderMissingSealedEvidenceException(string serviceOrderId)
        : base($"Cut order '{serviceOrderId}' cannot be closed without a valid, sealed post-removal photo.")
    {
        ServiceOrderId = serviceOrderId;
    }

    public CutOrderMissingSealedEvidenceException()
    {
    }

    public CutOrderMissingSealedEvidenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
