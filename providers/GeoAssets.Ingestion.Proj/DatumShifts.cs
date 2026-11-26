namespace GeoAssets.Ingestion.Proj;

/// <summary>
/// The SAD69 → SIRGAS 2000 operations, as PROJ pipelines over geographic (lon, lat) degrees: the IBGE grid
/// (EPSG:5528's <c>hgridshift</c> with <c>br_ibge_SAD69_003.tif</c>) and the 3-parameter Helmert of EPSG:15485 (5 m).
/// The grid operation names the file by absolute path, so the choice never depends on PROJ's cached grid lookups.
/// </summary>
internal static class DatumShifts
{
    public const string Sad69GridFile = "br_ibge_SAD69_003.tif";

    public const string Sad69Helmert =
        "+proj=pipeline +step +proj=unitconvert +xy_in=deg +xy_out=rad +step +proj=push +v_3 " +
        "+step +proj=cart +ellps=aust_SA +step +proj=helmert +x=-67.35 +y=3.88 +z=-38.22 " +
        "+step +inv +proj=cart +ellps=GRS80 +step +proj=pop +v_3 +step +proj=unitconvert +xy_in=rad +xy_out=deg";

    public static string Sad69Grid(string gridPath) =>
        "+proj=pipeline +step +proj=unitconvert +xy_in=deg +xy_out=rad " +
        $"+step +proj=hgridshift +grids={Quote(gridPath)} +step +proj=unitconvert +xy_in=rad +xy_out=deg";

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    /// <summary>The first <see cref="Sad69GridFile"/> found in <paramref name="searchPaths"/>, else <c>null</c>.</summary>
    public static string? FindSad69Grid(IEnumerable<string> searchPaths) =>
        searchPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.Combine(path, Sad69GridFile))
            .FirstOrDefault(File.Exists);
}
