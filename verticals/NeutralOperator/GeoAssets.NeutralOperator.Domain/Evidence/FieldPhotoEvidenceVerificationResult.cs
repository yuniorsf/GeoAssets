namespace GeoAssets.NeutralOperator.Domain.Evidence;

/// <summary>
/// Outcome of verifying a submitted field photo's evidence seal on ingestion.
/// </summary>
public enum FieldPhotoEvidenceVerificationResult
{
    Verified,
    DigestMissing,
    DigestMismatch,
}
