namespace GeoAssets.Tools.SharedUseImport;

/// <summary>One run of the tool, as given on the command line.</summary>
/// <param name="SharedUsesPath">The CSV export of the legacy <c>SharedUses</c> table.</param>
/// <param name="MappingPath">The JSON mapping file.</param>
/// <param name="ReportPath">Where to write the JSON reconciliation report.</param>
/// <param name="OrganizationId">The pole owner the import runs as.</param>
/// <param name="DryRun">Compare with the store and report, but write nothing.</param>
/// <param name="ConnectionString">The PostgreSQL database holding pole occupancy.</param>
internal sealed record SharedUseImportOptions(
    string SharedUsesPath,
    string MappingPath,
    string ReportPath,
    Guid OrganizationId,
    bool DryRun,
    string ConnectionString)
{
    /// <summary>The environment variable the connection string is read from (kept off the command line).</summary>
    public const string ConnectionStringVariable = "GEOASSETS_SHAREDUSE_IMPORT_CONNECTION";

    /// <summary>What the tool prints when its arguments are wrong.</summary>
    public const string Usage =
        "Usage: GeoAssets.Tools.SharedUseImport --shared-uses <SharedUses.csv> --mapping <mapping.json> " +
        "--organization <pole-owner-organization-id> --report <report.json> [--dry-run]\n" +
        "The PostgreSQL connection string is read from " + ConnectionStringVariable + ".";
}
