namespace GeoAssets.FieldService.Server;

/// <summary>Request body for <c>POST /api/geoassets/topology/traverse</c> (XD01-239) — the
/// payload an external OMS posts on a SCADA breaker-trip alarm.</summary>
internal sealed record ScadaTopologyTraverseRequest(string TrippedSwitchFeatureId);
