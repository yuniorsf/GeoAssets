namespace GeoAssets.Domain.Electrical.Continuity;

/// <summary>Computes PRODIST Módulo 8 individual continuity indicators (DIC/FIC/DMIC) from raw interruption events.</summary>
public static class ContinuityIndicatorsCalculator
{
    /// <summary>
    /// Calculates DIC/FIC/DMIC for <paramref name="consumerUnitId"/> over
    /// [<paramref name="periodStart"/>, <paramref name="periodEndExclusive"/>).
    ///
    /// Each qualifying event is clipped to the apuração period before contributing to DIC/DMIC,
    /// so an interruption that started before the period or ends after it only counts the portion
    /// actually inside it.
    ///
    /// <paramref name="minimumQualifyingDuration"/> excludes "interrupções de curta duração"
    /// (short-duration interruptions PRODIST carves out of DIC/FIC/DMIC) by the event's full,
    /// unclipped duration — left <c>null</c> (no exclusion) by default rather than hardcoding a
    /// specific minute threshold, since that threshold has changed across PRODIST revisions.
    /// </summary>
    public static ContinuityIndicators Calculate(
        IEnumerable<InterruptionEvent> events,
        string consumerUnitId,
        DateTimeOffset periodStart,
        DateTimeOffset periodEndExclusive,
        TimeSpan? minimumQualifyingDuration = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerUnitId);
        if (periodEndExclusive <= periodStart)
            throw new ArgumentOutOfRangeException(nameof(periodEndExclusive), "Must be after periodStart.");

        var dic = TimeSpan.Zero;
        var dmic = TimeSpan.Zero;
        var fic = 0;

        foreach (var ev in events)
        {
            if (!string.Equals(ev.ConsumerUnitId, consumerUnitId, StringComparison.Ordinal)) continue;
            if (minimumQualifyingDuration is { } threshold && ev.Duration < threshold) continue;

            var clippedStart = ev.StartedAt < periodStart ? periodStart : ev.StartedAt;
            var clippedEnd = ev.EndedAt > periodEndExclusive ? periodEndExclusive : ev.EndedAt;
            if (clippedEnd <= clippedStart) continue;

            var clippedDuration = clippedEnd - clippedStart;
            dic += clippedDuration;
            fic++;
            if (clippedDuration > dmic) dmic = clippedDuration;
        }

        return new ContinuityIndicators(dic, fic, dmic);
    }
}
