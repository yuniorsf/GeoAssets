using System.Runtime.InteropServices;

namespace GeoAssets.Shared.Services.Rendering;

/// <summary>What one completed <see cref="MapViewportRenderer"/> pass drew (XD01-429): the renderers' chunk/feature
/// count and how long it took, from the end of the debounce.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ViewportRenderOutcome(int Count, TimeSpan Elapsed);
