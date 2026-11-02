namespace GeoAssets.Core.Services;

/// <summary>Where an OTDR-measured fault resolves to along a cable route (XD01-282).</summary>
public enum FaultLocationType
{
    /// <summary>Within the central office's own slack loop — no outside-plant coordinate.</summary>
    StationSlack,

    /// <summary>At a mid-route technical reserve (spare slack) loop.</summary>
    TechnicalReserveLoop,

    /// <summary>At a splice closure.</summary>
    SpliceClosure,

    /// <summary>Along a direct aerial/buried span between two known points, not at a slack/splice point.</summary>
    AerialSpan,
}
