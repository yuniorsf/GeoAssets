namespace GeoAssets.Workflow.Infrastructure;

/// <summary>
/// One sub-duct (32mm or 40mm) inside a 100mm PVC conduit bore (XD01-265). Diameter is plain
/// data, not an enum — a manhole audit records whatever sub-duct sizes are actually present,
/// not a fixed catalog of allowed sizes.
/// </summary>
public sealed record SubDuct(int DiameterMillimeters, bool Occupied);
