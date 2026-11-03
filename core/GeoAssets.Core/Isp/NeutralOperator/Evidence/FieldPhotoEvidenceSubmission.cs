namespace GeoAssets.Core.Isp.NeutralOperator.Evidence;

/// <summary>
/// What a server-side ingestion endpoint receives for a field photo: the image bytes, the
/// claimed capture metadata, and the digest the device computed (null/empty if the client omitted
/// it, which <see cref="FieldPhotoEvidenceIngestionVerifier"/> treats as a hard rejection when
/// evidence is mandatory).
/// </summary>
public sealed record FieldPhotoEvidenceSubmission(
    byte[] ImageBytes,
    FieldPhotoEvidenceMetadata Metadata,
    string? ClaimedDigestBase64);
