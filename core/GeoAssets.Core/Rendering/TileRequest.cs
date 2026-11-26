namespace GeoAssets.Core.Rendering;

/// <summary>What a tile source is asked for (XD01-431): the tile at <see cref="Address"/> of <see cref="LayerSet"/>.</summary>
public sealed record TileRequest
{
    /// <summary>The tile at <paramref name="address"/> of <paramref name="layerSet"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="layerSet"/> is <c>null</c>.</exception>
    public TileRequest(TileLayerSet layerSet, TileAddress address)
    {
        ArgumentNullException.ThrowIfNull(layerSet);
        LayerSet = layerSet;
        Address = address;
    }

    /// <summary>The layers the tile carries.</summary>
    public TileLayerSet LayerSet { get; }

    /// <summary>Which tile.</summary>
    public TileAddress Address { get; }
}
