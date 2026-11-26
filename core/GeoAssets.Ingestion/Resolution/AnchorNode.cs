using System.Runtime.InteropServices;

namespace GeoAssets.Ingestion.Resolution;

/// <summary>A compact native point of a group-anchor group (an endpoint of one of its elements).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AnchorNode(double X, double Y);
