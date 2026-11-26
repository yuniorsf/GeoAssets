using System.Globalization;

namespace GeoAssets.Core.Rendering;

/// <summary>
/// Thrown when a layer's <see cref="LodThresholds"/> break <see cref="LodThresholds.FindDefect"/>'s rules (XD01-431) —
/// by <see cref="LodPolicy"/>'s constructor, so an invalid LOD configuration fails at startup (ADR-002 D7).
/// <see cref="Defect"/> carries the reason as a stable, typed value.
/// </summary>
public sealed class InvalidLodPolicyException : InvalidOperationException
{
    public InvalidLodPolicyException(LodThresholdsDefect defect, string? layerId, LodThresholds thresholds)
        : base(string.Create(
            CultureInfo.InvariantCulture,
            $"The LOD thresholds of {(layerId is null ? "the default layer" : $"layer '{layerId}'")} " +
            $"(raster from z{thresholds.RasterFromZoom}, vector from z{thresholds.VectorFromZoom}, " +
            $"visible z{thresholds.MinZoom?.ToString(CultureInfo.InvariantCulture) ?? "open"}–z{thresholds.MaxZoom?.ToString(CultureInfo.InvariantCulture) ?? "open"}) " +
            $"are invalid: {defect}."))
    {
        Defect = defect;
        LayerId = layerId;
        Thresholds = thresholds;
    }

    public InvalidLodPolicyException()
    {
    }

    public InvalidLodPolicyException(string message) : base(message)
    {
    }

    public InvalidLodPolicyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The rule broken.</summary>
    public LodThresholdsDefect? Defect { get; }

    /// <summary>The layer whose thresholds are invalid; <c>null</c> for the policy's default thresholds.</summary>
    public string? LayerId { get; }

    /// <summary>The invalid thresholds.</summary>
    public LodThresholds Thresholds { get; }
}
