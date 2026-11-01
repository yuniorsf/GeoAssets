namespace GeoAssets.Domain.Electrical.Continuity;

/// <summary>
/// One supply interruption affecting a single consumer unit — the raw input to the PRODIST
/// Módulo 8 individual continuity indicators (DIC/FIC/DMIC).
/// </summary>
public sealed record InterruptionEvent(string ConsumerUnitId, DateTimeOffset StartedAt, DateTimeOffset EndedAt)
{
    /// <summary>Full duration of this event, unclipped to any apuração period.</summary>
    public TimeSpan Duration => EndedAt > StartedAt ? EndedAt - StartedAt : TimeSpan.Zero;
}
