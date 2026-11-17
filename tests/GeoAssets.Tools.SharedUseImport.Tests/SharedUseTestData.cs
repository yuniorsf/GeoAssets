namespace GeoAssets.Tools.SharedUseImport.Tests;

/// <summary>Synthetic input for the import tests. No value here comes from a real Geos database.</summary>
internal static class SharedUseTestData
{
    public const string IdentifiedCnpj = "10.000.000/0001-01";
    public const string UnmatchedCnpj = "20.000.000/0001-02";
    public const int IdentifiedOccupant = 10;
    public const int UnmatchedOccupant = 11;
    public const int OccupantWithoutCnpj = 12;
    public const int UnknownOccupant = 99;
    public const int WireType = 1;
    public const int UnmappedWireType = 9;

    public static readonly Guid Owner = new("0000000a-0000-0000-0000-000000000001");
    public static readonly Guid OccupantOrganization = new("0000000b-0000-0000-0000-000000000002");
    public static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Legacy poles 1 → "pole-1", 2 → "pole-2", 3 → "pole-3"; wire type 1 is 12.3456 mm / 1.2345 daN before rounding.</summary>
    public static SharedUseImportMappingDocument Document(
        IReadOnlyList<PoleMapping>? poles = null,
        IReadOnlyList<OccupantMapping>? occupants = null,
        IReadOnlyList<OrganizationMapping>? organizations = null,
        IReadOnlyList<WireTypeMapping>? wireTypes = null) =>
        new(
            poles ?? [new(1, "pole-1"), new(2, "pole-2"), new(3, "pole-3")],
            occupants ?? [new(IdentifiedOccupant, IdentifiedCnpj), new(UnmatchedOccupant, UnmatchedCnpj), new(OccupantWithoutCnpj, null)],
            organizations ?? [new("10000000000101", OccupantOrganization)],
            wireTypes ?? [new(WireType, 12.3456m, 1.2345m)]);

    public static SharedUseImportMapping Mapping(SharedUseImportMappingDocument? document = null) =>
        SharedUseImportMapping.From(document ?? Document());

    public static LegacySharedUseRow Row(int id, int pole = 1, int occupant = IdentifiedOccupant, int wireType = WireType, int count = 1) =>
        new(id, pole, occupant, wireType, count, LineNumber: id + 1);

    public static SharedUseImportPlan Plan(params LegacySharedUseRow[] rows) =>
        SharedUseImportPlanner.Plan(rows, Mapping(), Owner, Now);
}
