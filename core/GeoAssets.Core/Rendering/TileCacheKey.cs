using System.Globalization;
using System.Runtime.InteropServices;

namespace GeoAssets.Core.Rendering;

/// <summary>
/// The identity of one cached tile (ADR-002 D6, XD01-431): <c>(orgScope, grantScopeHash, layerVersion)</c> plus the tile
/// itself — rendered for <see cref="Scope"/>, of <see cref="LayerSetId"/> at <see cref="LayerVersion"/>, at
/// <see cref="Address"/>, in <see cref="Format"/>. Every part is in <see cref="Value"/>, so a caller in another
/// organization or with other grants never gets another's tile, and a new layer version never gets a stale one.
/// <see cref="AddressTag"/> groups every scope, layer set, version and format of one tile, for eviction
/// (<see cref="ITileCache.RemoveAsync"/>).
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct TileCacheKey
{
    /// <summary>The first segment of every <see cref="Value"/>.</summary>
    public const string KeyPrefix = "tile";

    /// <summary>The first segment of every <see cref="AddressTag"/>.</summary>
    public const string AddressTagPrefix = "tile-at";

    /// <summary>The key of the tile at <paramref name="address"/> of <paramref name="layerSetId"/> at <paramref name="layerVersion"/>, in <paramref name="format"/>, for <paramref name="scope"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is <c>default</c>, or <paramref name="layerSetId"/>/<paramref name="layerVersion"/> breaks <see cref="TileKeySegment"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="layerSetId"/> or <paramref name="layerVersion"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a defined <see cref="TileFormat"/>.</exception>
    public TileCacheKey(TileCacheScope scope, string layerSetId, string layerVersion, TileAddress address, TileFormat format)
    {
        if (!scope.IsSpecified)
            throw new ArgumentException("A tile cache key needs the caller's scope.", nameof(scope));
        TileKeySegment.EnsureValid(layerSetId, nameof(layerSetId));
        TileKeySegment.EnsureValid(layerVersion, nameof(layerVersion));
        var extension = format.FileExtension();

        Scope = scope;
        LayerSetId = layerSetId;
        LayerVersion = layerVersion;
        Address = address;
        Format = format;
        Value = string.Create(
            CultureInfo.InvariantCulture,
            $"{KeyPrefix}/{scope.OrganizationScope}/{scope.GrantScopeHash}/{layerSetId}/{layerVersion}/{address.Z}/{address.X}/{address.Y}.{extension}");
    }

    /// <summary>Who the tile was rendered for.</summary>
    public TileCacheScope Scope { get; }

    /// <summary>The <see cref="TileLayerSet.Id"/> of the tile's layers.</summary>
    public string LayerSetId { get; }

    /// <summary>The layer set's version — the <c>{v}</c> segment of the tile URL.</summary>
    public string LayerVersion { get; }

    /// <summary>Which tile.</summary>
    public TileAddress Address { get; }

    /// <summary>The tile's encoding.</summary>
    public TileFormat Format { get; }

    /// <summary>
    /// The key string, <c>tile/{orgScope}/{grantScopeHash}/{layerSetId}/{layerVersion}/{z}/{x}/{y}.{mvt|png}</c>, in
    /// invariant digits; <c>/</c> never occurs inside a segment, so distinct keys never collide.
    /// </summary>
    public string Value { get; }

    /// <summary>The eviction tag of the key's tile, <see cref="AddressTagFor"/> its <see cref="Address"/>.</summary>
    public string AddressTag => AddressTagFor(Address);

    /// <summary>The eviction tag shared by every cached entry of the tile at <paramref name="address"/>: <c>tile-at/{z}/{x}/{y}</c>.</summary>
    public static string AddressTagFor(TileAddress address) => AddressTagPrefix + "/" + address.ToString();

    /// <summary>The key string (<see cref="Value"/>).</summary>
    public override string ToString() => Value;
}
