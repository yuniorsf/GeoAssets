using System.Runtime.InteropServices;

namespace GeoAssets.Ingestion.Resolution;

/// <summary>
/// The anchor candidates of a group: how many distinct candidates lie within the tolerance of any of its
/// <see cref="NodeCount"/> nodes and, when exactly one does, that <see cref="Anchor"/> and its smallest node distance.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AnchorMatch(int CandidateCount, IndexEntry Anchor, double DistanceMeters, int NodeCount)
{
    public bool IsResolved => CandidateCount == 1;
}
