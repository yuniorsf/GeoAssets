namespace GeoAssets.Shared.Services.Evidence;

public sealed record EvidenceVerificationResult(bool IsValid, string? FailureReason);
