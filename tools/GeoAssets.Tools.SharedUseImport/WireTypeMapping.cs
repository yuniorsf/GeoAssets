namespace GeoAssets.Tools.SharedUseImport;

/// <param name="LegacyWireTypeId">The legacy <c>WireTypes.Id</c>.</param>
/// <param name="CableDiameterMm">Cable diameter in millimetres; rounded to 3 decimals on import.</param>
/// <param name="CableLoadDaN">Declared load in daN; rounded to 3 decimals on import.</param>
internal sealed record WireTypeMapping(int LegacyWireTypeId, decimal CableDiameterMm, decimal CableLoadDaN);
