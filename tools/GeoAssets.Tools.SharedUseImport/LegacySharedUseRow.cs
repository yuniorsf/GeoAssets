namespace GeoAssets.Tools.SharedUseImport;

/// <summary>
/// One row of the legacy Geos <c>SharedUses</c> table (the live table, never its per-order
/// shadow copy <c>SharedUsesSO</c>): <see cref="Count"/> attachment points that one occupant has
/// on one pole. <see cref="OccupantId"/> 0 is Geos' "unidentified occupant".
/// </summary>
/// <param name="Id">The legacy row id (<c>SharedUses.Id</c>), unique in the table.</param>
/// <param name="PoleId">The legacy pole id (<c>SharedUses.PoleId</c>).</param>
/// <param name="OccupantId">The legacy occupant id (<c>SharedUses.OccupantId</c>); 0 when unidentified.</param>
/// <param name="WireTypeId">The legacy wire type id (<c>SharedUses.WireTypeId</c>).</param>
/// <param name="Count">How many attachment points the row stands for (<c>SharedUses.Count</c>).</param>
/// <param name="LineNumber">The line of the input file the row was read from, for the report.</param>
internal sealed record LegacySharedUseRow(int Id, int PoleId, int OccupantId, int WireTypeId, int Count, int LineNumber)
{
    /// <summary>The legacy occupant id Geos uses for an occupant nobody identified.</summary>
    public const int UnidentifiedOccupantId = 0;
}
