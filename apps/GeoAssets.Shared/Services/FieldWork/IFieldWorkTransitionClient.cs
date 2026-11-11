namespace GeoAssets.Shared.Services.FieldWork;

/// <summary>
/// Client contract for the crew-facing field-work transition endpoint
/// <c>GeoAssets.FieldService.Server</c>'s <c>FieldWorkTransitionRestApiExtensions</c> exposes
/// (XD01-295).
/// </summary>
public interface IFieldWorkTransitionClient
{
    /// <summary>
    /// Attempts to advance <paramref name="serviceOrderId"/> to <paramref name="newStatus"/>.
    /// <paramref name="attributes"/>, when non-null, is merged into the order's attributes
    /// server-side before the transition is evaluated (e.g. a safety-checklist submission).
    /// <paramref name="deviceLongitude"/>/<paramref name="deviceLatitude"/> are required only for
    /// the EnRoute → OnSite edge.
    /// </summary>
    Task<FieldWorkTransitionOutcome> AttemptTransitionAsync(
        string serviceOrderId,
        string newStatus,
        byte[] rowVersion,
        IReadOnlyDictionary<string, string>? attributes = null,
        double? deviceLongitude = null,
        double? deviceLatitude = null,
        CancellationToken ct = default);
}
