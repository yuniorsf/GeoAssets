namespace GeoAssets.Tools.SharedUseImport;

/// <param name="LegacyOccupantId">The legacy <c>SUOccupants.Id</c>; never 0, which Geos keeps for "unidentified".</param>
/// <param name="Cnpj">The occupant's CNPJ as Geos holds it, punctuated or not; null when Geos has none.</param>
internal sealed record OccupantMapping(int LegacyOccupantId, string? Cnpj);
