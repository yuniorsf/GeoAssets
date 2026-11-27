using GeoAssets.Core.Models;

namespace GeoAssets.Core.VectorFiles;

/// <summary>One page an <see cref="IVectorFileReader"/> read (XD01-437).</summary>
/// <param name="Features">The page's features, in file order, with their geometry in EPSG:4326 (longitude, latitude).</param>
/// <param name="NextOffset">The offset of the next page; <see langword="null"/> when this page is the last.</param>
/// <param name="TotalCount">The layer's feature count when the format knows it cheaply; otherwise <see langword="null"/>.</param>
public sealed record VectorFilePage(IReadOnlyList<GeoFeature> Features, long? NextOffset, long? TotalCount);
