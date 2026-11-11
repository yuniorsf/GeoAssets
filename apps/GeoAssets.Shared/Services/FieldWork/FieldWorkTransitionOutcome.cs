namespace GeoAssets.Shared.Services.FieldWork;

/// <summary>
/// Result of a field-work transition attempt (XD01-295) — mirrors
/// <c>GeoAssets.FieldService.Server</c>'s internal <c>FieldWorkTransitionResponse</c> on success,
/// or carries <see cref="RejectionReason"/> (the server's plain-text 400/404/409 body) on
/// failure, so a UI can show the crew why a gate rejected their attempt without the caller
/// needing to catch an HTTP exception.
/// </summary>
public sealed record FieldWorkTransitionOutcome(bool Succeeded, string? NewStatus, byte[]? NewRowVersion, string? RejectionReason)
{
    public static FieldWorkTransitionOutcome Success(string newStatus, byte[] newRowVersion) =>
        new(true, newStatus, newRowVersion, null);

    public static FieldWorkTransitionOutcome Rejected(string reason) =>
        new(false, null, null, reason);
}
