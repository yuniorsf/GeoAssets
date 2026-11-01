namespace GeoAssets.Core.Services.Evidence;

public sealed record EvidenceVerificationResult(bool IsValid, string? FailureReason);
