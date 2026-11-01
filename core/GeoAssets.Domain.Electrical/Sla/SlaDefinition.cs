namespace GeoAssets.Domain.Electrical.Sla;

/// <summary>
/// One ANEEL REN 1.000/2021 service-level deadline (e.g. a connection request, an outage
/// response, a technical visit) — a service type key paired with the time the distributor has
/// to resolve it once the clock starts.
///
/// <see cref="Deadline"/> is deliberately caller-supplied, not a baked-in table of the dozens
/// of REN 1.000 Anexo deadlines (which vary by service type, consumer group, and urban/rural
/// classification, and are amended by ANEEL over time) — hardcoding specific day-counts here
/// would risk going stale or simply being wrong for a given distributor/region without a
/// reviewer ever noticing. Construct one <see cref="SlaDefinition"/> per service type from
/// whatever table the operator's own regulatory/compliance team currently has on file.
/// </summary>
public sealed record SlaDefinition(string ServiceTypeKey, TimeSpan Deadline, string? Description = null);
