using GeoAssets.Core.Models;

namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Writes a set of named layers to a real ESRI File Geodatabase (.gdb), zipped into a single
/// archive for transport — the ANEEL BDGD submission format (XD01-225). Unlike
/// <see cref="IShapefileExportService"/>'s per-geometry-type split, a single FileGDB container
/// can hold multiple named layers (one per BDGD entity type: PONNOT, UNTRD, UNREMT, SEGCON, UC).
/// </summary>
public interface IFileGeodatabaseExportService
{
    /// <summary>
    /// Builds a ZIP archive containing one .gdb directory with one layer per non-empty entry in
    /// <paramref name="layeredFeatures"/> (layer name → features, already reprojected into the
    /// CRS described by <paramref name="targetCrsWkt"/>/<paramref name="targetSrid"/>). Throws
    /// <see cref="InvalidOperationException"/> if every layer is empty.
    /// </summary>
    byte[] ExportToZip(
        IReadOnlyDictionary<string, IReadOnlyList<GeoFeature>> layeredFeatures,
        string targetCrsWkt,
        int targetSrid);
}
