using System.Runtime.InteropServices;

namespace GeoAssets.Ingestion.Resolution;

/// <summary>A key under which an element is indexed: the key namespace and the invariant value.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct IndexKey(string Namespace, string Value);
