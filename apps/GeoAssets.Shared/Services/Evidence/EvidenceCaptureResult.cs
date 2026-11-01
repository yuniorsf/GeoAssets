namespace GeoAssets.Shared.Services.Evidence;

public sealed record EvidenceCaptureResult(byte[] SignedJpegBytes, string Signature);
