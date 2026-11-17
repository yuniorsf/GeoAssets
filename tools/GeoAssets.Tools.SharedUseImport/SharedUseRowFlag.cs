namespace GeoAssets.Tools.SharedUseImport;

/// <summary>
/// Something about an imported row a person should look at. A flagged row is still imported in
/// full; every flag but <see cref="UnmappedWireType"/> makes its entries unidentified.
/// </summary>
internal enum SharedUseRowFlag
{
    /// <summary>Geos itself recorded the occupant as unidentified (occupant id 0).</summary>
    UnidentifiedOccupant,

    /// <summary>The row's legacy occupant is not in the mapping.</summary>
    UnknownOccupant,

    /// <summary>The legacy occupant is mapped but Geos has no CNPJ for it.</summary>
    OccupantWithoutCnpj,

    /// <summary>The occupant's CNPJ matches no GeoAssets organization; the CNPJ is kept on the entries.</summary>
    UnmatchedCnpj,

    /// <summary>The row's wire type is not in the mapping; its entries carry no cable measurements.</summary>
    UnmappedWireType,
}
