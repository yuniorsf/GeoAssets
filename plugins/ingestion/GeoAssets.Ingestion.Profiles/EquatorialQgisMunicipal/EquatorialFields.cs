namespace GeoAssets.Ingestion.Profiles.EquatorialQgisMunicipal;

/// <summary>Layer names, file-stem patterns, key namespaces and QGIS field names of the <c>equatorial-qgis-municipal</c> profile.</summary>
internal static class EquatorialFields
{
    public const string ProfileId = "equatorial-qgis-municipal";

    public const string LayerTrechoSecundario = "TRECHO_SECUNDARIO";
    public const string LayerTrechoPrimario = "TRECHO_PRIMARIO";
    public const string LayerTrafos = "TRAFOS";
    public const string LayerPgs = "PGS";

    public const string StemTrechoSecundario = "shape-trecho-secundario";
    public const string StemTrechoPrimario = "shape-trecho-primario";
    public const string StemTrafos = "shape-trafos";
    public const string StemPgs = "shape-pgs";

    public const string PatternTrechoSecundario = "^shape-trecho-secundario(-.+)?$";
    public const string PatternTrechoPrimario = "^shape-trecho-primario(-.+)?$";
    public const string PatternTrafos = "^shape-(trafos|instalacao)(-.+)?$";
    public const string PatternPgs = "^shapes?-pgs(-.+)?$";

    public const string NamespacePg = "PG";
    public const string NamespaceInst = "INST";
    public const string NamespaceTrafo = "TRAFO";
    public const string NamespaceTrechoMt = "TRECHO_MT";
    public const string NamespaceTrechoBt = "TRECHO_BT";

    public const string PoleId = "PGF_BARRAM";
    public const string InstallationNumber = "INT_NUM";
    public const string InstallationDescription = "TB_IN_DESC";
    public const string SegmentId = "MSLINK";
    public const string FeedingInstallation = "MSLINK_INS";

    public const string TransformerMarker = "transformadora";

    /// <summary>The LV layer's drawing offset: its lines are drawn 1 m east and 1 m north of the poles they join.</summary>
    public const double LvDrawingOffsetX = 1.0;

    /// <summary>See <see cref="LvDrawingOffsetX"/>.</summary>
    public const double LvDrawingOffsetY = 1.0;
}
