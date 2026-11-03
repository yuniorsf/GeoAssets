namespace GeoAssets.Core.Isp.NeutralOperator.Evidence;

/// <summary>
/// Server-side ingestion gate: shared by the census workflow (XD01-259/260) and the
/// clandestine-fiber/cleanup workflows (XD01-263/264) once those are wired up to call it.
/// Pure/stateless — the device secret is supplied per call (e.g. looked up from a device
/// registry by whichever caller owns that concern), not held by this type.
/// </summary>
public static class FieldPhotoEvidenceIngestionVerifier
{
    public static FieldPhotoEvidenceVerificationResult Verify(FieldPhotoEvidenceSubmission submission, string deviceSecret)
    {
        ArgumentNullException.ThrowIfNull(submission);

        if (string.IsNullOrEmpty(submission.ClaimedDigestBase64))
            return FieldPhotoEvidenceVerificationResult.DigestMissing;

        var verified = FieldPhotoEvidenceSealer.VerifyDigest(
            submission.ImageBytes,
            submission.Metadata,
            submission.ClaimedDigestBase64,
            deviceSecret);

        return verified
            ? FieldPhotoEvidenceVerificationResult.Verified
            : FieldPhotoEvidenceVerificationResult.DigestMismatch;
    }

    /// <summary>
    /// The AC's "server rejects any order submission missing a valid evidence digest where
    /// photographic evidence is mandatory" rule, as a single boolean a caller can gate on.
    /// </summary>
    public static bool RequiresRejection(FieldPhotoEvidenceSubmission submission, string deviceSecret, bool evidenceMandatory)
    {
        if (!evidenceMandatory)
            return false;

        return Verify(submission, deviceSecret) != FieldPhotoEvidenceVerificationResult.Verified;
    }
}
