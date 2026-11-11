using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Workflow.Dispatch;

/// <summary>
/// Routable projection of a field crew — depot location plus documented skill/equipment
/// tags, used by <see cref="ICrewRoutingEngine"/> to decide eligibility (XD01-240). Mirrors the
/// crew-feature convention established by <c>GeoAssets.Workflow.Scada.ScadaOutageOrderTypes</c>
/// (XD01-239): a <c>GeoFeature</c> with a well-known <c>AssetTypeId</c>, tags carried in
/// <c>CustomAttributes</c>.
/// </summary>
public sealed record CrewRosterEntry(
    string CrewFeatureId,
    GeoPoint DepotLocation,
    IReadOnlySet<string> Skills,
    IReadOnlySet<string> Equipment,
    DateTimeOffset? ShiftEnd = null);
