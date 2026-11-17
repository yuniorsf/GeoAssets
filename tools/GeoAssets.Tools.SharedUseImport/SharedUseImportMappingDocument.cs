namespace GeoAssets.Tools.SharedUseImport;

/// <summary>The mapping file, as written by whoever prepares the import (JSON, camelCase).</summary>
/// <param name="Poles">Legacy pole id to GeoAssets pole feature id.</param>
/// <param name="Occupants">Legacy occupant id to the occupant's CNPJ (the <c>SUOccupants</c> export).</param>
/// <param name="Organizations">CNPJ to GeoAssets organization id.</param>
/// <param name="WireTypes">Legacy wire type id to the cable measurements an attachment of that type carries.</param>
internal sealed record SharedUseImportMappingDocument(
    IReadOnlyList<PoleMapping> Poles,
    IReadOnlyList<OccupantMapping> Occupants,
    IReadOnlyList<OrganizationMapping> Organizations,
    IReadOnlyList<WireTypeMapping> WireTypes);
