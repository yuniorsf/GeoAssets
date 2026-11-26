using NetTopologySuite.Features;

namespace GeoAssets.Ingestion.Tests.Fakes;

internal static class ShapefileTestFeatures
{
    public static IReadOnlyList<IFeature> Poles(int count, double originX = 554271.85, double originY = 8566172.49) =>
        [.. Enumerable.Range(1, count).Select(i => ShapefileDeliveryBuilder.PointFeature(
            originX + i, originY + i, ("NU_PG_ID", (long)i), ("CD_TIPO_PG", 1L), ("DE_NOME", "Poste " + i)))];

    public static IReadOnlyList<IFeature> Lines(int count) =>
        [.. Enumerable.Range(1, count).Select(i => ShapefileDeliveryBuilder.LineFeature(
            [(554271.0 + i, 8566172.0), (554281.0 + i, 8566182.0)], ("NU_TRECHO_M", (long)i), ("NU_PG_INIC", (long)i)))];

    public static IReadOnlyList<IFeature> QuantizedPoints(int count) =>
        [.. Enumerable.Range(0, count).Select(i => ShapefileDeliveryBuilder.PointFeature(
            554000 + (i % 5), 8566000 + i, ("NU_PG_ID", (long)i)))];

    public static IReadOnlyList<IFeature> PointsWithSentinels(int count, int sentinels) =>
        [.. Enumerable.Range(0, count).Select(i => ShapefileDeliveryBuilder.PointFeature(
            i < sentinels ? 2.2e8 : 554000 + (i * 3.7), 8566000 + (i * 2.3), ("NU_PG_ID", (long)i)))];
}
