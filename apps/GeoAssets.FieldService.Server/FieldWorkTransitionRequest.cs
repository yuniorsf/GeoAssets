namespace GeoAssets.FieldService.Server;

/// <summary>
/// A crew's attempt to drive their field-work order to <see cref="NewStatus"/> (XD01-295).
/// <see cref="Attributes"/>, when present, is merged into the order's existing
/// <c>IServiceOrder.Attributes</c> before the transition is evaluated — e.g. a safety-checklist
/// submission arriving in the same call as the "start work" attempt it's meant to unlock, rather
/// than requiring two round-trips. <see cref="DeviceLongitude"/>/<see cref="DeviceLatitude"/> are
/// required only for the EnRoute → OnSite edge (the geofence-gated arrival check) — every other
/// edge ignores them, same as <c>FieldWorkOrderTransitioner.TransitionAsync</c>'s own
/// <c>devicePoint</c> parameter.
/// </summary>
internal sealed record FieldWorkTransitionRequest(
    string NewStatus,
    byte[] RowVersion,
    Dictionary<string, string>? Attributes = null,
    double? DeviceLongitude = null,
    double? DeviceLatitude = null);
