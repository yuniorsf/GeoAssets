using System.Globalization;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Domain.Sanitation.Icerp;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Reads whether a water/sewer repair order is paving-eligible, its crew-recorded trench
/// measurements, and the georeferenced as-built boundary polygon
/// <see cref="WaterPavingSubOrderSpawner"/> validates that trench against, from
/// <see cref="GeoAssets.Workflow.Orders.IServiceOrder.Attributes"/> (XD01-291) — same
/// attribute-bag convention <see cref="GeoAssets.Workflow.Contractors.ServiceOrderBoqAttributes"/>
/// already documents, no dedicated column:
/// <code>
///   Attributes["pavingEligible"] = "true"
///   Attributes["pavingOrderTypeId"] = "water-paving-restoration"
///   Attributes["pavingTrenchLengthMeters"] = "4.2"
///   Attributes["pavingTrenchWidthMeters"] = "0.8"
///   Attributes["pavingTrenchDepthMeters"] = "1.1"
///   Attributes["pavingBoundaryGeoJson"] = "{\"type\":\"Polygon\",\"coordinates\":[[...]]}"
/// </code>
/// The boundary is attribute text rather than one of the order's own
/// <see cref="GeoAssets.Workflow.Orders.ServiceOrder.Features"/> deliberately:
/// <see cref="GeoAssets.Workflow.Persistence.EFServiceOrderRepository"/> only persists feature
/// *ids* (resolving the full geometry needs an <see cref="GeoAssets.Core.Interfaces.IAssetProvider"/>
/// this service has no reason to depend on otherwise), while every other attribute this type
/// reads already round-trips through <see cref="GeoAssets.Workflow.Orders.IServiceOrder.Attributes"/> as plain text.
/// </summary>
public static class ServiceOrderPavingAttributes
{
    public const string PavingEligibleKey = "pavingEligible";
    public const string PavingOrderTypeIdKey = "pavingOrderTypeId";
    public const string RecordedTrenchLengthMetersKey = "pavingTrenchLengthMeters";
    public const string RecordedTrenchWidthMetersKey = "pavingTrenchWidthMeters";
    public const string RecordedTrenchDepthMetersKey = "pavingTrenchDepthMeters";
    public const string RecordedBoundaryGeoJsonKey = "pavingBoundaryGeoJson";

    public static bool IsPavingEligible(IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        return attributes.TryGetValue(PavingEligibleKey, out var raw) && string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static string ReadPavingOrderTypeId(IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        return attributes.TryGetValue(PavingOrderTypeIdKey, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"ServiceOrder.Attributes['{PavingOrderTypeIdKey}'] is missing.");
    }

    public static TrenchDimensions ReadRecordedTrench(IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        return new TrenchDimensions(
            LengthMeters: ReadRequiredDouble(attributes, RecordedTrenchLengthMetersKey),
            WidthMeters: ReadRequiredDouble(attributes, RecordedTrenchWidthMetersKey),
            DepthMeters: ReadRequiredDouble(attributes, RecordedTrenchDepthMetersKey));
    }

    public static GeoPolygon ReadGeoreferencedBoundary(IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        if (!attributes.TryGetValue(RecordedBoundaryGeoJsonKey, out var json) || string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException($"ServiceOrder.Attributes['{RecordedBoundaryGeoJsonKey}'] is missing.");

        // Deserialized as the abstract GeoGeometry (not directly as GeoPolygon) — that's the
        // shape GeoGeometryConverter (registered on GeoJsonSerializer.Options) actually
        // dispatches on; see GeoJsonSerializer.DeserializeGeometry's own identical pattern.
        return GeoJsonSerializer.DeserializeGeometry(json) as GeoPolygon
            ?? throw new InvalidOperationException($"ServiceOrder.Attributes['{RecordedBoundaryGeoJsonKey}'] could not be parsed as a polygon.");
    }

    private static double ReadRequiredDouble(IReadOnlyDictionary<string, string> attributes, string key)
    {
        if (!attributes.TryGetValue(key, out var raw) || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new InvalidOperationException($"ServiceOrder.Attributes['{key}'] is missing or not a number.");

        return value;
    }
}
