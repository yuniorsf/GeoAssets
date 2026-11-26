using System.Runtime.InteropServices;

namespace GeoAssets.Core.Rendering;

/// <summary>
/// A map viewport a provider is asked for (XD01-429): the <paramref name="Bounds"/> to fetch and, when the map knows
/// it, its <paramref name="Zoom"/> level, which a provider may forward so the server can leave out what is not drawn
/// at that zoom. <c>null</c> = unknown.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ViewportQuery(BoundingBox Bounds, int? Zoom = null);
