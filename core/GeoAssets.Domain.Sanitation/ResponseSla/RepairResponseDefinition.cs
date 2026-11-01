namespace GeoAssets.Domain.Sanitation.ResponseSla;

/// <summary>
/// One ANA (Agência Nacional de Águas) repair-response deadline — an event type (e.g. visible
/// leak, total water outage, sewage overflow) paired with the time the concessionaire has to
/// resolve it once reported.
///
/// <see cref="Deadline"/> is deliberately caller-supplied, not a baked-in table of ANA's
/// per-event-type response intervals (which vary by event severity/category and are amended
/// over time) — hardcoding specific hour-counts here would risk going stale or being wrong for
/// a given concessionaire/region without a reviewer ever noticing. Construct one
/// <see cref="RepairResponseDefinition"/> per event type from whatever table the operator's own
/// regulatory/compliance team currently has on file.
/// </summary>
public sealed record RepairResponseDefinition(string EventTypeKey, TimeSpan Deadline, string? Description = null);
