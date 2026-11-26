namespace GeoAssets.Ingestion.Model;

/// <summary>Canonical element kinds of a distribution network.</summary>
public enum ElementKind
{
    /// <summary>A point that carries network: pole, tower, other geographic point.</summary>
    Structure,

    /// <summary>Equipment located at a structure: transformer, switch, breaker, capacitor bank…</summary>
    Device,

    /// <summary>A conductor stretch between two structures (MV, LV, HV).</summary>
    Segment,
}
