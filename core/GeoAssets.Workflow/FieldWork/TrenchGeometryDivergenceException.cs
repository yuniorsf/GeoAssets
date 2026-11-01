namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// Thrown when a water/sewer repair order's recorded trench dimensions diverge from its
/// georeferenced bounding polygon's actual area by more than
/// <see cref="WaterPavingSubOrderSpawner.MaxAreaDivergenceFraction"/> (XD01-230).
/// </summary>
public sealed class TrenchGeometryDivergenceException : InvalidOperationException
{
    public double RecordedAreaSquareMeters { get; }
    public double ActualAreaSquareMeters { get; }

    public TrenchGeometryDivergenceException(double recordedAreaSquareMeters, double actualAreaSquareMeters)
        : base($"Recorded trench area ({recordedAreaSquareMeters:F2} m²) diverges from the georeferenced boundary's actual area ({actualAreaSquareMeters:F2} m²) by more than {WaterPavingSubOrderSpawner.MaxAreaDivergenceFraction:P0}.")
    {
        RecordedAreaSquareMeters = recordedAreaSquareMeters;
        ActualAreaSquareMeters = actualAreaSquareMeters;
    }

    public TrenchGeometryDivergenceException()
    {
    }

    public TrenchGeometryDivergenceException(string message) : base(message)
    {
    }

    public TrenchGeometryDivergenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
