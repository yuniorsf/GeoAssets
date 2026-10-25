namespace GeoAssets.Core.Models;

/// <summary>Initial map view for a <see cref="Project"/>. Defaults match <see cref="Project"/>'s
/// map-loading pass-through target (Santo Domingo, Dominican Republic, zoom 5).</summary>
public sealed class ProjectViewState
{
    public double Lat { get; set; } = 20.0;
    public double Lon { get; set; } = -77.0;
    public int Zoom { get; set; } = 5;
}
