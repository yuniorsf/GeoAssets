using GeoAssets.Shared.Interfaces;

namespace GeoAssets.Shared.Services;

/// <summary>
/// Remembers the fence the map selection was made with, for as long as the selection is still
/// exactly what that fence resolved to (XD01-312). <see cref="MapSelectionCoordinator"/> records the
/// rings of a box or fence gesture that replaced the selection; any later change of the
/// <see cref="IMapSelectionSet"/> — a click toggle, an additive gesture, a clear — forgets them,
/// because the selection then no longer is "everything inside that fence". An order created from
/// the selection can then send the fence itself to the server (which re-resolves it for the order
/// type's asset types) instead of a list of ids, and the order keeps the fence for its outline.
/// Scoped, like the set; used from the renderer's synchronization context only.
/// </summary>
public sealed class MapSelectionFenceMemory : IDisposable
{
    private readonly IMapSelectionSet _selection;
    private bool _disposed;

    public MapSelectionFenceMemory(IMapSelectionSet selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        _selection = selection;
        _selection.Changed += OnSelectionChanged;
    }

    /// <summary>
    /// The closed <c>[longitude, latitude]</c> rings of the fence the current selection is the
    /// result of, or null when the selection was not (or is no longer) made by one fence alone.
    /// </summary>
    public double[][][]? Rings { get; private set; }

    /// <summary>
    /// Records <paramref name="rings"/> as the fence of the current selection. Call it after the
    /// selection was replaced with that fence's result.
    /// </summary>
    public void Remember(double[][][] rings)
    {
        ArgumentNullException.ThrowIfNull(rings);
        if (_disposed) return;

        Rings = rings;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _selection.Changed -= OnSelectionChanged;
        Rings = null;
    }

    private void OnSelectionChanged(MapSelectionChange change) => Rings = null;
}
