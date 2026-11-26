namespace GeoAssets.Ingestion.Proj;

/// <summary>
/// Options of <see cref="ProjCoordinateTransformerFactory"/>. <see cref="ProjDataDirectory"/>, when set, is prepended to
/// PROJ's search paths — typically the folder holding the IBGE grid <c>br_ibge_SAD69_003.tif</c>.
/// </summary>
public sealed record ProjOptions(string? ProjDataDirectory = null);
